using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private PanelLayout preferredLayout = LayoutPreferences.Load(LayoutPreferences.DefaultPath);
    private bool resizingPanels;
    private double PanelContentWidth => Math.Max(0, Workspace.ActualWidth - Workspace.Padding.Left - Workspace.Padding.Right);
    private double PanelContentHeight => Math.Max(0, Workspace.ActualHeight - Workspace.Padding.Top - Workspace.Padding.Bottom - (StatusBar?.ActualHeight ?? 0));

    private void Workspace_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!resizingPanels)
            ApplyPanelLayout(preferredLayout);
    }

    private void ApplyPanelLayout(PanelLayout requested)
    {
        if (WorkspaceColumn == null || PanelContentWidth <= 0 || PanelContentHeight <= 0)
            return;
        var layout = requested.Constrain(PanelContentWidth, PanelContentHeight);
        WorkspaceColumn.Width = new GridLength(layout.WorkspaceWidth);
        InspectorColumn.Width = new GridLength(layout.InspectorWidth);
        TimelineRow.Height = new GridLength(layout.TimelineHeight);
    }

    private void Divider_DragStarted(object sender, EventArgs e) => resizingPanels = true;
    private void Divider_DragDelta(object sender, PanelDragDeltaEventArgs e)
    {
        var current = new PanelLayout(WorkspaceColumn.Width.Value, InspectorColumn.Width.Value, TimelineRow.Height.Value);
        string? panel = ((FrameworkElement)sender).Tag as string;
        var requested = panel switch
        {
            "workspace" => current with
            {
                WorkspaceWidth = current.WorkspaceWidth + e.HorizontalChange / preferences.Scale
            },
            "inspector" => current with
            {
                InspectorWidth = current.InspectorWidth - e.HorizontalChange / preferences.Scale
            },
            "timeline" => current with
            {
                TimelineHeight = current.TimelineHeight - e.VerticalChange / preferences.Scale
            },
            _ => current
        };
        ApplyPanelLayout(requested);
    }

    private void Divider_DragCompleted(object sender, EventArgs e)
    {
        resizingPanels = false;
        preferredLayout = new PanelLayout(WorkspaceColumn.Width.Value, InspectorColumn.Width.Value, TimelineRow.Height.Value).SnapToGrid();
        ApplyPanelLayout(preferredLayout);
        SavePanelLayout();
    }

    private void SavePanelLayout()
    {
        if (resizingPanels)
            preferredLayout = new PanelLayout(WorkspaceColumn.Width.Value, InspectorColumn.Width.Value, TimelineRow.Height.Value).SnapToGrid();
        try
        {
            LayoutPreferences.Save(LayoutPreferences.DefaultPath, preferredLayout);
        }
        catch (Exception exception)when (exception is IOException or UnauthorizedAccessException)
        {
            Status.Text = $"パネル位置を保存できません: {exception.Message}";
        }
    }
}


