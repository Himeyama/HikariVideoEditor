using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VideoEditor;

public sealed record AnonymousPlaybackClient(string Code, string Version, string Agent, Dictionary<string, object> Context);

public sealed record AnonymousVideoSession(string VisitorData, string WebVersion, int SignatureTimestamp, JsonElement? Player)
{
    public string EmbeddedVersion { get; init; } = "1.20261027.01.00";
    public const string DesktopAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    public const string SafariAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15";
    private const string VrAgent = "com.google.android.apps.youtube.vr.oculus/1.71.26 (Linux; U; Android 12; en_US; Quest 3 Build/SQ3A.220605.009.A1)";
    public static AnonymousVideoSession Empty => new("", "2.20261029.11.00", 0, null);

    public static AnonymousVideoSession Parse(string html, string agent)
    {
        var timestamp = Regex.Match(html, "\"STS\"\\s*:\\s*([0-9]+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var player = ReadObject(html, @"(?:var\s+)?ytInitialPlayerResponse\s*=\s*");
        return new AnonymousVideoSession(ReadString(html, "VISITOR_DATA"),
            ReadString(html, "INNERTUBE_CLIENT_VERSION") is { Length: > 0 } version ? version : Empty.WebVersion,
            int.TryParse(timestamp.Groups[1].Value, out var sts) ? sts : 0, player);
    }

    public static JsonElement? ReadObject(string input, string prefixPattern)
    {
        var match = Regex.Match(input, prefixPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) return null;
        var bytes = Encoding.UTF8.GetBytes(input[match.Index..][match.Length..]);
        var reader = new Utf8JsonReader(bytes);
        try
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return document.RootElement.Clone();
        }
        catch (JsonException) { return null; }
    }

    private static string ReadString(string html, string name)
    {
        var match = Regex.Match(html, "\"" + Regex.Escape(name) + "\"\\s*:\\s*(\"(?:[^\"\\\\]|\\\\.)*\")",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? JsonSerializer.Deserialize<string>(match.Groups[1].Value) ?? "" : "";
    }

    public static IEnumerable<AnonymousPlaybackClient> Clients(AnonymousVideoSession session)
    {
        Dictionary<string, object> Context(string name, string version, string agent) => new()
        {
            ["clientName"] = name, ["clientVersion"] = version, ["userAgent"] = agent,
            ["hl"] = "en", ["gl"] = "US", ["visitorData"] = session.VisitorData
        };
        var safari = Context("WEB", session.WebVersion, SafariAgent);
        safari["browserName"] = "Safari";
        safari["browserVersion"] = "18.0";
        safari["osName"] = "Macintosh";
        safari["osVersion"] = "15.0.0";
        yield return new("1", session.WebVersion, SafariAgent, safari);
        var vr = Context("ANDROID_VR", "1.71.26", VrAgent);
        vr["androidSdkVersion"] = 32;
        vr["osName"] = "Android";
        vr["osVersion"] = "12";
        vr["deviceMake"] = "Oculus";
        vr["deviceModel"] = "Quest 3";
        yield return new("28", "1.71.26", VrAgent, vr);
        var embedded = Context("WEB_EMBEDDED_PLAYER", session.EmbeddedVersion, DesktopAgent);
        embedded["clientScreen"] = "EMBED";
        yield return new("56", session.EmbeddedVersion, DesktopAgent, embedded);
    }
}

public static class DownloadHttp
{
    public static async Task<HttpResponseMessage> OpenMediaAsync(HttpClient http, Uri uri, string? agent, CancellationToken token, bool probe = false)
    {
        for (int redirect = 0; redirect < 5; redirect++)
        {
            if (!VideoDownloadSource.IsMediaUrl(uri)) throw new InvalidOperationException("配信 URL が不正です。");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (!string.IsNullOrWhiteSpace(agent)) request.Headers.UserAgent.ParseAdd(agent);
            request.Headers.Referrer = new Uri("https://www.youtube.com/");
            request.Headers.TryAddWithoutValidation("Origin", "https://www.youtube.com");
            if (probe) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location == null || !Uri.TryCreate(uri, location, out var next)) break;
            uri = next;
        }
        throw new InvalidOperationException("配信先の移動を確認できませんでした。");
    }

    public static async Task<string> ReadTextAsync(HttpClient http, HttpRequestMessage request, int limit, CancellationToken token)
    {
        using var response = VideoDownloadSource.IsMediaUrl(request.RequestUri!)
            ? await OpenMediaAsync(http, request.RequestUri!, request.Headers.UserAgent.ToString(), token)
            : await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new InvalidOperationException("配信元が確認画面への移動を要求しています。匿名で取得できる別の方式を確認します。");
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var bytes = new byte[32768];
        int count;
        while ((count = await body.ReadAsync(bytes, token)) > 0)
        {
            if (buffer.Length + count > limit) throw new InvalidOperationException("配信情報のサイズが大きすぎます。");
            buffer.Write(bytes, 0, count);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
