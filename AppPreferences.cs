using System.Text.Json;

namespace VideoEditor;

public sealed record VoicevoxFavorite(string SpeakerUuid, int StyleId, string Character, string Style)
{
    public string Name => $"VOICEVOX: {Character}（{Style}）";
    public string VoiceId => $"voicevox:{SpeakerUuid}:{StyleId}";
}

public sealed record SubtitleStyle
{
    public string FontFamily { get; set; } = "Noto Sans JP";
    public double FontSize { get; set; } = 64;
    public string Horizontal { get; set; } = "center";
    public string Vertical { get; set; } = "bottom";
    public double MarginX { get; set; } = 80;
    public double MarginY { get; set; } = 80;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(FontFamily) || FontFamily.Length > 200 ||
            !double.IsFinite(FontSize) || FontSize is < 8 or > 400 ||
            Horizontal is not ("left" or "center" or "right") || Vertical is not ("top" or "center" or "bottom") ||
            !double.IsFinite(MarginX) || !double.IsFinite(MarginY) || MarginX is < 0 or > 4000 || MarginY is < 0 or > 4000)
            throw new ArgumentException("字幕のフォント・サイズ・位置が不正です。");
    }
}

public sealed class AppPreferences
{
    public int PreferencesVersion { get; set; } = 2;
    public bool AutoSaveEnabled { get; set; }
    public double Scale { get; set; } = 1;
    public SubtitleStyle Subtitle { get; set; } = new();
    public List<string> RecentWorkspaces { get; set; } = new();
    public List<VoicevoxFavorite> VoicevoxFavorites { get; set; } = new();
    public string? LastSpeechVoice { get; set; }
    public int LastSpeechRate { get; set; }
    public int LastSpeechVolume { get; set; } = 100;

    public void RememberWorkspace(string directory)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        RecentWorkspaces = (RecentWorkspaces ?? new())
            .Prepend(fullPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20).ToList();
    }
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HikariVideoEditor", "settings.json");

    public void Validate()
    {
        if (!double.IsFinite(Scale) || Scale is < .375 or > 2)
            throw new ArgumentException("アプリの倍率は37.5〜200%で指定してください。");
        Subtitle.Validate();
        if (LastSpeechRate is < -10 or > 10 || LastSpeechVolume is < 0 or > 100)
            throw new ArgumentException("音声の速度は-10〜10、音量は0〜100です。");
    }

    public static AppPreferences Load(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppPreferences>(json) ?? new();
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.TryGetProperty("PreferencesVersion", out _) && settings.Subtitle?.FontFamily == "Yu Gothic UI")
                settings.Subtitle.FontFamily = "Noto Sans JP";
            settings.Validate();
            return settings;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException)
        {
            return new();
        }
    }

    public void Save(string path)
    {
        Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}

public sealed record SubtitleOverrides
{
    public string? FontFamily { get; set; }
    public double? FontSize { get; set; }
    public string? Horizontal { get; set; }
    public string? Vertical { get; set; }
    public double? MarginX { get; set; }
    public double? MarginY { get; set; }

    public SubtitleStyle Resolve(SubtitleStyle defaults) => new()
    {
        FontFamily = FontFamily ?? defaults.FontFamily, FontSize = FontSize ?? defaults.FontSize,
        Horizontal = Horizontal ?? defaults.Horizontal, Vertical = Vertical ?? defaults.Vertical,
        MarginX = MarginX ?? defaults.MarginX, MarginY = MarginY ?? defaults.MarginY
    };

    public static SubtitleOverrides Explicit(SubtitleStyle style) => new()
    {
        FontFamily = style.FontFamily, FontSize = style.FontSize,
        Horizontal = style.Horizontal, Vertical = style.Vertical, MarginX = style.MarginX, MarginY = style.MarginY
    };
}



