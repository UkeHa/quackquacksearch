using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using QuackQuackSearch.Core.System;

namespace QuackQuackSearch.Gui.Theming;

public sealed record SystemColorPalette(
    string SourceName,
    bool IsDark,
    Color WindowBackground,
    Color ViewBackground,
    Color ChromeBackground,
    Color ButtonBackground,
    Color Foreground,
    Color Accent);

public static class SystemThemeDetector
{
    public static SystemColorPalette DetectSystemPalette()
    {
        Color? portalAccent = null;
        bool? portalDark = null;

        try
        {
            var platformSettings = Application.Current?.PlatformSettings;
            if (platformSettings != null)
            {
                var colorValues = platformSettings.GetColorValues();
                if (colorValues.ThemeVariant == PlatformThemeVariant.Dark)
                    portalDark = true;
                else if (colorValues.ThemeVariant == PlatformThemeVariant.Light)
                    portalDark = false;

                if (colorValues.AccentColor1.A > 0 &&
                    (colorValues.AccentColor1.R != 0 || colorValues.AccentColor1.G != 0 || colorValues.AccentColor1.B != 0))
                {
                    portalAccent = colorValues.AccentColor1;
                }
            }
        }
        catch { }

        string desktop = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ??
                          Environment.GetEnvironmentVariable("DESKTOP_SESSION") ??
                          string.Empty).ToLowerInvariant();

        bool isKdeOrQtDesktop = desktop.Contains("kde") || desktop.Contains("plasma") || desktop.Contains("lxqt");
        bool isTilingOrWlroots = desktop.Contains("hyprland") || desktop.Contains("sway") ||
                                 desktop.Contains("i3") || desktop.Contains("awesome") ||
                                 desktop.Contains("bspwm") || desktop.Contains("qtile");

        if (isKdeOrQtDesktop)
        {
            if (TryDetectKdeOrQtPalette(portalAccent, out var kdePalette))
                return kdePalette;
            if (TryDetectGtkPalette(portalAccent, portalDark, out var gtkPalette))
                return gtkPalette;
        }
        else if (isTilingOrWlroots)
        {
            // On Hyprland/Sway/i3, check user GTK overrides first, then KDE/qt6ct, then system GTK themes, then Xresources
            if (TryDetectGtkPalette(portalAccent, portalDark, out var gtkPalette))
                return gtkPalette;
            if (TryDetectKdeOrQtPalette(portalAccent, out var qtPalette))
                return qtPalette;
            if (TryDetectXResourcesPalette(portalAccent, out var xresPalette))
                return xresPalette;
        }
        else
        {
            // GNOME, XFCE, Cinnamon, MATE, Budgie, or generic desktop
            if (TryDetectGtkPalette(portalAccent, portalDark, out var gtkPalette))
                return gtkPalette;
            if (TryDetectKdeOrQtPalette(portalAccent, out var kdePalette))
                return kdePalette;
            if (TryDetectXResourcesPalette(portalAccent, out var xresPalette))
                return xresPalette;
        }

        bool isDark = portalDark ?? DetectFallbackDarkPreference();
        bool isGnome = desktop.Contains("gnome") || desktop.Contains("ubuntu") || desktop.Contains("pantheon");

