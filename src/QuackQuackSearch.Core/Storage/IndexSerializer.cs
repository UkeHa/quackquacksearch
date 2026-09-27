using MessagePack;
using QuackQuackSearch.Core.Config;
using QuackQuackSearch.Core.Index;

namespace QuackQuackSearch.Core.Storage;

[MessagePackObject]
public sealed class DirectoryNodeDto
{
    [Key(0)] public uint Id { get; set; }
    [Key(1)] public uint ParentId { get; set; }
    [Key(2)] public string Name { get; set; } = string.Empty;
}

[MessagePackObject]
public sealed class CompactFileEntryDto
{
    [Key(0)] public uint DirectoryId { get; set; }
    [Key(1)] public string Name { get; set; } = string.Empty;
    [Key(2)] public long Size { get; set; }
    [Key(3)] public uint ModifiedUnixSeconds { get; set; }
    [Key(4)] public ushort Flags { get; set; }
}

[MessagePackObject]
public sealed class IndexSnapshotDto
{
    [Key(0)] public int Version { get; set; } = 1;
    [Key(1)] public long CreatedAtUnix { get; set; }
    [Key(2)] public List<DirectoryNodeDto> Nodes { get; set; } = [];
    [Key(3)] public List<CompactFileEntryDto> Entries { get; set; } = [];
}

public static class IndexSerializer
{
    public static async Task SaveAsync(SearchIndexEngine engine, string? filePath = null, CancellationToken cancellationToken = default)
    {
        filePath ??= ConfigManager.GetCacheIndexFilePath();
        string dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);

        var (nodes, entries) = engine.GetSnapshot();

        var snapshot = new IndexSnapshotDto
        {
            Version = 1,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Nodes = nodes.Select(n => new DirectoryNodeDto
            {
                Id = n.Id,
                ParentId = n.ParentId,
                Name = n.Name
            }).ToList(),
            Entries = entries.Select(e => new CompactFileEntryDto
            {
                DirectoryId = e.DirectoryId,
                Name = e.Name,
                Size = e.Size,
                ModifiedUnixSeconds = e.ModifiedUnixSeconds,
                Flags = (ushort)e.Flags
            }).ToList()
        };

        string tempPath = filePath + ".tmp";
        await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536, useAsync: true))
        {
            await MessagePackSerializer.SerializeAsync(fileStream, snapshot, cancellationToken: cancellationToken).ConfigureAwait(false);
            await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, filePath, overwrite: true);
    }

    public static async Task<bool> TryLoadAsync(SearchIndexEngine engine, string? filePath = null, CancellationToken cancellationToken = default)
    {
        filePath ??= ConfigManager.GetCacheIndexFilePath();
        if (!File.Exists(filePath)) return false;

        try
        {
            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536, useAsync: true);
            var snapshot = await MessagePackSerializer.DeserializeAsync<IndexSnapshotDto>(fileStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (snapshot == null || snapshot.Version != 1) return false;

            var nodes = snapshot.Nodes.Select(n => new DirectoryNode(n.Id, n.ParentId, n.Name));
            var entries = snapshot.Entries.Select(e => new CompactFileEntry(
                e.DirectoryId,
                e.Name,
                e.Size,
                e.ModifiedUnixSeconds,
                (FileEntryFlags)e.Flags
            ));

            engine.RestoreSnapshot(nodes, entries);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
