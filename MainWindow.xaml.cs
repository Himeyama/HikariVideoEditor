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
    private EditProject _project = new();
    private readonly Stack<EditProject> _undo = new();
    private readonly Stack<EditProject> _redo = new();
    private string? _selected;
    private readonly Bridge _bridge;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MediaComposition? _composition;
    private MediaSource? _source;
    private MediaStreamSource? _previewStream;
    private bool _closed;
    public MainWindow()
    {
        InitializeComponent();
        _savedProjectSnapshot = ProjectSnapshot();
        AppWindow.Closing += Window_Closing;
        UpdateRecentWorkspacesMenu();
        InitializePages();
        InitializeStatusMessages();
        Title = "Video Editor";
        Windows.Globalization.NumberFormatting.DecimalFormatter secondsFormat = new()
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
        FadeInInput.NumberFormatter = secondsFormat;
        FadeOutInput.NumberFormatter = secondsFormat;
        LayerInput.NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter { FractionDigits = 0, IntegerDigits = 1 };
        foreach (NumberBox input in new[] { StartInput, TrimInput, DurationInput, LayerInput, VolumeInput, FadeInInput, FadeOutInput })
            input.LostFocus += ObjectInput_LostFocus;
        ConfigureWindow();
        ApplyAppScale();
        InitializePlayback();
        _bridge = new Bridge(DispatchCommand);
        Closed += (_, _) =>
        {
            _closed = true;
            _workspaceWatcher?.Dispose();
            _workspaceRefreshTimer?.Stop();
            _exportCancellation?.Cancel();
            _downloadCancellation?.Cancel();
            _playbackTimer.Stop();
            DisposeStatusMessages();
            if (Root.XamlRoot != null)
                Root.XamlRoot.Changed -= XamlRoot_Changed;
            _bridge.Dispose();
            Preview.MediaPlayer?.Dispose();
            _source?.Dispose();
        };
        Draw();
        McpPortStatus.Text = $":{_bridge.Port}";
        InitializeVoicevox();
    }

    private async Task Run(Func<Task> action)
    {
        using IDisposable operation = BeginStatusOperation();
        await _gate.WaitAsync();
        try
        {
            string previous = ProjectSnapshot();
            await action();
            await AutoSaveChanges(previous);
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Remember()
    {
        Remember(_project);
    }

    private void Remember(EditProject previous)
    {
        _undo.Push(previous.Copy());
        _redo.Clear();
        UpdateHistoryButtons();
    }

    private void UpdateHistoryButtons()
    {
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
    }

    private void Draw()
    {
        UpdateGenerationAvailability();
        UpdateHistoryButtons();
        ClipEditing.FrameRate = _project.Video.FrameRate;
        if (_selected == null)
        {
            DisplaySubtitleSettings(null);
            AudioInspector.Visibility = Visibility.Collapsed;
        }
        _selectedIds.RemoveWhere(id => !_project.Clips.Any(clip => clip.Id == id));
        UpdateVideoCanvas();
        DrawTimeline();
    }

    private void Select(EditClip c, bool redraw = true, bool preserveSelection = false)
    {
        if (!preserveSelection)
            _selectedIds.Clear();
        _selectedIds.Add(c.Id);
        _selected = c.Id;
        SelectedName.Text = _selectedIds.Count > 1 ? $"{_selectedIds.Count} 素材を選択" : c.ToString();
        DisplayObjectValues(c);
        DisplaySubtitleSettings(c);
        UpdateTransformOverlay();
        if (redraw)
            Draw();
    }

    private async Task<EditClip> Add(string path, double? insertionTime = null)
    {
        path = Path.GetFullPath(path);
        string kind = MediaFiles.Kind(path);
        EditClip c = new()
        {
            Path = path,
            Kind = kind,
            Start = _project.Clips.Select(x => x.Start + x.Duration).DefaultIfEmpty(0).Max()
        };
        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        if (kind == "video")
            c.Duration = (await MediaClip.CreateFromFileAsync(file)).OriginalDuration.TotalSeconds;
        if (kind == "audio")
            c.Duration = (await BackgroundAudioTrack.CreateFromFileAsync(file)).OriginalDuration.TotalSeconds;
        if (insertionTime.HasValue)
        {
            c.Start = insertionTime.Value;
            c.Layer = ClipPlacement.FirstAvailableLayer(_project.Clips, c.Start, c.Duration);
        }

        c.Validate();
        while (_project.Layer(c.Layer).Locked && c.Layer < 2000) c.Layer++;
        _project.RequireEditable(c.Layer);
        Remember();
        _project.Clips.Add(c);
        Select(c);
        return c;
    }

    private void InitPicker(object picker) => WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
    private async void Import_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        // Use the desktop dialog: the WinRT picker can return E_ACCESSDENIED
        // when this unpackaged desktop app inherits an elevated process token.
        using System.Windows.Forms.OpenFileDialog picker = new()
        {
            Title = "素材を追加",
            Filter = $"対応する素材|{string.Join(";", MediaFiles.Extensions.Select(extension => "*" + extension))}",
            Multiselect = true,
            CheckFileExists = true,
            InitialDirectory = _workspaceCurrentDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            RestoreDirectory = true
        };
        FileDialogOwner owner = new(WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (picker.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK)
            return;
        foreach (string path in picker.FileNames)
            await Add(path);
        await Refresh();
    });

    private sealed record FileDialogOwner(IntPtr Handle) : System.Windows.Forms.IWin32Window;
    private async Task Refresh() => await Refresh(await CompositionBuilder.Build(_project));

    private Task Refresh(MediaComposition next)
    {
        MediaStreamSource nextStream = next.GeneratePreviewMediaStreamSource(_project.Video.Width, _project.Video.Height);
        MediaSource nextSource = MediaSource.CreateFromMediaStreamSource(nextStream);
        HideStill();
        _pendingSeek = Math.Clamp(_playhead, 0, ProjectEnd);
        _resumePlayback = Preview.MediaPlayer.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing;
        _previewOpening = true;
        _playbackError = null;
        Preview.MediaPlayer.Source = nextSource;
        _source?.Dispose();
        _source = nextSource;
        _composition = next;
        _previewStream = nextStream;
        UpdatePosition(_pendingSeek);
        Status.Text = "プレビューを更新しました";
        return Task.CompletedTask;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Run(Refresh);
    private async void Apply_Click(object sender, RoutedEventArgs e) => await ApplyObjectSettings(null);
    private async void Delete_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        await DeleteSelection(false);
    });
    private async void Undo_Click(object sender, RoutedEventArgs e) => await Run(() => RestoreHistory(_undo, _redo));
    private async void Redo_Click(object sender, RoutedEventArgs e) => await Run(() => RestoreHistory(_redo, _undo));

    private async Task RestoreHistory(Stack<EditProject> from, Stack<EditProject> to)
    {
        if (from.TryPeek(out EditProject? previous))
        {
            MediaComposition prepared = await CompositionBuilder.Build(previous);
            to.Push(_project.Copy());
            from.Pop();
            _project = previous;
            _selectedIds.Clear();
            _selected = null;
            SelectedName.Text = "クリップ未選択";
            Draw();
            await Refresh(prepared);
        }
    }
}









