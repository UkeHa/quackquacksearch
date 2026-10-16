using Tmds.DBus;

namespace QuackQuackSearch.Core.DBus;

/// <summary>
/// Shared helper for connecting to the QuackQuackSearch D-Bus session daemon.
/// </summary>
public static class DaemonClient
{
    public const string ServiceName = "org.quackquacksearch.Daemon";
    public static readonly ObjectPath ServicePath = new("/org/quackquacksearch/Daemon");

    private static IDaemonService? _cachedProxy;

    public static async Task<IDaemonService?> TryGetServiceAsync()
    {
        try
        {
            var conn = Connection.Session;
            await conn.ConnectAsync().ConfigureAwait(false);
            bool active = await conn.IsServiceActiveAsync(ServiceName).ConfigureAwait(false);
            if (!active)
            {
                _cachedProxy = null;
                return null;
            }

            return _cachedProxy ??= conn.CreateProxy<IDaemonService>(ServiceName, ServicePath);
        }
        catch
        {
            _cachedProxy = null;
            return null;
        }
    }
}
