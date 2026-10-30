using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private MenuFlyout CreateClipContextMenu(EditClip clip)
    {
        var menu = new MenuFlyout();
        var duplicate = new MenuFlyoutItem
        {
            Text = "複製"
        };
        duplicate.Click += async (_, _) => await Run(() => DuplicateClip(clip.Id));
        var split = new MenuFlyoutItem
        {
            Text = "再生位置で分割"
        };
        split.Click += async (_, _) => await Run(async () =>
        {
            await Split(clip.Id, playhead);
        });
        var open = new MenuFlyoutItem
        {
            Text = MediaFiles.OpenLabel(clip.Kind)
        };
        open.Click += async (_, _) => await Run(() => OpenMediaFile(clip.Path));
        var delete = new MenuFlyoutItem
        {
            Text = "削除"
        };
        delete.Click += (_, _) =>
        {
            selected = clip.Id;
            Delete_Click(this, new RoutedEventArgs());
        };
        menu.Items.Add(CreateAddSubtitleItem(playhead, clip.Layer));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(duplicate);
        menu.Items.Add(split);
        if (clip.Kind == "subtitle")
        {
            var edit = new MenuFlyoutItem { Text = "字幕を編集…" };
            edit.Click += async (_, _) => await Run(() => EditSubtitle(clip.Id));
            menu.Items.Add(edit);
        }
        else menu.Items.Add(open);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(delete);
        return menu;
    }

    private async Task<EditClip> DuplicateClip(string id)
    {
        var original = project.Clips.Single(clip => clip.Id == id);
        var duplicate = ClipEditing.Duplicate(original);
        var next = project.Copy();
        next.Clips.Add(duplicate);
        await CompositionBuilder.Build(next);
        Remember();
        project = next;
        Select(duplicate);
        await Refresh();
        return duplicate;
    }
}


