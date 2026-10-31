using VideoEditor;
static void Near(double actual, double expected)
{
    if (Math.Abs(actual - expected) > 1e-7) throw new Exception($"Expected {expected}, got {actual}");
}
static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
var video = new EditClip { Start = 4, Trim = 2, Duration = 6, Layer = 3, Kind = "video", Volume = .8 };
var moved = ClipEditing.Move(video, -8, 20);
Near(moved.Start, 0); Check(moved.Layer == 23); Near(video.Start, 4);
var movedBack = ClipEditing.Move(video, 1.011, -8);
Near(movedBack.Start, 5); Check(movedBack.Layer == 1);
var left = ClipEditing.Resize(video, -5, true, 20);
Near(left.Start, 2); Near(left.Trim, 0); Near(left.Duration, 8);
Near(left.Start + left.Duration, video.Start + video.Duration);
var right = ClipEditing.Resize(video, 100, false, 12);
Near(right.Duration, 10);
var minimum = ClipEditing.Resize(video, -100, false, 12);
Near(minimum.Duration, 1.0 / 30);
var split = ClipEditing.Split(video, 7);
Near(split.Left.Duration, 3); Near(split.Right.Start, 7); Near(split.Right.Trim, 5); Near(split.Right.Duration, 3);
Check(split.Right.Id != video.Id && split.Left.Id == video.Id);
Check(split.Right.Layer == video.Layer); Near(split.Right.Volume, video.Volume);
Near(split.Left.Trim + split.Left.Duration, split.Right.Trim);
Near(split.Left.Duration + split.Right.Duration, video.Duration);
foreach (var boundary in new[] { 4.0, 10.0, double.NaN })
{
    bool rejected = false;
    try { ClipEditing.Split(video, boundary); } catch (ArgumentException) { rejected = true; }
    Check(rejected);
}
var image = new EditClip { Kind = "image", Start = 1, Duration = 4 };
var imageLeft = ClipEditing.Resize(image, -1, true, double.PositiveInfinity);
Near(imageLeft.Start, 0); Near(imageLeft.Duration, 5); Near(imageLeft.Trim, 0);
var imageSplit = ClipEditing.Split(image, 3); Near(imageSplit.Right.Trim, 0);
var neighbor = new EditClip { Start = 10.017, Duration = 2 };
var nearStart = new EditClip { Start = 7.9, Duration = 2 };
var fitted = ClipEditing.SnapToEdges(nearStart, [neighbor], 0, .17, double.PositiveInfinity);
Near(fitted.Start + fitted.Duration, neighbor.Start); Near(fitted.Duration, 2);
var nearEnd = new EditClip { Start = 12.1, Duration = 2 };
Near(ClipEditing.SnapToEdges(nearEnd, [neighbor], 0, .17, double.PositiveInfinity).Start, neighbor.Start + neighbor.Duration);
var distant = new EditClip { Start = 5, Duration = 2 };
Near(ClipEditing.SnapToEdges(distant, [neighbor], 0, .17, double.PositiveInfinity).Start, 5);
var extending = new EditClip { Start = 8, Duration = 1.9, Kind = "video" };
Near(ClipEditing.SnapToEdges(extending, [neighbor], 1, .17, 1.95).Duration, 1.9);
Near(ClipEditing.SnapToEdges(extending, [neighbor], 1, .17, 3).Duration, 2.017);
Console.WriteLine("PASS: frame snapping, move/layer clamps, trim limits, minimum duration, split continuity, IDs, image edits, rejected split boundaries");

var transformed = new EditClip { Kind = "video", Start = 2, Duration = 3, OffsetX = 100, OffsetY = 50, Scale = 1.5 };
var duplicate = ClipEditing.Duplicate(transformed);
Check(duplicate.Id != transformed.Id); Near(duplicate.Start, 5); Near(duplicate.Trim, transformed.Trim);
Near(duplicate.OffsetX, 100); Near(duplicate.OffsetY, 50); Near(duplicate.Scale, 1.5);
var settings = new VideoSettings();
var baseClip = new EditClip();
var bounds = ClipGeometry.Fit(baseClip, 640, 480, settings);
Near(bounds.X, 240); Near(bounds.Y, 0); Near(bounds.Width, 1440); Near(bounds.Height, 1080);
var translated = ClipGeometry.Move(baseClip, 80, -40);
var translatedBounds = ClipGeometry.Fit(translated, 640, 480, settings);
Near(translatedBounds.X, 320); Near(translatedBounds.Y, -40);
var enlarged = ClipGeometry.Resize(baseClip, bounds, bounds.X + bounds.Width * 1.5, bounds.Y + bounds.Height * 1.5, false, false);
var enlargedBounds = ClipGeometry.Fit(enlarged, 640, 480, settings);
Near(enlargedBounds.X, bounds.X); Near(enlargedBounds.Y, bounds.Y); Near(enlarged.Scale, 1.5);
Near(enlargedBounds.Width / enlargedBounds.Height, bounds.Width / bounds.Height);
var copiedProject = new EditProject { WorkspaceDirectory = "C:/Media", Clips = [transformed] }.Copy();
Near(copiedProject.Clips[0].Scale, 1.5); Check(copiedProject.WorkspaceDirectory == "C:/Media");
Check(ProjectDisplay.Position(0, 0, 30) == "00:00:00.000  0 / 0");
Check(ProjectDisplay.Position(.5, 4, 30) == "00:00:00.500  16 / 120");
Check(ProjectDisplay.Position(4, 4, 30) == "00:00:04.000  120 / 120");
Check(ProjectDisplay.Settings(new EditProject()).Contains("1920x1080  30fps  44.1kHz"));
Check(MediaFiles.OpenLabel("audio") == "音声を開く");
Check(MediaFiles.OpenLabel("image") == "画像を開く");
Check(MediaFiles.OpenLabel("video") == "動画を開く");
string workspaceDirectory = Path.GetFullPath("artifacts/workspace-checks");
Directory.CreateDirectory(Path.Combine(workspaceDirectory, "Child"));
File.WriteAllText(Path.Combine(workspaceDirectory, "sample.mp4"), "");
File.WriteAllText(Path.Combine(workspaceDirectory, "ignore.txt"), "");
var workspaceEntries = WorkspaceEntry.ReadDirectory(workspaceDirectory);
Check(workspaceEntries.Count == 2 && workspaceEntries[0].IsDirectory);
Check(workspaceEntries[1].Name == "sample.mp4");
Check(WorkspaceEntry.ReadDirectory(workspaceDirectory, foldersOnly: true).Count == 1);
Console.WriteLine("PASS: duplicate identity and transforms, fit/move/resize anchor, project persistence, status text, workspace filtering");

