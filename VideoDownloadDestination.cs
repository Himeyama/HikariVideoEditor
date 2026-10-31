namespace VideoEditor;

public static class VideoDownloadDestination
{
    public static string FileName(string title)
    {
        string name = new string(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (name.Length > 100) name = name[..100].TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(name)) name = "Video";
        string stem = name.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase)) name = "_" + name;
        return name;
    }

    public static string InWorkspace(string directory, string title)
    {
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory)) throw new ArgumentException("保存先のワークスペースが見つかりません。フォルダーを選択し直してください。");
        string name = FileName(title);
        string path = Path.Combine(directory, name + ".mp4");
        for (int index = 2; File.Exists(path) || Directory.Exists(path); index++)
            path = Path.Combine(directory, $"{name} ({index}).mp4");
        return path;
    }
}
