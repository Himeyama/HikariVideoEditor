using System.Drawing;
using VideoEditor;

void Check(bool value, string name) { if (!value) throw new Exception(name); }
async Task Reject(Func<Task> action, string name)
{
    try { await action(); } catch (Exception ex) when (ex is ArgumentException or IOException) { return; }
    throw new Exception("Expected rejection: " + name);
}
string directory = Path.Combine(Path.GetTempPath(), "Hikari-generation-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    Check(Path.GetFileName(MediaGeneration.ContentPath(directory, "おはようございます", ".wav")) == "おはようございます.wav", "script filename");
    Check(Path.GetFileName(MediaGeneration.ContentPath(directory, "見出し", ".png")) == "見出し.png", "image filename");
    Check(Path.GetFileName(MediaGeneration.ContentPath(directory, "a/b: c\n", ".wav")) == "a_b_ c.wav", "invalid filename characters");
    Check(Path.GetFileName(MediaGeneration.ContentPath(directory, "CON", ".png")) == "_CON.png", "reserved filename");
    File.WriteAllText(Path.Combine(directory, "重複.wav"), "keep");
    Check(Path.GetFileName(MediaGeneration.ContentPath(directory, "重複", ".wav")) == "重複 (2).wav", "duplicate filename");
    Check(MediaGeneration.ContentPath(directory, new string('あ', 10000), ".wav").Length < directory.Length + 100, "long filename");
    if (args.Contains("--filenames-only"))
    {
        Console.WriteLine("PASS: content filenames, invalid characters, reserved names, duplicates and long content.");
        return;
    }
    foreach (var template in MediaGeneration.ImageTemplates)
    {
        var options = new MediaGeneration.ImageOptions("空はなぜ青い？", "太陽の光は、さまざまな色の光が集まったものです。\n\nその中でも青い光は、大気中で強く散乱するため、空全体が青く見えます。", "太陽の光が、大気で散乱するからだよ",
            Background: template.Background, TextColor: template.TextColor, Accent: template.Accent, Template: template.Id, Subtitle: "空が青く見える理由");
        string rendered = Path.Combine(directory, template.Id + ".png");
        await MediaGeneration.Image(rendered, options);
        var preview = await MediaGeneration.ImagePreview(options);
        Check(File.ReadAllBytes(rendered).SequenceEqual(preview), "preview matches output: " + template.Id);
        var blank = options with { Title = "", Subtitle = "", Text = "", Footer = "" };
        var backgroundOnly = await MediaGeneration.ImagePreview(blank);
        var whitespaceOnly = await MediaGeneration.ImagePreview(blank with { Title = " ", Subtitle = "\t", Text = "\n", Footer = " " });
        Check(backgroundOnly.SequenceEqual(whitespaceOnly), "blank fields draw no text: " + template.Id);
        foreach (var single in new[] { blank with { Title = "タイトル" }, blank with { Subtitle = "副タイトル" },
            blank with { Text = "説明" }, blank with { Footer = "字幕" } })
            Check(!backgroundOnly.SequenceEqual(await MediaGeneration.ImagePreview(single)), "each optional field draws independently: " + template.Id);
        using var bitmap = new Bitmap(rendered);
        Check(bitmap.Width == 1920 && bitmap.Height == 1080, "template dimensions");
        if (args.Contains("--templates-only"))
        {
            string previews = Path.GetFullPath(Path.Combine("artifacts", "image-template-previews"));
            Directory.CreateDirectory(previews);
            File.Copy(rendered, Path.Combine(previews, template.Id + ".png"), true);
            File.WriteAllBytes(Path.Combine(previews, template.Id + "-background.png"), backgroundOnly);
        }
    }
    var gothicPreview = await MediaGeneration.ImagePreview(new("フォントの確認", FontFamily: "Yu Gothic"));
    var serifPreview = await MediaGeneration.ImagePreview(new("フォントの確認", FontFamily: "Yu Mincho"));
    Check(!gothicPreview.SequenceEqual(serifPreview), "selected font changes rendered text");
    await Reject(() => MediaGeneration.Image(Path.Combine(directory, "unknown.png"), new("test", Template: "unknown")), "unknown template");
    if (args.Contains("--templates-only"))
    {
        Console.WriteLine("PASS: all template PNGs, exact preview/output match, four optional fields, font selection, dimensions and unknown template rejection.");
        return;
    }
    var voices = await MediaGeneration.Voices();
    Check(voices.Length > 0 && voices.All(v => v.Id.Length > 0 && v.Name.Length > 0), "installed voices");
    var voice = voices.FirstOrDefault(v => v.Language == "ja-JP") ?? voices[0];
    string wav = Path.Combine(directory, "speech.wav"), png = Path.Combine(directory, "card.png");
    await MediaGeneration.Speech(wav, "こんにちは。音声のテストです。", voice.Id);
    var bytes = File.ReadAllBytes(wav);
    Check(bytes.Length > 44 && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE", "valid WAV");
    await MediaGeneration.Image(png, new("基本のセキュリティ", "多要素認証を有効にしましょう。", "今日からできること"));
    using (var image = new Bitmap(png))
    {
        Check(image.Width == 1920 && image.Height == 1080, "PNG dimensions");
        Check(image.GetPixel(0, 0).ToArgb() == Color.FromArgb(255, 16, 27, 48).ToArgb(), "background color");
        Check(image.GetPixel(120, 110).G > 150, "accent rectangle");
    }
    await Reject(() => MediaGeneration.Speech(wav, "overwrite"), "no WAV overwrite");
    Check(File.ReadAllBytes(wav).SequenceEqual(bytes), "existing WAV unchanged");
    await Reject(() => MediaGeneration.Image(png, new("overwrite")), "no PNG overwrite");
    await Reject(() => MediaGeneration.Speech(Path.Combine(directory, "empty.wav"), " "), "empty script");
    await Reject(() => MediaGeneration.Speech(Path.Combine(directory, "rate.wav"), "test", rate: 11), "rate limit");
    await Reject(() => MediaGeneration.Speech(Path.Combine(directory, "badvoice.wav"), "test", "nonexistent-voice"), "unknown voice");
    await Reject(() => MediaGeneration.Image(Path.Combine(directory, "color.png"), new("test", Background: "bad")), "invalid color");
    await Reject(() => MediaGeneration.Image(Path.Combine(directory, "size.png"), new("test", Width: 100000)), "image size limit");
    await Reject(() => MediaGeneration.Image(Path.Combine(directory, "overflow.png"), new("test", new string('あ', 4000), Width: 320, Height: 240)), "overflow text");
    Check(!Directory.GetFiles(directory, "*.tmp").Any(), "temporary files cleaned");
    Console.WriteLine("PASS: installed voices, Japanese WAV, PNG layout, overwrite protection, invalid arguments, overflow and cleanup.");
}
finally { Directory.Delete(directory, true); }
