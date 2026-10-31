namespace VideoEditor;
public static class ClipPlacement
{
    public static bool OverlapsExisting(IEnumerable<EditClip> candidates, IReadOnlyList<EditClip> existing)
    {
        foreach (EditClip candidate in candidates)
            foreach (EditClip clip in existing)
                if (clip.Layer == candidate.Layer && clip.Start < candidate.Start + candidate.Duration - 1e-8 &&
                    candidate.Start < clip.Start + clip.Duration - 1e-8) return true;
        return false;
    }

    public static bool HasOverlap(IEnumerable<EditClip> clips)
    {
        foreach (IGrouping<int, EditClip> layer in clips.GroupBy(clip => clip.Layer))
        {
            double end = -1;
            foreach (EditClip clip in layer.OrderBy(clip => clip.Start))
            {
                if (clip.Start < end - 1e-8) return true;
                end = Math.Max(end, clip.Start + clip.Duration);
            }
        }
        return false;
    }

    /// <summary>Keep the requested start time and use the first non-overlapping layer.</summary>
    public static int FirstAvailableLayer(IEnumerable<EditClip> clips, double start, double duration)
    {
        if (!double.IsFinite(start) || !double.IsFinite(duration) || start < 0 || duration <= 0 || start + duration > 86400)
            throw new ArgumentException("素材の配置時間が不正です。");
        List<EditClip> existing = clips.ToList();
        for (int layer = 1; layer <= Math.Min(2000, existing.Select(c => c.Layer).DefaultIfEmpty(0).Max() + 1); layer++)
        {
            bool overlaps = existing.Any(clip => clip.Layer == layer && clip.Start < start + duration - 1e-8 && start < clip.Start + clip.Duration - 1e-8);
            if (!overlaps)
                return layer;
        }

        throw new InvalidOperationException("レイヤー数の上限（2000）に達しました。");
    }
}


