namespace QuackQuackSearch.Daemon.DBus;

public static class KRunnerPluginInstaller
{
    public static string GetPluginDesktopPath()
    {
        return Path.Combine(QuackQuackSearch.Core.System.XdgDirectories.KRunnerPluginsDir, "quackquacksearch.desktop");
    }

    public static void InstallDesktopFile()
    {
        string filePath = GetPluginDesktopPath();
        string dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);

        const string content = """
[Desktop Entry]
Name=QuackQuackSearch
Comment=Lightning fast file search via QuackQuackSearch
X-KDE-ServiceTypes=Plasma/Runner
Type=Service
Icon=system-search
X-KDE-PluginInfo-Name=quackquacksearch
X-KDE-PluginInfo-Version=1.0
X-KDE-PluginInfo-License=GPL-3.0
X-KDE-PluginInfo-EnabledByDefault=true
X-Plasma-API=DBus
X-Plasma-DBusRunner-Service=org.quackquacksearch.Daemon
X-Plasma-DBusRunner-Path=/quackquacksearch
X-Plasma-Request-Actions-Once=true
X-Plasma-Runner-Min-Letter-Count=2
X-Plasma-Runner-Unique-Results=true
X-Plasma-Runner-Weak-Results=true
""";

        File.WriteAllText(filePath, content);
        Console.WriteLine($"[KRunner] Plugin registered at {filePath}");
    }
}
