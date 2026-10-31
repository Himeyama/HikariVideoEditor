using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace VideoEditor;

public sealed class SubtitleSettingsEditor : StackPanel
{
    private readonly ComboBox _font;
    private readonly ComboBox _textType;
    private readonly TextBox _textColor;
    private readonly TextBox _innerColor;
    private readonly TextBox _outerColor;
    private readonly NumberBox _size;
    private readonly ComboBox _horizontal;
    private readonly ComboBox _vertical;
    private readonly NumberBox _marginX;
    private readonly NumberBox _marginY;
    private readonly bool _optional;
    private readonly SubtitleStyle _defaults;
    private readonly string _defaultFontLabel;
    public event Action<SubtitleStyle>? StyleChanged;
    public event Action<string>? ValidationFailed;

    public SubtitleSettingsEditor(SubtitleStyle value, bool optional = false, SubtitleStyle? defaults = null, SubtitleOverrides? overrides = null, bool compact = false)
    {
        this._optional = optional;
        this._defaults = defaults ?? value;
        _defaultFontLabel = this._defaults.FontFamily + " (規定)";
        Spacing = 24;
        _textType = Choice(optional ? ["規定", "標準", "袋文字"] : ["標準", "袋文字"],
            optional ? overrides?.TextType == null ? 0 : overrides.TextType == "outline" ? 2 : 1 : value.TextType == "outline" ? 1 : 0);
        TextBox ColorInput(string? color, string fallback) => new() { Text = color ?? "", PlaceholderText = fallback + " (規定)", Width = 220, MaxLength = 7 };
        _textColor = ColorInput(optional ? overrides?.TextColor : value.TextColor, _defaults.TextColor);
        _innerColor = ColorInput(optional ? overrides?.InnerColor : value.InnerColor, _defaults.InnerColor);
        _outerColor = ColorInput(optional ? overrides?.OuterColor : value.OuterColor, _defaults.OuterColor);
        FrameworkElement ColorControl(TextBox input, string fallback)
        {
            ColorPicker picker = new() { IsAlphaEnabled = false, IsAlphaSliderVisible = false };
            string initial = SubtitleStyle.IsColor(input.Text) ? input.Text : fallback;
            uint.TryParse(initial.AsSpan(1), NumberStyles.HexNumber, null, out uint rgb);
            picker.Color = Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            picker.ColorChanged += (_, args) => input.Text = $"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";
            Button choose = new() { Content = "色を選択", Flyout = new Flyout { Content = picker } };
            input.Width = double.NaN;
            input.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid panel = new() { ColumnSpacing = 8, Width = compact ? double.NaN : 220, HorizontalAlignment = HorizontalAlignment.Stretch };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(choose, 1);
            panel.Children.Add(input);
            panel.Children.Add(choose);
            return panel;
        }
        (string Label, FrameworkElement Control)[] appearance = [("テキストの種類", _textType), ("文字色（#RRGGBB）", ColorControl(_textColor, _defaults.TextColor)), ("内側の縁色（#RRGGBB）", ColorControl(_innerColor, _defaults.InnerColor)), ("外側の縁色（#RRGGBB）", ColorControl(_outerColor, _defaults.OuterColor))];
        Children.Add(compact ? CompactGroup("文字の見た目", appearance) : Group("文字の見た目", appearance));
        using System.Drawing.Text.InstalledFontCollection fonts = new();
        _font = new ComboBox { IsEditable = true, Width = 220 };
        if (optional) _font.Items.Add(_defaultFontLabel);
        foreach (string name in fonts.Families.Select(f => f.Name).Distinct().OrderBy(n => n)) _font.Items.Add(name);
        if (optional && overrides?.FontFamily == null) _font.SelectedIndex = 0;
        else
        {
            string initialFont = optional ? overrides!.FontFamily! : value.FontFamily;
            _font.SelectedItem = _font.Items.Cast<string>().FirstOrDefault(name => name == initialFont);
            _font.Text = initialFont;
        }
        _size = Number(optional ? overrides?.FontSize ?? double.NaN : value.FontSize, 8, 400);
        _marginX = Number(optional ? overrides?.MarginX ?? double.NaN : value.MarginX, 0, 4000);
        _marginY = Number(optional ? overrides?.MarginY ?? double.NaN : value.MarginY, 0, 4000);
        _size.PlaceholderText = $"{this._defaults.FontSize} (規定)";
        _marginX.PlaceholderText = $"{this._defaults.MarginX} (規定)";
        _marginY.PlaceholderText = $"{this._defaults.MarginY} (規定)";
        string[] hLabels = new[] { "左寄せ", "中央寄せ", "右寄せ" };
        string[] vLabels = new[] { "上", "中央", "下" };
        string[] hValues = new[] { "left", "center", "right" };
        string[] vValues = new[] { "top", "center", "bottom" };
        _horizontal = Choice(optional ? new[] { hLabels[Array.IndexOf(hValues, this._defaults.Horizontal)] + " (規定)" }.Concat(hLabels).ToArray() : hLabels,
            optional ? overrides?.Horizontal == null ? 0 : Array.IndexOf(hValues, overrides.Horizontal) + 1 : Array.IndexOf(hValues, value.Horizontal));
        _vertical = Choice(optional ? new[] { vLabels[Array.IndexOf(vValues, this._defaults.Vertical)] + " (規定)" }.Concat(vLabels).ToArray() : vLabels,
            optional ? overrides?.Vertical == null ? 0 : Array.IndexOf(vValues, overrides.Vertical) + 1 : Array.IndexOf(vValues, value.Vertical));
        Children.Add(compact ? CompactGroup("フォント", ("フォント", _font), ("フォントサイズ（px）", _size)) : Group("フォント", ("フォント", _font), ("フォントサイズ（px）", _size)));
        Children.Add(compact ? CompactGroup("テキストの位置", ("横方向", _horizontal), ("縦方向", _vertical), ("左右の余白（px）", _marginX), ("上下の余白（px）", _marginY)) : Group("テキストの位置", ("横方向", _horizontal), ("縦方向", _vertical), ("左右の余白（px）", _marginX), ("上下の余白（px）", _marginY)));
        if (!optional)
        {
            SubtitleStyle current = value with { };
            void Change(Func<SubtitleStyle, SubtitleStyle> update)
            {
                try
                {
                    SubtitleStyle next = update(current);
                    next.Validate();
                    if (next.FontFamily != current.FontFamily)
                    {
                        using System.Drawing.FontFamily family = new(next.FontFamily);
                    }
                    current = next;
                    StyleChanged?.Invoke(next with { });
                }
                catch (Exception exception) { ValidationFailed?.Invoke(exception.Message); }
            }
            double ReadNumberText(NumberBox input)
            {
                if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double number) || !double.IsFinite(number))
                    throw new ArgumentException("数値を入力してください。");
                return number;
            }
            _textType.SelectionChanged += (_, _) => Change(style => style with { TextType = _textType.SelectedIndex == 1 ? "outline" : "standard" });
            void ColorChanged(TextBox input, Func<SubtitleStyle, string, SubtitleStyle> update)
            {
                if (SubtitleStyle.IsColor(input.Text)) Change(style => update(style, input.Text));
                else ValidationFailed?.Invoke("色は #RRGGBB 形式で入力してください。");
            }
            _textColor.TextChanged += (_, _) => ColorChanged(_textColor, (style, color) => style with { TextColor = color });
            _innerColor.TextChanged += (_, _) => ColorChanged(_innerColor, (style, color) => style with { InnerColor = color });
            _outerColor.TextChanged += (_, _) => ColorChanged(_outerColor, (style, color) => style with { OuterColor = color });
            _font.SelectionChanged += (_, _) =>
            {
                if (_font.SelectedItem is string name) Change(style => style with { FontFamily = name });
            };
            _font.RegisterPropertyChangedCallback(ComboBox.TextProperty, (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(_font.Text)) Change(style => style with { FontFamily = _font.Text.Trim() });
            });
            _size.RegisterPropertyChangedCallback(NumberBox.TextProperty, (_, _) => Change(style => style with { FontSize = ReadNumberText(_size) }));
            _marginX.RegisterPropertyChangedCallback(NumberBox.TextProperty, (_, _) => Change(style => style with { MarginX = ReadNumberText(_marginX) }));
            _marginY.RegisterPropertyChangedCallback(NumberBox.TextProperty, (_, _) => Change(style => style with { MarginY = ReadNumberText(_marginY) }));
            _horizontal.SelectionChanged += (_, _) => Change(style => style with { Horizontal = new[] { "left", "center", "right" }[_horizontal.SelectedIndex] });
            _vertical.SelectionChanged += (_, _) => Change(style => style with { Vertical = new[] { "top", "center", "bottom" }[_vertical.SelectedIndex] });
        }
    }

    public SubtitleOverrides ReadOverrides()
    {
        double? ReadNumber(NumberBox input)
        {
            if (string.IsNullOrWhiteSpace(input.Text)) return null;
            if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) ||
                !double.IsFinite(value) || value < input.Minimum || value > input.Maximum)
                throw new ArgumentException("字幕の数値が不正です。");
            return value;
        }
        return new SubtitleOverrides
        {
            TextType = _optional && _textType.SelectedIndex == 0 ? null : _textType.SelectedIndex - (_optional ? 1 : 0) == 1 ? "outline" : "standard",
            TextColor = _optional && string.IsNullOrWhiteSpace(_textColor.Text) ? null : _textColor.Text.Trim(),
            InnerColor = _optional && string.IsNullOrWhiteSpace(_innerColor.Text) ? null : _innerColor.Text.Trim(),
            OuterColor = _optional && string.IsNullOrWhiteSpace(_outerColor.Text) ? null : _outerColor.Text.Trim(),
            FontFamily = _optional && (string.IsNullOrWhiteSpace(_font.Text) || _font.Text == _defaultFontLabel) ? null : _font.Text.Trim(),
            FontSize = ReadNumber(_size), MarginX = ReadNumber(_marginX), MarginY = ReadNumber(_marginY),
            Horizontal = _optional && _horizontal.SelectedIndex == 0 ? null : new[] { "left", "center", "right" }[_horizontal.SelectedIndex - (_optional ? 1 : 0)],
            Vertical = _optional && _vertical.SelectedIndex == 0 ? null : new[] { "top", "center", "bottom" }[_vertical.SelectedIndex - (_optional ? 1 : 0)]
        };
    }

    public SubtitleStyle Read()
    {
        SubtitleOverrides values = ReadOverrides();
        if (!_optional && (values.FontSize == null || values.MarginX == null || values.MarginY == null))
            throw new ArgumentException("字幕のサイズ・余白を入力してください。");
        SubtitleStyle value = values.Resolve(_defaults);
        value.Validate();
        using System.Drawing.FontFamily family = new(value.FontFamily);
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
        ComboBox choice = new() { Width = 220 };
        foreach (string value in values) choice.Items.Add(value);
        choice.SelectedIndex = Math.Max(0, index);
        return choice;
    }

    private static FrameworkElement CompactGroup(string title, params (string Label, FrameworkElement Control)[] rows)
    {
        StackPanel panel = new() { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        foreach ((string label, FrameworkElement control) in rows)
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
        StackPanel panel = new() { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        StackPanel content = new() { Spacing = 0 };
        for (int i = 0; i < rows.Length; i++)
        {
            (string label, FrameworkElement control) = rows[i];
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, label);
            Grid row = new() { Padding = new Thickness(16), ColumnSpacing = 24, MinHeight = 64 };
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


