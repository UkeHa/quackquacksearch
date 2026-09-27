# QuackQuackSearch - Technische Dokumentation

Willkommen in der Dokumentation von **QuackQuackSearch**, dem blitzschnellen Datei-Suchtool für Linux nach dem Vorbild von "Everything" unter Windows.

---

## 📚 Dokumentationsübersicht

1. [**01. Architektur & Konzeptbewertung**](01_architektur_und_bewertung.md)  
   Stärken, Herausforderungen (Linux vs. Windows USN-Journal), Gesamtsystem-Diagramm und Kernentscheidungen.

2. [**02. In-Memory Suchindex & Datenstrukturen**](02_index_und_datenstrukturen.md)  
   Speicheroptimierung (Directory-Tree mit Pfadkompression), SIMD-beschleunigte Substring-Suche, Trigram-Index, Ranking-System und Persistenz.

3. [**03. Dateisystem-Monitoring (inotify & Polling)**](03_dateisystem_monitoring.md)  
   Rekursives inotify-Management, Limit-Handling (`max_user_watches`), Polling-Strategie für NFS/SMB/GVFS mit Ausfallschutz sowie Ausschlusslisten.

4. [**04. D-Bus Schnittstellen & KRunner Integration**](04_dbus_und_krunner_schnittstelle.md)  
   Spezifikation der eigenen D-Bus-API (`org.quackquacksearch.Daemon`), des KDE KRunner Protokolls (`org.kde.krunner1`) und der `.desktop`-Registrierung.

5. [**05. Konfiguration, Pfade & Mount-Scanner**](05_konfiguration_und_pfade.md)  
   XDG-Konformität, JSON-Schema für `config.json` und Erkennung von Speichermedien via `/proc/mounts`.

6. [**06. Meilensteine & Architekturentscheidungen (ADR)**](06_meilensteine_und_entscheidungen.md)  
   Architecture Decision Records (ADR-001 bis ADR-006) und schrittweiser Phasenplan zur Realisierung.
