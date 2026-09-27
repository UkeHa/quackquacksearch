# In-Memory Suchindex & Datenstrukturen

Der Suchindex ist das Herzstück von **QuackQuackSearch**. Er entscheidet darüber, ob sich die Anwendung wie "Everything" (sub-millisekündliche Suchergebnisse bei Tastenanschlägen) oder wie eine träge Hintergrundanwendung anfühlt.

---

## 1. Speicherbedarf & Pfadkompression

### 1.1 Problem des naiven Ansatzes
Angenommen, ein System besitzt 1.500.000 indexierte Dateien.
Ein naiver C#-Ansatz sieht oft so aus:

```csharp
public class NaiveFileEntry
{
    public string FullPath { get; set; }   // ca. 80-120 Bytes Zeichenkette + Referenz
    public string FileName { get; set; }   // redundanter Substring
    public long Size { get; set; }         // 8 Bytes
    public DateTime LastModified { get; set; } // 8 Bytes
}
```

- **Speicherverbrauch**:
  - Jeder String hat 24–26 Bytes Overhead im .NET Heap + UTF-16 Zeichen (2 Bytes/Char).
  - Ein durchschnittlicher voller Pfad hat ~70 Zeichen = ~166 Bytes pro Pfad.
  - Jedes Objekt hat 16 Bytes Objektheader + 8 Bytes Referenzpointer.
  - **Ergebnis bei 1,5 Mio. Dateien**: Über **450–600 MB RAM** allein für den Roh-Index, plus massiver GC-Druck beim Indizieren.

---

### 1.2 Die optimierte Datenstruktur (Directory Tree + Compact File Entries)

Dateipfade weisen extrem hohe Redundanz auf: Tausende Dateien teilen sich dasselbe übergeordnete Verzeichnis.

```
/home/user/projects/web/src/components/Header.tsx
/home/user/projects/web/src/components/Footer.tsx
/home/user/projects/web/src/components/Sidebar.tsx
```

#### Komprimiertes Modell:

1. **Verzeichnistabelle (`DirectoryTable`)**:
   - Jedes Verzeichnis erhält eine eindeutige fortlaufende Ganzzahl-ID (`uint32`).
   - Ein Verzeichnis speichert nur seinen eigenen Namen und die `ParentDirectoryId`.
   - Bei typischen Systemen gibt es ca. 10-mal mehr Dateien als Verzeichnisse (z. B. 100.000 Verzeichnisse für 1.000.000 Dateien).

```csharp
public readonly record struct DirectoryNode(
    uint DirectoryId,
    uint ParentDirectoryId,
    string Name
);
```

