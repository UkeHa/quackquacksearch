using System.Net.Sockets;
using System.Text;
using Avalonia.Threading;

namespace QuackQuackSearch.Gui;

public sealed class SingleInstanceIpc : IDisposable
{
    private readonly string _socketPath;
    private Socket? _listener;
    private CancellationTokenSource? _cts;
    private readonly Action _onTriggerAction;

    public SingleInstanceIpc(Action onTriggerAction)
    {
        _onTriggerAction = onTriggerAction;
        string runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? Path.GetTempPath();
        _socketPath = Path.Combine(runtimeDir, $"quackquacksearch-gui-{Environment.UserName}.sock");
    }

    /// <summary>
    /// Checks if another instance is already running. If so, sends the trigger signal and returns true.
    /// Otherwise, initializes the listener socket and returns false.
    /// </summary>
    public bool TryActivateExistingInstance()
    {
        if (File.Exists(_socketPath))
        {
            try
            {
                using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                client.Connect(new UnixDomainSocketEndPoint(_socketPath));
                byte[] data = Encoding.UTF8.GetBytes("TOGGLE\n");
                client.Send(data);
                return true;
            }
            catch
            {
                // Stale socket file from previous crashed instance
                try { File.Delete(_socketPath); } catch { }
            }
        }

        StartListening();
        return false;
    }

    private void StartListening()
    {
        try
        {
            if (File.Exists(_socketPath))
            {
                File.Delete(_socketPath);
            }

            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
            _listener.Listen(5);

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SingleInstanceIpc] Warning: Could not setup IPC socket: {ex.Message}");
        }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptAsync(token);
                _ = Task.Run(() =>
                {
                    using (client)
                    {
                        byte[] buffer = new byte[64];
                        int read = client.Receive(buffer);
                        if (read > 0)
                        {
                            Dispatcher.UIThread.Post(() => _onTriggerAction());
                        }
                    }
                }, token);
            }
            catch when (token.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await Task.Delay(100, token);
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _listener?.Close(); } catch { }
        try { _listener?.Dispose(); } catch { }
        try
        {
            if (File.Exists(_socketPath))
            {
                File.Delete(_socketPath);
            }
        }
        catch { }
    }
}
