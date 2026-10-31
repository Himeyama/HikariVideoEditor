using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private MenuFlyout CreateClipContextMenu(EditClip clip)
    {
        MenuFlyout menu = new();
        MenuFlyoutItem duplicate = new()
        {
            Text = "複製"
        };
        duplicate.Click += async (_, _) => await Run(() => DuplicateClip(clip.Id));
        MenuFlyoutItem split = new()
        {
            Text = "再生位置で分割"
        };
        split.Click += async (_, _) => await Run(async () =>
        {
            await Split(clip.Id, _playhead);
        });
        MenuFlyoutItem open = new()
        {
            Text = MediaFiles.OpenLabel(clip.Kind)
        };
        open.Click += async (_, _) => await Run(() => OpenMediaFile(clip.Path));
        MenuFlyoutItem delete = new()
        {
            Text = "削除"
        };
        delete.Click += (_, _) =>
        {
            Select(clip, preserveSelection: _selectedIds.Contains(clip.Id));
            Delete_Click(this, new RoutedEventArgs());
        };
        menu.Items.Add(CreateAddSubtitleItem(_playhead, clip.Layer));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(duplicate);
        menu.Items.Add(split);
        if (clip.Kind == "subtitle")
        {
            MenuFlyoutItem edit = new() { Text = "字幕を編集…" };
            edit.Click += async (_, _) => await Run(() => EditSubtitle(clip.Id));
            menu.Items.Add(edit);
        }
        else menu.Items.Add(open);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(delete);
        MenuFlyoutItem ripple = new() { Text = "リップル削除（同じレイヤーを詰める）" };
        ripple.Click += async (_, _) => await Run(async () =>
        {
            Select(clip, preserveSelection: _selectedIds.Contains(clip.Id));
            await DeleteSelection(true);
        });
        menu.Items.Add(ripple);
        return menu;
    }

    private async Task<EditClip> DuplicateClip(string id)
    {
        EditClip original = _project.Clips.Single(clip => clip.Id == id);
        _project.RequireEditable(original.Layer);
        EditClip duplicate = ClipEditing.Duplicate(original);
        EditProject next = _project.Copy();
        next.Clips.Add(duplicate);
        await CompositionBuilder.Build(next);
        Remember();
        _project = next;
        Select(duplicate);
        await Refresh();
        return duplicate;
    }
}


