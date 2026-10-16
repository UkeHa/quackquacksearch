namespace QuackQuackSearch.Daemon.DBus;

public static class KRunnerPluginInstaller
{
    public static string GetPluginDesktopPath()
    {
        return Path.Combine(QuackQuackSearch.Core.System.XdgDirectories.KRunnerPluginsDir, "quackquacksearch.desktop");
    }

    public static void InstallDesktopFile(int minCharCount = 2)
    {
        string filePath = GetPluginDesktopPath();
        string dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);

        int minLetters = Math.Max(1, minCharCount);
        string content = $"""
[Desktop Entry]
Name=QuackQuackSearch
Comment=Everything-like ultra fast file search via QuackQuackSearch
X-KDE-ServiceTypes=Plasma/Runner
Type=Service
Icon=system-search
X-KDE-PluginInfo-Name=quackquacksearch
X-KDE-PluginInfo-Version=1.0
X-KDE-PluginInfo-License=GPL-3.0
X-KDE-PluginInfo-EnabledByDefault=true
X-Plasma-API=DBus2
X-Plasma-DBusRunner-Service=org.quackquacksearch.Daemon
X-Plasma-DBusRunner-Path=/quackquacksearch
X-Plasma-Request-Actions-Once=true
X-Plasma-Runner-Min-Letter-Count={minLetters}
X-Plasma-Runner-Match-Regex=^.*$
X-Plasma-Runner-Syntaxes=:q:
X-Plasma-Runner-Syntax-Descriptions=Fast file search matching query
X-Plasma-Runner-Unique-Results=true
X-Plasma-Runner-Weak-Results=true
""";

        File.WriteAllText(filePath, content);
        Console.WriteLine($"[KRunner] Plugin registered at {filePath}");
    }
}
