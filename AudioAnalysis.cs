using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace VideoEditor;

public static class AudioAnalysis
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<PcmAudio>>> cache = new();
    private static readonly SemaphoreSlim decoder = new(1);
    private static readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HikariVideoEditor", "Audio");
    private static string Key(string path)
    {
        var file = new FileInfo(path);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}")));
    }
    public static Task<PcmAudio> Get(string path)
    {
        string key = Key(path);
        return cache.GetOrAdd(key, _ => new Lazy<Task<PcmAudio>>(() => Decode(path, key))).Value;
    }
    private static async Task<PcmAudio> Decode(string path, string key)
    {
        await decoder.WaitAsync();
        string temporary = System.IO.Path.Combine(directory, key + ".partial.wav");
        try
        {
            Directory.CreateDirectory(directory);
            string destination = System.IO.Path.Combine(directory, key + ".wav");
            if (!File.Exists(destination))
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(directory);
                var file = await folder.CreateFileAsync(System.IO.Path.GetFileName(temporary), CreationCollisionOption.ReplaceExisting);
                var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.High);
                profile.Audio = AudioEncodingProperties.CreatePcm(48000, 2, 16);
                var prepared = await new MediaTranscoder().PrepareFileTranscodeAsync(await StorageFile.GetFileFromPathAsync(path), file, profile);
                if (!prepared.CanTranscode) throw new InvalidOperationException($"音声を解析できません: {prepared.FailureReason}");
                await prepared.TranscodeAsync();
                File.Move(temporary, destination, true);
            }
            return await Task.Run(() => PcmAudio.Read(destination));
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            decoder.Release();
        }
    }
    public static async Task<string> Faded(EditClip clip)
    {
        var audio = await Get(clip.Path);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new { Source = Key(clip.Path), clip.Trim, clip.Duration, clip.FadeIn, clip.FadeOut }))));
        string path = System.IO.Path.Combine(directory, key + "-fade.wav");
        if (!File.Exists(path))
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N");
            try { await Task.Run(() => audio.RenderFade(temporary, clip)); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return path;
    }
}
