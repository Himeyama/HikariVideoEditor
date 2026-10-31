using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Playback;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private long _stillVersion;
    private long _stillEpoch;
    private bool _stillWorker;
    private bool NeedsStill => _scrubPointer != null || (!_playbackIntent && Preview.MediaPlayer.PlaybackSession.PlaybackState != MediaPlaybackState.Playing && !_resumePlayback);

    private void HideStill()
    {
        _stillVersion++;
        _stillEpoch++;
        PausedPreview.Visibility = Visibility.Collapsed;
        UpdateTransformOverlay();
    }

    private async void RequestStill()
    {
        _stillVersion++;
        if (_stillWorker || _closed || !NeedsStill)
            return;
        _stillWorker = true;
        try
        {
            while (!_closed && NeedsStill)
            {
                // Coalesce pointer events and discard late frames from earlier positions.
                await Task.Delay(32);
                long version = _stillVersion;
                long epoch = _stillEpoch;
                EditProject snapshot = _project.Copy();
                double position = Math.Min(_playhead, Math.Max(0, ProjectEnd - 1.0 / _project.Video.FrameRate));
                List<StillPreview.Layer> layers = await StillPreview.ReadLayers(snapshot, position);
                List<PreviewVisual> visuals = new();
                try
                {
                    foreach (StillPreview.Layer layer in layers)
                    {
                        BitmapImage bitmap = new();
                        await bitmap.SetSourceAsync(layer.Stream);
                        Image image = new()
                        {
                            Source = bitmap,
                            Stretch = Stretch.Fill
                        };
                        ClipBounds bounds = ClipGeometry.Fit(layer.Clip, bitmap.PixelWidth, bitmap.PixelHeight, snapshot.Video);
                        PlacePreviewImage(image, bounds);
                        visuals.Add(new PreviewVisual(layer.Clip, image, bounds, bitmap.PixelWidth, bitmap.PixelHeight));
                    }
                }
                finally
                {
                    foreach (StillPreview.Layer layer in layers)
                        layer.Stream.Dispose();
                }

                if (_closed || !NeedsStill)
                    break;
                if (_previewDrag != null)
                    break;
                if (epoch != _stillEpoch)
                    continue;
                PausedFrames.Children.Clear();
                _previewVisuals = visuals;
                foreach (PreviewVisual visual in visuals)
                    PausedFrames.Children.Add(visual.Image);
                PausedPreview.Visibility = Visibility.Visible;
                UpdateTransformOverlay();
                if (version == _stillVersion)
                    break;
            }
        }
        catch (Exception ex)
        {
            if (!_closed)
                Status.Text = $"停止フレームを表示できません: {ex.Message}";
        }
        finally
        {
            _stillWorker = false;
        }
    }
}

