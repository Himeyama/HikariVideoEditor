using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace VideoEditor;

public sealed record HlsSegment(Uri Url, bool Initialization);

// Only clear, finite playlists are accepted. Encrypted media and live playlists are not decoded.
public sealed class VideoDownloadHls
{
    private readonly HttpClient http;
    public VideoDownloadHls(HttpClient http) => this.http = http;

    public async Task<IReadOnlyList<DownloadStream>> GetFormatsAsync(Uri manifest, string agent, CancellationToken token)
    {
        var text = await ReadPlaylistAsync(manifest, agent, token);
        var candidates = ParseMaster(manifest, text, agent);
        var readable = new List<DownloadStream>();
        foreach (var format in candidates.Take(12))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (await CanReadPlaylistAsync(format, token) &&
                    (format.Audio == null || await CanReadPlaylistAsync(format.Audio, token))) readable.Add(format);
            }
            catch (InvalidOperationException) { }
            catch (HttpRequestException) { }
        }
        return readable;
    }

    private async Task<bool> CanReadPlaylistAsync(DownloadStream stream, CancellationToken token)
    {
        var media = await ReadPlaylistAsync(stream.Url, stream.UserAgent, token);
        var segments = ParseMedia(stream.Url, media);
        var first = segments.First(segment => !segment.Initialization);
        using var response = await DownloadHttp.OpenMediaAsync(http, first.Url, stream.UserAgent, token, probe: true);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType is "text/html" or "application/json") return false;
        await using var body = await response.Content.ReadAsStreamAsync(token);
        return await body.ReadAsync(new byte[1], token) == 1;
    }

    public static IReadOnlyList<DownloadStream> ParseMaster(Uri manifest, string text, string agent)
    {
        var lines = Lines(text);
        if (lines.Length == 0 || lines[0] != "#EXTM3U") throw new InvalidOperationException("分割配信情報が不正です。");
        var audio = new Dictionary<string, Uri>();
        foreach (var line in lines.Where(l => l.StartsWith("#EXT-X-MEDIA:", StringComparison.Ordinal)))
        {
            var fields = Attributes(line);
            if (fields.GetValueOrDefault("TYPE") == "AUDIO" && fields.TryGetValue("GROUP-ID", out var group) &&
                fields.TryGetValue("URI", out var path) && (fields.GetValueOrDefault("DEFAULT") == "YES" || !audio.ContainsKey(group)))
                audio[group] = Resolve(manifest, path);
        }
        var result = new List<DownloadStream>();
        for (int i = 0; i < lines.Length - 1; i++)
        {
            if (!lines[i].StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal)) continue;
            var fields = Attributes(lines[i]);
            var codecs = fields.GetValueOrDefault("CODECS", "");
            if (!codecs.Contains("avc1", StringComparison.OrdinalIgnoreCase)) continue;
            var resolution = fields.GetValueOrDefault("RESOLUTION", "").Split('x');
            if (resolution.Length != 2 || !int.TryParse(resolution[1], out var height) || height <= 0) continue;
            var next = lines[i + 1];
            if (next.StartsWith('#')) continue;
            bool hasAudio = codecs.Contains("mp4a", StringComparison.OrdinalIgnoreCase) && !fields.ContainsKey("AUDIO");
            DownloadStream? sound = null;
            if (fields.TryGetValue("AUDIO", out var group))
            {
                if (!audio.TryGetValue(group, out var audioUri)) continue;
                sound = new DownloadStream(audioUri, "audio/mp4", null, 0, 0, 128000, true) { IsHls = true, UserAgent = agent };
            }
            if (!hasAudio && sound == null) continue;
            _ = long.TryParse(fields.GetValueOrDefault("BANDWIDTH"), out var bitrate);
            _ = double.TryParse(fields.GetValueOrDefault("FRAME-RATE"), NumberStyles.Float, CultureInfo.InvariantCulture, out var fps);
            result.Add(new DownloadStream(Resolve(manifest, next), "video/mp4", null, height, fps > 0 ? (int)Math.Round(fps) : 30, bitrate, hasAudio)
            {
                IsHls = true, UserAgent = agent, Audio = sound
            });
        }
        return result.OrderByDescending(f => f.Height).ThenByDescending(f => f.Fps).DistinctBy(f => (f.Height, f.Fps)).ToArray();
    }

    public static IReadOnlyList<HlsSegment> ParseMedia(Uri playlist, string text)
    {
        var lines = Lines(text);
        if (lines.Length == 0 || lines[0] != "#EXTM3U" || !lines.Contains("#EXT-X-ENDLIST"))
            throw new InvalidOperationException("終了済みの分割配信だけに対応しています。");
        var result = new List<HlsSegment>();
        foreach (var line in lines)
        {
            if (line.StartsWith("#EXT-X-KEY:", StringComparison.Ordinal) && Attributes(line).GetValueOrDefault("METHOD") != "NONE")
                throw new InvalidOperationException("暗号化された分割配信には対応していません。");
            if (line.StartsWith("#EXT-X-BYTERANGE:", StringComparison.Ordinal) || line == "#EXT-X-DISCONTINUITY")
                throw new InvalidOperationException("この分割配信の構成には現在対応していません。");
            if (line.StartsWith("#EXT-X-MAP:", StringComparison.Ordinal))
            {
                var fields = Attributes(line);
                if (result.Count != 0 || fields.ContainsKey("BYTERANGE") || !fields.TryGetValue("URI", out var init))
                    throw new InvalidOperationException("この分割配信の初期化形式には現在対応していません。");
                result.Add(new HlsSegment(Resolve(playlist, init), true));
            }
            else if (!line.StartsWith('#')) result.Add(new HlsSegment(Resolve(playlist, line), false));
            if (result.Count > 50000) throw new InvalidOperationException("分割配信の項目数が多すぎます。");
        }
        if (result.Count == 0 || result.All(s => s.Initialization)) throw new InvalidOperationException("分割配信に動画データがありません。");
        return result;
    }

    public async Task SaveAsync(DownloadStream stream, string path, string stage, IProgress<DownloadProgress>? progress, CancellationToken token)
    {
        var text = await ReadPlaylistAsync(stream.Url, stream.UserAgent, token);
        var segments = ParseMedia(stream.Url, text);
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true);
        var buffer = new byte[131072];
        for (int i = 0; i < segments.Count; i++)
        {
            long start = output.Position;
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromMinutes(2));
                    using var response = await DownloadHttp.OpenMediaAsync(http, segments[i].Url, stream.UserAgent, timeout.Token);
                    if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
                        throw new InvalidOperationException("配信情報が無効になりました。動画情報を取得し直してください。");
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentType?.MediaType is "text/html" or "application/json")
                        throw new InvalidOperationException("配信元から動画データが返されませんでした。");
                    await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                    int read;
                    long received = 0;
                    while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                        received += read;
                    }
                    if (received == 0 || (response.Content.Headers.ContentLength is { } size && size != received))
                        throw new IOException("分割データの受信が途中で終了しました。");
                    break;
                }
                catch (Exception exception) when (attempt < 2 && !token.IsCancellationRequested &&
                    (exception is IOException or OperationCanceledException || exception is HttpRequestException { StatusCode: null or System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests or >= System.Net.HttpStatusCode.InternalServerError }))
                {
                    output.Position = start;
                    output.SetLength(start);
                    await Task.Delay(TimeSpan.FromSeconds(attempt + 1), token);
                }
            }
            progress?.Report(new DownloadProgress(stage, (i + 1) * 100.0 / segments.Count));
        }
    }

    private async Task<string> ReadPlaylistAsync(Uri uri, string? agent, CancellationToken token)
    {
        using var request = Request(uri, agent);
        return await DownloadHttp.ReadTextAsync(http, request, 8 * 1024 * 1024, token);
    }

    private static HttpRequestMessage Request(Uri uri, string? agent)
    {
        if (!VideoDownloadSource.IsMediaUrl(uri)) throw new InvalidOperationException("配信 URL が不正です。");
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (agent != null) request.Headers.UserAgent.ParseAdd(agent);
        return request;
    }

    private static Uri Resolve(Uri basis, string value)
    {
        if (!Uri.TryCreate(basis, value, out var uri) || !VideoDownloadSource.IsMediaUrl(uri))
            throw new InvalidOperationException("配信 URL が不正です。");
        return uri;
    }

    private static string[] Lines(string text) => text.TrimStart('\uFEFF').Split('\n').Select(l => l.Trim()).Where(l => l.Length != 0).ToArray();

    private static Dictionary<string, string> Attributes(string line)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(line[(line.IndexOf(':') + 1)..], "([A-Z0-9-]+)=(?:\"([^\"]*)\"|([^,]*))", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            fields[match.Groups[1].Value] = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
        return fields;
    }
}
