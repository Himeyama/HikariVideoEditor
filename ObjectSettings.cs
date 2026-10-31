using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Globalization.NumberFormatting;

namespace VideoEditor;

public sealed partial class MainWindow
{
    private SubtitleSettingsEditor? _objectSubtitleEditor;
    private void DisplaySubtitleSettings(EditClip? clip)
    {
        SubtitleInspector.Visibility = clip?.Kind == "subtitle" ? Visibility.Visible : Visibility.Collapsed;
        _objectSubtitleEditor = null;
        SubtitleStyleHost.Content = null;
        if (clip?.Kind != "subtitle") return;
        SubtitleTextInput.Text = clip.Text;
        _objectSubtitleEditor = new SubtitleSettingsEditor(clip.Subtitle, compact: true);
        _objectSubtitleEditor.ValidationFailed += message => Status.Text = message;
        SubtitleStyleHost.Content = _objectSubtitleEditor;
    }

    private bool _applyingObjectSettings;
    private bool _displayingObjectValues;
    private bool _processingObjectSteps;
    private (string Id, NumberBox Input, double Value)? _pendingObjectStep;
    private readonly Dictionary<NumberBox, string> _displayedObjectValues = new();

    private void DisplayObjectValues(EditClip clip)
    {
        AudioInspector.Visibility = clip.Kind is "audio" or "video" ? Visibility.Visible : Visibility.Collapsed;
        _displayingObjectValues = true;
        try
        {
        foreach ((NumberBox input, double value) in new[]
        {
            (StartInput, clip.Start), (TrimInput, clip.Trim), (DurationInput, clip.Duration),
            (LayerInput, (double)clip.Layer), (VolumeInput, clip.Volume),
            (FadeInInput, clip.FadeIn), (FadeOutInput, clip.FadeOut)
        })
        {
            input.Value = value;
            // Setting the same Value does not restore an invalid edit's Text.
            input.Text = input.NumberFormatter.FormatDouble(value);
            _displayedObjectValues[input] = input.Text;
        }
        }
        finally { _displayingObjectValues = false; }
    }

