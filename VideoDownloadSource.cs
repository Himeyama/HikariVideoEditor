using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VideoEditor;

public sealed record DownloadStream(Uri Url, string MimeType, long? Size, int Height, int Fps, long Bitrate, bool HasAudio)
{
    public bool IsHls { get; init; }
    public DownloadStream? Audio { get; init; }
    public string? UserAgent { get; init; }
    public override string ToString() => $"{Height}p / {Fps} fps";
}

public sealed record DownloadVideo(string Id, string Title, TimeSpan Duration,
    IReadOnlyList<DownloadStream> Formats, DownloadStream? Audio);

// Protocol handling is kept separate from the UI and the file transfer engine.
// No third-party downloader code or executable is used.
public sealed class VideoDownloadSource
{
    private readonly HttpClient _http;
    public VideoDownloadSource(HttpClient http) => this._http = http;

    public static string ParseId(string input)
    {
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
            throw new ArgumentException("有効な HTTPS の動画 URL を入力してください。");
        string host = uri.IdnHost.ToLowerInvariant();
        string? id = null;
        if (host == "youtu.be")
            id = uri.AbsolutePath.Trim('/');
        else if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com")
        {
            string[] parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0] is "shorts" or "embed" or "live")
                id = parts[1];
            else if (uri.AbsolutePath == "/watch")
                id = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2))
                    .Where(p => p.Length == 2 && p[0] == "v").Select(p => Uri.UnescapeDataString(p[1])).FirstOrDefault();
        }
        if (id == null || !Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("この URL には対応していません。単体の動画 URL を入力してください。");
        return id;
    }

    public async Task<DownloadVideo> ResolveAsync(string input, CancellationToken token,
        IProgress<DownloadProgress>? progress = null)
    {
        string id = ParseId(input);
        InvalidOperationException? failure = null;
        AnonymousVideoSession session = AnonymousVideoSession.Empty;
        // A fresh HTTP session is owned by the dialog. No system browser profile or account cookies are read.
        foreach (string agent in new[] { AnonymousVideoSession.SafariAgent, AnonymousVideoSession.DesktopAgent })
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new DownloadProgress("匿名セッションで動画情報を取得中…", null));
            try
            {
                using HttpRequestMessage page = new(HttpMethod.Get, $"https://www.youtube.com/watch?v={id}");
                page.Headers.UserAgent.ParseAdd(agent);
                page.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
                string html = await DownloadHttp.ReadTextAsync(_http, page, 8 * 1024 * 1024, token);
                session = AnonymousVideoSession.Parse(html, agent);
                if (session.Player is { } player)
                {
                    DownloadVideo? candidate = await ResolvePlayerAsync(id, player, agent, token);
                    if (candidate != null) return candidate;
                }
            }
            catch (InvalidOperationException exception) { failure = exception; }
            catch (HttpRequestException) { /* Other anonymous playback methods can still work. */ }
        }
        try
        {
            using HttpRequestMessage embed = new(HttpMethod.Get, $"https://www.youtube.com/embed/{id}");
            embed.Headers.UserAgent.ParseAdd(AnonymousVideoSession.DesktopAgent);
            string html = await DownloadHttp.ReadTextAsync(_http, embed, 8 * 1024 * 1024, token);
            AnonymousVideoSession embedded = AnonymousVideoSession.Parse(html, AnonymousVideoSession.DesktopAgent);
            if (embedded.WebVersion.StartsWith("1.", StringComparison.Ordinal))
                session = session with { EmbeddedVersion = embedded.WebVersion };
        }
        catch (InvalidOperationException) { }
        catch (HttpRequestException) { }
        foreach (AnonymousPlaybackClient client in AnonymousVideoSession.Clients(session))
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new DownloadProgress("別の配信方式で動画情報を確認中…", null));
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Post, "https://www.youtube.com/youtubei/v1/player?prettyPrint=false");
                request.Headers.UserAgent.ParseAdd(client.Agent);
                request.Headers.Add("X-Youtube-Client-Name", client.Code);
                request.Headers.Add("X-Youtube-Client-Version", client.Version);
                if (!string.IsNullOrEmpty(session.VisitorData)) request.Headers.Add("X-Goog-Visitor-Id", session.VisitorData);
                request.Headers.Referrer = new Uri($"https://www.youtube.com/watch?v={id}");
                request.Content = JsonContent.Create(new
                {
                    context = new { client = client.Context, thirdParty = new { embedUrl = "https://www.youtube.com/" } },
                    videoId = id, contentCheckOk = true, racyCheckOk = true,
                    playbackContext = new { contentPlaybackContext = new { signatureTimestamp = session.SignatureTimestamp, html5Preference = "HTML5_PREF_WANTS" } }
                });
                using JsonDocument json = JsonDocument.Parse(await DownloadHttp.ReadTextAsync(_http, request, 8 * 1024 * 1024, token));
                DownloadVideo? candidate = await ResolvePlayerAsync(id, json.RootElement, client.Agent, token);
                if (candidate != null) return candidate;
            }
            catch (InvalidOperationException exception) { failure = exception; }
            catch (HttpRequestException) { }
        }
        throw failure ?? new InvalidOperationException("匿名で取得できる配信情報が見つかりませんでした。接続を確認して再試行してください。");
    }

    public async Task<DownloadVideo?> ResolvePlayerAsync(string id, JsonElement player, string agent, CancellationToken token)
    {
        // A successful metadata response alone is insufficient: some clients return formats without URLs.
        try
        {
            DownloadVideo video = ParsePlayer(id, player);
            DownloadStream[] formats = video.Formats.Select(f => f with { UserAgent = agent }).ToArray();
            DownloadStream? audio = video.Audio is { } sound ? sound with { UserAgent = agent } : null;
            // Reject unusable signatures/tokens before offering these formats to the user.
            if (await CanReadAsync(formats[^1], token) && (audio == null || await CanReadAsync(audio, token)))
                return video with { Formats = formats, Audio = audio };
        }
        catch (InvalidOperationException) when (player.TryGetProperty("streamingData", out _)) { }
        if (player.TryGetProperty("playabilityStatus", out JsonElement status) && Text(status, "status") == "OK" &&
            player.TryGetProperty("streamingData", out JsonElement data) &&
            Uri.TryCreate(Text(data, "hlsManifestUrl"), UriKind.Absolute, out Uri? manifest) && IsMediaUrl(manifest))
        {
            JsonElement details = player.GetProperty("videoDetails");
            if (details.TryGetProperty("isLiveContent", out JsonElement live) && live.ValueKind == JsonValueKind.True)
                throw new InvalidOperationException("ライブ形式の動画には対応していません。");
            IReadOnlyList<DownloadStream> formats = await new VideoDownloadHls(_http).GetFormatsAsync(manifest, agent, token);
            if (formats.Count > 0)
                return new DownloadVideo(id, Text(details, "title"), TimeSpan.FromSeconds(Number(details, "lengthSeconds")), formats, null);
        }
        return null;
    }

    private async Task<bool> CanReadAsync(DownloadStream stream, CancellationToken token)
    {
        using HttpResponseMessage response = await DownloadHttp.OpenMediaAsync(_http, stream.Url, stream.UserAgent, token, probe: true);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType is "text/html" or "application/json") return false;
        await using Stream body = await response.Content.ReadAsStreamAsync(token);
        return await body.ReadAsync(new byte[1], token) == 1;
    }

    public static DownloadVideo ParsePlayer(string id, JsonElement root)
    {
        if (root.TryGetProperty("playabilityStatus", out JsonElement restricted) && Text(restricted, "status") == "LOGIN_REQUIRED")
            throw new InvalidOperationException("配信元がログインまたは本人確認を要求しています。この動画は現在取得できません。");
        if (!root.TryGetProperty("playabilityStatus", out JsonElement status) || Text(status, "status") != "OK")
            throw new InvalidOperationException("この動画は取得できません。公開状態や視聴条件を確認してください。");
        if (!root.TryGetProperty("videoDetails", out JsonElement details) ||
            (details.TryGetProperty("isLiveContent", out JsonElement live) && live.ValueKind == JsonValueKind.True))
            throw new InvalidOperationException("ライブ形式の動画には対応していません。");
        if (!root.TryGetProperty("streamingData", out JsonElement data))
            throw new InvalidOperationException("動画の配信情報がありません。");
        List<DownloadStream> streams = new();
        foreach (string key in new[] { "formats", "adaptiveFormats" })
        {
            if (!data.TryGetProperty(key, out JsonElement formats)) continue;
            foreach (JsonElement item in formats.EnumerateArray())
            {
                string mime = Text(item, "mimeType");
                if (item.TryGetProperty("drmFamilies", out _) || item.TryGetProperty("drmTrackType", out _) ||
                    !Uri.TryCreate(Text(item, "url"), UriKind.Absolute, out Uri? url) || !IsMediaUrl(url)) continue;
                bool video = mime.StartsWith("video/mp4", StringComparison.Ordinal) && mime.Contains("avc1", StringComparison.Ordinal);
                bool audio = mime.StartsWith("audio/mp4", StringComparison.Ordinal) && mime.Contains("mp4a", StringComparison.Ordinal);
                if (!video && !audio) continue;
                long size = Number(item, "contentLength");
                streams.Add(new DownloadStream(url, mime, size > 0 ? size : null,
                    (int)Number(item, "height"), (int)Number(item, "fps"), Number(item, "bitrate"),
                    audio || mime.Contains("mp4a", StringComparison.Ordinal)));
            }
        }
        DownloadStream? sound = streams.Where(s => s.Height == 0 && s.HasAudio).OrderByDescending(s => s.Bitrate).FirstOrDefault();
        DownloadStream[] choices = streams.Where(s => s.Height > 0 && (s.HasAudio || sound != null))
            .OrderByDescending(s => s.Height).ThenByDescending(s => s.Fps).ThenByDescending(s => s.Bitrate)
            .DistinctBy(s => (s.Height, s.Fps)).ToArray();
        if (choices.Length == 0)
            throw new InvalidOperationException("対応する配信形式を取得できません。追加の認証・署名処理が必要な動画には現在対応していません。");
        return new DownloadVideo(id, Text(details, "title"), TimeSpan.FromSeconds(Number(details, "lengthSeconds")), choices, sound);
    }

    public static bool IsMediaUrl(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) && uri.IdnHost.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase);

    private static string Text(JsonElement item, string key) => item.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static long Number(JsonElement item, string key) => item.TryGetProperty(key, out JsonElement value) && long.TryParse(value.ToString(), out long number) ? number : 0;
}

