using System.Net;
using VideoEditor;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.UI;

string directory = Path.GetFullPath("artifacts/download-media-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(directory);
MediaComposition fixture = new();
fixture.Clips.Add(MediaClip.CreateFromColor(Color.FromArgb(255, 255, 0, 0), TimeSpan.FromSeconds(1)));
StorageFile input = await folder.CreateFileAsync("input.mp4");
if (await fixture.RenderToFileAsync(input, MediaTrimmingPreference.Precise, MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Wvga)) != TranscodeFailureReason.None)
    throw new Exception("Fixture generation failed");
string wavPath = Path.Combine(directory, "input.wav");
using (BinaryWriter writer = new(File.Create(wavPath)))
{
    const int samples = 44100;
    writer.Write("RIFF"u8.ToArray()); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8.ToArray());
    writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(samples);
    writer.Write(samples * 2); writer.Write((short)2); writer.Write((short)16);
    writer.Write("data"u8.ToArray()); writer.Write(samples * 2);
    for (int i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * 440 * Math.Tau / samples) * 4000));
}
StorageFile aac = await folder.CreateFileAsync("input.m4a");
PrepareTranscodeResult transcode = await new MediaTranscoder().PrepareFileTranscodeAsync(await StorageFile.GetFileFromPathAsync(wavPath), aac, MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High));
if (!transcode.CanTranscode) throw new Exception("Audio fixture conversion failed");
await transcode.TranscodeAsync();
using MediaHandler handler = new(File.ReadAllBytes(input.Path), File.ReadAllBytes(aac.Path));
using HttpClient http = new(handler);
VideoDownloadTransfer transfer = new(http);
DownloadStream video = new(new Uri("https://r1.googlevideo.com/video"), "video/mp4", handler.Video.Length, 480, 30, 5_000_000, false);
DownloadStream audio = new(new Uri("https://r1.googlevideo.com/audio"), "audio/mp4", handler.Audio.Length, 0, 0, 128000, true);
DownloadVideo info = new("test", "Test", TimeSpan.FromSeconds(1), [video], audio);
string destination = Path.Combine(directory, "saved.mp4");
await VideoDownloadWriter.WriteAsync(transfer, info, video, destination, null, default);
StorageFile saved = await StorageFile.GetFileFromPathAsync(destination);
Windows.Storage.FileProperties.VideoProperties properties = await saved.Properties.GetVideoPropertiesAsync();
if (properties.Width != 800 || properties.Duration.TotalSeconds < .9) throw new Exception("Invalid saved video");
Windows.Storage.FileProperties.MusicProperties audioProperties = await saved.Properties.GetMusicPropertiesAsync();
if (audioProperties.Duration.TotalSeconds < .9) throw new Exception("Missing output audio");
byte[] original = File.ReadAllBytes(destination);
try
{
    await VideoDownloadWriter.WriteAsync(transfer, info, video, destination, null, default, overwrite: false);
    throw new Exception("Workspace output must not overwrite existing files");
}
catch (IOException) { }
if (!File.ReadAllBytes(destination).SequenceEqual(original)) throw new Exception("Workspace output replaced existing file");
DownloadStream hlsVideo = video with { IsHls = true, Url = new Uri("https://r1.googlevideo.com/video.m3u8"), Audio = audio };
await VideoDownloadWriter.WriteAsync(transfer, info, hlsVideo, Path.Combine(directory, "hls.mp4"), null, default);
StorageFile hlsOutput = await StorageFile.GetFileFromPathAsync(Path.Combine(directory, "hls.mp4"));
if ((await hlsOutput.Properties.GetVideoPropertiesAsync()).Duration.TotalSeconds < .9) throw new Exception("HLS media was not rendered");
if (args.Length == 2 && args[0] == "--ts-fixture")
{
    handler.Transport = File.ReadAllBytes(args[1]);
    DownloadStream tsVideo = video with { IsHls = true, HasAudio = true, Url = new Uri("https://r1.googlevideo.com/transport.m3u8") };
    await VideoDownloadWriter.WriteAsync(transfer, info, tsVideo, Path.Combine(directory, "transport.mp4"), null, default);
    StorageFile tsOutput = await StorageFile.GetFileFromPathAsync(Path.Combine(directory, "transport.mp4"));
    if ((await tsOutput.Properties.GetVideoPropertiesAsync()).Duration.TotalSeconds < .9) throw new Exception("Transport stream was not rendered");
    Console.WriteLine("PASS: MPEG-TS playlist normalized to MP4 using Windows media processing.");
}
handler.FailAudio = true;
try { await VideoDownloadWriter.WriteAsync(transfer, info, video, destination, null, default); throw new Exception("Failure expected"); }
catch (InvalidOperationException) { }
if (!File.ReadAllBytes(destination).SequenceEqual(original)) throw new Exception("Existing file changed after failure");
handler.FailAudio = false;
using CancellationTokenSource duringRender = new();
try
{
    await VideoDownloadWriter.WriteAsync(transfer, info, video, destination,
        new CancelDuringRender(duringRender), duringRender.Token);
    throw new Exception("Render cancellation expected");
}
catch (OperationCanceledException) { }
if (!File.ReadAllBytes(destination).SequenceEqual(original)) throw new Exception("Existing file changed after cancellation");
using CancellationTokenSource cancellation = new();
cancellation.Cancel();
try { await VideoDownloadWriter.WriteAsync(transfer, info, video, destination, null, cancellation.Token); throw new Exception("Cancellation expected"); }
catch (OperationCanceledException) { }
if (Directory.GetDirectories(directory, ".hikari-download-*").Length != 0) throw new Exception("Temporary files remain");
Console.WriteLine("PASS: separate H264/AAC transfer, Windows MP4 render, decode properties, existing file survives failure, cancellation cleanup.");

sealed class CancelDuringRender(CancellationTokenSource cancellation) : IProgress<DownloadProgress>
{
    public void Report(DownloadProgress progress)
    {
        if (progress.Stage.StartsWith("映像と音声")) cancellation.Cancel();
    }
}

sealed class MediaHandler(byte[] video, byte[] audio) : HttpMessageHandler
{
    public byte[] Video = video;
    public byte[] Audio = audio;
    public bool FailAudio;
    public byte[] Transport = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        bool sound = request.RequestUri!.AbsolutePath == "/audio";
        if (request.RequestUri.AbsolutePath == "/video.m3u8")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("#EXTM3U\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:1,\nfragment.m4s\n#EXT-X-ENDLIST") });
        if (request.RequestUri.AbsolutePath == "/transport.m3u8")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("#EXTM3U\n#EXTINF:1,\nsegment.ts\n#EXT-X-ENDLIST") });
        if (request.RequestUri.AbsolutePath is "/init.mp4" or "/fragment.m4s" or "/segment.ts")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(request.RequestUri.AbsolutePath == "/init.mp4" ? Video[..128] : request.RequestUri.AbsolutePath == "/segment.ts" ? Transport : Video[128..])
            });
        return Task.FromResult(sound && FailAudio ? new HttpResponseMessage(HttpStatusCode.Forbidden) :
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(sound ? Audio : Video) });
    }
}
