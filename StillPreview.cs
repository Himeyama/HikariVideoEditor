using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;

namespace VideoEditor;
public static class StillPreview
{
    public static async Task<List<IRandomAccessStream>> ReadFrames(EditProject project, double seconds)
    {
        List<Layer> layers = await ReadLayers(project, seconds);
        return layers.Select(layer => layer.Stream).ToList();
    }

    public sealed record Layer(EditClip Clip, IRandomAccessStream Stream);
    public static async Task<List<Layer>> ReadLayers(EditProject project, double seconds)
    {
        List<Layer> frames = new();
        try
        {
            foreach (EditClip clip in project.Clips.Where(c => c.Kind != "audio" && !project.Layer(c.Layer).Hidden && seconds >= c.Start && seconds < c.Start + c.Duration).OrderByDescending(c => c.Layer).ThenBy(c => c.Start))
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(SubtitleRenderer.SourcePath(clip, project.Video));
                if (clip.IsStill)
                    frames.Add(new Layer(clip, await file.OpenReadAsync()));
                else
                {
                    MediaClip media = await MediaClip.CreateFromFileAsync(file);
                    MediaComposition single = new();
                    single.Clips.Add(media);
                    double position = Math.Clamp(clip.Trim + seconds - clip.Start, 0, Math.Max(0, media.OriginalDuration.TotalSeconds - 1.0 / project.Video.FrameRate));
                    Windows.Graphics.Imaging.ImageStream stream = await single.GetThumbnailAsync(TimeSpan.FromSeconds(position), 960, 0, VideoFramePrecision.NearestFrame);
                    frames.Add(new Layer(clip, stream));
                }
            }

            return frames;
        }
        catch
        {
            foreach (Layer frame in frames)
                frame.Stream.Dispose();
            throw;
        }
    }
}



