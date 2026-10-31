namespace VideoEditor;
public static class ProjectDisplay
{
    public static string Position(double seconds, double duration, int frameRate)
    {
        TimeSpan time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        long totalFrames = (long)Math.Ceiling(duration * frameRate);
        long currentFrame = totalFrames == 0 ? 0 : Math.Clamp((long)Math.Floor(seconds * frameRate) + 1, 1, totalFrames);
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}  {currentFrame} / {totalFrames}";
    }

    public static string Settings(EditProject project)
    {
        string workspaceName = string.IsNullOrWhiteSpace(project.WorkspaceDirectory) ? "未選択" : Path.GetFileName(Path.TrimEndingDirectorySeparator(project.WorkspaceDirectory));
        if (string.IsNullOrEmpty(workspaceName))
            workspaceName = project.WorkspaceDirectory!;
        VideoSettings video = project.Video;
        return $"{workspaceName}  {video.Width}x{video.Height}  {video.FrameRate}fps  {video.AudioSampleRate / 1000.0:0.0}kHz";
    }
}

