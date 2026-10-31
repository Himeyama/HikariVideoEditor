using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private PanelLayout _preferredLayout = LayoutPreferences.Load(LayoutPreferences.DefaultPath);
    private bool _resizingPanels;
    private double PanelContentWidth => Math.Max(0, Workspace.ActualWidth - Workspace.Padding.Left - Workspace.Padding.Right);
    private double PanelContentHeight => Math.Max(0, Workspace.ActualHeight - Workspace.Padding.Top - Workspace.Padding.Bottom - (StatusBar?.ActualHeight ?? 0));

    private void Workspace_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_resizingPanels)
            ApplyPanelLayout(_preferredLayout);
    }

    private void ApplyPanelLayout(PanelLayout requested)
    {
        if (WorkspaceColumn == null || PanelContentWidth <= 0 || PanelContentHeight <= 0)
            return;
        PanelLayout layout = requested.Constrain(PanelContentWidth, PanelContentHeight);
        WorkspaceColumn.Width = new GridLength(layout.WorkspaceWidth);
        InspectorColumn.Width = new GridLength(layout.InspectorWidth);
        TimelineRow.Height = new GridLength(layout.TimelineHeight);
    }

    private void Divider_DragStarted(object sender, EventArgs e) => _resizingPanels = true;
    private void Divider_DragDelta(object sender, PanelDragDeltaEventArgs e)
    {
        PanelLayout current = new(WorkspaceColumn.Width.Value, InspectorColumn.Width.Value, TimelineRow.Height.Value);
        string? panel = ((FrameworkElement)sender).Tag as string;
        PanelLayout requested = panel switch
        {
            "workspace" => current with
            {
                WorkspaceWidth = current.WorkspaceWidth + e.HorizontalChange / _preferences.Scale
            },
            "inspector" => current with
            {
                InspectorWidth = current.InspectorWidth - e.HorizontalChange / _preferences.Scale
            },
            "timeline" => current with
            {
                TimelineHeight = current.TimelineHeight - e.VerticalChange / _preferences.Scale
            },
            _ => current
        };
        ApplyPanelLayout(requested);
    }

    private void Divider_DragCompleted(object sender, EventArgs e)
    {
        _resizingPanels = false;
        _preferredLayout = new PanelLayout(WorkspaceColumn.Width.Value, InspectorColumn.Width.Value, TimelineRow.Height.Value).SnapToGrid();
        ApplyPanelLayout(_preferredLayout);
        SavePanelLayout();
    }

    private void SavePanelLayout()
    {
        if (_resizingPanels)
            _preferredLayout = new PanelLayout(WorkspaceColumn.Width.Value, InspectorColumn.Width.Value, TimelineRow.Height.Value).SnapToGrid();
        try
        {
            LayoutPreferences.Save(LayoutPreferences.DefaultPath, _preferredLayout);
        }
        catch (Exception exception)when (exception is IOException or UnauthorizedAccessException)
        {
            Status.Text = $"パネル位置を保存できません: {exception.Message}";
        }
    }
}


