namespace QuackQuackSearch.Core.System;

/// <summary>
/// Provides standardized, spec-compliant access to XDG Base Directory specification paths.
/// Honors XDG_CONFIG_HOME, XDG_DATA_HOME, XDG_CACHE_HOME, XDG_STATE_HOME, and XDG_RUNTIME_DIR.
/// </summary>
public static class XdgDirectories
{
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string ConfigHome
    {
        get
        {
            string? env = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            return !string.IsNullOrWhiteSpace(env) ? env : Path.Combine(Home, ".config");
        }
    }

    public static string DataHome
    {
        get
        {
            string? env = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            return !string.IsNullOrWhiteSpace(env) ? env : Path.Combine(Home, ".local", "share");
        }
    }

    public static string CacheHome
    {
        get
        {
            string? env = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            return !string.IsNullOrWhiteSpace(env) ? env : Path.Combine(Home, ".cache");
        }
    }

    public static string StateHome
    {
        get
        {
            string? env = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            return !string.IsNullOrWhiteSpace(env) ? env : Path.Combine(Home, ".local", "state");
        }
    }

    public static string RuntimeDir
    {
        get
        {
            string? env = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            return !string.IsNullOrWhiteSpace(env) ? env : Path.GetTempPath();
        }
    }

    /// <summary>
    /// $XDG_CONFIG_HOME/quackquacksearch
    /// </summary>
    public static string AppConfigDir => Path.Combine(ConfigHome, "quackquacksearch");

    /// <summary>
    /// $XDG_CACHE_HOME/quackquacksearch
    /// </summary>
    public static string AppCacheDir => Path.Combine(CacheHome, "quackquacksearch");

    /// <summary>
    /// $XDG_DATA_HOME/quackquacksearch
    /// </summary>
    public static string AppDataDir => Path.Combine(DataHome, "quackquacksearch");

    /// <summary>
    /// $XDG_DATA_HOME/krunner/dbusplugins
    /// </summary>
    public static string KRunnerPluginsDir => Path.Combine(DataHome, "krunner", "dbusplugins");

    /// <summary>
    /// $XDG_DATA_HOME/applications
    /// </summary>
    public static string DesktopApplicationsDir => Path.Combine(DataHome, "applications");

    /// <summary>
    /// $XDG_CONFIG_HOME/systemd/user
    /// </summary>
    public static string SystemdUserDir => Path.Combine(ConfigHome, "systemd", "user");

    /// <summary>
    /// Returns a path within $XDG_RUNTIME_DIR (or temp fallback).
    /// </summary>
    public static string GetRuntimeSocketPath(string filename) => Path.Combine(RuntimeDir, filename);
}
