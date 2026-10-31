using Windows.Media.Editing;
using Windows.Storage;
using Windows.UI;
using Windows.Foundation;
using Windows.Media.MediaProperties;

namespace VideoEditor;
public static class CompositionBuilder
{
    public static async Task<MediaComposition> Build(EditProject project)
    {
        project.Validate();
        var composition = new MediaComposition();
        var end = project.Clips.Count == 0 ? 1 : Math.Max(1, project.Clips.Max(c => c.Start + c.Duration));
        composition.Clips.Add(MediaClip.CreateFromColor(Color.FromArgb(255, 0, 0, 0), TimeSpan.FromSeconds(end)));
        foreach (var layer in project.Clips.Where(c => c.Kind != "audio" && !project.Layer(c.Layer).Hidden).GroupBy(c => c.Layer).OrderByDescending(g => g.Key))
        {
            var overlayLayer = new MediaOverlayLayer();
            foreach (var item in layer.OrderBy(c => c.Start))
            {
                var file = await StorageFile.GetFileFromPathAsync(SubtitleRenderer.SourcePath(item, project.Video));
                var clip = item.IsStill ? await MediaClip.CreateFromImageFileAsync(file, TimeSpan.FromSeconds(item.Duration)) : await MediaClip.CreateFromFileAsync(file);
                if (!item.IsStill)
                {
                    if (item.Trim + item.Duration > clip.OriginalDuration.TotalSeconds + .02)
                        throw new ArgumentException($"素材の長さを超えています: {item}");
                    clip.TrimTimeFromStart = TimeSpan.FromSeconds(item.Trim);
                    clip.TrimTimeFromEnd = clip.OriginalDuration - TimeSpan.FromSeconds(item.Trim + item.Duration);
                }

                clip.Volume = project.Layer(item.Layer).Muted || item.FadeIn > 0 || item.FadeOut > 0 ? 0 : item.Volume;
                double width, height;
                if (item.IsStill)
                {
                    var image = await file.Properties.GetImagePropertiesAsync();
                    width = image.Width;
                    height = image.Height;
                }
                else
                {
                    var video = clip.GetVideoEncodingProperties();
                    width = video.Width;
                    height = video.Height;
                }

                if (width <= 0 || height <= 0)
                    throw new ArgumentException($"素材の画面サイズが不正です: {item}");
                var bounds = ClipGeometry.Fit(item, width, height, project.Video);
                // A MediaOverlay defaults to a zero-sized rectangle, which renders black.
                overlayLayer.Overlays.Add(new MediaOverlay(clip) {
                    Delay = TimeSpan.FromSeconds(item.Start),
                    Position = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                    Opacity = 1,
                    AudioEnabled = true
                });
            }

            composition.OverlayLayers.Add(overlayLayer);
        }

        foreach (var item in project.Clips.Where(c => (c.Kind == "audio" || c.Kind == "video" && (c.FadeIn > 0 || c.FadeOut > 0)) && !project.Layer(c.Layer).Muted && !project.Layer(c.Layer).Hidden))
        {
            bool faded = item.FadeIn > 0 || item.FadeOut > 0;
            var audio = await BackgroundAudioTrack.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(faded ? await AudioAnalysis.Faded(item) : item.Path));
            double trim = faded ? 0 : item.Trim;
            if (trim + item.Duration > audio.OriginalDuration.TotalSeconds + .02)
                throw new ArgumentException($"音声の長さを超えています: {item}");
            audio.TrimTimeFromStart = TimeSpan.FromSeconds(trim);
            audio.TrimTimeFromEnd = TimeSpan.FromSeconds(Math.Max(0, audio.OriginalDuration.TotalSeconds - trim - item.Duration));
            audio.Delay = TimeSpan.FromSeconds(item.Start);
            audio.Volume = item.Volume;
            composition.BackgroundAudioTracks.Add(audio);
        }

        return composition;
    }

    public static MediaEncodingProfile CreateEncodingProfile(EditProject project)
    {
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Video.Width = (uint)project.Video.Width;
        profile.Video.Height = (uint)project.Video.Height;
        profile.Video.FrameRate.Numerator = (uint)project.Video.FrameRate;
        profile.Video.FrameRate.Denominator = 1;
        profile.Audio.SampleRate = (uint)project.Video.AudioSampleRate;
        return profile;
    }
}


