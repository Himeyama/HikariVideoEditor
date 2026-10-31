namespace VideoEditor;
public static class ClipPlacement
{
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


