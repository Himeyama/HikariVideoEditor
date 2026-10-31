using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace VideoEditor;

public static class Voicevox
{
    private static readonly HttpClient _client = new(new HttpClientHandler { UseProxy = false })
    { BaseAddress = new Uri("http://127.0.0.1:50021/"), Timeout = TimeSpan.FromMinutes(3) };

    public static string? FindExecutable()
    {
        List<string> candidates = new();
        foreach (Process process in Process.GetProcessesByName("VOICEVOX"))
        {
            using (process)
                try { if (process.MainModule?.FileName is string path) candidates.Add(path); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
        foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            candidates.Add(Path.Combine(root, "Programs", "VOICEVOX", "VOICEVOX.exe"));
            candidates.Add(Path.Combine(root, "VOICEVOX", "VOICEVOX.exe"));
        }
        foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        foreach (string branch in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            using RegistryKey? uninstall = hive.OpenSubKey(branch);
            if (uninstall == null) continue;
            foreach (string name in uninstall.GetSubKeyNames())
            {
                using RegistryKey? key = uninstall.OpenSubKey(name);
                if (!(key?.GetValue("DisplayName") as string ?? "").StartsWith("VOICEVOX", StringComparison.OrdinalIgnoreCase)) continue;
                if (key?.GetValue("InstallLocation") is string location) candidates.Add(Path.Combine(location, "VOICEVOX.exe"));
                if (key?.GetValue("DisplayIcon") is string icon) candidates.Add(icon.Split(',')[0].Trim('"'));
            }
        }
        return candidates.FirstOrDefault(path => Path.GetFileName(path).Equals("VOICEVOX.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path));
    }

    public static async Task<bool> IsRunning()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
        try { using HttpResponseMessage response = await _client.GetAsync("version", timeout.Token); return response.IsSuccessStatusCode; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { return false; }
    }

    public static async Task<VoicevoxFavorite[]> Voices()
    {
        JsonArray json = JsonNode.Parse(await _client.GetStringAsync("speakers"))!.AsArray();
        return json.SelectMany(s => s!["styles"]!.AsArray()
            .Where(style => style!["type"] == null || style["type"]!.GetValue<string>() == "talk")
            .Select(style => new VoicevoxFavorite(s!["speaker_uuid"]!.GetValue<string>(), style!["id"]!.GetValue<int>(), s["name"]!.GetValue<string>(), style["name"]!.GetValue<string>()))).ToArray();
    }

    public static async Task<string> Speech(string path, string text, VoicevoxFavorite favorite, int rate, int volume)
    {
        if (!Path.IsPathFullyQualified(path) || !Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("保存先はWAVファイルの絶対パスで指定してください。");
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("保存先が既に存在します。新しいファイル名を指定してください。");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 10000 || rate is < -10 or > 10 || volume is < 0 or > 100) throw new ArgumentException("原稿・速度・音量の値が不正です。");
        VoicevoxFavorite[] voices = await Voices();
        if (!voices.Any(v => v.VoiceId == favorite.VoiceId)) throw new InvalidOperationException("お気に入りの声が見つかりません。VOICEVOXのお気に入りを設定し直してください。");
        using HttpResponseMessage queryResponse = await _client.PostAsync($"audio_query?speaker={favorite.StyleId}&text={Uri.EscapeDataString(text)}", null);
        queryResponse.EnsureSuccessStatusCode();
        JsonNode query = JsonNode.Parse(await queryResponse.Content.ReadAsStringAsync())!;
        query["speedScale"] = Math.Pow(2, rate / 10.0);
        query["volumeScale"] = volume / 100.0;
        using StringContent body = new(query.ToJsonString(), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _client.PostAsync($"synthesis?speaker={favorite.StyleId}", body);
        response.EnsureSuccessStatusCode();
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temporary, await response.Content.ReadAsByteArrayAsync()); File.Move(temporary, path); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return path;
    }
}
