using Windows.Media.Editing;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace VideoEditor;

public static class VideoExport
{
    public static async Task Write(EditProject project, string path, bool overwrite = false,
        uint? videoBitrate = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var snapshot = project.Copy();
        var full = Path.GetFullPath(path);
        if (snapshot.Clips.Count == 0)
            throw new ArgumentException("素材を追加してください。");
        if (!string.Equals(Path.GetExtension(full), ".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("出力先には .mp4 を指定してください。");
        if (snapshot.Clips.Any(c => c.Kind != "subtitle" && string.Equals(Path.GetFullPath(c.Path), full, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("素材への上書きはできません。");
        if (!overwrite && File.Exists(full))
            throw new ArgumentException("出力先は既に存在します。別のファイル名を指定してください。");
        cancellationToken.ThrowIfCancellationRequested();
        var render = await CompositionBuilder.Build(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        var profile = CompositionBuilder.CreateEncodingProfile(snapshot);
        if (videoBitrate.HasValue)
            profile.Video.Bitrate = videoBitrate.Value;
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(full)!);
        var temporary = await folder.CreateFileAsync($".hikari-export-{Guid.NewGuid():N}.mp4", CreationCollisionOption.FailIfExists);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = render.RenderToFileAsync(temporary, MediaTrimmingPreference.Precise, profile);
            operation.Progress = (_, value) => progress?.Report(value);
            var result = await operation.AsTask(cancellationToken);
            if (result != TranscodeFailureReason.None)
                throw new InvalidOperationException($"動画の書き出しに失敗しました: {result}");
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary.Path, full, overwrite);
        }
        finally
        {
            if (File.Exists(temporary.Path))
                File.Delete(temporary.Path);
        }
    }
}