public sealed record DownloadProgress(string Stage, double? Percent);

public sealed class VideoDownloadTransfer
{
    private readonly HttpClient _http;
    public VideoDownloadTransfer(HttpClient http) => this._http = http;

    public async Task SaveStreamAsync(DownloadStream stream, string path, string stage,
        IProgress<DownloadProgress>? progress, CancellationToken token)
    {
        if (!VideoDownloadSource.IsMediaUrl(stream.Url)) throw new ArgumentException("配信 URL が不正です。");
        if (stream.IsHls)
        {
            await new VideoDownloadHls(_http).SaveAsync(stream, path, stage, progress, token);
            return;
        }
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using HttpResponseMessage response = await DownloadHttp.OpenMediaAsync(_http, stream.Url, stream.UserAgent, token);
                if (response.StatusCode == HttpStatusCode.Forbidden)
                    throw new InvalidOperationException("配信元がダウンロードを許可しませんでした。動画情報を取得し直してください。");
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentType?.MediaType is "text/html" or "application/json")
                    throw new InvalidOperationException("配信元から動画データが返されませんでした。");
                long? expected = stream.Size ?? response.Content.Headers.ContentLength;
                await using Stream input = await response.Content.ReadAsStreamAsync(token);
                await using FileStream output = new(path, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true);
                byte[] buffer = new byte[131072];
                long total = 0;
                int read;
                while ((read = await ReadWithTimeoutAsync(input, buffer, token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    total += read;
                    progress?.Report(new DownloadProgress(stage, expected > 0 ? Math.Min(100, total * 100.0 / expected.Value) : null));
                }
                if (total == 0 || (expected.HasValue && total != expected))
                    throw new IOException("動画データの受信が途中で終了しました。");
                return;
            }
            catch (Exception ex) when (attempt < 2 && !token.IsCancellationRequested &&
                (ex is IOException || ex is HttpRequestException { StatusCode: null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests } ||
                 ex is HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError }))
            {
                progress?.Report(new DownloadProgress("通信を再試行中…", null));
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), token);
            }
        }
    }

    private static async Task<int> ReadWithTimeoutAsync(Stream input, byte[] buffer, CancellationToken token)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try { return await input.ReadAsync(buffer, timeout.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException("動画データの受信がタイムアウトしました。");
        }
    }
}
