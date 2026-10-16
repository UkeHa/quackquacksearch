using MessagePack;
using QuackQuackSearch.Core.Config;
using QuackQuackSearch.Core.Index;

namespace QuackQuackSearch.Core.Storage;

[MessagePackObject]
public sealed class IndexSnapshotDto
{
    [Key(0)] public int Version { get; set; } = 1;
    [Key(1)] public long CreatedAtUnix { get; set; }
    [Key(2)] public IReadOnlyList<DirectoryNode> Nodes { get; set; } = [];
    [Key(3)] public IReadOnlyList<CompactFileEntry> Entries { get; set; } = [];
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
            Nodes = nodes,
            Entries = entries
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

            engine.RestoreSnapshot(snapshot.Nodes, snapshot.Entries);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
