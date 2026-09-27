using System.Runtime.InteropServices;
using QuackQuackSearch.Daemon.DBus;
using QuackQuackSearch.Daemon.Services;
using Tmds.DBus;

namespace QuackQuackSearch.Daemon;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("  QuackQuackSearch Daemon (Background File Search)");
        Console.WriteLine("==================================================");

        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("[Daemon] Shutdown signal (SIGINT) received.");
            cts.Cancel();
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ =>
            {
                Console.WriteLine("[Daemon] Termination signal (SIGTERM) received.");
                cts.Cancel();
            });
        }

        using var daemonService = new QuackDaemonService();

        try
        {
            await daemonService.InitializeAsync(cts.Token);

            Console.WriteLine("[Daemon] Connecting to D-Bus Session Bus...");
            var connection = new Connection(Address.Session);
            await connection.ConnectAsync();

            // Register QuackQuackSearch Daemon API
            await connection.RegisterObjectAsync(daemonService);
            Console.WriteLine($"[Daemon] Registered D-Bus API on {daemonService.ObjectPath}");

            // Register KDE KRunner API
            var krunner = new KRunnerDbusObject(daemonService.Engine, daemonService.Config);
            await connection.RegisterObjectAsync(krunner);
            Console.WriteLine($"[Daemon] Registered KRunner API on {krunner.ObjectPath}");

            // Request service name
            await connection.RegisterServiceAsync("org.quackquacksearch.Daemon");
            Console.WriteLine("[Daemon] Acquired service name: org.quackquacksearch.Daemon");

            // Install KRunner .desktop plugin registration file
            KRunnerPluginInstaller.InstallDesktopFile();

            Console.WriteLine($"[Daemon] Ready! Monitoring {daemonService.Engine.TotalFiles:N0} files across {daemonService.Engine.TotalDirectories:N0} directories.");
            Console.WriteLine("[Daemon] Press Ctrl+C or send SIGTERM to terminate.");

            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }

            Console.WriteLine("[Daemon] Saving snapshot and shutting down...");
            await daemonService.SaveSnapshotSafeAsync();
            Console.WriteLine("[Daemon] Shutdown complete. Goodbye!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Daemon] Fatal error: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }
}
