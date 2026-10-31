using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VideoEditor;

public static class SubtitleRenderer
{
    public static string SourcePath(EditClip clip, VideoSettings video)
    {
        if (clip.Kind != "subtitle") return clip.Path;
        clip.Validate(video.FrameRate);
        video.Validate();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            clip.Text, clip.Subtitle, video.Width, video.Height, clip.Scale, clip.ScaleX, clip.ScaleY, RendererVersion = 4
        }))));
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HikariVideoEditor", "subtitles");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, key + ".png");
        SubtitleStyle style = clip.Subtitle;
        using FontFamily family = new(style.FontFamily);
        using StringFormat format = new()
        {
            Alignment = style.Horizontal switch { "left" => StringAlignment.Near, "right" => StringAlignment.Far, _ => StringAlignment.Center },
            LineAlignment = style.Vertical switch { "top" => StringAlignment.Near, "bottom" => StringAlignment.Far, _ => StringAlignment.Center },
            Trimming = StringTrimming.None
        };
        float marginX = (float)Math.Min(style.MarginX, (video.Width - 16) / 2.0);
        float marginY = (float)Math.Min(style.MarginY, (video.Height - 16) / 2.0);
        // Glyph strokes may overlap; winding fill preserves their union and glyph counters.
        using GraphicsPath outline = new(FillMode.Winding);
        outline.AddString(clip.Text.ReplaceLineEndings("\n"), family, (int)FontStyle.Regular, (float)style.FontSize,
            new RectangleF(marginX, marginY, video.Width - marginX * 2, video.Height - marginY * 2), format);
        using Pen pen = new(Color.Black, Math.Max(2, (float)style.FontSize / 14)) { LineJoin = LineJoin.Round };
        RectangleF ink = outline.GetBounds(null, pen);
        ink.Inflate(2, 2);
        Rectangle crop = Rectangle.Intersect(Rectangle.FromLTRB((int)Math.Floor(ink.Left), (int)Math.Floor(ink.Top),
            (int)Math.Ceiling(ink.Right), (int)Math.Ceiling(ink.Bottom)), new Rectangle(0, 0, video.Width, video.Height));
        if (crop.Width <= 0 || crop.Height <= 0) crop = new Rectangle(0, 0, 1, 1);
        clip.SubtitleBounds = new ClipBounds(crop.X, crop.Y, crop.Width, crop.Height);
        if (File.Exists(path)) return path;
        // Rasterize glyph outlines at the final display scale instead of enlarging a small PNG.
        double rasterX = Math.Max(2, clip.Scale * clip.ScaleX);
        double rasterY = Math.Max(2, clip.Scale * clip.ScaleY);
        double limit = Math.Min(1, Math.Min(8192 / (crop.Width * rasterX), 8192 / (crop.Height * rasterY)));
        limit = Math.Min(limit, Math.Sqrt(16_000_000 / (crop.Width * rasterX * crop.Height * rasterY)));
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(crop.Width * rasterX * limit));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(crop.Height * rasterY * limit));
        using Bitmap bitmap = new(pixelWidth, pixelHeight, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using Matrix transform = new((float)pixelWidth / crop.Width, 0, 0, (float)pixelHeight / crop.Height,
            -(float)crop.X * pixelWidth / crop.Width, -(float)crop.Y * pixelHeight / crop.Height);
        graphics.Transform = transform;
        graphics.DrawPath(pen, outline);
        graphics.FillPath(Brushes.White, outline);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            bitmap.Save(temporary, ImageFormat.Png);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return path;
    }
}


