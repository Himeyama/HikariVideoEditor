using VideoEditor;

static void Check(bool value)
{
    if (!value) throw new Exception("Workspace history assertion failed.");
}

var preferences = new AppPreferences();
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
    preferences.Save(path);
    Check(AppPreferences.Load(path).RecentWorkspaces.SequenceEqual(preferences.RecentWorkspaces));
    File.WriteAllText(path, "{}");
    Check(AppPreferences.Load(path).RecentWorkspaces.Count == 0);
}
finally
{
    if (File.Exists(path)) File.Delete(path);
}
Console.WriteLine("PASS: latest 20 workspaces, ordering, normalized duplicates, persistence, legacy settings");
