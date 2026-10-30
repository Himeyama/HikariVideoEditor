using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;

namespace VideoEditor;
public static class StillPreview
{
    public static async Task<List<IRandomAccessStream>> ReadFrames(EditProject project, double seconds)
    {
        var layers = await ReadLayers(project, seconds);
        return layers.Select(layer => layer.Stream).ToList();
    }

    public sealed record Layer(EditClip Clip, IRandomAccessStream Stream);
    public static async Task<List<Layer>> ReadLayers(EditProject project, double seconds)
    {
        var frames = new List<Layer>();
        try
        {
            foreach (var clip in project.Clips.Where(c => c.Kind != "audio" && seconds >= c.Start && seconds < c.Start + c.Duration).OrderByDescending(c => c.Layer).ThenBy(c => c.Start))
            {
                var file = await StorageFile.GetFileFromPathAsync(SubtitleRenderer.SourcePath(clip, project.Video));
                if (clip.IsStill)
                    frames.Add(new Layer(clip, await file.OpenReadAsync()));
                else
                {
                    var media = await MediaClip.CreateFromFileAsync(file);
                    var single = new MediaComposition();
                    single.Clips.Add(media);
                    var position = Math.Clamp(clip.Trim + seconds - clip.Start, 0, Math.Max(0, media.OriginalDuration.TotalSeconds - 1.0 / project.Video.FrameRate));
                    var stream = await single.GetThumbnailAsync(TimeSpan.FromSeconds(position), 960, 0, VideoFramePrecision.NearestFrame);
                    frames.Add(new Layer(clip, stream));
                }
            }

            return frames;
        }
        catch
        {
            foreach (var frame in frames)
                frame.Stream.Dispose();
            throw;
        }
    }
}



