namespace VideoEditor;
// All edit math uses seconds, independent of physical pixels and display scaling.
public static class ClipEditing
{
    public static int FrameRate { get; set; } = 30;
    public static double MinimumDuration => 1.0 / FrameRate;
    public static double Snap(double seconds) => Math.Round(seconds * FrameRate) / FrameRate;
    public static EditClip Move(EditClip original, double delta, int layerDelta)
    {
        EditClip next = Clone(original);
        next.Start = Math.Clamp(Snap(original.Start + delta), 0, 86400 - original.Duration);
        next.Layer = Math.Clamp(original.Layer + layerDelta, 1, 2000);
        return next;
    }

    public static EditClip Resize(EditClip original, double delta, bool left, double sourceDuration)
    {
        EditClip next = Clone(original);
        if (left)
        {
            double lower = original.IsStill ? -original.Start : -Math.Min(original.Start, original.Trim);
            delta = Math.Clamp(Snap(delta), lower, original.Duration - MinimumDuration);
            next.Start += delta;
            next.Duration -= delta;
            if (!original.IsStill)
                next.Trim += delta;
        }
        else
        {
            double maximum = Math.Min(86400 - original.Start, original.IsStill ? 86400 : sourceDuration - original.Trim);
            next.Duration = Math.Clamp(Snap(original.Duration + delta), MinimumDuration, maximum);
        }

        return next;
    }

    public static (EditClip Left, EditClip Right) Split(EditClip original, double seconds)
    {
        if (!double.IsFinite(seconds))
            throw new ArgumentException("分割位置が不正です。");
        double offset = Snap(seconds - original.Start);
        if (offset < MinimumDuration - 1e-8 || original.Duration - offset < MinimumDuration - 1e-8)
            throw new ArgumentException("クリップの内側に再生位置を移動してください。");
        EditClip left = Clone(original);
        EditClip right = Clone(original);
        left.Duration = offset;
        right.Id = Guid.NewGuid().ToString("N");
        right.Start = original.Start + offset;
        right.Duration = original.Duration - offset;
        if (!original.IsStill)
            right.Trim = original.Trim + offset;
        return (left, right);
    }

    public static EditClip SnapToEdges(EditClip candidate, IEnumerable<EditClip> clips, int edge, double tolerance, double sourceDuration)
    {
        EditClip result = candidate;
        double best = tolerance + 1e-9;
        IEnumerable<double> targets = clips.Where(c => c.Id != candidate.Id).SelectMany(c => new[] { c.Start, c.Start + c.Duration }).Append(0);
        foreach (double target in targets)
            foreach (int movingEdge in edge == 0 ? new[]
            {
                -1,
                1
            }

            : new[]
            {
                edge
            }

            )
            {
                double difference = target - (movingEdge == -1 ? candidate.Start : candidate.Start + candidate.Duration);
                if (Math.Abs(difference) >= best)
                    continue;
                EditClip next = Clone(candidate);
                if (edge == 0)
                    next.Start += difference;
                else if (edge == -1)
                {
                    next.Start += difference;
                    next.Duration -= difference;
                    if (!next.IsStill)
                        next.Trim += difference;
                }
                else
                    next.Duration += difference;
                if (next.Start < 0 || next.Trim < 0 || next.Duration < MinimumDuration - 1e-8 || next.Start + next.Duration > 86400 || (edge != 0 && !next.IsStill && next.Trim + next.Duration > sourceDuration + 1e-8))
                    continue;
                best = Math.Abs(difference);
                result = next;
            }

        return result;
    }

    public static EditClip Duplicate(EditClip original)
    {
        EditClip duplicate = Clone(original);
        duplicate.Id = Guid.NewGuid().ToString("N");
        duplicate.Start = original.Start + original.Duration;
        if (duplicate.Start + duplicate.Duration > 86400)
            throw new ArgumentException("複製先がプロジェクトの最大時間を超えます。");
        return duplicate;
    }

    public static List<EditClip> MoveGroup(IReadOnlyCollection<EditClip> originals, double delta, int layerDelta)
    {
        if (originals.Count == 0)
            return[];
        double movement = Math.Clamp(Snap(delta), -originals.Min(clip => clip.Start), 86400 - originals.Max(clip => clip.Start + clip.Duration));
        int layers = Math.Clamp(layerDelta, 1 - originals.Min(clip => clip.Layer), 2000 - originals.Max(clip => clip.Layer));
        return originals.Select(clip =>
        {
            EditClip moved = Clone(clip);
            moved.Start += movement;
            moved.Layer += layers;
            return moved;
        }).ToList();
    }

    public static EditClip Clone(EditClip clip) => new()
    {
        Id = clip.Id,
        Path = clip.Path,
        Kind = clip.Kind,
        Start = clip.Start,
        Trim = clip.Trim,
        Duration = clip.Duration,
        Layer = clip.Layer,
        Volume = clip.Volume,
        FadeIn = clip.FadeIn,
        FadeOut = clip.FadeOut,
        OffsetX = clip.OffsetX,
        OffsetY = clip.OffsetY,
        Text = clip.Text,
        SubtitleOverrides = clip.SubtitleOverrides is { } overrides ? overrides with { } : null,
        Subtitle = clip.Subtitle with { },
        ScaleX = clip.ScaleX,
        ScaleY = clip.ScaleY,
        SubtitleBounds = clip.SubtitleBounds,
        Scale = clip.Scale
    };
}





