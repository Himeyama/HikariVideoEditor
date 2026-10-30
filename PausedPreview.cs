using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Playback;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private long stillVersion;
    private long stillEpoch;
    private bool stillWorker;
    private bool NeedsStill => scrubPointer != null || (!playbackIntent && Preview.MediaPlayer.PlaybackSession.PlaybackState != MediaPlaybackState.Playing && !resumePlayback);

    private void HideStill()
    {
        stillVersion++;
        stillEpoch++;
        PausedPreview.Visibility = Visibility.Collapsed;
        UpdateTransformOverlay();
    }

    private async void RequestStill()
    {
        stillVersion++;
        if (stillWorker || closed || !NeedsStill)
            return;
        stillWorker = true;
        try
        {
            while (!closed && NeedsStill)
            {
                // Coalesce pointer events and discard late frames from earlier positions.
                await Task.Delay(32);
                var version = stillVersion;
                var epoch = stillEpoch;
                var snapshot = project.Copy();
                var position = Math.Min(playhead, Math.Max(0, ProjectEnd - 1.0 / project.Video.FrameRate));
                var layers = await StillPreview.ReadLayers(snapshot, position);
                var visuals = new List<PreviewVisual>();
                try
                {
                    foreach (var layer in layers)
                    {
                        var bitmap = new BitmapImage();
                        await bitmap.SetSourceAsync(layer.Stream);
                        var image = new Image
                        {
                            Source = bitmap,
                            Stretch = Stretch.Fill
                        };
                        var bounds = ClipGeometry.Fit(layer.Clip, bitmap.PixelWidth, bitmap.PixelHeight, snapshot.Video);
                        PlacePreviewImage(image, bounds);
                        visuals.Add(new PreviewVisual(layer.Clip, image, bounds, bitmap.PixelWidth, bitmap.PixelHeight));
                    }
                }
                finally
                {
                    foreach (var layer in layers)
                        layer.Stream.Dispose();
                }

                if (closed || !NeedsStill)
                    break;
                if (previewDrag != null)
                    break;
                if (epoch != stillEpoch)
                    continue;
                PausedFrames.Children.Clear();
                previewVisuals = visuals;
                foreach (var visual in visuals)
                    PausedFrames.Children.Add(visual.Image);
                PausedPreview.Visibility = Visibility.Visible;
                UpdateTransformOverlay();
                if (version == stillVersion)
                    break;
            }
        }
        catch (Exception ex)
        {
            if (!closed)
                Status.Text = $"停止フレームを表示できません: {ex.Message}";
        }
        finally
        {
            stillWorker = false;
        }
    }
}

