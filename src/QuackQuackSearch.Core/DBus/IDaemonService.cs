using System.Runtime.InteropServices;
using Tmds.DBus;

namespace QuackQuackSearch.Core.DBus;

[StructLayout(LayoutKind.Sequential)]
public struct SearchResultDto
{
    public string FullPath;
    public string FileName;
    public long Size;
    public uint ModifiedTime;
    public bool IsDirectory;
    public double Score;
}

[StructLayout(LayoutKind.Sequential)]
public struct PathInfoDto
{
    public string Path;
    public string Type;
    public bool Enabled;
    public string Status;
}

[StructLayout(LayoutKind.Sequential)]
public struct DaemonStatusDto
{
    public int TotalFiles;
    public int TotalDirectories;
    public int ActiveWatches;
    public long MemoryBytes;
    public long UptimeSeconds;
}

[DBusInterface("org.quackquacksearch.Daemon")]
public interface IDaemonService : IDBusObject
{
    Task<SearchResultDto[]> SearchAsync(string query, int maxResults);
    Task<bool> AddPathAsync(string path, string type);
    Task<bool> RemovePathAsync(string path);
    Task<bool> SetPathEnabledAsync(string path, bool enabled);
    Task<PathInfoDto[]> ListPathsAsync();
    Task<DaemonStatusDto> GetStatusAsync();
    Task TriggerRescanAsync(string path);
}

[DBusInterface("org.kde.krunner1")]
public interface IKRunner1 : IDBusObject
{
    Task<(string id, string text, string icon)[]> ActionsAsync();
    Task<(string id, string text, string icon, int type, double relevance, IDictionary<string, object> properties)[]> MatchAsync(string query);
    Task RunAsync(string id, string action_id);
    Task TeardownAsync();
}
