using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using QuackQuackSearch.Core.Config;
using QuackQuackSearch.Gui.Theming;
using QuackQuackSearch.Gui.Views;

namespace QuackQuackSearch.Gui;

public partial class App : Application
{
    public static SystemColorPalette? CurrentPalette { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var config = ConfigManager.LoadOrCreateDefault();
        ApplyTheme(config.Appearance.ThemeMode);

        try
        {
            if (PlatformSettings != null)
            {
                PlatformSettings.ColorValuesChanged += (_, _) =>
                {
                    var latestConfig = ConfigManager.LoadOrCreateDefault();
                    if (string.Equals(latestConfig.Appearance.ThemeMode, "auto", StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyTheme("auto");
                    }
                };
            }
        }
        catch { }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyTheme(string? themeMode)
    {
        if (Current is not App app) return;

        var palette = SystemThemeDetector.GetPaletteForMode(themeMode);
        CurrentPalette = palette;

        var variant = palette.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

        foreach (var style in app.Styles)
        {
            if (style is FluentTheme fluent)
            {
                fluent.Palettes[variant] = CreateColorPaletteResources(palette);
            }
        }

        ApplyDynamicResources(app, palette);
        app.RequestedThemeVariant = variant;
    }

    private static ColorPaletteResources CreateColorPaletteResources(SystemColorPalette p)
    {
        return new ColorPaletteResources
        {
            Accent = p.Accent,
            RegionColor = p.WindowBackground,
            AltHigh = p.ViewBackground,
            AltMediumHigh = Color.FromArgb(215, p.ViewBackground.R, p.ViewBackground.G, p.ViewBackground.B),
            AltMedium = Color.FromArgb(153, p.ViewBackground.R, p.ViewBackground.G, p.ViewBackground.B),
            AltMediumLow = Color.FromArgb(102, p.ViewBackground.R, p.ViewBackground.G, p.ViewBackground.B),
            AltLow = Color.FromArgb(51, p.ViewBackground.R, p.ViewBackground.G, p.ViewBackground.B),
            ChromeLow = p.ChromeBackground,
            ChromeMediumLow = p.WindowBackground,
            ChromeMedium = p.ChromeBackground,
            ChromeHigh = p.ButtonBackground,
            BaseHigh = p.Foreground,
            BaseMediumHigh = Color.FromArgb(204, p.Foreground.R, p.Foreground.G, p.Foreground.B),
            BaseMedium = Color.FromArgb(153, p.Foreground.R, p.Foreground.G, p.Foreground.B),
            BaseMediumLow = Color.FromArgb(102, p.Foreground.R, p.Foreground.G, p.Foreground.B),
            BaseLow = Color.FromArgb(55, p.Foreground.R, p.Foreground.G, p.Foreground.B),
            ListLow = Color.FromArgb(28, p.Foreground.R, p.Foreground.G, p.Foreground.B),
            ListMedium = Color.FromArgb(55, p.Foreground.R, p.Foreground.G, p.Foreground.B)
        };
    }

    private static void ApplyDynamicResources(Application app, SystemColorPalette p)
    {
        Color baseLow = Color.FromArgb(55, p.Foreground.R, p.Foreground.G, p.Foreground.B);
        Color baseMed = Color.FromArgb(153, p.Foreground.R, p.Foreground.G, p.Foreground.B);
        Color baseMedHigh = Color.FromArgb(204, p.Foreground.R, p.Foreground.G, p.Foreground.B);

        app.Resources["SystemRegionColor"] = p.WindowBackground;
        app.Resources["SystemRegionBrush"] = new SolidColorBrush(p.WindowBackground);
        app.Resources["SystemControlPageBackgroundAltHighBrush"] = new SolidColorBrush(p.WindowBackground);

        app.Resources["SystemAltHighColor"] = p.ViewBackground;
        app.Resources["SystemControlBackgroundAltHighBrush"] = new SolidColorBrush(p.ViewBackground);

        app.Resources["SystemChromeLowColor"] = p.ChromeBackground;
        app.Resources["SystemChromeMediumLowColor"] = p.WindowBackground;
        app.Resources["SystemChromeMediumColor"] = p.ChromeBackground;
        app.Resources["SystemChromeHighColor"] = p.ButtonBackground;
        app.Resources["SystemControlBackgroundChromeMediumLowBrush"] = new SolidColorBrush(p.ChromeBackground);
        app.Resources["SystemControlBackgroundChromeMediumBrush"] = new SolidColorBrush(p.ChromeBackground);

        app.Resources["SystemBaseHighColor"] = p.Foreground;
        app.Resources["SystemBaseMediumHighColor"] = baseMedHigh;
        app.Resources["SystemBaseMediumColor"] = baseMed;
        app.Resources["SystemBaseLowColor"] = baseLow;
        app.Resources["SystemControlForegroundBaseHighBrush"] = new SolidColorBrush(p.Foreground);
        app.Resources["SystemControlForegroundBaseMediumHighBrush"] = new SolidColorBrush(baseMedHigh);
        app.Resources["SystemControlForegroundBaseMediumBrush"] = new SolidColorBrush(baseMed);
        app.Resources["SystemControlForegroundBaseLowBrush"] = new SolidColorBrush(baseLow);

        app.Resources["SystemAccentColor"] = p.Accent;
        app.Resources["SystemAccentColorLight1"] = SystemThemeDetector.AdjustBrightness(p.Accent, 0.12);
        app.Resources["SystemAccentColorLight2"] = SystemThemeDetector.AdjustBrightness(p.Accent, 0.24);
        app.Resources["SystemAccentColorLight3"] = SystemThemeDetector.AdjustBrightness(p.Accent, 0.36);
        app.Resources["SystemAccentColorDark1"] = SystemThemeDetector.AdjustBrightness(p.Accent, -0.10);
        app.Resources["SystemAccentColorDark2"] = SystemThemeDetector.AdjustBrightness(p.Accent, -0.20);
        app.Resources["SystemAccentColorDark3"] = SystemThemeDetector.AdjustBrightness(p.Accent, -0.30);

        app.Resources["SystemControlHighlightAccentBrush"] = new SolidColorBrush(p.Accent);
        app.Resources["SystemControlHighlightListAccentLowBrush"] = new SolidColorBrush(Color.FromArgb(150, p.Accent.R, p.Accent.G, p.Accent.B));
        app.Resources["SystemControlHighlightListAccentMediumBrush"] = new SolidColorBrush(Color.FromArgb(195, p.Accent.R, p.Accent.G, p.Accent.B));
        app.Resources["SystemControlHighlightListAccentHighBrush"] = new SolidColorBrush(Color.FromArgb(230, p.Accent.R, p.Accent.G, p.Accent.B));

        app.Resources["TextControlBackground"] = new SolidColorBrush(p.ViewBackground);
        app.Resources["TextControlBackgroundFocused"] = new SolidColorBrush(p.ViewBackground);
        app.Resources["TextControlBackgroundPointerOver"] = new SolidColorBrush(SystemThemeDetector.AdjustBrightness(p.ViewBackground, p.IsDark ? 0.03 : -0.02));
        app.Resources["TextControlForeground"] = new SolidColorBrush(p.Foreground);

        app.Resources["ButtonBackground"] = new SolidColorBrush(p.ButtonBackground);
        app.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(SystemThemeDetector.AdjustBrightness(p.ButtonBackground, p.IsDark ? 0.06 : -0.05));
        app.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(SystemThemeDetector.AdjustBrightness(p.ButtonBackground, p.IsDark ? -0.04 : 0.04));
        app.Resources["ButtonForeground"] = new SolidColorBrush(p.Foreground);
        app.Resources["ButtonForegroundPointerOver"] = new SolidColorBrush(p.Foreground);
    }
}
