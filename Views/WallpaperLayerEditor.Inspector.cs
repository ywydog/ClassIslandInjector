using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Controls;
using FluentAvalonia.UI.Controls;

namespace ClassIslandInjector.Views;

/// <summary>
/// 底图图层编辑器的「检查器」职责分区（与 <see cref="WallpaperLayerEditorWindow"/> 主体同属一个
/// partial 类）。集中承载右侧检查器的显隐同步（<see cref="WallpaperLayerEditorWindow.RefreshInspector"/>）、
/// 显示名 / 选项映射与颜色解析等纯辅助逻辑，主文件只保留窗口骨架与画布 / 图层面板交互。
/// </summary>
internal sealed partial class WallpaperLayerEditorWindow
{
    /// <summary>「目标分块」下拉（分体多图层）：整岛或某个分体块（组件 GUID）。</summary>
    private readonly ComboBox _splitBlockBox = new() { MinWidth = 180 };
    /// <summary>「目标分块」行（未选中图层时隐藏）。</summary>
    private Control _splitBlockItem = null!;

    /// <summary>纯图标工具栏按钮（带提示文字）。</summary>
    private static Slider SliderControl(double min, double max, double tick) => new()
    {
        Width = 150,
        Minimum = min,
        Maximum = max,
        TickFrequency = tick,
        IsSnapToTickEnabled = true,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static T Selected<T>(ComboBox box, T fallback) => box.SelectedItem is Pick<T> choice ? choice.Value : fallback;

    private static string DisplayModeName(WallpaperDisplayMode mode) => mode switch
    {
        WallpaperDisplayMode.Fill => "填充（裁剪）",
        WallpaperDisplayMode.Fit => "适应",
        WallpaperDisplayMode.Stretch => "拉伸",
        WallpaperDisplayMode.Tile => "平铺",
        _ => "填充"
    };

    private static string DisplaySource(WallpaperLayerItem layer) => layer.Source switch
    {
        WallpaperSource.LocalImage => "本地图片",
        WallpaperSource.FolderSlideshow => "幻灯片",
        WallpaperSource.SmtcAlbum => "SMTC 封面",
        _ => "无来源"
    };

    /// <summary>图层类型显示名（位图 / 形状·类型 / 文本）。</summary>
    private static string DisplayKind(WallpaperLayerItem layer) => layer.Kind switch
    {
        WallpaperLayerKind.Shape => $"形状·{ShapeTypeName(layer.ShapeType)}",
        WallpaperLayerKind.Text => "文本",
        _ => DisplaySource(layer)
    };

    private static string ShapeTypeName(WallpaperShapeType type) => type switch
    {
        WallpaperShapeType.Rectangle => "矩形",
        WallpaperShapeType.RoundedRectangle => "圆角矩形",
        WallpaperShapeType.Ellipse => "椭圆",
        WallpaperShapeType.Line => "直线",
        WallpaperShapeType.Triangle => "三角形",
        WallpaperShapeType.Diamond => "菱形",
        WallpaperShapeType.Pentagon => "五边形",
        WallpaperShapeType.Hexagon => "六边形",
        WallpaperShapeType.Star => "五角星",
        WallpaperShapeType.Heart => "心形",
        WallpaperShapeType.Parallelogram => "平行四边形",
        _ => "矩形"
    };

    /// <summary>SMTC 图层在列表副标题里的模式后缀。</summary>
    private static string SmtcModeSuffix(WallpaperLayerItem layer) =>
        layer.Source == WallpaperSource.SmtcAlbum
            ? layer.SmtcMode == WallpaperLayerSmtcMode.AsImage ? "（当作图片）" : "（默认铺满）"
            : string.Empty;

    private static string DisplayZOrder(WallpaperLayerZOrder order) => order switch
    {
        WallpaperLayerZOrder.BehindBackground => "底色之后（默认）",
        WallpaperLayerZOrder.AboveBackground => "底色之上、组件之下",
        WallpaperLayerZOrder.AboveComponents => "组件之上",
        _ => "底色之后"
    };

    private static readonly Pick<WallpaperDisplayMode>[] DisplayModeChoices =
    [
        new(WallpaperDisplayMode.Fill, "填充（裁剪）"),
        new(WallpaperDisplayMode.Fit, "适应（完整显示）"),
        new(WallpaperDisplayMode.Stretch, "拉伸（变形）"),
    ];

    private static readonly Pick<WallpaperBrushTip>[] BrushTipChoices =
    [
        new(WallpaperBrushTip.Round, "圆形"),
        new(WallpaperBrushTip.Square, "方形"),
        new(WallpaperBrushTip.Flat, "横线"),
    ];

    private static readonly Pick<WallpaperLayerSmtcMode>[] SmtcModeChoices =
    [
        new(WallpaperLayerSmtcMode.AsImage, "当作图片处理"),
        new(WallpaperLayerSmtcMode.Default, "默认处理（铺满主界面）"),
    ];

    private static readonly Pick<WallpaperShapeType>[] ShapeTypeChoices =
    [
        new(WallpaperShapeType.Rectangle, "矩形"),
        new(WallpaperShapeType.RoundedRectangle, "圆角矩形"),
        new(WallpaperShapeType.Ellipse, "椭圆"),
        new(WallpaperShapeType.Line, "直线"),
        new(WallpaperShapeType.Triangle, "三角形"),
        new(WallpaperShapeType.Diamond, "菱形"),
        new(WallpaperShapeType.Pentagon, "五边形"),
        new(WallpaperShapeType.Hexagon, "六边形"),
        new(WallpaperShapeType.Star, "五角星"),
        new(WallpaperShapeType.Heart, "心形"),
        new(WallpaperShapeType.Parallelogram, "平行四边形"),
    ];

    private static readonly Pick<WallpaperTextAlign>[] TextAlignChoices =
    [
        new(WallpaperTextAlign.Left, "左对齐"),
        new(WallpaperTextAlign.Center, "居中"),
        new(WallpaperTextAlign.Right, "右对齐"),
    ];

    /// <summary>检查器用的颜色选择器（跟随主题，浅色/深色均可）。</summary>
    private static ColorPicker ColorPicker() => new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 6, 0)
    };

    private sealed record Pick<T>(T Value, string Text)
    {
        public override string ToString() => Text;
    }

    // ---- 检查器刷新：图层面板 / 检查器 / 状态栏的显隐与取值同步 ----

    private void RefreshCustomSizePanel()
    {
        var layer = _canvas.SelectedLayer;
        var custom = layer is { SizeMode: WallpaperLayerSizeMode.Custom } &&
                     !(layer.Source == WallpaperSource.SmtcAlbum && layer.SmtcMode == WallpaperLayerSmtcMode.Default);
        _widthItem.IsVisible = custom;
        _heightItem.IsVisible = custom;
    }

    /// <summary>重建「目标分块」下拉选项（整岛 + 当前主界面各分体块），并尽量保持既有选择。</summary>
    private void RefreshSplitBlockOptions()
    {
        var current = _splitBlockBox.SelectedItem is Pick<string> cur ? cur.Value : "";
        var items = new List<Pick<string>> { new("", "整岛") };
        foreach (var b in MainWindowStyleInjector.EnumerateSplitBlocks())
        {
            items.Add(new Pick<string>(b.Id, b.Name));
        }

        _splitBlockBox.ItemsSource = items;
        _splitBlockBox.SelectedItem = items.FirstOrDefault(p => p.Value == current) ?? items[0];
    }

    /// <summary>把「目标分块」下拉同步为选中图层的 <see cref="WallpaperLayerItem.SplitBlockId"/>。</summary>
    private void UpdateSplitBlockSelection()
    {
        if (_splitBlockBox.ItemsSource is not IReadOnlyList<Pick<string>> items)
        {
            return;
        }

        var id = _canvas.SelectedLayer?.SplitBlockId ?? "";
        _splitBlockBox.SelectedItem = items.FirstOrDefault(p => p.Value == id) ?? items.FirstOrDefault(p => p.Value == "");
    }

    /// <summary>SMTC 图层是否处于「默认处理」模式（仅透明度/显示方式可改）。</summary>
    private static bool IsSmtcDefaultMode(WallpaperLayerItem layer) =>
        layer.Source == WallpaperSource.SmtcAlbum && layer.SmtcMode == WallpaperLayerSmtcMode.Default;

    private void RefreshInspector()
    {
        _updatingInspector = true;
        try
        {
            // 工具上下文：选区工具只显示选区操作；画笔 / 吸管显示「画笔」设置（取色后在检查器
            // 展示颜色）；其余工具显示图层的常规设置。与是否选中图层无关，提前统一控制。
            var selectionTool = _canvas.Tool is WallpaperEditorTool.RectSelect or WallpaperEditorTool.Lasso;
            var brushTool = _canvas.Tool is WallpaperEditorTool.Brush or WallpaperEditorTool.Eraser or WallpaperEditorTool.Eyedropper;
            var hasSelection = _canvas.HasSelection;
            _selectionGroup.IsVisible = selectionTool || hasSelection;
            _selectionToLayerButton.IsEnabled = hasSelection;
            _clearSelectionButton.IsEnabled = hasSelection;
            _brushGroup.IsVisible = brushTool;
            // 工具内细节：橡皮擦不显示「画笔」字样与颜色（只留大小 + 笔触）；吸管只显示取到的颜色。
            var isEraser = _canvas.Tool == WallpaperEditorTool.Eraser;
            var isEyedropper = _canvas.Tool == WallpaperEditorTool.Eyedropper;
            _brushGroupTitle.Text = isEraser ? "橡皮擦" : isEyedropper ? "取色" : "画笔";
            // 图标随工具变化：橡皮擦用橡皮图标，吸管用取色图标，画笔保持笔刷。
            _brushGroupTitle.Glyph = isEraser ? "\uE7FE" : isEyedropper ? "\uE81D" : "\uEC49";
            _brushColorItem.IsVisible = !isEraser;
            _brushSizeItem.IsVisible = !isEyedropper;
            _brushTipItem.IsVisible = !isEyedropper;
            _brushTaperItem.IsVisible = !isEyedropper;
            _brushAaItem.IsVisible = !isEyedropper;
            // 画笔 / 橡皮擦设置：值始终同步（吸管取色后这里展示当前颜色）。
            _brushColorPicker.Color = _canvas.ActiveColor;
            _brushSizeSlider.Value = _canvas.BrushSize;
            _brushTipBox.SelectedItem = BrushTipChoices.FirstOrDefault(c => c.Value == _canvas.BrushTip) ?? BrushTipChoices[0];
            _brushTaperToggle.IsChecked = _canvas.BrushTaper;
            _brushAaToggle.IsChecked = _canvas.BrushAntiAlias;
            var layer = _canvas.SelectedLayer;
            if (layer == null)
            {
                _nameItem.IsVisible = false;
                _splitBlockItem.IsVisible = false;
                _opacityItem.IsVisible = false;
                _displayModeItem.IsVisible = false;
                _smtcModeItem.IsVisible = false;
                _smtcHidePausedItem.IsVisible = false;
                _shadowItem.IsVisible = false;
                _shadowBlurItem.IsVisible = false;
                _shadowOffsetXItem.IsVisible = false;
                _shadowOffsetYItem.IsVisible = false;
                _shadowColorItem.IsVisible = false;
                _shadowOpacityItem.IsVisible = false;
                _fillIslandItem.IsVisible = false;
                _widthItem.IsVisible = false;
                _heightItem.IsVisible = false;
                _rotationItem.IsVisible = false;
                _offsetXItem.IsVisible = false;
                _offsetYItem.IsVisible = false;
                _anchorItem.IsVisible = false;
                _resetTransformItem.IsVisible = false;
                _shapeTypeItem.IsVisible = false;
                _shapeCornerRadiusItem.IsVisible = false;
                _shapeStarPointsItem.IsVisible = false;
                _shapeStarInsetItem.IsVisible = false;
                _shapeFillItem.IsVisible = false;
                _shapeFillThemeItem.IsVisible = false;
                _shapeStrokeItem.IsVisible = false;
                _shapeStrokeThemeItem.IsVisible = false;
                _shapeStrokeWidthItem.IsVisible = false;
                _textItem.IsVisible = false;
                _textFontSizeItem.IsVisible = false;
                _textFontFamilyItem.IsVisible = false;
                _textColorItem.IsVisible = false;
                _textColorThemeItem.IsVisible = false;
                _textStrokeItem.IsVisible = false;
                _textStrokeColorItem.IsVisible = false;
                _textStrokeThicknessItem.IsVisible = false;
                _textUseSmtcTitleItem.IsVisible = false;
                _textBoldItem.IsVisible = false;
                _textAlignItem.IsVisible = false;
                _relativeHint.Text = "未选中图层。点击画布上的图层，或在左侧图层面板选择。";
                RefreshCustomSizePanel();
                // 无选中图层：隐藏 TabStrip 与分组页，仅保留占位提示（画笔 / 选区工具时连提示也隐藏）。
                _lastSelectionProfile = string.Empty;
                RefreshInspectorSegments(false, brushTool, selectionTool, false, false, false, false, false);
                return;
            }

            var selected = _canvas.SelectedLayers;
            var multi = selected.Count > 1;
            // 形状/文本专属项仅在「全部选中图层同类型」时显示，避免混合多选时出现误导。
            var allSameKind = selected.Select(l => l.Kind).Distinct().Count() <= 1;
            var smtcDefault = IsSmtcDefaultMode(layer);
            var isShape = allSameKind && layer.Kind == WallpaperLayerKind.Shape;
            var isText = allSameKind && layer.Kind == WallpaperLayerKind.Text;
            // 名称 / 不透明度是通用属性，任何图层选中都显示（此前只在无选中分支设 false，
            // 选中分支从未设 true → 形状/文本的「图层」页因只剩图片专属行而显得空白）。
            _nameItem.IsVisible = true;
            _opacityItem.IsVisible = true;
            _nameBox.IsEnabled = true;
            _splitBlockItem.IsVisible = true;
            UpdateSplitBlockSelection();
            _opacitySlider.IsEnabled = true;
            _displayModeBox.IsEnabled = layer.Kind == WallpaperLayerKind.Image;
            _smtcModeBox.IsEnabled = true;
            _smtcModeItem.IsVisible = layer.Source == WallpaperSource.SmtcAlbum;
            _smtcHidePausedItem.IsVisible = layer.Source == WallpaperSource.SmtcAlbum;
            _smtcHidePausedToggle.IsChecked = layer.SmtcHideWhenPaused;
            _displayModeItem.IsVisible = layer.Kind == WallpaperLayerKind.Image;
            // 效果仅图片图层显示；投影子项仅在启用投影后展开。
            var isImage = allSameKind && layer.Kind == WallpaperLayerKind.Image;
            _resetTransformItem.IsVisible = true;
            _shadowItem.IsVisible = isImage;
            _shadowBlurItem.IsVisible = isImage && layer.ShadowEnabled;
            _shadowOffsetXItem.IsVisible = isImage && layer.ShadowEnabled;
            _shadowOffsetYItem.IsVisible = isImage && layer.ShadowEnabled;
            _shadowColorItem.IsVisible = isImage && layer.ShadowEnabled;
            _shadowOpacityItem.IsVisible = isImage && layer.ShadowEnabled;
            _shadowToggle.IsChecked = layer.ShadowEnabled;
            _shadowBlurSpin.DoubleValue = layer.ShadowBlurRadius;
            _shadowOffsetXSpin.DoubleValue = layer.ShadowOffsetX;
            _shadowOffsetYSpin.DoubleValue = layer.ShadowOffsetY;
            _shadowColorPicker.Color = ReadColor(layer.ShadowColor, Color.FromArgb(0x99, 0, 0, 0));
            _shadowOpacitySlider.Value = layer.ShadowOpacity;
            // Custom（布尔运算结果）没有可切换的固定形状类型，隐藏形状类型行。
            _shapeTypeItem.IsVisible = isShape && layer.ShapeType != WallpaperShapeType.Custom;
            // 圆角半径仅圆角矩形、星角/内凹仅五角星显示；属性值在 ShapeType 变化时由读值刷新。
            _shapeCornerRadiusItem.IsVisible = isShape && layer.ShapeType == WallpaperShapeType.RoundedRectangle;
            _shapeStarPointsItem.IsVisible = isShape && layer.ShapeType == WallpaperShapeType.Star;
            _shapeStarInsetItem.IsVisible = isShape && layer.ShapeType == WallpaperShapeType.Star;
            _shapeFillItem.IsVisible = isShape;
            _shapeFillThemeItem.IsVisible = isShape;
            _shapeStrokeItem.IsVisible = isShape;
            _shapeStrokeThemeItem.IsVisible = isShape;
            _shapeStrokeWidthItem.IsVisible = isShape;
            _textItem.IsVisible = isText;
            _textFontSizeItem.IsVisible = isText;
            _textFontFamilyItem.IsVisible = isText;
            _textColorItem.IsVisible = isText;
            _textColorThemeItem.IsVisible = isText;
            _textStrokeItem.IsVisible = isText;
            _textStrokeColorItem.IsVisible = isText && layer.TextStrokeEnabled;
            _textStrokeThicknessItem.IsVisible = isText && layer.TextStrokeEnabled;
            _textUseSmtcTitleItem.IsVisible = isText;
            _textBoldItem.IsVisible = isText;
            _textAlignItem.IsVisible = isText;
            // 默认处理模式：隐藏尺寸/位移/旋转/锚点，强制铺满主界面（宽高行由 RefreshCustomSizePanel 隐藏）。
            _fillIslandItem.IsVisible = !smtcDefault;
            _rotationItem.IsVisible = !smtcDefault;
            _offsetXItem.IsVisible = !smtcDefault;
            _offsetYItem.IsVisible = !smtcDefault;
            _anchorItem.IsVisible = !smtcDefault;
            _nameBox.Text = layer.Name;
            _opacitySlider.Value = layer.Opacity;
            _displayModeBox.SelectedItem = DisplayModeChoices.FirstOrDefault(c => c.Value == layer.DisplayMode) ?? DisplayModeChoices[0];
            _smtcModeBox.SelectedItem = SmtcModeChoices.FirstOrDefault(c => c.Value == layer.SmtcMode) ?? SmtcModeChoices[0];
            _shapeTypeBox.SelectedItem = ShapeTypeChoices.FirstOrDefault(c => c.Value == layer.ShapeType) ?? ShapeTypeChoices[0];
            _shapeCornerRadiusSpin.DoubleValue = layer.ShapeCornerRadius;
            _shapeStarPointsSpin.DoubleValue = layer.ShapeStarPoints;
            _shapeStarInsetSpin.DoubleValue = layer.ShapeStarInset;
            _shapeFillPicker.Color = InspectorColor(layer.FillColor, Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF), layer.FillUsesThemeColor);
            _shapeStrokePicker.Color = InspectorColor(layer.StrokeColor, Colors.White, layer.StrokeUsesThemeColor);
            _shapeStrokeSpin.DoubleValue = layer.StrokeThickness;
            _textBox.Text = layer.Text;
            _textFontSizeSpin.DoubleValue = layer.TextFontSize;
            _textFontFamilyBox.SelectedItem = ((IEnumerable<FontFamily>)_textFontFamilyBox.ItemsSource!)
                .FirstOrDefault(font => string.Equals(font.Name, layer.TextFontFamily, StringComparison.CurrentCultureIgnoreCase))
                ?? FontFamily.Default;
            _textColorPicker.Color = InspectorColor(layer.TextColor, Colors.White, layer.TextUsesThemeColor);
            _textStrokeToggle.IsChecked = layer.TextStrokeEnabled;
            _textStrokeColorPicker.Color = ReadColor(layer.TextStrokeColor, Colors.Black);
            _textStrokeThicknessSpin.DoubleValue = layer.TextStrokeThickness;
            _textUseSmtcTitleToggle.IsChecked = layer.TextUseSmtcTitle;
            _textBoldToggle.IsChecked = layer.TextBold;
            _textAlignBox.SelectedItem = TextAlignChoices.FirstOrDefault(c => c.Value == layer.TextAlign) ?? TextAlignChoices[1];
            _fillIslandToggle.IsChecked = smtcDefault || layer.SizeMode == WallpaperLayerSizeMode.FillIsland;
            _widthSpin.DoubleValue = layer.Width;
            _heightSpin.DoubleValue = layer.Height;
            _rotationSpin.DoubleValue = layer.Rotation;
            _offsetXSpin.DoubleValue = layer.OffsetX;
            _offsetYSpin.DoubleValue = layer.OffsetY;
            _anchorPicker.AnchorX = layer.AnchorX;
            _anchorPicker.AnchorY = layer.AnchorY;
            _anchorPicker.InvalidateVisual();
            // 主题色跟随：开启时颜色选择器只读（实际颜色由宿主主题驱动，这里展示当前强调色）。
            _shapeFillThemeToggle.IsChecked = layer.FillUsesThemeColor;
            _shapeStrokeThemeToggle.IsChecked = layer.StrokeUsesThemeColor;
            _textColorThemeToggle.IsChecked = layer.TextUsesThemeColor;
            _shapeFillPicker.IsEnabled = isShape && !layer.FillUsesThemeColor;
            _shapeStrokePicker.IsEnabled = isShape && !layer.StrokeUsesThemeColor;
            _textColorPicker.IsEnabled = isText && !layer.TextUsesThemeColor;
            _relativeHint.Text = multi
                ? $"已选中 {selected.Count} 个图层：对属性的修改将应用到全部选中图层（部分类型专属设置仅在全部同类型时可用）。"
                : RelativeHintText(layer);
            RefreshCustomSizePanel();
            // 画布图层操作（选中画布图层时显示）；画布尺寸固定为整张画布，强制隐藏宽高设置。
            var isCanvasLayer = layer is { IsCanvasLayer: true };
            _canvasGroup.IsVisible = isCanvasLayer;
            if (isCanvasLayer)
            {
                _widthItem.IsVisible = false;
                _heightItem.IsVisible = false;
            }
            // TabStrip 分段与分组页显隐：按工具上下文与选中图层类型刷新
            // （内容=形状/文本、效果=图片、变换=非 SMTC 默认处理）。
            var profile = string.Join(",", selected
                .OrderBy(l => l.Id, StringComparer.Ordinal)
                .Select(l => l.Id + ":" + (int)l.Kind));
            var profileChanged = !string.Equals(profile, _lastSelectionProfile, StringComparison.Ordinal);
            _lastSelectionProfile = profile;
            RefreshInspectorSegments(true, brushTool, selectionTool, isShape, isText, isImage, smtcDefault, profileChanged);
        }
        finally
        {
            _updatingInspector = false;
        }
    }

    /// <summary>解析颜色（失败回退）。</summary>
    private static Color ReadColor(string text, Color fallback) => ColorUtil.Parse(text, fallback);

    /// <summary>取检查器显示的颜色：启用主题色时显示当前主题强调色（保留配置颜色的透明度）。</summary>
    private static Color InspectorColor(string text, Color fallback, bool useTheme)
    {
        var color = ReadColor(text, fallback);
        if (!useTheme)
        {
            return color;
        }

        var accent = ThemePalette.AccentColor();
        return Color.FromArgb(color.A, accent.R, accent.G, accent.B);
    }

    /// <summary>把选中图层的相对位置表达成人类可读的提示，如「右边缘 = 主界面右边缘 - 16px」。</summary>
    private string RelativeHintText(WallpaperLayerItem layer)
    {
        if (IsSmtcDefaultMode(layer))
        {
            return "当前为 SMTC 图层的默认处理：铺满整个主界面，仅可调整透明度与显示方式。\n切换为「当作图片处理」后可自由位移、缩放、旋转。";
        }

        if (layer.SizeMode == WallpaperLayerSizeMode.FillIsland)
        {
            return "当前图层铺满整个主界面，随主界面尺寸自适应。拖动手柄或旋转后会切换为自定义尺寸。";
        }

        var xText = layer.AnchorX switch
        {
            WallpaperLayerAnchorX.Left => $"左边缘 = 主界面左边缘 {OffsetText(layer.OffsetX)}",
            WallpaperLayerAnchorX.Center => $"中心 = 主界面中心 {OffsetText(layer.OffsetX)}",
            WallpaperLayerAnchorX.Right => $"右边缘 = 主界面右边缘 {OffsetText(layer.OffsetX)}",
            _ => string.Empty
        };
        var yText = layer.AnchorY switch
        {
            WallpaperLayerAnchorY.Top => $"上边缘 = 主界面上边缘 {OffsetText(layer.OffsetY)}",
            WallpaperLayerAnchorY.Center => $"垂直中心 = 主界面垂直中心 {OffsetText(layer.OffsetY)}",
            WallpaperLayerAnchorY.Bottom => $"下边缘 = 主界面下边缘 {OffsetText(layer.OffsetY)}",
            _ => string.Empty
        };
        return $"{xText}\n{yText} · 宽 {layer.Width:0}px × 高 {layer.Height:0}px · 旋转 {layer.Rotation:0}°";
    }

    private static string OffsetText(double offset)
    {
        if (Math.Abs(offset) < 0.05)
        {
            return "（0px，精确对齐）";
        }

        return offset < 0 ? $"+ {Math.Abs(offset):0}px（向左/上）" : $"+ {offset:0}px（向右/下）";
    }

    // ---- 检查器 TabStrip 分段与分组页 ----

    private void BuildInspectorTabStrip()
    {
        _inspectorSegmented = new TabStrip { HorizontalAlignment = HorizontalAlignment.Left };
        if (ThemePalette.FindResource("TabStripStyle") is Avalonia.Styling.ControlTheme tabTheme)
        {
            _inspectorSegmented.Theme = tabTheme;
        }

        _inspectorSegmented.Classes.Add("compact");
        _inspectorTabs.Clear();
        foreach (var (key, label, glyph) in new[]
        {
            ("general", "图层", "\uE9B2"),
            ("content", "内容", "\uEA2E"),
            ("effect", "效果", "\uF42F"),
            ("transform", "变换", "\uE0EC")
        })
        {
            var item = new TabStripItem();
            var button = new AnimatedIconButton { Glyph = glyph, Text = label };
            button.Classes.Add("display-role");
            item.PropertyChanged += (_, e) =>
            {
                if (e.Property == ListBoxItem.IsSelectedProperty)
                {
                    button.IsKeepingExpanded = item.IsSelected;
                }
            };
            item.Content = button;
            _inspectorTabs[key] = item;
            _inspectorSegmented.Items.Add(item);
        }

        _inspectorSegmented.SelectionChanged += (_, _) =>
        {
            if (_updatingSegments || _inspectorSegmented.SelectedItem is not TabStripItem sel)
            {
                return;
            }

            foreach (var (key, item) in _inspectorTabs)
            {
                if (ReferenceEquals(item, sel))
                {
                    _activeInspectorPage = key;
                    SetPageVisibility(key);
                    break;
                }
            }
        };
    }

    /// <summary>切换激活的分组页并同步 TabStrip 选中项（SelectionChanged 会兜底再同步一次，无副作用）。</summary>
    private void ActivateInspectorPage(string key)
    {
        _activeInspectorPage = key;
        SetPageVisibility(key);
        if (_inspectorTabs.TryGetValue(key, out var item))
        {
            _inspectorSegmented.SelectedItem = item;
        }
    }

    /// <summary>让分组页只有 key 对应页可见（其余页收起，避免多页叠加）。</summary>
    private void SetPageVisibility(string key)
    {
        foreach (var (k, page) in _inspectorPages)
        {
            page.IsVisible = k == key;
        }
    }

    /// <summary>
    /// 按工具上下文与选中图层类型刷新检查器分段：内容段仅形状/文本、效果段仅图片、
    /// 变换段在 SMTC 默认处理时隐藏；画笔 / 选区工具或无选中图层时隐藏整个分段与分组页。
    /// 形状 / 文本「新选中」（profileChanged）时默认落到「内容」段（教程 #EditorShapeType 需可见）；
    /// 原地编辑（拖动数值等）不跳段，避免在变换页改旋转 / 偏移时 Tab 乱跳。
    /// </summary>
    private void RefreshInspectorSegments(bool hasLayer, bool brushTool, bool selectionTool, bool isShape, bool isText, bool isImage, bool smtcDefault, bool profileChanged)
    {
        if (_inspectorPages.Count == 0)
        {
            return;
        }

        var showTabs = hasLayer && !brushTool && !selectionTool;
        _inspectorSegmented.IsVisible = showTabs;
        // 无选中图层且不在画笔 / 选区工具时显示占位提示。
        _noLayerHint.IsVisible = !hasLayer && !brushTool && !selectionTool;
        var showContent = showTabs && (isShape || isText);
        var showEffect = showTabs && isImage;
        var showTransform = showTabs && !smtcDefault;
        _updatingSegments = true;
        _inspectorTabs["content"].IsVisible = showContent;
        _inspectorTabs["effect"].IsVisible = showEffect;
        _inspectorTabs["transform"].IsVisible = showTransform;
        _updatingSegments = false;
        if (!showTabs)
        {
            // 隐藏所有分组页，避免与画笔 / 选区上下文组叠加。
            foreach (var (_, page) in _inspectorPages)
            {
                page.IsVisible = false;
            }

            return;
        }

        var active = _activeInspectorPage;
        var activeOk = active == "general"
            || (active == "content" && showContent)
            || (active == "effect" && showEffect)
            || (active == "transform" && showTransform);
        // 形状 / 文本「新选中」→ 默认「内容」段（加完形状马上换类型、加完文字马上改字；
        // 教程「换形状类型」的 #EditorShapeType 也在内容段，必须可见）。
        // 若用户正在其它可用段原地编辑（profileChanged=false）则保留当前段不跳转。
        if ((isShape || isText) && showContent && (profileChanged || !activeOk))
        {
            active = "content";
        }
        else if (!activeOk)
        {
            active = "general";
        }

        ActivateInspectorPage(active);
    }

    /// <summary>设置分组小标题（不包裹卡片）。</summary>
    private static IconText GroupSubtitle(string glyph, string text) => new()
    {
        Glyph = glyph,
        Text = text,
        Margin = new Thickness(0, 10, 0, 2),
        Opacity = 0.85
    };

    /// <summary>设置项行：左侧标签 + 右侧控件（平铺，无卡片；统一行高保证视觉对齐）。</summary>
    private static Control SettingsRow(string label, Control footer)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            MinHeight = 32,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.9,
                    Margin = new Thickness(0, 0, 10, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap
                },
                footer
            }
        };
        Grid.SetColumn(footer, 1);
        return row;
    }

    /// <summary>构建右侧检查器：配置各控件的事件订阅并组装成分段（常规 / 内容 / 效果 / 变换）分组。</summary>
    private StackPanel BuildInspector()
    {
        _displayModeBox.ItemsSource = DisplayModeChoices;
        _displayModeBox.SelectedItem = DisplayModeChoices[0];
        _smtcModeBox.ItemsSource = SmtcModeChoices;
        _smtcModeBox.SelectedItem = SmtcModeChoices[0];
        _shapeTypeBox.ItemsSource = ShapeTypeChoices;
        _shapeTypeBox.SelectedItem = ShapeTypeChoices[0];
        _textAlignBox.ItemsSource = TextAlignChoices;
        _textAlignBox.SelectedItem = TextAlignChoices[1];
        var fonts = FontManager.Current.SystemFonts
            .Append(FontFamily.Default)
            .DistinctBy(font => font.Name)
            .OrderBy(font => font.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        _textFontFamilyBox.ItemsSource = fonts;
        _textFontFamilyBox.ItemTemplate = new FuncDataTemplate<FontFamily>((font, _) =>
        {
            // 虚拟化回收 ComboBox 项时模板可能短暂收到 null，不能直接读取 Name。
            if (font == null)
            {
                return new TextBlock { Height = 24 };
            }

            return new TextBlock
            {
                Text = font.Name,
                FontFamily = font,
                Width = 220,
                Height = 24,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        });
        _textFontFamilyBox.SelectedItem = FontFamily.Default;
        _fillIslandToggle.IsChecked = true;

        _nameBox.TextChanged += (_, _) => ApplyToSelected(l => l.Name = _nameBox.Text ?? "底图图层");
        _opacitySlider.ValueChanged += (_, _) => ApplyToSelected(l => l.Opacity = _opacitySlider.Value);
        _displayModeBox.SelectionChanged += (_, _) => ApplyToSelected(l => l.DisplayMode = Selected(_displayModeBox, WallpaperDisplayMode.Fill));
        _shadowToggle.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == ToggleSwitch.IsCheckedProperty)
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Image) l.ShadowEnabled = _shadowToggle.IsChecked == true; });
            }
        };
        _shadowBlurSpin.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == NumericUpDown.ValueProperty)
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Image) l.ShadowBlurRadius = _shadowBlurSpin.DoubleValue; });
            }
        };
        _shadowOffsetXSpin.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == NumericUpDown.ValueProperty)
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Image) l.ShadowOffsetX = _shadowOffsetXSpin.DoubleValue; });
            }
        };
        _shadowOffsetYSpin.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == NumericUpDown.ValueProperty)
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Image) l.ShadowOffsetY = _shadowOffsetYSpin.DoubleValue; });
            }
        };
        _shadowColorPicker.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property?.Name == "Color")
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Image) l.ShadowColor = _shadowColorPicker.Color.ToString(); });
            }
        };
        _shadowOpacitySlider.ValueChanged += (_, _) => ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Image) l.ShadowOpacity = _shadowOpacitySlider.Value; });
        _brushColorPicker.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property?.Name == "Color")
            {
                RememberActiveColor(_brushColorPicker.Color);
            }
        };
        _brushSizeSlider.ValueChanged += (_, _) =>
        {
            if (!_updatingInspector)
            {
                _canvas.BrushSize = _brushSizeSlider.Value;
            }
        };
        _brushTipBox.ItemsSource = BrushTipChoices;
        _brushTipBox.SelectedItem = BrushTipChoices[0];
        _brushTipBox.SelectionChanged += (_, _) =>
        {
            if (!_updatingInspector)
            {
                _canvas.BrushTip = Selected(_brushTipBox, WallpaperBrushTip.Round);
            }
        };
        _brushTaperToggle.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == ToggleSwitch.IsCheckedProperty)
            {
                _canvas.BrushTaper = _brushTaperToggle.IsChecked == true;
            }
        };
        _brushAaToggle.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == ToggleSwitch.IsCheckedProperty)
            {
                _canvas.BrushAntiAlias = _brushAaToggle.IsChecked == true;
            }
        };
        _shapeTypeBox.SelectionChanged += (_, _) =>
        {
            // 刷新检查器时的程序化选择不应触发应用或教程推进。
            if (_updatingInspector)
            {
                return;
            }

            ApplyToSelected(l =>
            {
                if (l.Kind == WallpaperLayerKind.Shape)
                {
                    l.ShapeType = Selected(_shapeTypeBox, WallpaperShapeType.Rectangle);
                }
            });
            // 推进教程的「选择形状类型」等待句（非该句时自动忽略）。
            TutorialServicePush("shape-type");
        };
        _shapeFillPicker.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property?.Name == "Color")
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Shape) l.FillColor = _shapeFillPicker.Color.ToString(); });
                // 手动换的填充色也会成为「记忆颜色」（新建形状 / 文本 / 画笔的默认色）。
                RememberActiveColor(_shapeFillPicker.Color);
            }
        };
        _shapeStrokePicker.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property?.Name == "Color")
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Shape) l.StrokeColor = _shapeStrokePicker.Color.ToString(); });
            }
        };
        _shapeStrokeSpin.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == NumericUpDown.ValueProperty)
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Shape) l.StrokeThickness = _shapeStrokeSpin.DoubleValue; });
            }
        };
        _shapeFillThemeToggle.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == ToggleSwitch.IsCheckedProperty)
            {
                var on = _shapeFillThemeToggle.IsChecked == true;
                ApplyToSelected(l =>
                {
                    if (l.Kind == WallpaperLayerKind.Shape)
                    {
                        l.FillUsesThemeColor = on;
                    }
                });
            }
        };
        _shapeStrokeThemeToggle.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == ToggleSwitch.IsCheckedProperty)
            {
                var on = _shapeStrokeThemeToggle.IsChecked == true;
                ApplyToSelected(l =>
                {
                    if (l.Kind == WallpaperLayerKind.Shape)
                    {
                        l.StrokeUsesThemeColor = on;
                    }
                });
            }
        };
        _shapeCornerRadiusSpin.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == NumericUpDown.ValueProperty)
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Shape) l.ShapeCornerRadius = _shapeCornerRadiusSpin.DoubleValue; });
            }
        };
        _shapeStarPointsSpin.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == NumericUpDown.ValueProperty)
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Shape) l.ShapeStarPoints = (int)_shapeStarPointsSpin.DoubleValue; });
            }
        };
        _shapeStarInsetSpin.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == NumericUpDown.ValueProperty)
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Shape) l.ShapeStarInset = _shapeStarInsetSpin.DoubleValue; });
            }
        };
        _textBox.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == TextBox.TextProperty)
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Text) l.Text = _textBox.Text ?? string.Empty; });
            }
        };
        _textFontSizeSpin.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == NumericUpDown.ValueProperty)
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Text) l.TextFontSize = _textFontSizeSpin.DoubleValue; });
            }
        };
        _textFontFamilyBox.SelectionChanged += (_, _) =>
        {
            if (!_updatingInspector && _textFontFamilyBox.SelectedItem is FontFamily font)
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Text) l.TextFontFamily = font.Name; });
            }
        };
        _textColorPicker.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property?.Name == "Color")
            {
                ApplyToSelected(l =>
                {
                    if (l.Kind == WallpaperLayerKind.Text)
                    {
                        l.TextColor = _textColorPicker.Color.ToString();
                    }
                });
                // 手动换的文字颜色也会成为「记忆颜色」（新建形状 / 文本 / 画笔的默认色）。
                RememberActiveColor(_textColorPicker.Color);
            }
        };
        _textColorThemeToggle.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == ToggleSwitch.IsCheckedProperty)
            {
                var on = _textColorThemeToggle.IsChecked == true;
                ApplyToSelected(l =>
                {
                    if (l.Kind == WallpaperLayerKind.Text)
                    {
                        l.TextUsesThemeColor = on;
                    }
                });
            }
        };
        _textStrokeToggle.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == ToggleSwitch.IsCheckedProperty)
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Text) l.TextStrokeEnabled = _textStrokeToggle.IsChecked == true; });
            }
        };
        _textStrokeColorPicker.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property?.Name == "Color")
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Text) l.TextStrokeColor = _textStrokeColorPicker.Color.ToString(); });
            }
        };
        _textStrokeThicknessSpin.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == NumericUpDown.ValueProperty)
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Text) l.TextStrokeThickness = _textStrokeThicknessSpin.DoubleValue; });
            }
        };
        _textUseSmtcTitleToggle.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == ToggleSwitch.IsCheckedProperty)
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Text) l.TextUseSmtcTitle = _textUseSmtcTitleToggle.IsChecked == true; });
            }
        };
        _textBoldToggle.PropertyChanged += (_, e) =>
        {
            if (!_updatingInspector && e.Property == ToggleSwitch.IsCheckedProperty)
            {
                ApplyToSelected(l => { if (l.Kind == WallpaperLayerKind.Text) l.TextBold = _textBoldToggle.IsChecked == true; });
            }
        };
        _textAlignBox.SelectionChanged += (_, _) => ApplyToSelected(l =>
        {
            if (l.Kind == WallpaperLayerKind.Text)
            {
                l.TextAlign = Selected(_textAlignBox, WallpaperTextAlign.Center);
            }
        });
        _smtcModeBox.SelectionChanged += (_, _) => ApplyToSelected(l =>
        {
            l.SmtcMode = Selected(_smtcModeBox, WallpaperLayerSmtcMode.AsImage);
            // 默认处理：强制铺满主界面（不可自定义尺寸/位移）。
            if (l.SmtcMode == WallpaperLayerSmtcMode.Default)
            {
                l.SizeMode = WallpaperLayerSizeMode.FillIsland;
            }
        });
        _smtcHidePausedToggle.PropertyChanged += (_, e) =>
        {
            if (_updatingInspector || e.Property != ToggleSwitch.IsCheckedProperty)
            {
                return;
            }

            ApplyToSelected(l =>
            {
                if (l.Source == WallpaperSource.SmtcAlbum)
                {
                    l.SmtcHideWhenPaused = _smtcHidePausedToggle.IsChecked == true;
                }
            });
        };
        _fillIslandToggle.PropertyChanged += (_, e) =>
        {
            if (_updatingInspector || e.Property != ToggleSwitch.IsCheckedProperty)
            {
                return;
            }

            ApplyToSelected(l =>
            {
                l.SizeMode = _fillIslandToggle.IsChecked == true ? WallpaperLayerSizeMode.FillIsland : WallpaperLayerSizeMode.Custom;
                if (l.SizeMode == WallpaperLayerSizeMode.Custom && (l.Width <= 0 || l.Height <= 0))
                {
                    l.Width = _canvas.IslandWidth;
                    l.Height = _canvas.IslandHeight;
                }
            });
            RefreshCustomSizePanel();
        };
        _widthSpin.PropertyChanged += (_, e) => { if (!_updatingInspector && e.Property == NumericUpDown.ValueProperty) ApplyToSelected(l => l.Width = _widthSpin.DoubleValue); };
        _heightSpin.PropertyChanged += (_, e) => { if (!_updatingInspector && e.Property == NumericUpDown.ValueProperty) ApplyToSelected(l => l.Height = _heightSpin.DoubleValue); };
        _rotationSpin.PropertyChanged += (_, e) => { if (!_updatingInspector && e.Property == NumericUpDown.ValueProperty) ApplyToSelected(l => l.Rotation = _rotationSpin.DoubleValue); };
        _offsetXSpin.PropertyChanged += (_, e) => { if (!_updatingInspector && e.Property == NumericUpDown.ValueProperty) ApplyToSelected(l => l.OffsetX = _offsetXSpin.DoubleValue); };
        _offsetYSpin.PropertyChanged += (_, e) => { if (!_updatingInspector && e.Property == NumericUpDown.ValueProperty) ApplyToSelected(l => l.OffsetY = _offsetYSpin.DoubleValue); };
        _anchorPicker.Changed += () =>
        {
            ApplyToSelected(l =>
            {
                l.AnchorX = _anchorPicker.AnchorX;
                l.AnchorY = _anchorPicker.AnchorY;
            });
            // 推进教程的「右对齐」等待句：第二张图锚点必须改成「右中」（水平靠右 + 垂直居中）才前进。
            if (HostTutorial.GetCurrentSentenceTag() == "anchor-right" &&
                _anchorPicker.AnchorX == WallpaperLayerAnchorX.Right &&
                _anchorPicker.AnchorY == WallpaperLayerAnchorY.Center)
            {
                TutorialServicePush("anchor-right");
            }
        };

        // 检查器布局：仿视频编辑器用 TabStrip 分段（常规 / 内容 / 效果 / 变换）对配置项分组。
        // 下面按「常规 → 效果 → 内容 → 变换」把行拼进对应分组页；段内行仍由 RefreshInspector
        // 按选中图层类型逐项显隐，TabStrip 段本身按类型显隐（内容=形状/文本、效果=图片、
        // 变换=非 SMTC 默认处理），画笔 / 选区是工具上下文组、与分段无关。
        var inspector = new StackPanel { Spacing = 8 };
        var generalPage = new StackPanel { Spacing = 8 };   // 「常规」：图层 + 外观 + 画布图层操作
        var contentPage = new StackPanel { Spacing = 8 };   // 「内容」：形状 / 文本图层专属
        var effectPage = new StackPanel { Spacing = 8 };    // 「效果」：投影（图片图层）
        var transformPage = new StackPanel { Spacing = 8 }; // 「变换」：尺寸 / 旋转 / 相对定位
        _inspectorPages["general"] = generalPage;
        _inspectorPages["content"] = contentPage;
        _inspectorPages["effect"] = effectPage;
        _inspectorPages["transform"] = transformPage;
        // 「常规」分组：名称 + SMTC / 不透明度 / 显示方式。
        _nameItem = SettingsRow("名称", _nameBox);
        generalPage.Children.Add(_nameItem);
        // 目标分块（分体多图层）：整岛或某个分体块；分体模式下运行时按块各自绘制。
        RefreshSplitBlockOptions();
        _splitBlockItem = SettingsRow("目标分块", _splitBlockBox);
        generalPage.Children.Add(_splitBlockItem);
        _splitBlockBox.SelectionChanged += (_, _) =>
        {
            // SelectionChanged 在程序化回填时会以 _updatingInspector 抑制，此处仅处理用户选择。
            if (_updatingInspector)
            {
                return;
            }

            ApplyToSelected(l => l.SplitBlockId = Selected(_splitBlockBox, ""));
        };

        _smtcModeItem = SettingsRow("SMTC 模式", _smtcModeBox);
        generalPage.Children.Add(_smtcModeItem);
        _smtcHidePausedItem = SettingsRow("暂停/停止时隐藏", _smtcHidePausedToggle);
        generalPage.Children.Add(_smtcHidePausedItem);
        _opacityItem = SettingsRow("不透明度", _opacitySlider);
        generalPage.Children.Add(_opacityItem);
        _displayModeItem = SettingsRow("显示方式", _displayModeBox);
        generalPage.Children.Add(_displayModeItem);
        // 「效果」分组：投影（高斯模糊 / 色相饱和度 / 亮度对比度改由顶部命令栏的滤镜窗口调整）。
        _shadowItem = SettingsRow("投影", _shadowToggle);
        _shadowBlurItem = SettingsRow("投影模糊", _shadowBlurSpin);
        _shadowOffsetXItem = SettingsRow("投影水平偏移", _shadowOffsetXSpin);
        _shadowOffsetYItem = SettingsRow("投影垂直偏移", _shadowOffsetYSpin);
        _shadowColorItem = SettingsRow("投影颜色", _shadowColorPicker);
        _shadowOpacityItem = SettingsRow("投影不透明度", _shadowOpacitySlider);
        effectPage.Children.Add(_shadowItem);
        effectPage.Children.Add(_shadowBlurItem);
        effectPage.Children.Add(_shadowOffsetXItem);
        effectPage.Children.Add(_shadowOffsetYItem);
        effectPage.Children.Add(_shadowColorItem);
        effectPage.Children.Add(_shadowOpacityItem);
        // 画笔 / 橡皮擦 / 取色设置（对应工具激活时独占显示，与图层常规设置分开）。
        _brushColorItem = SettingsRow("画笔颜色", _brushColorPicker);
        _brushSizeItem = SettingsRow("画笔大小", _brushSizeSlider);
        _brushTipItem = SettingsRow("笔触", _brushTipBox);
        _brushTaperItem = SettingsRow("笔锋", _brushTaperToggle);
        _brushAaItem = SettingsRow("抗锯齿", _brushAaToggle);
        _brushGroupTitle = GroupSubtitle("\uEC49", "画笔");
        _brushGroup = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _brushGroupTitle,
                _brushColorItem,
                _brushSizeItem,
                _brushTipItem,
                _brushTaperItem,
                _brushAaItem
            }
        };
        // 「内容」分组：形状图层专属（形状类型 / 填充 / 描边）。
        _shapeTypeItem = SettingsRow("形状类型", _shapeTypeBox);
        _shapeCornerRadiusItem = SettingsRow("圆角半径", _shapeCornerRadiusSpin);
        _shapeStarPointsItem = SettingsRow("星角数", _shapeStarPointsSpin);
        _shapeStarInsetItem = SettingsRow("内凹比例", _shapeStarInsetSpin);
        _shapeFillItem = SettingsRow("填充色", _shapeFillPicker);
        _shapeFillThemeItem = SettingsRow("填充色跟随主题", _shapeFillThemeToggle);
        _shapeStrokeItem = SettingsRow("描边色", _shapeStrokePicker);
        _shapeStrokeThemeItem = SettingsRow("描边色跟随主题", _shapeStrokeThemeToggle);
        _shapeStrokeWidthItem = SettingsRow("描边粗细", _shapeStrokeSpin);
        contentPage.Children.Add(_shapeTypeItem);
        contentPage.Children.Add(_shapeCornerRadiusItem);
        contentPage.Children.Add(_shapeStarPointsItem);
        contentPage.Children.Add(_shapeStarInsetItem);
        contentPage.Children.Add(_shapeFillItem);
        contentPage.Children.Add(_shapeFillThemeItem);
        contentPage.Children.Add(_shapeStrokeItem);
        contentPage.Children.Add(_shapeStrokeThemeItem);
        contentPage.Children.Add(_shapeStrokeWidthItem);
        // 「内容」分组：文本图层专属（内容 / 字体 / 颜色 / 描边 / 对齐）。
        _textItem = SettingsRow("文本内容", _textBox);
        _textFontSizeItem = SettingsRow("字号", _textFontSizeSpin);
        _textFontFamilyItem = SettingsRow("字体", _textFontFamilyBox);
        _textColorItem = SettingsRow("文字颜色", _textColorPicker);
        _textColorThemeItem = SettingsRow("文字颜色跟随主题", _textColorThemeToggle);
        _textStrokeItem = SettingsRow("文字描边", _textStrokeToggle);
        _textStrokeColorItem = SettingsRow("描边颜色", _textStrokeColorPicker);
        _textStrokeThicknessItem = SettingsRow("描边粗细", _textStrokeThicknessSpin);
        _textUseSmtcTitleItem = SettingsRow("显示为媒体标题", _textUseSmtcTitleToggle);
        _textBoldItem = SettingsRow("加粗", _textBoldToggle);
        _textAlignItem = SettingsRow("水平对齐", _textAlignBox);
        contentPage.Children.Add(_textItem);
        contentPage.Children.Add(_textFontSizeItem);
        contentPage.Children.Add(_textFontFamilyItem);
        contentPage.Children.Add(_textColorItem);
        contentPage.Children.Add(_textColorThemeItem);
        contentPage.Children.Add(_textStrokeItem);
        contentPage.Children.Add(_textStrokeColorItem);
        contentPage.Children.Add(_textStrokeThicknessItem);
        contentPage.Children.Add(_textUseSmtcTitleItem);
        contentPage.Children.Add(_textBoldItem);
        contentPage.Children.Add(_textAlignItem);
        // 「变换」分组：尺寸（铺满主界面 / 自定义宽高）+ 旋转 + 相对定位（锚点 / 偏移 / 重置）。
        _widthItem = SettingsRow("宽度 (px)", _widthSpin);
        _heightItem = SettingsRow("高度 (px)", _heightSpin);
        _fillIslandItem = SettingsRow("铺满主界面", _fillIslandToggle);
        transformPage.Children.Add(_fillIslandItem);
        transformPage.Children.Add(_widthItem);
        transformPage.Children.Add(_heightItem);
        _rotationItem = SettingsRow("角度 (°)", _rotationSpin);
        transformPage.Children.Add(_rotationItem);
        _anchorItem = SettingsRow("锚点", _anchorPicker);
        transformPage.Children.Add(_anchorItem);
        _offsetXItem = SettingsRow("水平偏移 (px)", _offsetXSpin);
        transformPage.Children.Add(_offsetXItem);
        _offsetYItem = SettingsRow("垂直偏移 (px)", _offsetYSpin);
        transformPage.Children.Add(_offsetYItem);
        transformPage.Children.Add(_relativeHint);
        _resetTransformItem = SettingsRow("重置变换", Button("重置变换", ResetLayerTransform));
        transformPage.Children.Add(_resetTransformItem);

        // ---- 像素选区操作（当前图层有选区时显示，与分段无关）----
        _selectionToLayerButton = Button("从选区新建图层", () =>
        {
            _canvas.LayerFromSelection();
            RefreshInspector();
        });
        _clearSelectionButton = Button("清除选区", () => _canvas.ClearSelection());
        _selectionGroup = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                GroupSubtitle("\uEF06", "选区"),
                SettingsRow("操作", new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { _selectionToLayerButton, _clearSelectionButton }
                }),
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.75,
                    FontSize = 12,
                    Text = "使用移动工具（V）拖动选区内容。"
                }
            }
        };

        // ---- 画布图层操作（选中画布图层时显示，归入「常规」分组）----
        _rasterizeCanvasButton = Button("栅格化为图片", () =>
        {
            var layer = _canvas.SelectedLayer;
            if (layer != null)
            {
                RasterizeSelected();
                RefreshInspector();
            }
        });
        _canvasGroup = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                GroupSubtitle("\uE20C", "画布图层"),
                _rasterizeCanvasButton,
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.75,
                    FontSize = 12,
                    Text = "画布图层铺满整个画布，可自由绘制，但不会出现在主界面上；栅格化后裁出主界面区域转为普通图片图层。"
                }
            }
        };
        generalPage.Children.Add(_canvasGroup);

        // 组装：顶部 TabStrip 分段条 + 未选中占位提示 + 工具上下文组 + 各分组页。
        // 上下文组（画笔 / 选区）在分段之上（与旧版同序），选中图层后才显示分段条。
        BuildInspectorTabStrip();
        _noLayerHint = new TextBlock
        {
            Text = "未选中图层。点击画布上的图层，或在左侧图层面板选择。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            FontSize = 12
        };
        inspector.Children.Add(_inspectorSegmented);
        inspector.Children.Add(_noLayerHint);
        inspector.Children.Add(_brushGroup);
        inspector.Children.Add(_selectionGroup);
        inspector.Children.Add(generalPage);
        inspector.Children.Add(contentPage);
        inspector.Children.Add(effectPage);
        inspector.Children.Add(transformPage);
        // 初始只激活「常规」页，其余页等选中后由 RefreshInspector 决定。
        _inspectorSegmented.IsVisible = false;
        SetPageVisibility("general");
        _noLayerHint.IsVisible = true;
        return inspector;
    }
}