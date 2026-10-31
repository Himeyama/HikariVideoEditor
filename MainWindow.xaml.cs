using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;

namespace VideoEditor;
public sealed partial class MainWindow : Window
{
    private EditProject project = new();
    private readonly Stack<EditProject> undo = new();
    private readonly Stack<EditProject> redo = new();
    private string? selected;
    private readonly Bridge bridge;
    private readonly SemaphoreSlim gate = new(1, 1);
    private MediaComposition? composition;
    private MediaSource? source;
    private MediaStreamSource? previewStream;
    private bool closed;
    public MainWindow()
    {
        InitializeComponent();
        UpdateRecentWorkspacesMenu();
        InitializePages();
        InitializeStatusMessages();
        Title = "Video Editor";
        var secondsFormat = new Windows.Globalization.NumberFormatting.DecimalFormatter
        {
            FractionDigits = 2,
            IntegerDigits = 1,
            NumberRounder = new Windows.Globalization.NumberFormatting.IncrementNumberRounder
            {
                Increment = 0.01,
                RoundingAlgorithm = Windows.Globalization.NumberFormatting.RoundingAlgorithm.RoundHalfAwayFromZero
            }
        };
        StartInput.NumberFormatter = secondsFormat;
        TrimInput.NumberFormatter = secondsFormat;
        DurationInput.NumberFormatter = secondsFormat;
        VolumeInput.NumberFormatter = secondsFormat;
        LayerInput.NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter { FractionDigits = 0, IntegerDigits = 1 };
        ConfigureWindow();
        ApplyAppScale();
        InitializePlayback();
        bridge = new Bridge(DispatchCommand);
        Closed += (_, _) =>
        {
            SavePanelLayout();
            closed = true;
            exportCancellation?.Cancel();
            downloadCancellation?.Cancel();
            playbackTimer.Stop();
            DisposeStatusMessages();
            if (Root.XamlRoot != null)
                Root.XamlRoot.Changed -= XamlRoot_Changed;
            bridge.Dispose();
            Preview.MediaPlayer?.Dispose();
            source?.Dispose();
        };
        Draw();
        McpPortStatus.Text = $":{bridge.Port}";
    }

    private async Task Run(Func<Task> action)
    {
        await gate.WaitAsync();
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
        }
        finally
        {
            gate.Release();
        }
    }

    private void Remember()
    {
        Remember(project);
    }

    private void Remember(EditProject previous)
    {
        undo.Push(previous.Copy());
        redo.Clear();
        UpdateHistoryButtons();
    }

    private void UpdateHistoryButtons()
    {
        UndoButton.IsEnabled = undo.Count > 0;
        RedoButton.IsEnabled = redo.Count > 0;
    }

    private void Draw()
    {
        UpdateHistoryButtons();
        ClipEditing.FrameRate = project.Video.FrameRate;
        if (selected == null) DisplaySubtitleSettings(null);
        selectedIds.RemoveWhere(id => !project.Clips.Any(clip => clip.Id == id));
        UpdateVideoCanvas();
        DrawTimeline();
    }

    private void Select(EditClip c, bool redraw = true, bool preserveSelection = false)
    {
        if (!preserveSelection)
            selectedIds.Clear();
        selectedIds.Add(c.Id);
        selected = c.Id;
        SelectedName.Text = selectedIds.Count > 1 ? $"{selectedIds.Count} 素材を選択" : c.ToString();
        DisplayObjectValues(c);
        DisplaySubtitleSettings(c);
        UpdateTransformOverlay();
        if (redraw)
            Draw();
    }

    private async Task<EditClip> Add(string path, double? insertionTime = null)
    {
        path = Path.GetFullPath(path);
        var kind = MediaFiles.Kind(path);
        var c = new EditClip
        {
            Path = path,
            Kind = kind,
            Start = project.Clips.Select(x => x.Start + x.Duration).DefaultIfEmpty(0).Max()
        };
        var file = await StorageFile.GetFileFromPathAsync(path);
        if (kind == "video")
            c.Duration = (await MediaClip.CreateFromFileAsync(file)).OriginalDuration.TotalSeconds;
        if (kind == "audio")
            c.Duration = (await BackgroundAudioTrack.CreateFromFileAsync(file)).OriginalDuration.TotalSeconds;
        if (insertionTime.HasValue)
        {
            c.Start = insertionTime.Value;
            c.Layer = ClipPlacement.FirstAvailableLayer(project.Clips, c.Start, c.Duration);
        }

        c.Validate();
        Remember();
        project.Clips.Add(c);
        Select(c);
        return c;
    }

    private void InitPicker(object picker) => WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
    private async void Import_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        // Use the desktop dialog: the WinRT picker can return E_ACCESSDENIED
        // when this unpackaged desktop app inherits an elevated process token.
        using var picker = new System.Windows.Forms.OpenFileDialog
        {
            Title = "素材を追加",
            Filter = $"対応する素材|{string.Join(";", MediaFiles.Extensions.Select(extension => "*" + extension))}",
            Multiselect = true,
            CheckFileExists = true,
            InitialDirectory = workspaceCurrentDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            RestoreDirectory = true
        };
        var owner = new FileDialogOwner(WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (picker.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK)
            return;
        foreach (var path in picker.FileNames)
            await Add(path);
        await Refresh();
    });

    private sealed record FileDialogOwner(IntPtr Handle) : System.Windows.Forms.IWin32Window;
    private async Task Refresh() => await Refresh(await CompositionBuilder.Build(project));

    private Task Refresh(MediaComposition next)
    {
        var nextStream = next.GeneratePreviewMediaStreamSource(project.Video.Width, project.Video.Height);
        var nextSource = MediaSource.CreateFromMediaStreamSource(nextStream);
        HideStill();
        pendingSeek = Math.Clamp(playhead, 0, ProjectEnd);
        resumePlayback = Preview.MediaPlayer.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing;
        previewOpening = true;
        playbackError = null;
        Preview.MediaPlayer.Source = nextSource;
        source?.Dispose();
        source = nextSource;
        composition = next;
        previewStream = nextStream;
        UpdatePosition(pendingSeek);
        Status.Text = "プレビューを更新しました";
        return Task.CompletedTask;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Run(Refresh);
    private async void Apply_Click(object sender, RoutedEventArgs e) => await ApplyObjectSettings(null);
    private async void Delete_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (selected == null)
            return;
        Remember();
        project.Clips.RemoveAll(c => c.Id == selected);
        selectedIds.Clear();
        selected = null;
        SelectedName.Text = "クリップ未選択";
        Draw();
        await Refresh();
    });
    private async void Undo_Click(object sender, RoutedEventArgs e) => await Run(() => RestoreHistory(undo, redo));
    private async void Redo_Click(object sender, RoutedEventArgs e) => await Run(() => RestoreHistory(redo, undo));

    private async Task RestoreHistory(Stack<EditProject> from, Stack<EditProject> to)
    {
        if (from.TryPeek(out var previous))
        {
            var prepared = await CompositionBuilder.Build(previous);
            to.Push(project.Copy());
            from.Pop();
            project = previous;
            selectedIds.Clear();
            selected = null;
            SelectedName.Text = "クリップ未選択";
            Draw();
            await Refresh(prepared);
        }
    }
}









