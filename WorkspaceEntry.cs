namespace VideoEditor;
public sealed record WorkspaceEntry(string FullPath, bool IsDirectory)
{
    public string Name => Path.GetFileName(Path.TrimEndingDirectorySeparator(FullPath));
    public string Icon => IsDirectory ? "\uE8B7" : MediaFiles.Kind(FullPath) == "audio" ? "\uE8D6" : "\uE714";

    public static List<WorkspaceEntry> ReadDirectory(string directory, bool foldersOnly = false)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true
        };
        var folders = Directory.EnumerateDirectories(directory, "*", options).Select(path => new WorkspaceEntry(path, true));
        var files = foldersOnly ? Enumerable.Empty<WorkspaceEntry>() : Directory.EnumerateFiles(directory, "*", options).Where(MediaFiles.IsSupported).Select(path => new WorkspaceEntry(path, false));
        return folders.Concat(files).OrderByDescending(entry => entry.IsDirectory).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

public static class MediaFiles
{
    public static readonly string[] Extensions = [".mp4", ".avi", ".mov", ".wmv", ".png", ".jpg", ".jpeg", ".bmp", ".wav", ".mp3", ".m4a", ".aac"];
    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public static string Kind(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".bmp" => "image",
        ".wav" or ".mp3" or ".m4a" or ".aac" => "audio",
        _ => "video"
    };
    public static string OpenLabel(string kind) => kind switch
    {
        "image" => "画像を開く",
        "audio" => "音声を開く",
        _ => "動画を開く"
    };
}