var occupied = new List<EditClip> { new() { Start = 1, Duration = 3, Layer = 1 }, new() { Start = 0, Duration = 5, Layer = 2 } };
Check(ClipPlacement.FirstAvailableLayer(occupied, 2, 2) == 3);
Check(ClipPlacement.FirstAvailableLayer(occupied, 4, 1) == 1);
Check(ClipPlacement.FirstAvailableLayer(occupied, 0, .5) == 1);
var full = Enumerable.Range(1, 8).Select(layer => new EditClip { Start = 0, Duration = 10, Layer = layer });
Check(ClipPlacement.FirstAvailableLayer(full, 2, 1) == 9);
Check(new EditProject { Clips = full.ToList() }.LayerCount == 9);
var movedGroup = ClipEditing.MoveGroup(occupied, -10, 20);
Near(movedGroup[0].Start - movedGroup[1].Start, occupied[0].Start - occupied[1].Start);
Check(movedGroup[1].Layer - movedGroup[0].Layer == 1 && movedGroup[1].Layer == 22);
Near(movedGroup.Min(clip => clip.Start), 0);
var advancedGroup = ClipEditing.MoveGroup(occupied, 2, 1);
Near(advancedGroup[0].Start, 3); Near(advancedGroup[1].Start, 2);
Check(advancedGroup[0].Layer == 2 && advancedGroup[1].Layer == 3);
var cornerClip = new EditClip { Scale = .5, OffsetX = -470, OffsetY = -265 };
var snappedCorner = ClipGeometry.SnapToCanvas(cornerClip, 1920, 1080, new VideoSettings(), 16);
var cornerBounds = ClipGeometry.Fit(snappedCorner, 1920, 1080, new VideoSettings());
Near(cornerBounds.X, 0); Near(cornerBounds.Y, 0);
var panelLayout = new PanelLayout(320, 280, 240);
var layoutPath = Path.GetFullPath("artifacts/workspace-checks/layout.json");
LayoutPreferences.Save(layoutPath, panelLayout);
Check(LayoutPreferences.Load(layoutPath) == panelLayout);
var clampedLayout = new PanelLayout(9999, 9999, 9999).Constrain(960, 800);
Check(clampedLayout.WorkspaceWidth + clampedLayout.InspectorWidth + 16 <= 960 - 240);
Check(clampedLayout.TimelineHeight <= 800 - 176 - 8);
File.WriteAllText(layoutPath, "invalid JSON");
Check(LayoutPreferences.Load(layoutPath) == new PanelLayout());
Console.WriteLine("PASS: first free layer, interval boundaries, full layers, relative group movement, corner snapping, saved panel positions and corrupt settings fallback");


{
    var subtitle = new EditClip { Kind = "subtitle", Text = "日本語の字幕\n二行目", Start = 1, Duration = 3, Layer = 9 };
    subtitle.Validate();
    var copy = ClipEditing.Duplicate(subtitle);
    copy.Subtitle.FontSize = 80;
    Check(subtitle.Subtitle.FontSize == 64 && copy.Text == subtitle.Text);
    var parts = ClipEditing.Split(subtitle, 2);
    Near(parts.Right.Trim, 0);
    var extended = ClipEditing.Resize(subtitle, 2, false, 0);
    Near(extended.Duration, 5);
    var saved = System.Text.Json.JsonSerializer.Serialize(new EditProject { Clips = [subtitle] });
    var restored = System.Text.Json.JsonSerializer.Deserialize<EditProject>(saved)!;
    restored.Validate();
    Check(restored.Clips[0].Text == subtitle.Text && restored.LayerCount == 10);
    var path = Path.Combine(Path.GetTempPath(), $"editor-preferences-{Guid.NewGuid():N}.json");
    try
    {
        new AppPreferences { Scale = 1.5, Subtitle = subtitle.Subtitle with { Vertical = "top", MarginY = 120 } }.Save(path);
        var preferences = AppPreferences.Load(path);
        Near(preferences.Scale, 1.5);
        Check(preferences.Subtitle.Vertical == "top" && preferences.Subtitle.MarginY == 120);
        File.WriteAllText(path, "{}");
        Near(AppPreferences.Load(path).Scale, 1);
        File.WriteAllText(path, "{\"Scale\":0,\"Subtitle\":null}");
        Near(AppPreferences.Load(path).Scale, 1);
    }
    finally { if (File.Exists(path)) File.Delete(path); }
    Check(new WorkspaceEntry("music.mp3", false).Icon == "\uE8D6");
    Console.WriteLine("PASS: dynamic layers, subtitle validation/clone/split/resize/persistence, preference persistence/fallback, audio icon");
}


