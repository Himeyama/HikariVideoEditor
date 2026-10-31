using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace VideoEditor;

// SAPI and GDI+ run locally; no shell, network service or additional runtime is required.
public static class MediaGeneration
{
    public sealed record Voice(string Id, string Name, string Language);

    public static string ContentPath(string directory, string content, string extension)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string name = new(content.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        if (name.Length > 80) name = name[..(char.IsHighSurrogate(name[79]) ? 79 : 80)];
        name = name.TrimEnd(' ', '.');
        if (name.Length == 0) name = "無題";
        string stem = name.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3])))
            name = "_" + name;
        string path = Path.Combine(directory, name + extension);
        for (int index = 2; File.Exists(path) || Directory.Exists(path); index++)
            path = Path.Combine(directory, $"{name} ({index}){extension}");
        return path;
    }

    private static Task<T> OnSpeechThread<T>(Func<T> work)
    {
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try { completion.SetResult(work()); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "Hikari speech synthesis" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static dynamic Com(string name) => Activator.CreateInstance(Type.GetTypeFromProgID(name)
        ?? throw new InvalidOperationException("Windowsの音声合成機能が見つかりません。"))!;
    private static void Release(object? value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    public static Task<Voice[]> Voices() => OnSpeechThread(() =>
    {
        dynamic? speaker = null, tokens = null;
        try
        {
            speaker = Com("SAPI.SpVoice"); tokens = speaker.GetVoices();
            List<Voice> voices = new();
            for (int i = 0; i < tokens.Count; i++)
            {
                dynamic token = tokens.Item(i);
                try
                {
                    string language = token.GetAttribute("Language");
                    try { language = CultureInfo.GetCultureInfo(int.Parse(language.Split(';')[0], NumberStyles.HexNumber)).Name; }
                    catch (Exception ex) when (ex is FormatException or CultureNotFoundException) { }
                    voices.Add(new Voice((string)token.Id, (string)token.GetDescription(), language));
                }
                finally { Release(token); }
            }
            return voices.ToArray();
        }
        finally { Release(tokens); Release(speaker); }
    });

    public static Task<string> Speech(string path, string text, string? voice = null, int rate = 0, int volume = 100)
    {
        ValidateOutput(path, ".wav");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 10000) throw new ArgumentException("原稿は1〜10000文字で入力してください。");
        if (rate is < -10 or > 10 || volume is < 0 or > 100) throw new ArgumentException("速度は-10〜10、音量は0〜100です。");
        return OnSpeechThread(() =>
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            dynamic? speaker = null, stream = null, tokens = null, chosen = null;
            try
            {
                speaker = Com("SAPI.SpVoice");
                if (!string.IsNullOrWhiteSpace(voice))
                {
                    tokens = speaker.GetVoices();
                    for (int i = 0; i < tokens.Count; i++)
                    {
                        dynamic token = tokens.Item(i);
                        if (string.Equals((string)token.Id, voice, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals((string)token.GetDescription(), voice, StringComparison.OrdinalIgnoreCase))
                        { chosen = token; break; }
                        Release(token);
                    }
                    if (chosen == null) throw new ArgumentException("指定された音声がありません。speech_voicesで音声一覧を確認してください。");
                    speaker.Voice = chosen;
                }
                speaker.Rate = rate; speaker.Volume = volume;
                stream = Com("SAPI.SpFileStream");
                stream.Open(temporary, 3, false);
                speaker.AudioOutputStream = stream;
                speaker.Speak(text, 16); // SVSFIsNotXML: the script is always plain text.
                stream.Close();
                File.Move(temporary, path);
                return path;
            }
            finally
            {
                Release(stream); Release(chosen); Release(tokens); Release(speaker);
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        });
    }

    // Text/Footer remain aliases for existing callers; the four roles are title/subtitle/description/caption.
    public sealed record ImageOptions(string Title = "", string Text = "", string Footer = "", int Width = 1920, int Height = 1080,
        string Background = "#101B30", string TextColor = "#FFFFFF", string Accent = "#57E3C0", string FontFamily = "Yu Gothic", double FontSize = 64,
        string Template = "basic", string Subtitle = "");

    public sealed record ImageTemplate(string Id, string Name, string Background, string TextColor, string Accent);
    public static IReadOnlyList<ImageTemplate> ImageTemplates { get; } = new ImageTemplate[]
    {
        new("basic", "シンプル（従来）", "#101B30", "#FFFFFF", "#57E3C0"),
        new("clean", "すっきり解説", "#FAF5E9", "#102B4B", "#AD8736"),
        new("science", "サイエンス", "#061426", "#F4FAFF", "#55DDF5"),
        new("chalkboard", "黒板スタイル", "#144734", "#FFFBEA", "#F3D65C"),
        new("gentle", "やさしい雑学", "#F6F0FC", "#50318A", "#A483E5")
    };

    public static async Task<byte[]> ImagePreview(ImageOptions options)
    {
        string path = Path.Combine(Path.GetTempPath(), "Hikari-preview-" + Guid.NewGuid().ToString("N") + ".png");
        try { await Image(path, options); return await File.ReadAllBytesAsync(path); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static Color ParseColor(string value)
    {
        if (value.Length != 7 || value[0] != '#' || !int.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            throw new ArgumentException("色は #RRGGBB 形式で指定してください。");
        return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
    }

    public static Task<string> Image(string path, ImageOptions options)
    {
        ValidateOutput(path, ".png");
        if (options.Title.Length > 200 || options.Subtitle.Length > 300 || options.Text.Length > 4000 || options.Footer.Length > 300)
            throw new ArgumentException("タイトルは200文字以内、副タイトルは300文字以内、説明は4000文字以内、字幕は300文字以内です。すべて省略できます。");
        if (options.Width is < 320 or > 3840 || options.Height is < 240 or > 2160 || (long)options.Width * options.Height > 8_294_400)
            throw new ArgumentException("画像の幅は320〜3840、高さは240〜2160で指定してください。");
        if (!double.IsFinite(options.FontSize) || options.FontSize is < 12 or > 240) throw new ArgumentException("文字サイズは12〜240です。");
        if (!ImageTemplates.Any(t => t.Id == options.Template)) throw new ArgumentException("テンプレートがありません。");
        Color background = ParseColor(options.Background); Color foreground = ParseColor(options.TextColor); Color accent = ParseColor(options.Accent);
        return Task.Run(() =>
        {
            using FontFamily family = new(options.FontFamily);
            using Bitmap bitmap = new(options.Width, options.Height);
            using Graphics g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(background);
            if (options.Template != "basic")
            {
                using Stream resource = typeof(MediaGeneration).Assembly.GetManifestResourceStream($"VideoEditor.ImageTemplates.{options.Template}.png")
                    ?? throw new InvalidOperationException("テンプレートの背景画像が見つかりません。");
                using Image plate = System.Drawing.Image.FromStream(resource);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(plate, new Rectangle(0, 0, options.Width, options.Height));
            }
            // Layout and font sizes are authored independently for each theme at 1920 x 1080.
            float scale = (float)(options.FontSize / 64 * options.Width / 1920.0);
            switch (options.Template)
            {
                case "clean":
                    Draw(options.Title, .10f, .020f, .80f, .085f, 84, true, foreground, true);
                    Draw(options.Subtitle, .025f, .135f, .315f, .050f, 42, true, Color.White, true);
                    Draw(options.Text, .08f, .27f, .84f, .49f, 48, false, foreground);
                    Draw(options.Footer, .08f, .85f, .84f, .085f, 58, true, Color.White, true);
                    break;
                case "science":
                    Draw(options.Title, .35f, .025f, .43f, .085f, 94, true, foreground, true);
                    Draw(options.Subtitle, .035f, .027f, .26f, .075f, 46, true, accent);
                    Draw(options.Text, .095f, .35f, .81f, .38f, 46, false, foreground);
                    Draw(options.Footer, .085f, .855f, .83f, .08f, 56, true, foreground, true);
                    break;
                case "chalkboard":
                    Draw(options.Title, .17f, .045f, .66f, .095f, 100, true, foreground, true);
                    Draw(options.Subtitle, .12f, .24f, .76f, .08f, 52, true, accent, true);
                    Draw(options.Text, .13f, .37f, .74f, .32f, 50, false, foreground);
                    Draw(options.Footer, .12f, .855f, .76f, .08f, 64, true, Color.FromArgb(16, 62, 38), true);
                    break;
                case "gentle":
                    Draw(options.Title, .24f, .015f, .52f, .075f, 82, true, foreground, true);
                    Draw(options.Subtitle, .30f, .110f, .40f, .055f, 46, true, Color.White, true);
                    Draw(options.Text, .12f, .26f, .76f, .44f, 46, false, Color.FromArgb(58, 43, 84));
                    Draw(options.Footer, .11f, .825f, .78f, .075f, 58, true, foreground, true);
                    break;
                default:
                    using (SolidBrush highlight = new(accent))
                        g.FillRectangle(highlight, options.Width * .06f, options.Height * .10f, options.Width * .06f, Math.Max(3, options.Height * .008f));
                    Draw(options.Title, .06f, .19f, .88f, .20f, 64, true, foreground);
                    Draw(options.Subtitle, .06f, .40f, .88f, .07f, 44, true, accent);
                    Draw(options.Text, .06f, .50f, .88f, .29f, 40, false, foreground);
                    Draw(options.Footer, .06f, .87f, .88f, .08f, 24, false, accent);
                    break;
            }
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { bitmap.Save(temporary, ImageFormat.Png); File.Move(temporary, path); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return path;

            void Draw(string text, float x, float y, float width, float height, float size, bool bold, Color color, bool center = false)
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                RectangleF bounds = new(x * options.Width, y * options.Height, width * options.Width, height * options.Height);
                using SolidBrush brush = new(color);
                using StringFormat format = new() { Trimming = StringTrimming.None,
                    Alignment = center ? StringAlignment.Center : StringAlignment.Near, LineAlignment = bold ? StringAlignment.Center : StringAlignment.Near };
                float start = size * scale, minimum = Math.Min(start, 10);
                for (float current = start; current >= minimum; current -= 1)
                {
                    using Font font = new(family, current, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
                    SizeF measured = g.MeasureString(text, font, new SizeF(bounds.Width, 1_000_000), format, out int fitted, out _);
                    if (fitted < text.Length || measured.Height > bounds.Height || measured.Width > bounds.Width) continue;
                    g.DrawString(text, font, brush, bounds, format); return;
                }
                throw new ArgumentException("文字が画像に収まりません。文章を短くするか画像を大きくしてください。");
            }
        });
    }

    private static void ValidateOutput(string path, string extension)
    {
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"出力先は{extension}の絶対パスで指定してください。");
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("出力先が既に存在します。新しいファイル名を指定してください。");
        if (!Directory.Exists(Path.GetDirectoryName(path))) throw new DirectoryNotFoundException("出力先のフォルダーがありません。");
    }
}
