using System.Text.Json;

namespace VideoEditor;
/// <summary>Preferred panel sizes in DIPs, shared across application restarts.</summary>
public sealed record PanelLayout(double WorkspaceWidth = 240, double InspectorWidth = 280, double TimelineHeight = 288)
{
    public const double DividerSize = 8;
    public const double MinimumPreviewWidth = 240;
    public const double MinimumTopHeight = 176;
    public PanelLayout Constrain(double width, double height)
    {
        double left = Math.Clamp(FiniteOr(WorkspaceWidth, 240), 160, Math.Max(160, width - 224 - MinimumPreviewWidth - 2 * DividerSize));
        double right = Math.Clamp(FiniteOr(InspectorWidth, 280), 224, Math.Max(224, width - left - MinimumPreviewWidth - 2 * DividerSize));
        double bottom = Math.Clamp(FiniteOr(TimelineHeight, 288), 96, Math.Max(96, height - MinimumTopHeight - DividerSize));
        return new PanelLayout(left, right, bottom);
    }

    public PanelLayout SnapToGrid() => new(Snap(WorkspaceWidth), Snap(InspectorWidth), Snap(TimelineHeight));
    private static double Snap(double value) => Math.Round(value / 8) * 8;
    private static double FiniteOr(double value, double fallback) => double.IsFinite(value) ? value : fallback;
}

public static class LayoutPreferences
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HikariVideoEditor", "layout.json");

    public static PanelLayout Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<PanelLayout>(File.ReadAllText(path)) ?? new PanelLayout();
        }
        catch (Exception exception)when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new PanelLayout();
        }
    }

    public static void Save(string path, PanelLayout layout)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(layout));
        File.Move(temporary, fullPath, overwrite: true);
    }
}
