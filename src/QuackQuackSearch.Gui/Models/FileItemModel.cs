namespace QuackQuackSearch.Gui.Models;

public sealed record FileItemModel(
    string Name,
    string DirectoryPath,
    string FullPath,
    long Size,
    DateTimeOffset ModifiedTime,
    bool IsDirectory,
    double Score
)
{
    public string FormattedSize => IsDirectory ? "<ORDNER>" : FormatBytes(Size);
    public string FormattedDate => ModifiedTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

    public string IconGlyph => IsDirectory ? "📁" : GetIconByExtension(Name);

    private static string FormatBytes(long bytes)
    {
        if (bytes == 0) return "0 B";
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int i = 0;
        double d = bytes;
        while (d >= 1024 && i < suffixes.Length - 1)
        {
            d /= 1024;
            i++;
        }
        return $"{d:0.##} {suffixes[i]}";
    }

    private static string GetIconByExtension(string name)
    {
        string ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".svg" or ".gif" or ".bmp" => "🖼️",
            ".mp4" or ".mkv" or ".avi" or ".webm" or ".mov" => "🎬",
            ".mp3" or ".flac" or ".wav" or ".ogg" or ".m4a" => "🎵",
            ".pdf" => "📕",
            ".zip" or ".tar" or ".gz" or ".bz2" or ".xz" or ".7z" or ".zst" => "📦",
            ".cs" or ".rs" or ".cpp" or ".c" or ".py" or ".js" or ".ts" or ".go" or ".java" or ".html" or ".css" or ".sh" => "💻",
            ".json" or ".xml" or ".yaml" or ".yml" or ".toml" or ".ini" or ".conf" => "⚙️",
            ".txt" or ".md" or ".log" or ".rtf" or ".doc" or ".docx" or ".odt" => "📄",
            _ => "📄"
        };
    }
}
