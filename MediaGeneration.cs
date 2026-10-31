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
        var invalid = Path.GetInvalidFileNameChars();
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
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
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
            var voices = new List<Voice>();
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

    public sealed record ImageOptions(string Title, string Text = "", string Footer = "", int Width = 1920, int Height = 1080,
        string Background = "#101B30", string TextColor = "#FFFFFF", string Accent = "#57E3C0", string FontFamily = "Yu Gothic", double FontSize = 64);

    private static Color ParseColor(string value)
    {
        if (value.Length != 7 || value[0] != '#' || !int.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            throw new ArgumentException("色は #RRGGBB 形式で指定してください。");
        return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
    }

    public static Task<string> Image(string path, ImageOptions options)
    {
        ValidateOutput(path, ".png");
        if (string.IsNullOrWhiteSpace(options.Title) || options.Title.Length > 200 || options.Text.Length > 4000 || options.Footer.Length > 300)
            throw new ArgumentException("見出しは1〜200文字、本文は4000文字以内、フッターは300文字以内です。");
        if (options.Width is < 320 or > 3840 || options.Height is < 240 or > 2160 || (long)options.Width * options.Height > 8_294_400)
            throw new ArgumentException("画像の幅は320〜3840、高さは240〜2160で指定してください。");
        if (!double.IsFinite(options.FontSize) || options.FontSize is < 12 or > 240) throw new ArgumentException("文字サイズは12〜240です。");
        var background = ParseColor(options.Background); var foreground = ParseColor(options.TextColor); var accent = ParseColor(options.Accent);
        return Task.Run(() =>
        {
            using var family = new FontFamily(options.FontFamily);
            using var bitmap = new Bitmap(options.Width, options.Height);
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(background);
            using var ink = new SolidBrush(foreground); using var highlight = new SolidBrush(accent);
            float w = options.Width, h = options.Height, margin = w * .06f;
            g.FillRectangle(highlight, margin, h * .1f, w * .06f, Math.Max(3, h * .008f));
            DrawText(options.Title, new RectangleF(margin, h * .19f, w - margin * 2, h * .24f), (float)options.FontSize, FontStyle.Bold, ink);
            DrawText(options.Text, new RectangleF(margin, h * .47f, w - margin * 2, h * .32f), (float)options.FontSize * .62f, FontStyle.Regular, ink);
            DrawText(options.Footer, new RectangleF(margin, h * .87f, w - margin * 2, h * .08f), (float)options.FontSize * .36f, FontStyle.Regular, highlight);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { bitmap.Save(temporary, ImageFormat.Png); File.Move(temporary, path); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return path;

            void DrawText(string text, RectangleF bounds, float size, FontStyle style, Brush brush)
            {
                if (text.Length == 0) return;
                using var format = new StringFormat { Trimming = StringTrimming.None };
                for (float current = size; current >= 10; current -= 1)
                {
                    using var font = new Font(family, current, style, GraphicsUnit.Pixel);
                    var measured = g.MeasureString(text, font, new SizeF(bounds.Width, 1_000_000), format, out int fitted, out _);
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
