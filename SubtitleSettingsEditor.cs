using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace VideoEditor;

public sealed class SubtitleSettingsEditor : StackPanel
{
    private readonly ComboBox font;
    private readonly NumberBox size;
    private readonly ComboBox horizontal;
    private readonly ComboBox vertical;
    private readonly NumberBox marginX;
    private readonly NumberBox marginY;
    private readonly bool optional;
    private readonly SubtitleStyle defaults;
    private readonly string defaultFontLabel;
    public event Action<SubtitleStyle>? StyleChanged;
    public event Action<string>? ValidationFailed;

    public SubtitleSettingsEditor(SubtitleStyle value, bool optional = false, SubtitleStyle? defaults = null, SubtitleOverrides? overrides = null, bool compact = false)
    {
        this.optional = optional;
        this.defaults = defaults ?? value;
        defaultFontLabel = this.defaults.FontFamily + " (規定)";
        Spacing = 24;
        using var fonts = new System.Drawing.Text.InstalledFontCollection();
        font = new ComboBox { IsEditable = true, Width = 220 };
        if (optional) font.Items.Add(defaultFontLabel);
        foreach (var name in fonts.Families.Select(f => f.Name).Distinct().OrderBy(n => n)) font.Items.Add(name);
        if (optional && overrides?.FontFamily == null) font.SelectedIndex = 0;
        else
        {
            var initialFont = optional ? overrides!.FontFamily! : value.FontFamily;
            font.SelectedItem = font.Items.Cast<string>().FirstOrDefault(name => name == initialFont);
            font.Text = initialFont;
        }
        size = Number(optional ? overrides?.FontSize ?? double.NaN : value.FontSize, 8, 400);
        marginX = Number(optional ? overrides?.MarginX ?? double.NaN : value.MarginX, 0, 4000);
        marginY = Number(optional ? overrides?.MarginY ?? double.NaN : value.MarginY, 0, 4000);
        size.PlaceholderText = $"{this.defaults.FontSize} (規定)";
        marginX.PlaceholderText = $"{this.defaults.MarginX} (規定)";
        marginY.PlaceholderText = $"{this.defaults.MarginY} (規定)";
        var hLabels = new[] { "左寄せ", "中央寄せ", "右寄せ" };
        var vLabels = new[] { "上", "中央", "下" };
        var hValues = new[] { "left", "center", "right" };
        var vValues = new[] { "top", "center", "bottom" };
        horizontal = Choice(optional ? new[] { hLabels[Array.IndexOf(hValues, this.defaults.Horizontal)] + " (規定)" }.Concat(hLabels).ToArray() : hLabels,
            optional ? overrides?.Horizontal == null ? 0 : Array.IndexOf(hValues, overrides.Horizontal) + 1 : Array.IndexOf(hValues, value.Horizontal));
        vertical = Choice(optional ? new[] { vLabels[Array.IndexOf(vValues, this.defaults.Vertical)] + " (規定)" }.Concat(vLabels).ToArray() : vLabels,
            optional ? overrides?.Vertical == null ? 0 : Array.IndexOf(vValues, overrides.Vertical) + 1 : Array.IndexOf(vValues, value.Vertical));
        Children.Add(compact ? CompactGroup("フォント", ("フォント", font), ("フォントサイズ（px）", size)) : Group("フォント", ("フォント", font), ("フォントサイズ（px）", size)));
        Children.Add(compact ? CompactGroup("テキストの位置", ("横方向", horizontal), ("縦方向", vertical), ("左右の余白（px）", marginX), ("上下の余白（px）", marginY)) : Group("テキストの位置", ("横方向", horizontal), ("縦方向", vertical), ("左右の余白（px）", marginX), ("上下の余白（px）", marginY)));
        if (!optional)
        {
            var current = value with { };
            void Change(Func<SubtitleStyle, SubtitleStyle> update)
            {
                try
                {
                    var next = update(current);
                    next.Validate();
                    if (next.FontFamily != current.FontFamily)
                    {
                        using var family = new System.Drawing.FontFamily(next.FontFamily);
                    }
                    current = next;
                    StyleChanged?.Invoke(next with { });
                }
                catch (Exception exception) { ValidationFailed?.Invoke(exception.Message); }
            }
            double ReadNumberText(NumberBox input)
            {
                if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) || !double.IsFinite(number))
                    throw new ArgumentException("数値を入力してください。");
                return number;
            }
            font.SelectionChanged += (_, _) =>
            {
                if (font.SelectedItem is string name) Change(style => style with { FontFamily = name });
            };
            font.RegisterPropertyChangedCallback(ComboBox.TextProperty, (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(font.Text)) Change(style => style with { FontFamily = font.Text.Trim() });
            });
            size.RegisterPropertyChangedCallback(NumberBox.TextProperty, (_, _) => Change(style => style with { FontSize = ReadNumberText(size) }));
            marginX.RegisterPropertyChangedCallback(NumberBox.TextProperty, (_, _) => Change(style => style with { MarginX = ReadNumberText(marginX) }));
            marginY.RegisterPropertyChangedCallback(NumberBox.TextProperty, (_, _) => Change(style => style with { MarginY = ReadNumberText(marginY) }));
            horizontal.SelectionChanged += (_, _) => Change(style => style with { Horizontal = new[] { "left", "center", "right" }[horizontal.SelectedIndex] });
            vertical.SelectionChanged += (_, _) => Change(style => style with { Vertical = new[] { "top", "center", "bottom" }[vertical.SelectedIndex] });
        }
    }

    public SubtitleOverrides ReadOverrides()
    {
        double? ReadNumber(NumberBox input)
        {
            if (string.IsNullOrWhiteSpace(input.Text)) return null;
            if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ||
                !double.IsFinite(value) || value < input.Minimum || value > input.Maximum)
                throw new ArgumentException("字幕の数値が不正です。");
            return value;
        }
        return new SubtitleOverrides
        {
            FontFamily = optional && (string.IsNullOrWhiteSpace(font.Text) || font.Text == defaultFontLabel) ? null : font.Text.Trim(),
            FontSize = ReadNumber(size), MarginX = ReadNumber(marginX), MarginY = ReadNumber(marginY),
            Horizontal = optional && horizontal.SelectedIndex == 0 ? null : new[] { "left", "center", "right" }[horizontal.SelectedIndex - (optional ? 1 : 0)],
            Vertical = optional && vertical.SelectedIndex == 0 ? null : new[] { "top", "center", "bottom" }[vertical.SelectedIndex - (optional ? 1 : 0)]
        };
    }

    public SubtitleStyle Read()
    {
        var values = ReadOverrides();
        if (!optional && (values.FontSize == null || values.MarginX == null || values.MarginY == null))
            throw new ArgumentException("字幕のサイズ・余白を入力してください。");
        var value = values.Resolve(defaults);
        value.Validate();
        using var family = new System.Drawing.FontFamily(value.FontFamily);
        return value;
    }

    public static NumberBox Number(double value, double minimum, double maximum) => new()
    {
        Value = value, Minimum = minimum, Maximum = maximum, Width = 160,
        ValidationMode = NumberBoxValidationMode.Disabled,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
    };

    public static ComboBox Choice(string[] values, int index)
    {
        var choice = new ComboBox { Width = 220 };
        foreach (var value in values) choice.Items.Add(value);
        choice.SelectedIndex = Math.Max(0, index);
        return choice;
    }

    private static FrameworkElement CompactGroup(string title, params (string Label, FrameworkElement Control)[] rows)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        foreach (var (label, control) in rows)
        {
            control.Width = double.NaN;
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, label);
            panel.Children.Add(new TextBlock { Text = label });
            panel.Children.Add(control);
        }
        return panel;
    }
    public static FrameworkElement Group(string title, params (string Label, FrameworkElement Control)[] rows)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var content = new StackPanel { Spacing = 0 };
        for (int i = 0; i < rows.Length; i++)
        {
            var (label, control) = rows[i];
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, label);
            var row = new Grid { Padding = new Thickness(16), ColumnSpacing = 24, MinHeight = 64 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(control, 1);
            control.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(control);
            content.Children.Add(new Border
            {
                Child = row, BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0)
            });
        }
        panel.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"], Child = content
        });
        return panel;
    }
}