    private async void ObjectSettings_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        // Handle Enter before NumberBox normalizes/clamps its text or reverts an invalid number.
        e.Handled = true;
        if (e.KeyStatus.WasKeyDown) return;
        DependencyObject? focused = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
        while (focused != null && focused is not NumberBox)
            focused = VisualTreeHelper.GetParent(focused);
        if (focused is NumberBox input)
            await ApplyObjectSettings(input);
    }

    private async Task ApplyObjectSettings(NumberBox? entered, bool releaseFocus = true, double? steppedValue = null, string? expectedId = null)
    {
        if (_applyingObjectSettings) return;
        _applyingObjectSettings = true;
        using IDisposable operation = BeginStatusOperation();
        await _gate.WaitAsync();
        EditClip? original = null;
        NumberBox? invalidInput = entered;
        try
        {
            if (expectedId != null && _selected != expectedId) return;
            original = _project.Clips.FirstOrDefault(clip => clip.Id == _selected)
                ?? throw new ArgumentException("クリップを選択してください。");
            _project.RequireEditable(original.Layer);

            double Read(NumberBox input, double previous, string label)
            {
                if (steppedValue.HasValue)
                {
                    if (input != entered) return previous;
                    double value = steppedValue.Value;
                    if (!double.IsFinite(value) || value < input.Minimum || value > input.Maximum || (input == LayerInput && value != Math.Truncate(value)))
                        throw new ArgumentException($"{label}の値が不正です。");
                    return value;
                }
                // Preserve full precision when an unchanged value is displayed to two decimal places.
                if (_displayedObjectValues.TryGetValue(input, out string? displayed) && input.Text == displayed)
                    return previous;
                string text = input.Text.Trim();
                double? parsed = input.NumberFormatter is INumberParser parser ? parser.ParseDouble(text) : null;
                if (parsed == null && double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double number))
                    parsed = number;
                if (string.IsNullOrEmpty(text) || !parsed.HasValue || !double.IsFinite(parsed.Value) ||
                    parsed.Value < input.Minimum || parsed.Value > input.Maximum ||
                    (input == LayerInput && parsed.Value != Math.Truncate(parsed.Value)))
                {
                    invalidInput = input;
                    throw new ArgumentException($"{label}の値が不正です。");
                }
                return parsed.Value;
            }

            EditProject next = _project.Copy();
            EditClip edit = next.Clips.Single(clip => clip.Id == original.Id);
            edit.Start = Read(StartInput, original.Start, "開始位置");
            edit.Trim = Read(TrimInput, original.Trim, "素材開始");
            edit.Duration = Read(DurationInput, original.Duration, "長さ");
            edit.Layer = checked((int)Read(LayerInput, original.Layer, "レイヤー"));
            edit.Volume = Read(VolumeInput, original.Volume, "音量");
            edit.FadeIn = Read(FadeInInput, original.FadeIn, "フェードイン");
            edit.FadeOut = Read(FadeOutInput, original.FadeOut, "フェードアウト");
            _project.RequireEditable(edit.Layer);
            if (edit.Kind == "subtitle" && releaseFocus && _objectSubtitleEditor != null)
            {
                edit.Text = SubtitleTextInput.Text;
                edit.Subtitle = _objectSubtitleEditor.Read();
                edit.SubtitleOverrides = SubtitleOverrides.Explicit(edit.Subtitle);
            }
            Windows.Media.Editing.MediaComposition prepared = await CompositionBuilder.Build(next);
            EditProject previous = _project;
            _project = next;
            try
            {
                await Refresh(prepared);
            }
            catch
            {
                _project = previous;
                throw;
            }
            Remember(previous);
            await AutoSaveChanges(System.Text.Json.JsonSerializer.Serialize(previous));
            Draw();
            DisplayObjectValues(edit);
            SelectedName.Text = edit.ToString();
            if (releaseFocus) TimelineScroll.Focus(FocusState.Programmatic);
            Status.Text = "オブジェクト設定を反映しました。";
        }
        catch (Exception exception)
        {
            if (original != null)
                DisplayObjectValues(original);
            Status.Text = exception.Message;
            NumberBox input = invalidInput ?? StartInput;
            input.Focus(FocusState.Programmatic);
            // NumberBox contains the actual editable TextBox in its template.
            if (FindInputTextBox(input) is { } textBox)
            {
                textBox.Focus(FocusState.Programmatic);
                textBox.SelectAll();
            }
        }
        finally
        {
            _gate.Release();
            _applyingObjectSettings = false;
        }
    }

    private async void ObjectInput_LostFocus(object sender, RoutedEventArgs args)
    {
        if (sender is not NumberBox input || _displayingObjectValues || _applyingObjectSettings || _processingObjectSteps || _selected == null)
            return;
        if (_displayedObjectValues.TryGetValue(input, out string? displayed) && input.Text == displayed) return;
        await ApplyObjectSettings(input, releaseFocus: false, expectedId: _selected);
    }

    private async void ObjectInput_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_displayingObjectValues || _selected == null || !double.IsFinite(args.NewValue) ||
            !_project.Clips.Any(c => c.Id == _selected)) return;
        double minimum = sender == LayerInput ? 1 : sender == DurationInput ? 1.0 / _project.Video.FrameRate : 0;
        double maximum = sender == VolumeInput ? 1 : sender.Maximum;
        double normalized = Math.Clamp(args.NewValue, minimum, maximum);
        if (sender == LayerInput) normalized = Math.Round(normalized, MidpointRounding.AwayFromZero);
        if (sender == VolumeInput) normalized = Math.Round(normalized, 2, MidpointRounding.AwayFromZero);
        if (normalized != args.NewValue)
        {
            _displayingObjectValues = true;
            try
            {
                sender.Value = normalized;
                sender.Text = sender.NumberFormatter.FormatDouble(normalized);
            }
            finally { _displayingObjectValues = false; }
        }
        EditClip clip = _project.Clips.Single(c => c.Id == _selected);
        double existing = sender == StartInput ? clip.Start : sender == TrimInput ? clip.Trim :
            sender == DurationInput ? clip.Duration : sender == LayerInput ? clip.Layer :
            sender == FadeInInput ? clip.FadeIn : sender == FadeOutInput ? clip.FadeOut : clip.Volume;
        if (normalized == existing) return;
        _pendingObjectStep = (_selected, sender, normalized);
        if (_processingObjectSteps) return;
        _processingObjectSteps = true;
        try
        {
            while (_pendingObjectStep != null && !_closed)
            {
                while (_applyingObjectSettings && !_closed) await Task.Delay(16);
                if (_closed) break;
                (string Id, NumberBox Input, double Value) step = _pendingObjectStep.Value;
                _pendingObjectStep = null;
                await ApplyObjectSettings(step.Input, releaseFocus: false, steppedValue: step.Value, expectedId: step.Id);
            }
        }
        finally { _processingObjectSteps = false; }
    }
    private static TextBox? FindInputTextBox(DependencyObject parent)
    {
        if (parent is TextBox textBox) return textBox;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindInputTextBox(VisualTreeHelper.GetChild(parent, i)) is { } child)
                return child;
        return null;
    }
}



