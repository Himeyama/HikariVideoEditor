using System.Net;
using System.Net.Http;
using Microsoft.Web.WebView2.Core;
using VideoEditor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length < 2 || args[0] != "--live")
        {
            Console.WriteLine("Use --live <video URL> [output.mp4] for an opt-in anonymous browser test.");
            return;
        }
        using Form form = new() { Opacity = 0, ShowInTaskbar = false, Width = 16, Height = 16, Text = "Anonymous playback check" };
        form.Load += async (_, _) =>
        {
            CoreWebView2Controller? controller = null;
            try
            {
                using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(90));
                CancellationToken token = cancellation.Token;
                string directory = Path.GetFullPath("artifacts/anonymous-browser-check");
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, directory,
                    new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
                CoreWebView2ControllerOptions options = environment.CreateCoreWebView2ControllerOptions();
                options.IsInPrivateModeEnabled = true;
                controller = await environment.CreateCoreWebView2ControllerAsync(form.Handle, options);
                controller.IsVisible = false;
                CoreWebView2 core = controller.CoreWebView2;
                core.IsMuted = true;
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.NewWindowRequested += (_, e) => e.Handled = true;
                core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
                core.DownloadStarting += (_, e) => e.Cancel = true;
                List<Uri> urls = new();
                core.WebResourceResponseReceived += (_, e) =>
                {
                    if (e.Response.StatusCode is >= 200 and < 300 && Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out Uri? uri) && VideoDownloadSource.IsMediaUrl(uri))
                        urls.Add(uri);
                };
                await core.AddScriptToExecuteOnDocumentCreatedAsync(AnonymousBrowserResolver.InitializeScript);
                CookieContainer cookies = new();
                using HttpClient http = new(new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
                AnonymousBrowserResolver resolver = new(http,
                    uri => { urls.Clear(); core.Navigate(uri.AbsoluteUri); return Task.CompletedTask; },
                    script => core.ExecuteScriptAsync(script), () => urls.ToArray());
                DownloadVideo result;
                try
                {
                    result = await resolver.ResolveAsync(args[1], core.Settings.UserAgent, token,
                        new Progress<DownloadProgress>(p => Console.WriteLine(p.Stage)));
                }
                catch (InvalidOperationException) when (!token.IsCancellationRequested)
                {
                    core.Settings.UserAgent = AnonymousVideoSession.SafariAgent;
                    result = await resolver.ResolveAsync(args[1], core.Settings.UserAgent, token,
                        new Progress<DownloadProgress>(p => Console.WriteLine(p.Stage)));
                }
                Console.WriteLine($"LIVE: {result.Title}, {result.Formats.Count} formats, private browser profile, captured media URLs: {urls.Count}");
                foreach (Uri uri in urls.TakeLast(8))
                    Console.WriteLine($"MEDIA: path segments={uri.AbsolutePath.Split('/').Length}, playback={uri.AbsolutePath.Contains("videoplayback")}, playlist={uri.AbsolutePath.Contains("manifest")}, query keys={string.Join(',', uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=')[0]))}");
                foreach (Uri uri in result.Formats.Select(f => f.Url).Append(new Uri("https://www.youtube.com/")))
                    foreach (CoreWebView2Cookie cookie in await core.CookieManager.GetCookiesAsync(uri.AbsoluteUri))
                        cookies.Add(new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain) { Secure = cookie.IsSecure, HttpOnly = cookie.IsHttpOnly });
                if (args.Length >= 3)
                {
                    await VideoDownloadWriter.WriteAsync(new VideoDownloadTransfer(http), result, result.Formats[^1], args[2], null, token);
                    Console.WriteLine($"LIVE: saved {new FileInfo(args[2]).Length} bytes");
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine($"LIVE FAILED: {exception.GetType().Name}: {exception.Message}");
                if (controller != null)
                {
                    Console.WriteLine(await controller.CoreWebView2.ExecuteScriptAsync("(() => { const p=document.getElementById('movie_player');const r=p?.getPlayerResponse?.()||window.ytInitialPlayerResponse; return {host:location.host,status:r?.playabilityStatus?.status,id:r?.videoDetails?.videoId,hls:!!r?.streamingData?.hlsManifestUrl,formats:r?.streamingData?.adaptiveFormats?.length,quality:p?.getAvailableQualityLevels?.(),video:!!document.querySelector('video')}; })()"));
                }
                Environment.ExitCode = 1;
            }
            finally
            {
                controller?.Close();
                form.Close();
            }
        };
        Application.Run(form);
    }
}
