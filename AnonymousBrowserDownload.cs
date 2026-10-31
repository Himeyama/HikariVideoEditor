using System.Net;
using System.Net.Http;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace VideoEditor;

public sealed partial class MainWindow
{
    private async Task<DownloadVideo> ResolveInAnonymousBrowserAsync(string input, HttpClient http,
        CookieContainer cookies, CancellationToken token, IProgress<DownloadProgress> progress)
    {
        var view = new WebView2 { Width = 1, Height = 1, Opacity = 0, IsHitTestVisible = false };
        Root.Children.Add(view);
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HikariVideoEditor", "AnonymousPlayback");
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, directory,
                new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required" }).AsTask(token);
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.IsInPrivateModeEnabled = true;
            options.ProfileName = "AnonymousDownload";
            await view.EnsureCoreWebView2Async(environment, options).AsTask(token);
            var core = view.CoreWebView2;
            core.IsMuted = true;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                    uri.Host is not ("www.youtube.com" or "youtube.com")) args.Cancel = true;
            };
            var urls = new List<Uri>();
            core.WebResourceResponseReceived += (_, args) =>
            {
                if (args.Response.StatusCode is >= 200 and < 300 &&
                    Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) && VideoDownloadSource.IsMediaUrl(uri))
                {
                    if (urls.Count == 500) urls.RemoveAt(0);
                    urls.Add(uri);
                }
            };
            await core.AddScriptToExecuteOnDocumentCreatedAsync(AnonymousBrowserResolver.InitializeScript).AsTask(token);
            var resolver = new AnonymousBrowserResolver(http,
                uri => { urls.Clear(); core.Navigate(uri.AbsoluteUri); return Task.CompletedTask; },
                script => core.ExecuteScriptAsync(script).AsTask(), () => urls.ToArray());
            DownloadVideo result;
            try { result = await resolver.ResolveAsync(input, core.Settings.UserAgent, token, progress); }
            catch (InvalidOperationException) when (!token.IsCancellationRequested)
            {
                core.Settings.UserAgent = AnonymousVideoSession.SafariAgent;
                result = await resolver.ResolveAsync(input, core.Settings.UserAgent, token, progress);
            }
            // Copy only the cookies issued to this new private session, never a user's browser cookies.
            foreach (var uri in result.Formats.Select(f => f.Url).Append(new Uri("https://www.youtube.com/")))
            {
                foreach (var cookie in await core.CookieManager.GetCookiesAsync(uri.AbsoluteUri).AsTask(token))
                    cookies.Add(new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain) { Secure = cookie.IsSecure, HttpOnly = cookie.IsHttpOnly });
            }
            return result;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            throw new InvalidOperationException("匿名の再生環境を起動できませんでした。WebView2 Runtime を確認してください。");
        }
        finally
        {
            view.Close();
            Root.Children.Remove(view);
        }
    }
}
