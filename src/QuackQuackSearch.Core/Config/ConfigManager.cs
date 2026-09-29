using System.Text.Json;

namespace QuackQuackSearch.Core.Config;

public static class ConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string GetConfigDirectory() => System.XdgDirectories.AppConfigDir;

    public static string GetCacheDirectory() => System.XdgDirectories.AppCacheDir;

    public static string GetConfigFilePath() => Path.Combine(GetConfigDirectory(), "config.json");
    public static string GetCacheIndexFilePath() => Path.Combine(GetCacheDirectory(), "index.cache");

    public static QuackConfig LoadOrCreateDefault()
    {
        string filePath = GetConfigFilePath();
        if (File.Exists(filePath))
        {
            try
            {
                string json = File.ReadAllText(filePath);
                var config = JsonSerializer.Deserialize<QuackConfig>(json, JsonOptions);
                if (config != null) return config;
            }
            catch (Exception)
            {
                // Fallback to default if corrupted
            }
        }

        var defaultConfig = CreateDefault();
        Save(defaultConfig);
        return defaultConfig;
    }

    public static void Save(QuackConfig config)
    {
        string dir = GetConfigDirectory();
        Directory.CreateDirectory(dir);

        string filePath = GetConfigFilePath();
        string json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public static QuackConfig CreateDefault()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = new QuackConfig();

        // Default path: user's home directory (if it exists)
        if (Directory.Exists(home))
        {
            config.Paths.Add(new PathConfigEntry
            {
                Path = home,
                Type = "local",
                Enabled = true
            });
        }

        return config;
    }
}
