using VideoEditor;

static void Check(bool value)
{
    if (!value) throw new Exception("Workspace history assertion failed.");
}

var preferences = new AppPreferences();
Check(!preferences.AutoSaveEnabled);
preferences.AutoSaveEnabled = true;
for (var index = 0; index < 25; index++)
    preferences.RememberWorkspace(Path.Combine(Path.GetTempPath(), $"workspace-{index}"));
Check(preferences.RecentWorkspaces.Count == 20);
Check(preferences.RecentWorkspaces[0].EndsWith("workspace-24"));
Check(preferences.RecentWorkspaces[^1].EndsWith("workspace-5"));
var recent = preferences.RecentWorkspaces[5];
preferences.RememberWorkspace(recent.ToUpperInvariant() + Path.DirectorySeparatorChar);
Check(preferences.RecentWorkspaces.Count == 20);
Check(string.Equals(preferences.RecentWorkspaces[0], recent, StringComparison.OrdinalIgnoreCase));
var path = Path.Combine(Path.GetTempPath(), $"workspace-history-{Guid.NewGuid():N}.json");
try
{
    preferences.VoicevoxFavorites.Add(new VoicevoxFavorite("test-speaker", 3, "テスト", "ノーマル"));
    preferences.LastSpeechVoice = "voicevox:test-speaker:3";
    preferences.LastSpeechRate = -3;
    preferences.LastSpeechVolume = 75;
    preferences.Save(path);
    Check(AppPreferences.Load(path).LastSpeechVoice == preferences.LastSpeechVoice);
    Check(AppPreferences.Load(path).LastSpeechRate == -3);
    Check(AppPreferences.Load(path).LastSpeechVolume == 75);
    Check(AppPreferences.Load(path).VoicevoxFavorites.SequenceEqual(preferences.VoicevoxFavorites));
    Check(AppPreferences.Load(path).RecentWorkspaces.SequenceEqual(preferences.RecentWorkspaces));
    Check(AppPreferences.Load(path).AutoSaveEnabled);
    File.WriteAllText(path, "{}");
    Check(AppPreferences.Load(path).LastSpeechVoice == null);
    Check(AppPreferences.Load(path).LastSpeechRate == 0);
    Check(AppPreferences.Load(path).LastSpeechVolume == 100);
    Check(AppPreferences.Load(path).RecentWorkspaces.Count == 0);
    Check(!AppPreferences.Load(path).AutoSaveEnabled);
    Check(AppPreferences.Load(path).VoicevoxFavorites.Count == 0);
}
finally
{
    if (File.Exists(path)) File.Delete(path);
}
Console.WriteLine("PASS: latest 20 workspaces, ordering, normalized duplicates, persistence, legacy settings");
