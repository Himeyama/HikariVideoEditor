using System.Net;
using System.Text;
using System.Text.Json;
using VideoEditor;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

const string id = "u_yIGGhubZs";
foreach (string url in new[] { $"https://www.youtube.com/watch?v={id}&list=abc", $"https://youtu.be/{id}?t=1", $"https://m.youtube.com/shorts/{id}", $"https://www.youtube.com/embed/{id}" })
    Check(VideoDownloadSource.ParseId(url) == id, "URL parsing");
foreach (string url in new[] { "file:///x", $"https://youtube.com.evil.test/watch?v={id}", $"https://youtube.com@evil.test/watch?v={id}", "https://www.youtube.com/playlist?list=x", "https://youtu.be/no", $"http://youtube.com/watch?v={id}" })
{
    try { VideoDownloadSource.ParseId(url); throw new Exception("Invalid URL accepted"); }
    catch (ArgumentException) { }
}
string payload = """
{"playabilityStatus":{"status":"OK"},"videoDetails":{"title":"Test","lengthSeconds":"5"},"streamingData":{"adaptiveFormats":[
{"mimeType":"video/mp4; codecs=\"avc1.640028\"","height":1080,"fps":30,"bitrate":5000000,"contentLength":"6","url":"https://r1.googlevideo.com/video"},
{"mimeType":"audio/mp4; codecs=\"mp4a.40.2\"","bitrate":128000,"contentLength":"3","url":"https://r1.googlevideo.com/audio"},
{"mimeType":"video/mp4; codecs=\"avc1.640028\"","height":2160,"url":"https://evil.test/file"},
{"mimeType":"video/mp4; codecs=\"avc1.640028\"","height":720,"signatureCipher":"unavailable"},
{"mimeType":"video/mp4; codecs=\"avc1.640028\"","height":480,"url":"https://r1.googlevideo.com/drm","drmFamilies":["test"]}
]}}
""";
using JsonDocument json = JsonDocument.Parse(payload);
DownloadVideo video = VideoDownloadSource.ParsePlayer(id, json.RootElement);
Check(video.Title == "Test" && video.Duration.TotalSeconds == 5 && video.Formats.Count == 1 && video.Audio != null, "Format selection and blocked URLs");
foreach (string data in new[] { "{\"playabilityStatus\":{\"status\":\"LOGIN_REQUIRED\"}}", payload.Replace("\"title\":\"Test\"", "\"isLiveContent\":true,\"title\":\"Test\""), payload.Replace("https://r1.googlevideo.com/video", "https://evil.test/file") })
{
    using JsonDocument denied = JsonDocument.Parse(data);
    try { VideoDownloadSource.ParsePlayer(id, denied.RootElement); throw new Exception("Unavailable video accepted"); }
    catch (InvalidOperationException) { }
}
string directory = Path.Combine(Path.GetTempPath(), "download-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    string first = VideoDownloadDestination.InWorkspace(directory, "Saved video");
    File.WriteAllText(first, "keep");
    Directory.CreateDirectory(Path.Combine(directory, "Saved video (2).mp4"));
    string next = VideoDownloadDestination.InWorkspace(directory, "Saved video");
    Check(Path.GetFileName(next) == "Saved video (3).mp4" && File.ReadAllText(first) == "keep", "Workspace name collisions preserve existing files and directories");
    Check(Path.GetDirectoryName(VideoDownloadDestination.InWorkspace(directory, "../invalid:title")) == directory, "Title cannot escape workspace");
    Check(VideoDownloadDestination.FileName("NUL") == "_NUL" && VideoDownloadDestination.FileName("...") == "Video", "Windows-safe filenames");
    using FakeHandler handler = new(payload);
    using HttpClient http = new(handler);
    DownloadVideo source = await new VideoDownloadSource(http).ResolveAsync($"https://youtu.be/{id}", default);
    Check(source.Title == "Test" && handler.SawPlayerRequest, "Metadata protocol request");
    VideoDownloadTransfer transfer = new(http);
    string path = Path.Combine(directory, "video.mp4");
    await transfer.SaveStreamAsync(source.Formats[0], path, "transfer", null, default);
    Check(File.ReadAllText(path) == "abcdef", "Transfer bytes");
    handler.Forbidden = true;
    try { await transfer.SaveStreamAsync(source.Formats[0], path, "transfer", null, default); throw new Exception("403 accepted"); }
    catch (InvalidOperationException) { }
    Check(File.ReadAllText(path) == "abcdef", "403 does not truncate previous file");
    handler.Forbidden = false;
    handler.Short = true;
    try { await transfer.SaveStreamAsync(source.Formats[0], path, "transfer", null, default); throw new Exception("Truncation accepted"); }
    catch (IOException) { }
    Check(handler.MediaRequests == 7, "Truncated transfers retry twice");
    using CancellationTokenSource cancelled = new();
    cancelled.Cancel();
    try { await transfer.SaveStreamAsync(source.Formats[0], path, "transfer", null, cancelled.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { }
}
finally { Directory.Delete(directory, true); }
Console.WriteLine("PASS: URLs, metadata, format selection, DRM filtering, transfer, 403, truncated response retries, cancellation.");

string html = "<script>ytcfg.set({\"VISITOR_DATA\":\"visitor\\u003dvalue\",\"INNERTUBE_CLIENT_VERSION\":\"2.dynamic\",\"STS\":20753});var ytInitialPlayerResponse = " + payload + ";</script>";
AnonymousVideoSession session = AnonymousVideoSession.Parse(html, AnonymousVideoSession.SafariAgent);
Check(session.Player.HasValue && session.VisitorData == "visitor=value" && session.WebVersion == "2.dynamic" && session.SignatureTimestamp == 20753, "Balanced bootstrap parsing");
Check(AnonymousVideoSession.Clients(session).Count() == 3, "Multiple anonymous playback clients");
System.Text.Json.Nodes.JsonObject root = System.Text.Json.Nodes.JsonNode.Parse(payload)!.AsObject();
root["streamingData"]!["adaptiveFormats"]![0]!["itag"] = 137;
AnonymousBrowserResolver.MergeCapturedUrls(root, [new Uri("https://r1.googlevideo.com/videoplayback?itag=137&sig=signed&n=solved&range=0-1023")]);
Check(root["streamingData"]!["adaptiveFormats"]![0]!["url"]!.ToString().Contains("n=solved") &&
    !root["streamingData"]!["adaptiveFormats"]![0]!["url"]!.ToString().Contains("range="), "Signed browser URLs retain signature and remove transport range");
string master = """
#EXTM3U
#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="main",DEFAULT=YES,URI="audio.m3u8"
#EXT-X-STREAM-INF:BANDWIDTH=1000000,RESOLUTION=640x360,FRAME-RATE=29.97,CODECS="avc1.4d401e,mp4a.40.2",AUDIO="main"
video.m3u8
#EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1280x720,CODECS="vp09.00.31.08,mp4a.40.2"
unsupported.m3u8
""";
Uri playlistUri = new("https://r1.googlevideo.com/path/master.m3u8");
IReadOnlyList<DownloadStream> choices = VideoDownloadHls.ParseMaster(playlistUri, master, "test-agent");
Check(choices.Count == 1 && choices[0].Height == 360 && choices[0].Audio?.IsHls == true && !choices[0].HasAudio, "HLS codecs and audio group");
string media = "#EXTM3U\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:1,\npart1.m4s\n#EXTINF:1,\npart2.m4s\n#EXT-X-ENDLIST";
IReadOnlyList<HlsSegment> segments = VideoDownloadHls.ParseMedia(playlistUri, media);
Check(segments.Count == 3 && segments[0].Initialization && segments[1].Url.AbsolutePath == "/path/part1.m4s", "Finite fragmented playlist");
foreach (string invalid in new[] { media.Replace("#EXT-X-ENDLIST", ""), media.Replace("#EXTINF:1,", "#EXT-X-KEY:METHOD=AES-128,URI=\"key\""), media.Replace("part1.m4s", "https://evil.test/file"), media.Replace("#EXTINF:1,", "#EXT-X-BYTERANGE:100@0") })
{
    try { VideoDownloadHls.ParseMedia(playlistUri, invalid); throw new Exception("Invalid HLS accepted"); }
    catch (InvalidOperationException) { }
}
string hlsPath = Path.Combine(Path.GetTempPath(), "hls-check-" + Guid.NewGuid().ToString("N"));
try
{
    using HttpClient hlsHttp = new(new HlsHandler(media));
    await new VideoDownloadTransfer(hlsHttp).SaveStreamAsync(choices[0], hlsPath, "HLS", null, default);
    Check(File.ReadAllText(hlsPath) == "initfirstsecond", "HLS segment assembly");
    using HlsHandler probeHandler = new(media, master);
    using HttpClient probeHttp = new(probeHandler);
    Check((await new VideoDownloadHls(probeHttp).GetFormatsAsync(playlistUri, "test-agent", default)).Count == 1, "HLS media preflight");
    probeHandler.DenySegments = true;
    Check((await new VideoDownloadHls(probeHttp).GetFormatsAsync(playlistUri, "test-agent", default)).Count == 0, "Unusable HLS is not offered as a downloadable format");
}
finally { if (File.Exists(hlsPath)) File.Delete(hlsPath); }
Console.WriteLine("PASS: anonymous bootstrap, multiple clients, signed browser URLs, HLS audio groups, segment assembly, encrypted/live/unsafe playlist rejection.");

if (args.Length == 2 && args[0] == "--live")
{
    using HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
    DownloadVideo liveVideo = await new VideoDownloadSource(http).ResolveAsync(args[1], default);
    Console.WriteLine($"LIVE: {liveVideo.Title}, {liveVideo.Formats.Count} formats");
    using HttpRequestMessage request = new(HttpMethod.Get, liveVideo.Formats[0].Url);
    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1023);
    using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    response.EnsureSuccessStatusCode();
    Console.WriteLine($"LIVE: media endpoint {(int)response.StatusCode}");
}

sealed class HlsHandler(string playlist, string master = "") : HttpMessageHandler
{
    public bool DenySegments;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (DenySegments && request.RequestUri!.AbsolutePath.EndsWith("part1.m4s"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        string body = request.RequestUri!.AbsolutePath.Split('/').Last() switch
        {
            "master.m3u8" => master,
            "video.m3u8" or "audio.m3u8" => playlist,
            "init.mp4" => "init",
            "part1.m4s" => "first",
            "part2.m4s" => "second",
            _ => throw new Exception("Unexpected HLS request")
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}

sealed class FakeHandler(string payload) : HttpMessageHandler
{
    public bool SawPlayerRequest;
    public bool Forbidden;
    public bool Short;
    public int MediaRequests;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.Method == HttpMethod.Get && request.RequestUri!.Host == "www.youtube.com")
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<script>ytcfg.set({\"VISITOR_DATA\":\"anonymous-visitor\",\"INNERTUBE_CLIENT_VERSION\":\"2.test\",\"STS\":20000});</script>") };
        if (request.Method == HttpMethod.Post)
        {
            string body = await request.Content!.ReadAsStringAsync(token);
            if (body.Contains("\"clientName\":\"WEB\""))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"playabilityStatus\":{\"status\":\"LOGIN_REQUIRED\"}}") };
            SawPlayerRequest = body.Contains("ANDROID_VR") && body.Contains("u_yIGGhubZs");
            CheckSession(body, request);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        }
        MediaRequests++;
        if (Forbidden) return new HttpResponseMessage(HttpStatusCode.Forbidden);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(Short ? "abc" : "abcdef")) };
    }

    private static void CheckSession(string body, HttpRequestMessage request)
    {
        if (!body.Contains("anonymous-visitor") || !body.Contains("20000") ||
            !request.Headers.TryGetValues("X-Goog-Visitor-Id", out IEnumerable<string>? values) || values.Single() != "anonymous-visitor")
            throw new Exception("Anonymous session not propagated");
    }
}