        return CreateDefaultPalette(
            isDark,
            portalAccent,
            isGnome ? (isDark ? "GNOME Adwaita Dark" : "GNOME Adwaita Light")
                    : (isDark ? "System Dark" : "System Light"),
            isGnome);
    }

    public static SystemColorPalette GetPaletteForMode(string? themeMode)
    {
        var systemPalette = DetectSystemPalette();
        string mode = (themeMode ?? "auto").Trim().ToLowerInvariant();

        if (mode == "dark")
        {
            if (systemPalette.IsDark)
                return systemPalette;

            return CreateDefaultPalette(
                isDark: true,
                accentOverride: systemPalette.Accent,
                sourceName: "Dark",
                useAdwaitaTones: false);
        }

        if (mode == "light")
        {
            if (!systemPalette.IsDark)
                return systemPalette;

            return CreateDefaultPalette(
                isDark: false,
                accentOverride: systemPalette.Accent,
                sourceName: "Light",
                useAdwaitaTones: false);
        }

        return systemPalette;
    }

    public static SystemColorPalette CreateDefaultPalette(
        bool isDark,
        Color? accentOverride = null,
        string sourceName = "Default",
        bool useAdwaitaTones = false)
    {
        Color accent = accentOverride ?? (isDark ? Color.FromRgb(61, 174, 233) : Color.FromRgb(41, 128, 185));

        if (isDark)
        {
            Color winBg = useAdwaitaTones ? Color.FromRgb(36, 36, 36) : Color.FromRgb(32, 35, 38);
            Color viewBg = useAdwaitaTones ? Color.FromRgb(30, 30, 30) : Color.FromRgb(27, 30, 32);
            Color chromeBg = useAdwaitaTones ? Color.FromRgb(48, 48, 48) : Color.FromRgb(42, 46, 50);
            Color btnBg = useAdwaitaTones ? Color.FromRgb(56, 56, 56) : Color.FromRgb(49, 54, 59);
            Color fg = Color.FromRgb(252, 252, 252);

            return new SystemColorPalette(sourceName, true, winBg, viewBg, chromeBg, btnBg, fg, accent);
        }
        else
        {
            Color winBg = useAdwaitaTones ? Color.FromRgb(250, 250, 250) : Color.FromRgb(239, 240, 241);
            Color viewBg = Color.FromRgb(255, 255, 255);
            Color chromeBg = useAdwaitaTones ? Color.FromRgb(235, 235, 235) : Color.FromRgb(227, 229, 231);
            Color btnBg = useAdwaitaTones ? Color.FromRgb(230, 230, 230) : Color.FromRgb(223, 225, 227);
            Color fg = Color.FromRgb(35, 38, 41);

            return new SystemColorPalette(sourceName, false, winBg, viewBg, chromeBg, btnBg, fg, accent);
        }
    }

    private static bool TryDetectKdeOrQtPalette(Color? portalAccent, out SystemColorPalette palette)
    {
        palette = null!;

        try
        {
            string kdeglobals = Path.Combine(XdgDirectories.ConfigHome, "kdeglobals");
            if (File.Exists(kdeglobals) && TryParseKdeColorFile(kdeglobals, portalAccent, "KDE", out palette))
            {
                return true;
            }

            // Check qt6ct / qt5ct color scheme paths (used on Hyprland, Sway, i3, LXQt, XFCE)
            foreach (string qtDir in new[] { "qt6ct", "qt5ct" })
            {
                string confPath = Path.Combine(XdgDirectories.ConfigHome, qtDir, $"{qtDir}.conf");
                if (!File.Exists(confPath)) continue;

                var ini = ParseIniFile(confPath);
                if (ini.TryGetValue("Appearance", out var appSec) &&
                    appSec.TryGetValue("color_scheme_path", out string? schemePath) &&
                    !string.IsNullOrWhiteSpace(schemePath) &&
                    File.Exists(schemePath))
                {
                    if (TryParseQtCtColorScheme(schemePath, portalAccent, qtDir, out palette))
                        return true;
                }
            }
        }
        catch { }

        return false;
    }

    private static bool TryParseKdeColorFile(string filePath, Color? portalAccent, string sourcePrefix, out SystemColorPalette palette)
    {
        palette = null!;
        var sections = ParseIniFile(filePath);

        string schemeName = "Breeze";
        if (sections.TryGetValue("General", out var genSec) &&
            genSec.TryGetValue("ColorScheme", out string? cs) &&
            !string.IsNullOrWhiteSpace(cs))
        {
            schemeName = cs.Trim();
        }

        Color? winBg = GetIniColor(sections, "Colors:Window", "BackgroundNormal");
        Color? winFg = GetIniColor(sections, "Colors:Window", "ForegroundNormal");
        Color? viewBg = GetIniColor(sections, "Colors:View", "BackgroundNormal");
        Color? btnBg = GetIniColor(sections, "Colors:Button", "BackgroundNormal");
        Color? selBg = GetIniColor(sections, "Colors:Selection", "BackgroundNormal");

        // If kdeglobals only has ColorScheme=Name without inline [Colors:Window] RGB tables,
        // try looking up the scheme file in ~/.local/share/color-schemes or /usr/share/color-schemes
        if (winBg == null && !string.IsNullOrWhiteSpace(schemeName))
        {
            string? schemeFile = FindKdeColorSchemeFile(schemeName);
            if (schemeFile != null)
            {
                var schemeSections = ParseIniFile(schemeFile);
                winBg = GetIniColor(schemeSections, "Colors:Window", "BackgroundNormal");
                winFg = GetIniColor(schemeSections, "Colors:Window", "ForegroundNormal");
                viewBg = GetIniColor(schemeSections, "Colors:View", "BackgroundNormal");
                btnBg = GetIniColor(schemeSections, "Colors:Button", "BackgroundNormal");
                selBg ??= GetIniColor(schemeSections, "Colors:Selection", "BackgroundNormal");
            }
        }

        if (winBg == null)
        {
            // Even if no explicit RGB table was found, check if ColorScheme indicates Dark/Light
            if (schemeName.Contains("dark", StringComparison.OrdinalIgnoreCase))
            {
                palette = CreateDefaultPalette(true, selBg ?? portalAccent, $"{sourcePrefix} ({schemeName})");
                return true;
            }
            return false;
        }

        bool isDark = IsColorDark(winBg.Value);
        Color resolvedViewBg = viewBg ?? AdjustBrightness(winBg.Value, isDark ? -0.03 : 0.04);
        Color resolvedBtnBg = btnBg ?? AdjustBrightness(winBg.Value, isDark ? 0.06 : -0.05);
        Color resolvedChromeBg = BlendColors(winBg.Value, resolvedBtnBg, 0.5);
        Color resolvedFg = winFg ?? (isDark ? Color.FromRgb(252, 252, 252) : Color.FromRgb(35, 38, 41));
        Color resolvedAccent = selBg ?? portalAccent ?? (isDark ? Color.FromRgb(61, 174, 233) : Color.FromRgb(41, 128, 185));

        palette = new SystemColorPalette(
            $"{sourcePrefix} ({schemeName})",
            isDark,
            winBg.Value,
            resolvedViewBg,
            resolvedChromeBg,
            resolvedBtnBg,
            resolvedFg,
            resolvedAccent);
        return true;
    }

    private static string? FindKdeColorSchemeFile(string schemeName)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] dirs =
        [
            Path.Combine(home, ".local", "share", "color-schemes"),
            "/usr/local/share/color-schemes",
            "/usr/share/color-schemes"
        ];

        foreach (string dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            string exact = Path.Combine(dir, $"{schemeName}.colors");
            if (File.Exists(exact)) return exact;

            try
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*.colors"))
                {
                    if (Path.GetFileNameWithoutExtension(file).Equals(schemeName, StringComparison.OrdinalIgnoreCase))
                        return file;
                }
            }
            catch { }
        }

        return null;
    }

    private static bool TryParseQtCtColorScheme(string filePath, Color? portalAccent, string sourcePrefix, out SystemColorPalette palette)
    {
        palette = null!;
        var sections = ParseIniFile(filePath);
        if (!sections.TryGetValue("ColorScheme", out var csSec) ||
            !csSec.TryGetValue("active_colors", out string? activeColorsRaw) ||
            string.IsNullOrWhiteSpace(activeColorsRaw))
        {
            return false;
        }

        string[] parts = activeColorsRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        // Qt QPalette ColorRole indices: 0=WindowText, 1=Button, 9=Base(View), 10=Window, 12=Highlight
        if (parts.Length <= 12) return false;

        if (!TryParseColorString(parts[10], out Color winBg)) return false;
        TryParseColorString(parts[0], out Color winFg);
        TryParseColorString(parts[9], out Color viewBg);
        TryParseColorString(parts[1], out Color btnBg);
        TryParseColorString(parts[12], out Color accent);

        bool isDark = IsColorDark(winBg);
        Color resolvedChromeBg = BlendColors(winBg, btnBg.A > 0 ? btnBg : winBg, 0.5);
        string schemeName = Path.GetFileNameWithoutExtension(filePath);

        palette = new SystemColorPalette(
            $"{sourcePrefix} ({schemeName})",
            isDark,
            winBg,
            viewBg.A > 0 ? viewBg : AdjustBrightness(winBg, isDark ? -0.03 : 0.04),
            resolvedChromeBg,
            btnBg.A > 0 ? btnBg : AdjustBrightness(winBg, isDark ? 0.06 : -0.05),
            winFg.A > 0 ? winFg : (isDark ? Color.FromRgb(252, 252, 252) : Color.FromRgb(35, 38, 41)),
            accent.A > 0 ? accent : (portalAccent ?? Color.FromRgb(61, 174, 233)));
        return true;
    }

    private static bool TryDetectGtkPalette(Color? portalAccent, bool? portalDark, out SystemColorPalette palette)
    {
        palette = null!;

        try
        {
            // 1. Check user custom gtk-4.0/gtk.css and gtk-3.0/gtk.css (Gradience, Matugen, Wallust, nwg-look, etc.)
            string gtk4UserCss = Path.Combine(XdgDirectories.ConfigHome, "gtk-4.0", "gtk.css");
            if (File.Exists(gtk4UserCss) && TryParseGtkCssFile(gtk4UserCss, "GTK4 Custom", portalAccent, out palette))
                return true;

            string gtk3UserCss = Path.Combine(XdgDirectories.ConfigHome, "gtk-3.0", "gtk.css");
            if (File.Exists(gtk3UserCss) && TryParseGtkCssFile(gtk3UserCss, "GTK3 Custom", portalAccent, out palette))
                return true;

            // 2. Determine active GTK theme name from settings.ini, GTK_THEME env, or XFCE xsettings.xml
            var (themeName, preferDark) = GetConfiguredGtkTheme(portalDark);
            if (!string.IsNullOrWhiteSpace(themeName))
            {
                string? themeCss = FindGtkThemeCss(themeName, preferDark);
                if (themeCss != null && TryParseGtkCssFile(themeCss, $"GTK ({themeName})", portalAccent, out palette))
                {
                    return true;
                }

                // If the theme name itself indicates Adwaita or Dark/Light even without custom CSS file
                bool isThemeDark = preferDark ||
                                   themeName.Contains("dark", StringComparison.OrdinalIgnoreCase) ||
                                   themeName.Contains("mocha", StringComparison.OrdinalIgnoreCase) ||
                                   themeName.Contains("macchiato", StringComparison.OrdinalIgnoreCase) ||
                                   themeName.Contains("frappe", StringComparison.OrdinalIgnoreCase) ||
                                   themeName.Contains("dracula", StringComparison.OrdinalIgnoreCase) ||
                                   themeName.Contains("nord", StringComparison.OrdinalIgnoreCase);

                if (themeName.Contains("adwaita", StringComparison.OrdinalIgnoreCase))
                {
                    palette = CreateDefaultPalette(isThemeDark, portalAccent, $"GTK ({themeName})", useAdwaitaTones: true);
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    private static (string? ThemeName, bool PreferDark) GetConfiguredGtkTheme(bool? portalDark)
    {
        string? envTheme = Environment.GetEnvironmentVariable("GTK_THEME");
        if (!string.IsNullOrWhiteSpace(envTheme))
        {
            bool darkSuffix = envTheme.EndsWith(":dark", StringComparison.OrdinalIgnoreCase);
            string cleanName = darkSuffix ? envTheme[..^5] : envTheme;
            return (cleanName.Trim(), darkSuffix || (portalDark ?? false));
        }

        string? themeName = null;
        bool? preferDark = null;

        foreach (string gtkVer in new[] { "gtk-4.0", "gtk-3.0" })
        {
            string settingsIni = Path.Combine(XdgDirectories.ConfigHome, gtkVer, "settings.ini");
            if (!File.Exists(settingsIni)) continue;

            var ini = ParseIniFile(settingsIni);
            if (ini.TryGetValue("Settings", out var sec))
            {
                if (themeName == null && sec.TryGetValue("gtk-theme-name", out string? tn) && !string.IsNullOrWhiteSpace(tn))
                    themeName = tn.Trim().Trim('"', '\'');

                if (preferDark == null && sec.TryGetValue("gtk-application-prefer-dark-theme", out string? pd))
                {
                    preferDark = pd.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                                 pd.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        // Check XFCE xsettings.xml if themeName is still unknown
        if (string.IsNullOrWhiteSpace(themeName))
        {
            string xfceSettings = Path.Combine(XdgDirectories.ConfigHome, "xfce4", "xfconf", "xfce-perchannel-xml", "xsettings.xml");
            if (File.Exists(xfceSettings))
            {
                try
                {
                    var doc = XDocument.Load(xfceSettings);
                    foreach (var prop in doc.Descendants("property"))
                    {
                        if (prop.Attribute("name")?.Value == "ThemeName")
                        {
                            string? val = prop.Attribute("value")?.Value;
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                themeName = val.Trim();
                                break;
                            }
                        }
                    }
                }
                catch { }
            }
        }

        bool isDark = preferDark ?? portalDark ?? (themeName?.Contains("dark", StringComparison.OrdinalIgnoreCase) == true);
        return (themeName, isDark);
    }

    private static string? FindGtkThemeCss(string themeName, bool preferDark)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] baseDirs =
        [
            Path.Combine(home, ".local", "share", "themes"),
            Path.Combine(home, ".themes"),
            "/usr/local/share/themes",
            "/usr/share/themes"
        ];

        string[] subPaths = preferDark
            ? ["gtk-4.0/gtk-dark.css", "gtk-4.0/gtk.css", "gtk-3.0/gtk-dark.css", "gtk-3.0/gtk.css"]
            : ["gtk-4.0/gtk.css", "gtk-3.0/gtk.css"];

        foreach (string baseDir in baseDirs)
        {
            string themeDir = Path.Combine(baseDir, themeName);
            if (!Directory.Exists(themeDir)) continue;

            foreach (string sub in subPaths)
            {
                string candidate = Path.Combine(themeDir, sub);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static bool TryParseGtkCssFile(string cssPath, string sourceLabel, Color? portalAccent, out SystemColorPalette palette)
    {
        palette = null!;
        var colorVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CollectGtkCssColorVariables(cssPath, colorVars, depth: 0);

        if (colorVars.Count == 0) return false;

        Color? winBg = ResolveGtkColorVar(colorVars, "window_bg_color", "theme_bg_color", "bg_color");
        if (winBg == null) return false;

        Color? winFg = ResolveGtkColorVar(colorVars, "window_fg_color", "theme_fg_color", "fg_color", "theme_text_color");
        Color? viewBg = ResolveGtkColorVar(colorVars, "view_bg_color", "theme_base_color", "base_color", "card_bg_color");
        Color? chromeBg = ResolveGtkColorVar(colorVars, "headerbar_bg_color", "sidebar_bg_color", "card_bg_color");
        Color? accent = ResolveGtkColorVar(colorVars, "accent_bg_color", "accent_color", "theme_selected_bg_color", "selected_bg_color");

        bool isDark = IsColorDark(winBg.Value);
        Color resolvedViewBg = viewBg ?? AdjustBrightness(winBg.Value, isDark ? -0.03 : 0.04);
        Color resolvedChromeBg = chromeBg ?? AdjustBrightness(winBg.Value, isDark ? 0.04 : -0.04);
        Color resolvedBtnBg = AdjustBrightness(resolvedChromeBg, isDark ? 0.05 : -0.03);
        Color resolvedFg = winFg ?? (isDark ? Color.FromRgb(252, 252, 252) : Color.FromRgb(35, 38, 41));
        Color resolvedAccent = accent ?? portalAccent ?? (isDark ? Color.FromRgb(61, 174, 233) : Color.FromRgb(41, 128, 185));

        palette = new SystemColorPalette(
            sourceLabel,
            isDark,
            winBg.Value,
            resolvedViewBg,
            resolvedChromeBg,
            resolvedBtnBg,
            resolvedFg,
            resolvedAccent);
        return true;
    }

    private static void CollectGtkCssColorVariables(string cssPath, Dictionary<string, string> vars, int depth)
    {
        if (depth > 2 || !File.Exists(cssPath)) return;

        string content;
        try
        {
            var info = new FileInfo(cssPath);
            if (info.Length > 1024 * 1024) return; // Skip huge compiled bundles > 1 MB
            content = File.ReadAllText(cssPath);
        }
        catch
        {
            return;
        }

        // Follow local @import url("...");
        string? dir = Path.GetDirectoryName(cssPath);
        if (dir != null)
        {
            foreach (Match importMatch in Regex.Matches(content, @"@import\s+url\([""']?([^""')]+)[""']?\)", RegexOptions.IgnoreCase))
            {
                string rel = importMatch.Groups[1].Value.Trim();
                if (!rel.StartsWith("resource://", StringComparison.OrdinalIgnoreCase))
                {
                    string importedPath = Path.Combine(dir, rel);
                    CollectGtkCssColorVariables(importedPath, vars, depth + 1);
                }
            }
        }

        foreach (Match match in Regex.Matches(content, @"@define-color\s+([a-zA-Z0-9_-]+)\s+([^;]+);"))
        {
            string name = match.Groups[1].Value.Trim();
            string val = match.Groups[2].Value.Trim();
            vars[name] = val;
        }
    }

    private static Color? ResolveGtkColorVar(Dictionary<string, string> vars, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (TryResolveGtkValue(vars, key, 0, out Color c))
                return c;
        }
        return null;
    }

    private static bool TryResolveGtkValue(Dictionary<string, string> vars, string key, int depth, out Color color)
    {
        color = default;
        if (depth > 5 || !vars.TryGetValue(key, out string? raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        raw = raw.Trim();
        if (raw.StartsWith('@'))
        {
            string refKey = raw[1..].Trim();
            return TryResolveGtkValue(vars, refKey, depth + 1, out color);
        }

        return TryParseColorString(raw, out color);
    }

    private static bool TryDetectXResourcesPalette(Color? portalAccent, out SystemColorPalette palette)
    {
        palette = null!;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string xresPath = Path.Combine(home, ".Xresources");
        if (!File.Exists(xresPath))
            xresPath = Path.Combine(home, ".Xdefaults");

        if (!File.Exists(xresPath))
            return false;

        try
        {
            Color? bg = null;
            Color? fg = null;
            Color? accent = null;

            foreach (string rawLine in File.ReadLines(xresPath))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('!') || line.StartsWith('#'))
                    continue;

                int colon = line.IndexOf(':');
                if (colon <= 0) continue;

                string key = line[..colon].Trim().ToLowerInvariant();
                string val = line[(colon + 1)..].Trim();

                if ((key.EndsWith("*background") || key == "background") && TryParseColorString(val, out Color parsedBg))
                    bg = parsedBg;
                else if ((key.EndsWith("*foreground") || key == "foreground") && TryParseColorString(val, out Color parsedFg))
                    fg = parsedFg;
                else if ((key.EndsWith("*color4") || key.EndsWith("*color12")) && TryParseColorString(val, out Color parsedAcc))
                    accent ??= parsedAcc;
            }

            if (bg == null) return false;

            bool isDark = IsColorDark(bg.Value);
            Color winBg = bg.Value;
            Color viewBg = AdjustBrightness(winBg, isDark ? -0.03 : 0.04);
            Color chromeBg = AdjustBrightness(winBg, isDark ? 0.05 : -0.04);
            Color btnBg = AdjustBrightness(winBg, isDark ? 0.08 : -0.06);
            Color resolvedFg = fg ?? (isDark ? Color.FromRgb(252, 252, 252) : Color.FromRgb(35, 38, 41));
            Color resolvedAccent = accent ?? portalAccent ?? Color.FromRgb(61, 174, 233);

            palette = new SystemColorPalette("Xresources", isDark, winBg, viewBg, chromeBg, btnBg, resolvedFg, resolvedAccent);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectFallbackDarkPreference()
    {
        try
        {
            string kdeglobals = Path.Combine(XdgDirectories.ConfigHome, "kdeglobals");
            if (File.Exists(kdeglobals))
            {
                string text = File.ReadAllText(kdeglobals);
                if (text.Contains("dark", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }

        return true; // Default to modern dark palette if unknown
    }

    private static Dictionary<string, Dictionary<string, string>> ParseIniFile(string path)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        string currentSection = string.Empty;

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                if (!result.ContainsKey(currentSection))
                {
                    result[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq > 0)
            {
                string key = line[..eq].Trim();
                string val = line[(eq + 1)..].Trim();
                if (!result.TryGetValue(currentSection, out var sec))
                {
                    sec = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    result[currentSection] = sec;
                }
                sec[key] = val;
            }
        }

        return result;
    }

    private static Color? GetIniColor(Dictionary<string, Dictionary<string, string>> sections, string section, string key)
    {
        if (sections.TryGetValue(section, out var sec) &&
            sec.TryGetValue(key, out string? raw) &&
            TryParseColorString(raw, out Color c))
        {
            return c;
        }
        return null;
    }

    public static bool TryParseColorString(string raw, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        raw = raw.Trim();

        // 1. KDE R,G,B format (e.g. "32,35,38")
        string[] commaParts = raw.Split(',');
        if (commaParts.Length is 3 or 4 &&
            !raw.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) &&
            byte.TryParse(commaParts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte r) &&
            byte.TryParse(commaParts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte g) &&
            byte.TryParse(commaParts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte b))
        {
            color = Color.FromRgb(r, g, b);
            return true;
        }

        // 2. CSS rgb(r, g, b) or rgba(r, g, b, a)
        if (raw.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            int open = raw.IndexOf('(');
            int close = raw.IndexOf(')');
            if (open > 0 && close > open)
            {
                string[] parts = raw[(open + 1)..close].Split(',');
                if (parts.Length >= 3 &&
                    byte.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte cr) &&
                    byte.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte cg) &&
                    byte.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte cb))
                {
                    color = Color.FromRgb(cr, cg, cb);
                    return true;
                }
            }
        }

        // 3. Hex color (#RRGGBB, #AARRGGBB, #RGB)
        if (Color.TryParse(raw, out Color parsed))
        {
            color = Color.FromRgb(parsed.R, parsed.G, parsed.B);
            return true;
        }

        return false;
    }

    public static bool IsColorDark(Color c)
    {
        double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return luminance < 0.5;
    }

    public static Color AdjustBrightness(Color c, double delta)
    {
        int d = (int)Math.Round(delta * 255.0);
        byte r = (byte)Math.Clamp(c.R + d, 0, 255);
        byte g = (byte)Math.Clamp(c.G + d, 0, 255);
        byte b = (byte)Math.Clamp(c.B + d, 0, 255);
        return Color.FromRgb(r, g, b);
    }

    public static Color BlendColors(Color c1, Color c2, double ratio)
    {
        ratio = Math.Clamp(ratio, 0.0, 1.0);
        byte r = (byte)Math.Round(c1.R * (1.0 - ratio) + c2.R * ratio);
        byte g = (byte)Math.Round(c1.G * (1.0 - ratio) + c2.G * ratio);
        byte b = (byte)Math.Round(c1.B * (1.0 - ratio) + c2.B * ratio);
        return Color.FromRgb(r, g, b);
    }
}
