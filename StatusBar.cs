using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private void UpdateStatusBar()
    {
        TimelinePositionStatus.Text = ProjectDisplay.Position(playhead, ProjectEnd, project.Video.FrameRate);
        var directory = project.WorkspaceDirectory;
        WorkspaceStatus.Text = string.IsNullOrWhiteSpace(directory) ? "未選択" : Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        if (string.IsNullOrEmpty(WorkspaceStatus.Text)) WorkspaceStatus.Text = directory;
        ResolutionStatus.Text = $"{project.Video.Width}x{project.Video.Height}";
        FrameRateStatus.Text = $"{project.Video.FrameRate}fps";
        SampleRateStatus.Text = $"{project.Video.AudioSampleRate / 1000.0:0.0}kHz";
    }

    private void UpdateVideoCanvas()
    {
        VideoCanvas.Width = project.Video.Width;
        VideoCanvas.Height = project.Video.Height;
        PausedFrames.Width = project.Video.Width;
        PausedFrames.Height = project.Video.Height;
        VideoCanvas.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, project.Video.Width, project.Video.Height)
        };
        UpdateStatusBar();
        UpdateTransformOverlay();
    }
}

