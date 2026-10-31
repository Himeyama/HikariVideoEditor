using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VideoEditor;

// The provider's own player runs in an isolated anonymous browser. We observe only the signed
// media URLs it requests; no third-party signature implementation is copied or executed.
public sealed class AnonymousBrowserResolver
{
    private readonly Func<Uri, Task> navigate;
    private readonly Func<string, Task<string>> evaluate;
    private readonly Func<IReadOnlyList<Uri>> captured;
    private readonly VideoDownloadSource source;

    public AnonymousBrowserResolver(HttpClient http, Func<Uri, Task> navigate,
        Func<string, Task<string>> evaluate, Func<IReadOnlyList<Uri>> captured)
    {
        source = new VideoDownloadSource(http);
        this.navigate = navigate;
        this.evaluate = evaluate;
        this.captured = captured;
    }

    public const string InitializeScript = """
        (() => {
          const supported = MediaSource.isTypeSupported.bind(MediaSource);
          MediaSource.isTypeSupported = type => !/av01|vp0?9/i.test(type) && supported(type);
          document.addEventListener('play', e => { if (e.target instanceof HTMLMediaElement) e.target.muted = true; }, true);
        })();
        """;

    public const string ReadPlayerScript = """
        (() => {
          const player = document.getElementById('movie_player');
          const response = player?.getPlayerResponse?.() || window.ytInitialPlayerResponse;
          if (!response) return null;
          const video = document.querySelector('video');
          if (video) { video.muted = true; video.play().catch(() => {}); }
          if (player) { player.mute?.(); player.playVideo?.(); }
          return response;
        })();
        """;

    public async Task<DownloadVideo> ResolveAsync(string input, string agent, CancellationToken token,
        IProgress<DownloadProgress>? progress = null)
    {
        var id = VideoDownloadSource.ParseId(input);
        bool authenticationRequired = false;
        foreach (var page in new[] { $"https://www.youtube.com/watch?v={id}", $"https://www.youtube.com/embed/{id}?autoplay=1&mute=1" })
        {
            progress?.Report(new DownloadProgress("匿名の再生環境で配信情報を確認中…", null));
            await navigate(new Uri(page)).WaitAsync(token);
            string? previousCandidate = null;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(500, token);
                string json = await evaluate(ReadPlayerScript).WaitAsync(token);
                if (json is "null" or "" or "undefined") continue;
                JsonNode? player;
                try { player = JsonNode.Parse(json); }
                catch (JsonException) { continue; }
                if (player is not JsonObject root || root["videoDetails"]?["videoId"]?.GetValue<string>() != id) continue;
                var status = root["playabilityStatus"]?["status"]?.GetValue<string>();
                if (status == "LOGIN_REQUIRED") { authenticationRequired = true; break; }
                if (status != "OK") continue;
                var mediaUrls = captured();
                MergeCapturedUrls(root, mediaUrls);
                var fingerprint = root["streamingData"]?.ToJsonString() + "|" + mediaUrls.Count;
                if (fingerprint == previousCandidate) continue;
                previousCandidate = fingerprint;
                using var document = JsonDocument.Parse(root.ToJsonString());
                try
                {
                    var result = await source.ResolvePlayerAsync(id, document.RootElement, agent, token);
                    if (result != null)
                    {
                        await evaluate("document.querySelectorAll('video').forEach(v => v.pause());").WaitAsync(token);
                        return result;
                    }
                }
                catch (InvalidOperationException) { }
            }
        }
        throw new InvalidOperationException(authenticationRequired
            ? "匿名の再生環境でも配信元が本人確認を要求しました。ログインせずに取得できませんでした。"
            : "匿名で保存できる配信形式を確認できませんでした。追加の配信処理が必要な動画には現在対応していません。");
    }

    public static void MergeCapturedUrls(JsonObject player, IReadOnlyList<Uri> urls)
    {
        foreach (var key in new[] { "formats", "adaptiveFormats" })
        {
            if (player["streamingData"]?[key] is not JsonArray formats) continue;
            foreach (var format in formats.OfType<JsonObject>())
            {
                var itag = format["itag"]?.ToString();
                if (itag == null) continue;
                var capturedUrl = urls.LastOrDefault(uri => VideoDownloadSource.IsMediaUrl(uri) &&
                    uri.AbsolutePath == "/videoplayback" && Query(uri).GetValueOrDefault("itag") == itag &&
                    !Query(uri).ContainsKey("sabr") && !Query(uri).ContainsKey("ump"));
                if (capturedUrl == null) continue;
                // Range is a transport request detail, not the identity of the complete stream.
                // If it is signed, leave it untouched and don't offer a partial file.
                var fields = Query(capturedUrl);
                if (fields.GetValueOrDefault("sparams", "").Split(',').Contains("range")) continue;
                var builder = new UriBuilder(capturedUrl)
                {
                    Query = string.Join('&', capturedUrl.Query.TrimStart('?').Split('&').Where(p => p.Split('=', 2)[0] != "range"))
                };
                format["url"] = builder.Uri.AbsoluteUri;
                format.Remove("signatureCipher");
                format.Remove("cipher");
            }
        }
    }

    private static Dictionary<string, string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split('=', 2)).Where(p => p.Length == 2).GroupBy(p => p[0])
        .ToDictionary(g => g.Key, g => Uri.UnescapeDataString(g.Last()[1]));
}
