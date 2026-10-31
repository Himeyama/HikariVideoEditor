namespace VideoEditor;

public static class TimelineOperations
{
    public static EditProject Delete(EditProject project, IReadOnlySet<string> ids, bool ripple)
    {
        List<EditClip> removed = project.Clips.Where(c => ids.Contains(c.Id)).ToList();
        project.RequireEditable(removed.Select(c => c.Layer).ToArray());
        EditProject next = project.Copy();
        next.Clips.RemoveAll(c => ids.Contains(c.Id));
        if (!ripple) return next;
        foreach (IGrouping<int, EditClip> group in removed.GroupBy(c => c.Layer))
        {
            List<(double Start, double End)> ranges = Merge(group.Select(c => (c.Start, c.Start + c.Duration)));
            foreach (EditClip clip in next.Clips.Where(c => c.Layer == group.Key))
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
        List<(double Start, double End)> occupied = Merge(project.Clips.Select(c => (c.Start, c.Start + c.Duration)));
        List<(double Start, double End)> gaps = new();
        double end = 0;
        foreach ((double Start, double End) range in occupied)
        {
            if (range.Start > end + 1e-8) gaps.Add((end, range.Start));
            end = range.End;
        }
        EditProject next = project.Copy();
        foreach (EditClip clip in next.Clips)
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
        List<(double Start, double End)> ranges = new();
        foreach ((double Start, double End) range in source.OrderBy(r => r.Start))
            if (ranges.Count > 0 && range.Start <= ranges[^1].End + 1e-8)
                ranges[^1] = (ranges[^1].Start, Math.Max(ranges[^1].End, range.End));
            else ranges.Add(range);
        return ranges;
    }
}
