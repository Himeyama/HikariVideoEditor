namespace VideoEditor;

public static class TimelineOperations
{
    public static EditProject Delete(EditProject project, IReadOnlySet<string> ids, bool ripple)
    {
        var removed = project.Clips.Where(c => ids.Contains(c.Id)).ToList();
        project.RequireEditable(removed.Select(c => c.Layer).ToArray());
        var next = project.Copy();
        next.Clips.RemoveAll(c => ids.Contains(c.Id));
        if (!ripple) return next;
        foreach (var group in removed.GroupBy(c => c.Layer))
        {
            var ranges = Merge(group.Select(c => (c.Start, c.Start + c.Duration)));
            foreach (var clip in next.Clips.Where(c => c.Layer == group.Key))
            {
                if (ranges.Any(r => clip.Start < r.End - 1e-8 && clip.Start + clip.Duration > r.Start + 1e-8))
                    throw new ArgumentException("削除範囲に未選択の素材が重なっています。重なった素材も選択してください。");
                clip.Start -= ranges.Where(r => r.End <= clip.Start + 1e-8).Sum(r => r.End - r.Start);
            }
        }
        return next;
    }

    public static EditProject CloseGaps(EditProject project)
    {
        var occupied = Merge(project.Clips.Select(c => (c.Start, c.Start + c.Duration)));
        var gaps = new List<(double Start, double End)>();
        double end = 0;
        foreach (var range in occupied)
        {
            if (range.Start > end + 1e-8) gaps.Add((end, range.Start));
            end = range.End;
        }
        var next = project.Copy();
        foreach (var clip in next.Clips)
        {
            double shift = gaps.Where(g => g.End <= clip.Start + 1e-8).Sum(g => g.End - g.Start);
            if (shift == 0) continue;
            project.RequireEditable(clip.Layer);
            clip.Start -= shift;
        }
        return next;
    }

    private static List<(double Start, double End)> Merge(IEnumerable<(double Start, double End)> source)
    {
        var ranges = new List<(double Start, double End)>();
        foreach (var range in source.OrderBy(r => r.Start))
            if (ranges.Count > 0 && range.Start <= ranges[^1].End + 1e-8)
                ranges[^1] = (ranges[^1].Start, Math.Max(ranges[^1].End, range.End));
            else ranges.Add(range);
        return ranges;
    }
}
