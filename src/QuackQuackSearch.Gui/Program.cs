using Avalonia;
using QuackQuackSearch.Gui.Views;

namespace QuackQuackSearch.Gui;

internal sealed class Program
{
    private static SingleInstanceIpc? _ipc;

    [STAThread]
    public static void Main(string[] args)
    {
        _ipc = new SingleInstanceIpc(() =>
        {
            MainWindow.Instance?.ToggleOrFocus();
        });

        if (_ipc.TryActivateExistingInstance())
        {
            // Successfully alerted running instance; exit
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _ipc.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
