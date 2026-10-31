using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private void UpdateStatusBar()
    {
        TimelinePositionStatus.Text = ProjectDisplay.Position(_playhead, ProjectEnd, _project.Video.FrameRate);
        string? directory = _project.WorkspaceDirectory;
        WorkspaceStatus.Text = string.IsNullOrWhiteSpace(directory) ? "未選択" : Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        if (string.IsNullOrEmpty(WorkspaceStatus.Text)) WorkspaceStatus.Text = directory;
        ResolutionStatus.Text = $"{_project.Video.Width}x{_project.Video.Height}";
        FrameRateStatus.Text = $"{_project.Video.FrameRate}fps";
        SampleRateStatus.Text = $"{_project.Video.AudioSampleRate / 1000.0:0.0}kHz";
    }

    private void UpdateVideoCanvas()
    {
        VideoCanvas.Width = _project.Video.Width;
        VideoCanvas.Height = _project.Video.Height;
        PausedFrames.Width = _project.Video.Width;
        PausedFrames.Height = _project.Video.Height;
        VideoCanvas.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, _project.Video.Width, _project.Video.Height)
        };
        UpdateStatusBar();
        UpdateTransformOverlay();
    }
}

