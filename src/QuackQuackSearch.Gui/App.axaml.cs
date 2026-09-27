using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using QuackQuackSearch.Gui.Views;

namespace QuackQuackSearch.Gui;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ApplySystemTheme();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplySystemTheme()
    {
        bool isDark = DetectSystemDarkMode();
        RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;

        try
        {
            if (PlatformSettings != null)
            {
                PlatformSettings.ColorValuesChanged += (_, e) =>
                {
                    RequestedThemeVariant = e.ThemeVariant == PlatformThemeVariant.Dark
                        ? ThemeVariant.Dark
                        : ThemeVariant.Light;
                };
            }
        }
        catch { }
    }

    private static bool DetectSystemDarkMode()
    {
        try
        {
            if (Current?.PlatformSettings != null)
            {
                var variant = Current.PlatformSettings.GetColorValues().ThemeVariant;
                if (variant == PlatformThemeVariant.Dark) return true;
                if (variant == PlatformThemeVariant.Light) return false;
            }
        }
        catch { }

        // Fallback: inspect KDE Plasma configuration (~/.config/kdeglobals)
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string kdeglobals = Path.Combine(home, ".config", "kdeglobals");
            if (File.Exists(kdeglobals))
            {
                string text = File.ReadAllText(kdeglobals);
                if (text.Contains("ColorScheme=BreezeDark", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("dark", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }
}
