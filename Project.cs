using System.Text.Json;

namespace VideoEditor;
public sealed class EditProject
{
    public int Version { get; set; } = 1;
    public string? WorkspaceDirectory { get; set; }
    public VideoSettings Video { get; set; } = new();
    public List<EditClip> Clips { get; set; } = [];
    public Dictionary<int, LayerSettings> Layers { get; set; } = [];
    public LayerSettings Layer(int number) => Layers.TryGetValue(number, out LayerSettings? settings) ? settings : new();
    public void RequireEditable(params int[] layers)
    {
        if (layers.Any(number => Layer(number).Locked))
            throw new ArgumentException("ロックされたレイヤーは編集できません。先にロックを解除してください。");
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public int LayerCount => Math.Min(2000, Math.Max(8, Math.Max(Clips.Select(c => c.Layer + 1).DefaultIfEmpty(8).Max(), Layers.Keys.DefaultIfEmpty(1).Max())));

    public EditProject Copy() => JsonSerializer.Deserialize<EditProject>(JsonSerializer.Serialize(this))!;
    public void Validate()
    {
        if (Version != 1 || Clips.Count > 2000)
            throw new ArgumentException("未対応のプロジェクトです。");
        Video.Validate();
        if (Layers.Any(pair => pair.Key is < 1 or > 2000 || pair.Value == null))
            throw new ArgumentException("レイヤー設定が不正です。");
        if (Clips.Select(c => c.Id).Distinct().Count() != Clips.Count)
            throw new ArgumentException("クリップIDが重複しています。");
        foreach (EditClip c in Clips)
            c.Validate(Video.FrameRate);
    }
}

public sealed class EditClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "video";
    public double Start { get; set; }
    public double Trim { get; set; }
    public double Duration { get; set; } = 5;
    public int Layer { get; set; } = 1;
    public double Volume { get; set; } = 1;
    public double FadeIn { get; set; }
    public double FadeOut { get; set; }
    public double AudioGain(double localSeconds) => Math.Clamp(Math.Min(
        FadeIn > 0 ? localSeconds / FadeIn : 1,
        FadeOut > 0 ? (Duration - localSeconds) / FadeOut : 1), 0, 1);
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double Scale { get; set; } = 1;
    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;
    [System.Text.Json.Serialization.JsonIgnore]
    public ClipBounds? SubtitleBounds { get; set; }
    public string Text { get; set; } = "";
    public SubtitleOverrides? SubtitleOverrides { get; set; }
    public SubtitleStyle Subtitle { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsStill => Kind is "image" or "subtitle";

    public override string ToString() => Kind == "subtitle" ? Text.ReplaceLineEndings(" ") : System.IO.Path.GetFileName(Path);
    public void Validate(int? frameRate = null)
    {
        if (!double.IsFinite(FadeIn) || !double.IsFinite(FadeOut) || FadeIn < 0 || FadeOut < 0 || FadeIn > 86400 || FadeOut > 86400)
            throw new ArgumentException("音声フェードの秒数が不正です。");
        if (!double.IsFinite(OffsetX) || !double.IsFinite(OffsetY) || !double.IsFinite(Scale) || Scale is < 0.01 or > 100 || !double.IsFinite(ScaleX) || !double.IsFinite(ScaleY) || ScaleX is < 0.01 or > 100 || ScaleY is < 0.01 or > 100 || Math.Abs(OffsetX) > 1000000 || Math.Abs(OffsetY) > 1000000)
            throw new ArgumentException("素材の位置・拡大率が不正です。");
        if (Kind == "subtitle")
        {
            if (string.IsNullOrWhiteSpace(Text) || Text.Length > 10000)
                throw new ArgumentException("字幕は1〜10000文字で入力してください。");
            Subtitle.Validate();
        }
        else if (!System.IO.Path.IsPathFullyQualified(Path) || !File.Exists(Path))
            throw new ArgumentException($"素材が見つかりません: {Path}");
        if (Kind is not ("video" or "image" or "audio" or "subtitle"))
            throw new ArgumentException("未対応の素材種別です。");
        if (!double.IsFinite(Start) || !double.IsFinite(Trim) || !double.IsFinite(Duration) || !double.IsFinite(Volume) || Start < 0 || Trim < 0 || Duration < 1.0 / (frameRate ?? ClipEditing.FrameRate) - 1e-8 || Start + Duration > 86400 || Layer is < 1 or > 2000 || Volume is < 0 or > 1)
            throw new ArgumentException("クリップの時間・レイヤー・音量が不正です。");
    }
}

public sealed class LayerSettings
{
    public bool Locked { get; set; }
    public bool Hidden { get; set; }
    public bool Muted { get; set; }
}

public sealed class VideoSettings
{
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int FrameRate { get; set; } = 30;
    public int AudioSampleRate { get; set; } = 44100;

    public void Validate()
    {
        if (Width is < 16 or > 7680 || Height is < 16 or > 4320 || Width % 2 != 0 || Height % 2 != 0 || FrameRate is not (24 or 25 or 30 or 50 or 60) || AudioSampleRate is not (44100 or 48000))
            throw new ArgumentException("動画の設定が不正です。解像度は偶数、フレームレートは24・25・30・50・60fps、音声は44.1・48kHzで指定してください。");
    }
}