2. **Dateieinträge (`CompactFileEntry`) als Struct/Array**:

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct CompactFileEntry
{
    public readonly uint DirectoryId;      // 4 Bytes: Zeiger auf Verzeichnis
    public readonly uint NameOffset;       // 4 Bytes: Offset im String-/Byte-Pool
    public readonly ushort NameLength;     // 2 Bytes: Länge des Dateinamens
    public readonly ushort Flags;          // 2 Bytes: z.B. IsDirectory, Hidden, etc.
    public readonly long Size;             // 8 Bytes
    public readonly uint ModifiedUnixTime; // 4 Bytes (Unix Epoch Sekunden)
} // Gesamt: 24 Bytes pro Datei!
```

- **Dateinamen-Pool**:
  - Dateinamen können in zusammenhängenden Blöcken (`string[]` oder fortlaufenden UTF-8/UTF-16 Chunks) gehalten werden.
  - **Speicherverbrauch bei 1,5 Mio. Dateien**:
    - 1,5 Mio. × 24 Bytes = **36 MB** für Metadaten.
    - Dateinamen (durchschnittlich 16 Zeichen) = **~30–40 MB**.
    - Verzeichnistabelle (150.000 Verzeichnisse) = **~15 MB**.
    - **Gesamt-RAM**: Nur ca. **80–90 MB RAM** statt 600 MB!

---

## 2. Suchalgorithmen: Substring & Matcher

"Everything" zeichnet sich dadurch aus, dass man nach beliebigen Teilen des Dateinamens sucht (Infix-Suche).

### 2.1 Warum SQLite FTS5 für diesen Zweck ungeeignet ist
- FTS5 teilt Text in "Tokens" (Wörter) auf. Bei `quack_v1.0.tar.gz` matcht eine Standard-Suche nach `ack` standardmäßig nicht.
- Mit `tokenize='trigram'` unterstützt FTS5 zwar Substrings, aber:
  - FTS5-Trigram-Tabellen vervierfachen die Datenbankgröße auf der Festplatte.
  - Jede SQLite-Abfrage erfordert IPC/C-Interop, Kontextwechsel und Thread-Synchronisation.
  - Für Live-Typing in KRunner (<20ms Latenz) erzeugt In-Memory-Code ohne Disk-Overhead eine deutlich stabilere Reaktionszeit.

---

### 2.2 Suchstrategien im Vergleich

| Algorithmus | Suchzeit (1 Mio. Dateien) | Speicher-Overhead | Indexierungs-Aufwand | Eignung |
| :--- | :--- | :--- | :--- | :--- |
| **Klassischer Trie** | <1 ms (nur Präfix `abc*`) | Sehr hoch | Hoch | Schlecht für Infix `*bc*` |
| **Suffix-Tree / Suffix-Array** | <1 ms | Extrem hoch (100–300 MB) | Sehr komplex bei dynamischen Updates | Zu schwergewichtig |
| **Linearer SIMD-Scan** | **10–25 ms** | **0 MB** (nutzt Rohdaten) | **Keiner** (sofort suchbar) | **Ideal für Phase 1 & 2** |
| **Invertierter Trigram-Index** | **< 2 ms** | Moderat (ca. 40–60 MB) | Gering (3-Gram Hashsets) | **Ideal für Phase 3 (Endausbaustufe)** |

#### Der SIMD-Lineare Scan (.NET 10):
Dank moderner Vector-Extensions (AVX2 / AVX-512) kann .NET 10 (`string.Contains(..., StringComparison.OrdinalIgnoreCase)` bzw. `MemoryExtensions.IndexOf`) mehrere Megabytes an Text pro Millisekunde durchsuchen.
- 1,5 Millionen Dateinamen können in C# auf modernen x86_64-CPUs (z. B. AMD Zen 4/5 oder Intel Core 12th-15th Gen) parallelisiert über 4 Kerne in **unter 8–15 ms** vollständig durchsucht werden!
- Dies reicht für KRunner (50 ms Budget) bereits im ersten Entwicklungsschritt völlig aus.

---

## 3. Relevanz-Ranking (Scoring)

Wenn der Nutzer `calc` eingibt, sollen relevante Ergebnisse oben stehen:

1. **Exakter Treffer Dateiname**: `calc` -> Score 1000
2. **Präfix-Treffer Dateiname**: `calc.exe`, `calculator.py` -> Score 800
3. **Wortgrenzen-Treffer**: `my_calc_tool` -> Score 600
4. **Teilstring im Dateinamen**: `decalcification.doc` -> Score 400
5. **Treffer nur im Pfad**: `/home/calc/notes.txt` -> Score 200
6. **Tiefe & Aktualität**: Kürzere Pfade und kürzlich modifizierte Dateien erhalten einen Bonus (+1 bis +50).

---

## 4. Persistenz auf Disk

Um beim Systemstart nicht jedes Mal alle Laufwerke neu rekursiv crawlen zu müssen, wird der In-Memory-Index auf Disk serialisiert.

### 4.1 Format-Auswahl: MessagePack / Protobuf vs. Custom Binary
- **MessagePack-CSharp**:
  - Extrem schnell (nutzt Source Generators und `IBufferWriter<byte>`).
  - Plattformunabhängig, schema-evolutionär und typ-sicher.
- **Speicherort**: `$XDG_CACHE_HOME/quackquacksearch/index.cache` (Fallback: `~/.cache/quackquacksearch/index.cache`).
- **Atomares Schreiben**:
  1. Temporäre Datei schreiben: `index.cache.tmp`
  2. `fsync()` auf Dateideskriptor ausführen
  3. Atomarer Rename: `index.cache.tmp` -> `index.cache`
  - Verhindert defekte Indexdateien bei plötzlichem Herunterfahren oder Stromausfall.

### 4.2 Snapshot-Strategie
- Nach dem ersten vollständigen Scan: Snapshot schreiben.
- Im laufenden Betrieb: In-Memory-Updates sofort anwenden; Snapshots verzögert (z. B. 5 Minuten nach letzter Änderung oder beim sauberen Shutdown via SIGTERM/D-Bus) speichern.
