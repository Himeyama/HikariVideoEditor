using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace VideoEditor;

public static class VideoDownloadWriter
{
    public static async Task WriteAsync(VideoDownloadTransfer transfer, DownloadVideo video, DownloadStream format,
        string destination, IProgress<DownloadProgress>? progress, CancellationToken token, bool overwrite = true)
    {
        string full = Path.GetFullPath(destination);
        if (!string.Equals(Path.GetExtension(full), ".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("保存先には .mp4 を指定してください。");
        string folder = Path.GetDirectoryName(full)!;
        string temporary = Path.Combine(folder, $".hikari-download-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            string videoPath = Path.Combine(temporary, "video.mp4");
            await transfer.SaveStreamAsync(format, videoPath, "映像をダウンロード中", progress, token);
            videoPath = IdentifyContainer(videoPath, format.IsHls);
            string result = videoPath;
            if (!format.HasAudio || format.IsHls)
            {
                string? audioPath = null;
                if (!format.HasAudio)
                {
                    DownloadStream audio = format.Audio ?? video.Audio ?? throw new InvalidOperationException("音声の配信情報がありません。");
                    audioPath = Path.Combine(temporary, "audio.m4a");
                    await transfer.SaveStreamAsync(audio, audioPath, "音声をダウンロード中", progress, token);
                    audioPath = IdentifyContainer(audioPath, audio.IsHls);
                }
                token.ThrowIfCancellationRequested();
                progress?.Report(new DownloadProgress("映像と音声をまとめています…", null));
                MediaComposition composition = new();
                MediaClip clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(videoPath));
                composition.Clips.Add(clip);
                if (audioPath != null)
                    composition.BackgroundAudioTracks.Add(await BackgroundAudioTrack.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(audioPath)));
                MediaEncodingProfile profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
                VideoEncodingProperties properties = clip.GetVideoEncodingProperties();
                profile.Video.Width = properties.Width;
                profile.Video.Height = properties.Height;
                profile.Video.FrameRate.Numerator = properties.FrameRate.Numerator;
                profile.Video.FrameRate.Denominator = properties.FrameRate.Denominator;
                profile.Video.Bitrate = (uint)Math.Clamp(format.Bitrate, 5_000_000, 50_000_000);
                StorageFolder storage = await StorageFolder.GetFolderFromPathAsync(temporary);
                StorageFile output = await storage.CreateFileAsync("result.mp4", CreationCollisionOption.FailIfExists);
                Windows.Foundation.IAsyncOperationWithProgress<TranscodeFailureReason, double> operation = composition.RenderToFileAsync(output, MediaTrimmingPreference.Precise, profile);
                operation.Progress = (_, value) => progress?.Report(new DownloadProgress("映像と音声をまとめています", value));
                TranscodeFailureReason failure = await operation.AsTask(token);
                if (failure != TranscodeFailureReason.None)
                    throw new InvalidOperationException("動画を保存可能な形式に変換できませんでした。");
                result = output.Path;
            }
            token.ThrowIfCancellationRequested();
            // Commit only after all transfers and rendering succeed; existing files survive failures.
            File.Move(result, full, overwrite);
        }
        finally
        {
            Directory.Delete(temporary, true);
        }
    }

    private static string IdentifyContainer(string path, bool hls)
    {
        if (!hls) return path;
        Span<byte> header = stackalloc byte[384];
        using FileStream input = File.OpenRead(path);
        int count = input.Read(header);
        string? extension = count > 188 && header[0] == 0x47 && header[188] == 0x47 ? ".ts" :
            count >= 8 && (header[4..8].SequenceEqual("ftyp"u8) || header[4..8].SequenceEqual("styp"u8)) ? ".mp4" :
            count >= 2 && header[0] == 0xff && (header[1] & 0xf6) == 0xf0 ? ".aac" : null;
        if (extension == null) throw new InvalidOperationException("取得した分割データの形式を確認できませんでした。");
        input.Close();
        string next = Path.ChangeExtension(path, extension);
        if (!string.Equals(next, path, StringComparison.OrdinalIgnoreCase)) File.Move(path, next);
        return next;
    }
}
