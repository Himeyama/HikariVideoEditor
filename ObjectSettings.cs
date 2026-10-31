using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Globalization.NumberFormatting;

namespace VideoEditor;

public sealed partial class MainWindow
{
    private SubtitleSettingsEditor? objectSubtitleEditor;
    private void DisplaySubtitleSettings(EditClip? clip)
    {
        SubtitleInspector.Visibility = clip?.Kind == "subtitle" ? Visibility.Visible : Visibility.Collapsed;
        objectSubtitleEditor = null;
        SubtitleStyleHost.Content = null;
        if (clip?.Kind != "subtitle") return;
        SubtitleTextInput.Text = clip.Text;
        objectSubtitleEditor = new SubtitleSettingsEditor(clip.Subtitle, compact: true);
        objectSubtitleEditor.ValidationFailed += message => Status.Text = message;
        SubtitleStyleHost.Content = objectSubtitleEditor;
    }

    private bool applyingObjectSettings;
    private bool displayingObjectValues;
    private bool processingObjectSteps;
    private (string Id, NumberBox Input, double Value)? pendingObjectStep;
    private readonly Dictionary<NumberBox, string> displayedObjectValues = new();

    private void DisplayObjectValues(EditClip clip)
    {
        AudioInspector.Visibility = clip.Kind is "audio" or "video" ? Visibility.Visible : Visibility.Collapsed;
        displayingObjectValues = true;
        try
        {
        foreach (var (input, value) in new[]
        {
            (StartInput, clip.Start), (TrimInput, clip.Trim), (DurationInput, clip.Duration),
            (LayerInput, (double)clip.Layer), (VolumeInput, clip.Volume),
            (FadeInInput, clip.FadeIn), (FadeOutInput, clip.FadeOut)
        })
        {
            input.Value = value;
            // Setting the same Value does not restore an invalid edit's Text.
            input.Text = input.NumberFormatter.FormatDouble(value);
            displayedObjectValues[input] = input.Text;
        }
        }
        finally { displayingObjectValues = false; }
    }

    private async void ObjectSettings_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        // Handle Enter before NumberBox normalizes/clamps its text or reverts an invalid number.
        e.Handled = true;
        if (e.KeyStatus.WasKeyDown) return;
        var focused = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
        while (focused != null && focused is not NumberBox)
            focused = VisualTreeHelper.GetParent(focused);
        if (focused is NumberBox input)
            await ApplyObjectSettings(input);
    }

    private async Task ApplyObjectSettings(NumberBox? entered, bool releaseFocus = true, double? steppedValue = null, string? expectedId = null)
    {
        if (applyingObjectSettings) return;
        applyingObjectSettings = true;
        await gate.WaitAsync();
        EditClip? original = null;
        NumberBox? invalidInput = entered;
        try
        {
            if (expectedId != null && selected != expectedId) return;
            original = project.Clips.FirstOrDefault(clip => clip.Id == selected)
                ?? throw new ArgumentException("クリップを選択してください。");
            project.RequireEditable(original.Layer);

            double Read(NumberBox input, double previous, string label)
            {
                if (steppedValue.HasValue)
                {
                    if (input != entered) return previous;
                    var value = steppedValue.Value;
                    if (!double.IsFinite(value) || value < input.Minimum || value > input.Maximum || (input == LayerInput && value != Math.Truncate(value)))
                        throw new ArgumentException($"{label}の値が不正です。");
                    return value;
                }
                // Preserve full precision when an unchanged value is displayed to two decimal places.
                if (displayedObjectValues.TryGetValue(input, out var displayed) && input.Text == displayed)
                    return previous;
                var text = input.Text.Trim();
                double? parsed = input.NumberFormatter is INumberParser parser ? parser.ParseDouble(text) : null;
                if (parsed == null && double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number))
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

            var next = project.Copy();
            var edit = next.Clips.Single(clip => clip.Id == original.Id);
            edit.Start = Read(StartInput, original.Start, "開始位置");
            edit.Trim = Read(TrimInput, original.Trim, "素材開始");
            edit.Duration = Read(DurationInput, original.Duration, "長さ");
            edit.Layer = checked((int)Read(LayerInput, original.Layer, "レイヤー"));
            edit.Volume = Read(VolumeInput, original.Volume, "音量");
            edit.FadeIn = Read(FadeInInput, original.FadeIn, "フェードイン");
            edit.FadeOut = Read(FadeOutInput, original.FadeOut, "フェードアウト");
            project.RequireEditable(edit.Layer);
            if (edit.Kind == "subtitle" && releaseFocus && objectSubtitleEditor != null)
            {
                edit.Text = SubtitleTextInput.Text;
                edit.Subtitle = objectSubtitleEditor.Read();
                edit.SubtitleOverrides = SubtitleOverrides.Explicit(edit.Subtitle);
            }
            var prepared = await CompositionBuilder.Build(next);
            var previous = project;
            project = next;
            try
            {
                await Refresh(prepared);
            }
            catch
            {
                project = previous;
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
            var input = invalidInput ?? StartInput;
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
            gate.Release();
            applyingObjectSettings = false;
        }
    }

    private async void ObjectInput_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (displayingObjectValues || selected == null || !double.IsFinite(args.NewValue) ||
            !project.Clips.Any(c => c.Id == selected)) return;
        double minimum = sender == LayerInput ? 1 : sender == DurationInput ? 1.0 / project.Video.FrameRate : 0;
        double maximum = sender == VolumeInput ? 1 : sender.Maximum;
        double normalized = Math.Clamp(args.NewValue, minimum, maximum);
        if (sender == LayerInput) normalized = Math.Round(normalized, MidpointRounding.AwayFromZero);
        if (sender == VolumeInput) normalized = Math.Round(normalized, 2, MidpointRounding.AwayFromZero);
        if (normalized != args.NewValue)
        {
            displayingObjectValues = true;
            try
            {
                sender.Value = normalized;
                sender.Text = sender.NumberFormatter.FormatDouble(normalized);
            }
            finally { displayingObjectValues = false; }
        }
        var clip = project.Clips.Single(c => c.Id == selected);
        double existing = sender == StartInput ? clip.Start : sender == TrimInput ? clip.Trim :
            sender == DurationInput ? clip.Duration : sender == LayerInput ? clip.Layer :
            sender == FadeInInput ? clip.FadeIn : sender == FadeOutInput ? clip.FadeOut : clip.Volume;
        if (normalized == existing) return;
        pendingObjectStep = (selected, sender, normalized);
        if (processingObjectSteps) return;
        processingObjectSteps = true;
        try
        {
            while (pendingObjectStep != null && !closed)
            {
                while (applyingObjectSettings && !closed) await Task.Delay(16);
                if (closed) break;
                var step = pendingObjectStep.Value;
                pendingObjectStep = null;
                await ApplyObjectSettings(step.Input, releaseFocus: false, steppedValue: step.Value, expectedId: step.Id);
            }
        }
        finally { processingObjectSteps = false; }
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



