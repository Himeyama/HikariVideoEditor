namespace VideoEditor;
/// <summary>Coordinates are pixels in the project canvas, independent of monitor DPI.</summary>
public readonly record struct ClipBounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
}

public static class ClipGeometry
{
    public static ClipBounds Fit(EditClip clip, double sourceWidth, double sourceHeight, VideoSettings video)
    {
        if (clip.Kind == "subtitle" && clip.SubtitleBounds is { } text)
        {
            double textWidth = text.Width * clip.Scale * clip.ScaleX;
            double textHeight = text.Height * clip.Scale * clip.ScaleY;
            return new ClipBounds(text.X + (text.Width - textWidth) / 2 + clip.OffsetX,
                text.Y + (text.Height - textHeight) / 2 + clip.OffsetY, textWidth, textHeight);
        }
        double fit = Math.Min(video.Width / sourceWidth, video.Height / sourceHeight);
        double width = sourceWidth * fit * clip.Scale * clip.ScaleX;
        double height = sourceHeight * fit * clip.Scale * clip.ScaleY;
        return new ClipBounds((video.Width - width) / 2 + clip.OffsetX, (video.Height - height) / 2 + clip.OffsetY, width, height);
    }

    public static EditClip Move(EditClip original, double deltaX, double deltaY)
    {
        EditClip moved = ClipEditing.Clone(original);
        moved.OffsetX += deltaX;
        moved.OffsetY += deltaY;
        return moved;
    }

    public static EditClip SnapToCanvas(EditClip candidate, double sourceWidth, double sourceHeight, VideoSettings video, double tolerance)
    {
        ClipBounds bounds = Fit(candidate, sourceWidth, sourceHeight, video);
        EditClip snapped = ClipEditing.Clone(candidate);
        double left = -bounds.X;
        double right = video.Width - bounds.Right;
        double top = -bounds.Y;
        double bottom = video.Height - bounds.Bottom;
        double horizontal = Math.Abs(left) <= Math.Abs(right) ? left : right;
        double vertical = Math.Abs(top) <= Math.Abs(bottom) ? top : bottom;
        if (Math.Abs(horizontal) <= tolerance)
            snapped.OffsetX += horizontal;
        if (Math.Abs(vertical) <= tolerance)
            snapped.OffsetY += vertical;
        return snapped;
    }

    /// <summary>Resize uniformly while keeping the opposite corner fixed.</summary>
    public static EditClip Resize(EditClip original, ClipBounds bounds, double pointerX, double pointerY, bool left, bool top)
    {
        double anchorX = left ? bounds.Right : bounds.X;
        double anchorY = top ? bounds.Bottom : bounds.Y;
        double vectorX = left ? -bounds.Width : bounds.Width;
        double vectorY = top ? -bounds.Height : bounds.Height;
        double factor = ((pointerX - anchorX) * vectorX + (pointerY - anchorY) * vectorY) / (vectorX * vectorX + vectorY * vectorY);
        factor = Math.Clamp(factor, 0.01 / original.Scale, 100 / original.Scale);
        double width = bounds.Width * factor;
        double height = bounds.Height * factor;
        double newX = left ? anchorX - width : anchorX;
        double newY = top ? anchorY - height : anchorY;
        EditClip resized = ClipEditing.Clone(original);
        resized.Scale *= factor;
        resized.OffsetX += newX + width / 2 - (bounds.X + bounds.Width / 2);
        resized.OffsetY += newY + height / 2 - (bounds.Y + bounds.Height / 2);
        return resized;
    }

    public static EditClip ResizeEdge(EditClip original, ClipBounds bounds, double deltaX, double deltaY, int horizontal, int vertical)
    {
        EditClip resized = ClipEditing.Clone(original);
        if (horizontal != 0)
        {
            double factor = Math.Clamp((bounds.Width + horizontal * deltaX) / bounds.Width, .01 / original.ScaleX, 100 / original.ScaleX);
            resized.ScaleX *= factor;
            resized.OffsetX += horizontal * (bounds.Width * factor - bounds.Width) / 2;
        }
        if (vertical != 0)
        {
            double factor = Math.Clamp((bounds.Height + vertical * deltaY) / bounds.Height, .01 / original.ScaleY, 100 / original.ScaleY);
            resized.ScaleY *= factor;
            resized.OffsetY += vertical * (bounds.Height * factor - bounds.Height) / 2;
        }
        return resized;
    }
}
