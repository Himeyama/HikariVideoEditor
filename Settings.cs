using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VideoEditor;

public sealed partial class MainWindow
{
    private AppPreferences preferences = AppPreferences.Load(AppPreferences.DefaultPath);

    private void AppHost_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyAppScale();

    private void ApplyAppScale()
    {
        if (Root == null || AppHost == null || AppHost.ActualWidth <= 0 || AppHost.ActualHeight <= 0) return;
        Root.Width = AppHost.ActualWidth / preferences.Scale;
        Root.Height = AppHost.ActualHeight / preferences.Scale;
        UpdateTitleBarInsets();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        Pause();
        var scale = SubtitleSettingsEditor.Choice(new[] { "37.5%", "50%", "75%", "80%", "90%", "95%", "100%", "125%", "150%", "175%", "200%" },
            Array.IndexOf(new[] { .375, .5, .75, .8, .9, .95, 1, 1.25, 1.5, 1.75, 2 }, preferences.Scale));
        var autoSave = new ToggleSwitch { IsOn = preferences.AutoSaveEnabled, OnContent = "有効", OffContent = "無効" };
        var editor = new SubtitleSettingsEditor(preferences.Subtitle);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 24, Width = Math.Max(0, Root.Width - 64), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(32) };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        var back = new Button { Content = new FontIcon { Glyph = "\uE72B", FontSize = 16 }, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(back, "編集画面へ戻る");
        back.Click += (_, _) => CloseSettingsPage();
        heading.Children.Add(back);
        heading.Children.Add(new TextBlock { Text = "設定", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(heading);
        content.Children.Add(new TextBlock { Text = "一般", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(SubtitleSettingsEditor.Group("表示", ("アプリの倍率", scale)));
        content.Children.Add(SubtitleSettingsEditor.Group("保存", ("自動保存", autoSave)));
        content.Children.Add(new TextBlock { Text = "字幕の既定値", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(editor);
        content.Children.Add(error);
        void SavePreferences(SubtitleStyle subtitle)
        {
            try
            {
                var next = new AppPreferences { AutoSaveEnabled = autoSave.IsOn, Scale = new[] { .375, .5, .75, .8, .9, .95, 1, 1.25, 1.5, 1.75, 2 }[scale.SelectedIndex], Subtitle = subtitle, RecentWorkspaces = preferences.RecentWorkspaces };
                next.VoicevoxFavorites = preferences.VoicevoxFavorites;
                next.Save(AppPreferences.DefaultPath);
                preferences = next;
                ApplyAppScale();
                error.Text = "";
            }
            catch (Exception exception) { error.Text = exception.Message; }
        }
        scale.SelectionChanged += (_, _) => SavePreferences(preferences.Subtitle);
        autoSave.Toggled += (_, _) => SavePreferences(preferences.Subtitle);
        editor.StyleChanged += SavePreferences;
        editor.ValidationFailed += message => error.Text = message;
        SettingsPage.Content = content;
        Workspace.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Visible;
        NavigateToContent(typeof(SettingsContentPage), SettingsPage);
    }

    private void SettingsPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SettingsPage.Content is FrameworkElement content)
            content.Width = Math.Max(0, SettingsPage.ActualWidth - 64);
    }

    private void CloseSettingsPage()
    {
        SettingsPage.Visibility = Visibility.Collapsed;
        SettingsPage.Content = null;
        Workspace.Visibility = Visibility.Visible;
        NavigateToContent(typeof(EditorContentPage), Workspace);
        ApplyPanelLayout(preferredLayout);
    }
    private async void AddSubtitle_Click(object sender, RoutedEventArgs e) => await Run(() => EditSubtitle(null));

    private async void EditSubtitle_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var clip = project.Clips.FirstOrDefault(c => c.Id == selected && c.Kind == "subtitle")
            ?? throw new ArgumentException("編集する字幕を選択してください。");
        await EditSubtitle(clip.Id);
    });

    private async Task EditSubtitle(string? id, double? insertionTime = null, int? insertionLayer = null)
    {
        var original = project.Clips.FirstOrDefault(c => c.Id == id);
        if (original != null) project.RequireEditable(original.Layer);
        var text = new TextBox { Text = original?.Text ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 112, MaxLength = 10000, Header = "字幕テキスト（必須）", PlaceholderText = "表示する字幕を入力" };
        var start = SubtitleSettingsEditor.Number(original?.Start ?? insertionTime ?? playhead, 0, 86400);
        var duration = SubtitleSettingsEditor.Number(original?.Duration ?? 5, ClipEditing.MinimumDuration, 86400);
        var editor = new SubtitleSettingsEditor(original?.Subtitle ?? preferences.Subtitle, optional: true, defaults: preferences.Subtitle,
            overrides: original == null ? null : original.SubtitleOverrides ?? SubtitleOverrides.Explicit(original.Subtitle));
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 24, MinWidth = 480 };
        content.Children.Add(text);
        content.Children.Add(SubtitleSettingsEditor.Group("表示時間", ("開始位置（秒）", start), ("長さ（秒）", duration)));
        content.Children.Add(new Expander { Header = "書式（オプション）", Content = editor, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = id == null ? "字幕を追加" : "字幕を編集",
            Content = new ScrollViewer { Content = content, MaxHeight = Math.Max(200, Math.Min(560, AppHost.ActualHeight - 220)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = id == null ? "追加" : "適用", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary
        };
        EditClip? candidate = null;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                var clip = original == null ? new EditClip { Kind = "subtitle", Subtitle = preferences.Subtitle with { } } : ClipEditing.Clone(original);
                if (string.IsNullOrWhiteSpace(text.Text))
                    throw new ArgumentException("字幕テキストを入力してください。");
                clip.Text = text.Text;
                clip.Start = ClipEditing.Snap(ReadDialogNumber(start, "開始位置"));
                clip.Duration = Math.Max(ClipEditing.MinimumDuration, ClipEditing.Snap(ReadDialogNumber(duration, "長さ")));
                clip.Subtitle = editor.Read();
                clip.SubtitleOverrides = editor.ReadOverrides();
                if (original == null)
                    clip.Layer = insertionLayer ?? Math.Min(2000, project.LayerCount);
                clip.Validate();
                project.RequireEditable(clip.Layer);
                var next = project.Copy();
                next.Clips.RemoveAll(c => c.Id == clip.Id);
                next.Clips.Add(clip);
                await CompositionBuilder.Build(next);
                candidate = clip;
            }
            catch (Exception exception)
            {
                args.Cancel = true;
                error.Text = exception.Message;
            }
            finally { deferral.Complete(); }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || candidate == null) return;
        Remember();
        project.Clips.RemoveAll(c => c.Id == candidate.Id);
        project.Clips.Add(candidate);
        Select(candidate);
        await Refresh();
    }
}





