using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ClassIsland.Core.Controls;
using FluentAvalonia.UI.Controls;

namespace ClassIslandInjector.Views;

/// <summary>
/// Photoshop 风格底图图层编辑器。
/// 概念：
/// - 画布 = 一层「ClassIsland 主界面」（默认锁定，解锁后可拖动边缘模拟主界面长度变化）
///   叠加任意数量的图片图层（锚点相对定位 + 像素偏移 + 尺寸 + 旋转）。
/// - 相对定位：图层矩形由「锚点（左/中/右 × 上/中/下）+ 偏移」表达，
///   因此 ClassIsland 主界面长度变化时底图按锚点自适应。
/// - 拖动/缩放时显示智能对齐标尺（PS 式洋红色/青色参考线），并自动吸附。
/// </summary>
/// <remarks>检查器相关成员按职责拆分到 <c>WallpaperLayerEditor.Inspector.cs</c>（partial 类归档）。</remarks>
internal sealed partial class WallpaperLayerEditorWindow : MyWindow
{
    private const double DefaultIslandWidth = 400;
    private const double DefaultIslandHeight = 90;

    private readonly WallpaperLayerCanvas _canvas = new();
    private readonly StackPanel _layerStack = new() { Spacing = 4 };
    private readonly TextBlock _statusText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.85,
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center
    };

    // ---- 检查器控件 ----
    private readonly TextBox _nameBox = new() { MaxWidth = 180 };
    private readonly Slider _opacitySlider = SliderControl(0, 1, 0.05);
    private readonly ComboBox _displayModeBox = new() { MinWidth = 120 };
    private readonly ComboBox _smtcModeBox = new() { MinWidth = 150 };
    private readonly ToggleSwitch _smtcHidePausedToggle = new() { OnContent = "开", OffContent = "关" };
    private readonly ToggleSwitch _fillIslandToggle = new() { OnContent = "开", OffContent = "关" };
    private readonly EditorSpin _widthSpin = new(1, 2000, 1, "0");
    private readonly EditorSpin _heightSpin = new(1, 2000, 1, "0");
    private readonly EditorSpin _rotationSpin = new(-360, 360, 1, "0");
    private readonly EditorSpin _offsetXSpin = new(-2000, 2000, 1, "0");
    private readonly EditorSpin _offsetYSpin = new(-2000, 2000, 1, "0");
    private readonly AnchorGridPicker _anchorPicker = new() { Name = "EditorAnchorPicker" };
    /// <summary>相对位置说明。</summary>
    private readonly TextBlock _relativeHint = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.75,
        FontSize = 12
    };
    /// <summary>名称行 / 不透明度行（未选中图层时隐藏）。</summary>
    private Control _nameItem = null!;
    private Control _opacityItem = null!;
    /// <summary>铺满主界面 / 旋转 / 锚点 / 偏移行（未选中图层与 SMTC 默认模式时隐藏）。</summary>
    private Control _fillIslandItem = null!;
    private Control _rotationItem = null!;
    private Control _anchorItem = null!;
    private Control _offsetXItem = null!;
    private Control _offsetYItem = null!;
    /// <summary>检查器 TabStrip 分段条与分组页（仿视频编辑器：TabStrip 分段条切换配置分组）。</summary>
    private TabStrip _inspectorSegmented = null!;
    private readonly Dictionary<string, TabStripItem> _inspectorTabs = [];
    private readonly Dictionary<string, StackPanel> _inspectorPages = [];
    private string _activeInspectorPage = "general";
    /// <summary>上次分段刷新时的选中指纹（用于识别「新选中」而非原地编辑，避免 Tab 跳变）。</summary>
    private string _lastSelectionProfile = "";
    /// <summary>分段条程序化刷新时的重入保护。</summary>
    private bool _updatingSegments;
    /// <summary>未选中图层时的占位提示。</summary>
    private Control _noLayerHint = null!;
    /// <summary>「重置变换」行（未选中图层时隐藏）。</summary>
    private Control _resetTransformItem = null!;
    /// <summary>自定义尺寸的两行（铺满主界面关闭时显示）。</summary>
    private Control _widthItem = null!;
    private Control _heightItem = null!;
    /// <summary>SMTC 模式行（仅选中 SMTC 图层时显示）。</summary>
    private Control _smtcModeItem = null!;
    /// <summary>「暂停/停止时隐藏」行（仅选中 SMTC 图层时显示）。</summary>
    private Control _smtcHidePausedItem = null!;
    /// <summary>显示方式行（仅位图图层显示）。</summary>
    private Control _displayModeItem = null!;
    // 效果（仅图片图层）：投影（高斯模糊 / 色相饱和度等改由顶部命令栏的滤镜窗口调整）
    private readonly ToggleSwitch _shadowToggle = new() { OnContent = "开", OffContent = "关" };
    private readonly EditorSpin _shadowBlurSpin = new(0, 100, 0.5, "0.##");
    private readonly EditorSpin _shadowOffsetXSpin = new(-100, 100, 1, "0");
    private readonly EditorSpin _shadowOffsetYSpin = new(-100, 100, 1, "0");
    private readonly ColorPicker _shadowColorPicker = ColorPicker();
    private readonly Slider _shadowOpacitySlider = SliderControl(0, 1, 0.05);
    private Control _shadowItem = null!;
    private Control _shadowBlurItem = null!;
    private Control _shadowOffsetXItem = null!;
    private Control _shadowOffsetYItem = null!;
    private Control _shadowColorItem = null!;
    private Control _shadowOpacityItem = null!;
    // 画笔 / 橡皮擦设置（对应工具激活时显示）
    private readonly ColorPicker _brushColorPicker = ColorPicker();
    private readonly Slider _brushSizeSlider = SliderControl(1, 100, 1);
    private Control _brushColorItem = null!;
    private Control _brushSizeItem = null!;
    private readonly ComboBox _brushTipBox = new() { MinWidth = 120 };
    private Control _brushTipItem = null!;
    private readonly ToggleSwitch _brushTaperToggle = new() { OnContent = "开", OffContent = "关" };
    private readonly ToggleSwitch _brushAaToggle = new() { OnContent = "开", OffContent = "关" };
    private Control _brushTaperItem = null!;
    private Control _brushAaItem = null!;
    private IconText _brushGroupTitle = null!;
    /// <summary>逻辑运算按钮弹出的原生菜单（FAMenuFlyout，菜单项带图标）。</summary>
    private FAMenuFlyout _booleanMenu = null!;
    /// <summary>教学「缩放」等待句：记录第二张示例图插入时的图层 Id 与初始尺寸（拖动角落缩放到尺寸变化才算完成）。</summary>
    private string? _tutorialScaleLayerId;
    private (double W, double H)? _tutorialScaleBaseline;
    private string _lastReminder = string.Empty;
    private DateTime _lastReminderAt;
    /// <summary>像素选区操作组（当前图层有选区时显示）。</summary>
    private Control _selectionGroup = null!;
    private Button _selectionToLayerButton = null!;
    private Button _clearSelectionButton = null!;
    /// <summary>画布图层操作组（选中画布图层时显示）。</summary>
    private Control _canvasGroup = null!;
    private Button _rasterizeCanvasButton = null!;
    /// <summary>画笔 / 橡皮擦设置组（画笔工具激活时独占显示）。</summary>
    private Control _brushGroup = null!;
    /// <summary>命令栏滤镜按钮（仅选中图片图层时可用）。</summary>
    private CommandBarButton _hslFilterButton = null!;
    private CommandBarButton _brightnessFilterButton = null!;
    private CommandBarButton _blurFilterButton = null!;
    // 形状图层检查器
    private readonly ComboBox _shapeTypeBox = new() { MinWidth = 140, Name = "EditorShapeType" };
    private readonly ColorPicker _shapeFillPicker = ColorPicker();
    private readonly ColorPicker _shapeStrokePicker = ColorPicker();
    private readonly EditorSpin _shapeStrokeSpin = new(0, 40, 0.25, "0.##");
    private readonly ToggleSwitch _shapeFillThemeToggle = new() { OnContent = "开", OffContent = "关" };
    private readonly ToggleSwitch _shapeStrokeThemeToggle = new() { OnContent = "开", OffContent = "关" };
    private readonly EditorSpin _shapeCornerRadiusSpin = new(0, 100, 1, "0");
    private readonly EditorSpin _shapeStarPointsSpin = new(3, 16, 1, "0");
    private readonly EditorSpin _shapeStarInsetSpin = new(0.1, 0.95, 0.05, "0.##");
    private Control _shapeTypeItem = null!;
    private Control _shapeCornerRadiusItem = null!;
    private Control _shapeStarPointsItem = null!;
    private Control _shapeStarInsetItem = null!;
    private Control _shapeFillItem = null!;
    private Control _shapeStrokeItem = null!;
    private Control _shapeStrokeWidthItem = null!;
    private Control _shapeFillThemeItem = null!;
    private Control _shapeStrokeThemeItem = null!;
    // 文本图层检查器
    private readonly TextBox _textBox = new() { MaxWidth = 200, Watermark = "文本内容" };
    private readonly EditorSpin _textFontSizeSpin = new(6, 200, 1, "0");
    private readonly ComboBox _textFontFamilyBox = new() { MinWidth = 140, MaxDropDownHeight = 360 };
    private readonly ColorPicker _textColorPicker = ColorPicker();
    private readonly ToggleSwitch _textColorThemeToggle = new() { OnContent = "开", OffContent = "关" };
    private readonly ToggleSwitch _textBoldToggle = new() { OnContent = "开", OffContent = "关" };
    private readonly ToggleSwitch _textStrokeToggle = new() { OnContent = "开", OffContent = "关" };
    private readonly ColorPicker _textStrokeColorPicker = ColorPicker();
    private readonly EditorSpin _textStrokeThicknessSpin = new(0, 20, 0.25, "0.##");
    private readonly ToggleSwitch _textUseSmtcTitleToggle = new() { OnContent = "开", OffContent = "关" };
    private readonly ComboBox _textAlignBox = new() { MinWidth = 140 };
    private Control _textItem = null!;
    private Control _textFontSizeItem = null!;
    private Control _textFontFamilyItem = null!;
    private Control _textColorItem = null!;
    private Control _textColorThemeItem = null!;
    private Control _textStrokeItem = null!;
    private Control _textStrokeColorItem = null!;
    private Control _textStrokeThicknessItem = null!;
    private Control _textUseSmtcTitleItem = null!;
    private Control _textBoldItem = null!;
    private Control _textAlignItem = null!;

    // ---- 状态 ----
    /// <summary>图层文档（单一数据源）：统一持有图层列表、撤销/重做历史与未保存标记。</summary>
    private readonly WallpaperLayerDocument _document;
    /// <summary>当前图层列表（<c>_document.Layers</c> 的别名，供既有代码原地引用；保持单一数据源）。</summary>
    private List<WallpaperLayerItem> _layers;
    private bool _updatingInspector;
    /// <summary>窗口内容根。</summary>
    private Grid? _contentGrid;
    /// <summary>舞台 + 右侧栏所在的主体网格（提升为字段以在窗口缩放时约束其高度）。</summary>
    private Grid _body = null!;
    /// <summary>命令栏撤销/重做按钮（按栈状态启停）。</summary>
    private CommandBarButton _undoButton = null!;
    private CommandBarButton _redoButton = null!;
    /// <summary>命令栏组合/取消组合按钮（按选中状态启停）。</summary>
    private CommandBarButton _groupButton = null!;
    private CommandBarButton _ungroupButton = null!;
    /// <summary>图层面板操作按钮（按选中状态启停；效果仅背景可用）。</summary>
    private Button _newLayerButton = null!;
    /// <summary>图层面板「新建空白图层」按钮（始终可用）。</summary>
    private Button _newBlankLayerButton = null!;
    private Button _duplicateButton = null!;
    private Button _deleteButton = null!;
    private Button _effectButton = null!;
    /// <summary>图层面板「栅格化」按钮（形状 / 文本图层选中时可用）。</summary>
    private Button _rasterizeButton = null!;
    /// <summary>拖拽排序：独立置顶的「幽灵快照」预览窗口（参考「主界面 → 组件」拖拽）。</summary>
    private Window? _dragPreviewWindow;
    private Border? _dragPreviewHost;
    /// <summary>拖拽排序：指针在源行内的抓取偏移（屏幕像素）。</summary>
    private Point _reorderGrabOffset;
    /// <summary>左侧工具栏按钮（按工具选中态更新）。</summary>
    private readonly Dictionary<WallpaperEditorTool, Button> _toolButtons = [];

    // ---- 入场动画 ----
    /// <summary>入场动画已播放（只播一次）。</summary>
    private bool _entrancePlayed;
    /// <summary>顶部命令栏（入场动画目标）。</summary>
    private Control _commandBarHost = null!;
    /// <summary>左侧工具栏容器（入场动画目标）。</summary>
    private Control _toolbarHost = null!;
    /// <summary>右侧检查器列（入场动画目标）。</summary>
    private Control _rightColumn = null!;
    /// <summary>底部图层面板（入场动画目标）。</summary>
    private Control _layerPanelHost = null!;
    /// <summary>左侧工具栏 StackPanel（供按钮逐个进场）。</summary>
    private StackPanel _toolPanel = null!;
    /// <summary>工具栏宿主（按钮面板 + 选中高亮滑块叠加）。</summary>
    private Grid _toolHighlightHost = null!;
    /// <summary>工具栏选中高亮滑块（切换工具时滑动到新位置）。</summary>
    private Border _toolHighlight = null!;
    /// <summary>高亮滑块是否已定位（首次直接放置，之后滑动）。</summary>
    private bool _toolHighlightPositioned;
    /// <summary>各工具按钮的悬停状态（已选中工具 hover 用主题色高亮块表达）。</summary>
    private readonly Dictionary<WallpaperEditorTool, bool> _toolHovered = [];
    /// <summary>首次构建时收集的图层行（窗口打开时逐行滑入）。</summary>
    private readonly List<LayerRowControl> _pendingLayerRows = [];
    /// <summary>上次刷新时的图层 id（用于识别新增图层做入场动画）。</summary>
    private readonly HashSet<string> _knownLayerIds = [];
    /// <summary>编辑器入场动画诊断日志（配置目录）。</summary>
    private static readonly string EditorAnimLog = Path.Combine(InjectorRuntime.ConfigDirectory, "editor-anim.log");

    /// <summary>当前打开的编辑器实例。同一时刻只允许一个编辑器窗口（多开会造成关闭确认
    /// 的 ContentDialog 找不到 TopLevel 而崩溃）。</summary>
    public static WallpaperLayerEditorWindow? Current { get; private set; }

    public WallpaperLayerEditorWindow()
    {
        Title = "底图图层编辑器";
        Width = 1240;
        Height = 800;
        MinWidth = 980;
        MinHeight = 640;
        SystemDecorations = SystemDecorations.Full;
        // 在 Show 之前设置透明级别启用 Mica（不走宿主 EnableMicaWindow，其在 Loaded 才设置、
        // 太晚会整窗半透明看不清）；用半透明主题基底分层，侧栏和画布再使用独立表面，
        // 避免深色主题整窗一片灰。
        EditorMica.EnableMica(this);

        // 撤销 / 重做历史：捕获当前图层列表的深拷贝作为快照；连续高频变更（<500ms）合并为一条。
        _document = new WallpaperLayerDocument(
            InjectorRuntime.Settings.WallpaperLayers.Select(l => l.Clone()).ToList());
        _layers = _document.Layers;
        // UI 统一刷新入口：文档任何变更（增删 / 编辑 / 撤销 / 重做 / 保存）都会触发，
        // 收敛原先分散的手动刷新链（RefreshLayerList / RefreshInspector / UpdateStatus）。
        _document.Changed += () =>
        {
            _canvas.Layers = _layers;
            RefreshLayerList();
            RefreshInspector();
            UpdateStatus();
            UpdateGroupButtons();
            UpdateLayerActionButtons();
        };
        _document.UndoRedoChanged += UpdateUndoRedoState;
        var islandSize = InjectorRuntime.GetCurrentIslandSize();
        _canvas.SetIslandSize(islandSize?.Width > 0 ? islandSize.Value.Width : DefaultIslandWidth,
            islandSize?.Height > 0 ? islandSize.Value.Height : DefaultIslandHeight);
        _canvas.ZOrder = InjectorRuntime.Settings.WallpaperZOrder;
        _canvas.Layers = _layers;
        // 恢复上次吸管取到的默认色（新建形状 / 文本 / 画笔的默认色，自动记忆）。
        if (Color.TryParse(InjectorRuntime.Settings.EditorPickedColor, out var picked))
        {
            _canvas.ActiveColor = picked;
        }

        // 供宿主教程 TargetSelector 定位（#EditorCanvas）。
        _canvas.Name = "EditorCanvas";

        WireCanvas();
        BuildContent();
        // 状态栏文字更新时做淡入过渡（集中订阅 Text 变化，覆盖所有状态提示）。
        _statusText.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBlock.TextProperty)
            {
                _statusText.Opacity = 0.4;
                EditorAnimations.FadeIn(_statusText, 0.4, 0.85, EditorAnimations.TapDuration, EditorAnimations.Interaction);
            }
        };
        RefreshLayerList();
        RefreshInspector();
        UpdateStatus();
        // 编辑器级快捷键（撤销 / 重做、PS 式滤镜快捷键）；画布未处理的按键会冒泡到这里。
        KeyDown += EditorWindowOnKeyDown;
        Closing += OnClosingConfirm;
        // 单例跟踪：新窗口打开时覆盖 Current，关闭时若仍是本窗口则清空。
        Current = this;
        Closed += (_, _) =>
        {
            // 幽灵拖拽预览窗口只 Hide 不 Close 会残留置顶窗口：随编辑器一起关闭释放。
            _dragPreviewWindow?.Close();
            _dragPreviewWindow = null;
            _dragPreviewHost = null;
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }
        };
        Opened += OnOpened;
    }

    /// <summary>窗口显示后触发未完成的「底图编辑器入门」教程（只播一次，完成后不再自动出现）。</summary>
    private void OnOpened(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            PlayEntranceAnimations();
            HostTutorial.BeginNotCompletedTutorials("classislandInjector.tutorials.wallpaperEditor/prologue");
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 打开窗口时播放的入场动画（非线性，快速利落）：
    /// 顶部命令栏下滑淡入、左侧工具栏左滑淡入 + 工具按钮逐个弹性进场、右侧检查器右滑淡入、
    /// 底部图层面板上滑（稍晚）、画布内容缩放淡入、图层列表逐行滑入。
    /// 错峰统一用 Animation.Delay（UI 线程同步调度，确定性时序），不依赖 Task/挂载检查。
    /// </summary>
    private void PlayEntranceAnimations()
    {
        if (_entrancePlayed)
        {
            return;
        }

        _entrancePlayed = true;
        DiagnosticLog.Write(EditorAnimLog, "PlayEntranceAnimations 开始");

        // 顶部命令栏：自上而下滑入 + 淡入（不透明度柔滑，位移弹性）。
        EditorAnimations.FadeIn(_commandBarHost, 0, 1, EditorAnimations.InDuration, EditorAnimations.Interaction);
        EditorAnimations.SlideIn(_commandBarHost, 0, -14, EditorAnimations.InDuration, EditorAnimations.Entrance);

        // 左侧工具栏：自左滑入 + 淡入（按钮再逐个进场）。
        EditorAnimations.FadeIn(_toolbarHost, 0, 1, EditorAnimations.InDuration, EditorAnimations.Interaction);
        EditorAnimations.SlideIn(_toolbarHost, -24, 0, EditorAnimations.InDuration, EditorAnimations.Entrance);

        // 右侧检查器：自右滑入 + 淡入。
        EditorAnimations.FadeIn(_rightColumn, 0, 1, EditorAnimations.InDuration, EditorAnimations.Interaction);
        EditorAnimations.SlideIn(_rightColumn, 28, 0, EditorAnimations.InDuration, EditorAnimations.Entrance);

        // 底部图层面板：自下而上（稍晚于检查器）。
        var panelDelay = TimeSpan.FromMilliseconds(70);
        EditorAnimations.FadeIn(_layerPanelHost, 0, 1, EditorAnimations.InDuration, EditorAnimations.Interaction, panelDelay);
        EditorAnimations.SlideIn(_layerPanelHost, 0, 20, EditorAnimations.InDuration, EditorAnimations.Entrance, panelDelay);

        // 左侧工具栏按钮逐个进场（错峰 + 弹性缩放弹出，用 Animation.Delay 同步错峰）。
        var toolButtons = _toolPanel.Children.OfType<Button>().ToList();
        for (var i = 0; i < toolButtons.Count; i++)
        {
            EditorAnimations.PopIn(toolButtons[i], -12, 0, 0.85, EditorAnimations.InDuration, EditorAnimations.Entrance,
                TimeSpan.FromMilliseconds(60 + i * EditorAnimations.StaggerStep.TotalMilliseconds));
        }

        // 选中高亮滑块：布局完成后定位到当前工具位置，并随第一个按钮淡入。
        SlideToolHighlight(false);
        EditorAnimations.FadeIn(_toolHighlight, 0, 1, EditorAnimations.InDuration, EditorAnimations.Interaction,
            TimeSpan.FromMilliseconds(60));

        // 图层列表逐行滑入（首个图层行稍晚于工具栏按钮）。
        for (var i = 0; i < _pendingLayerRows.Count; i++)
        {
            EditorAnimations.PopIn(_pendingLayerRows[i], 18, 0, 0.94, EditorAnimations.InDuration, EditorAnimations.Entrance,
                TimeSpan.FromMilliseconds(120 + i * EditorAnimations.StaggerStep.TotalMilliseconds));
        }
        _pendingLayerRows.Clear();

        // 画布内容（主界面 + 图层）：缩放淡入。
        _canvas.PlayEntranceAnimation();
        DiagnosticLog.Write(EditorAnimLog, $"PlayEntranceAnimations 完成：工具栏按钮 {toolButtons.Count} 个、图层行已调度");

        // 诊断：入场结束后（约 900ms）复查各按钮实际不透明度，确认动画真实执行到位。
        var checkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        checkTimer.Tick += (_, _) =>
        {
            checkTimer.Stop();
            var hidden = toolButtons.Where(b => b.Opacity < 0.5).Select(b => b.Name ?? b.GetType().Name).ToList();
            DiagnosticLog.Write(EditorAnimLog,
                $"入场后 900ms 复查：按钮 {toolButtons.Count} 个，仍不可见 {hidden.Count} 个：{string.Join(",", hidden)}");
        };
        checkTimer.Start();
    }

    /// <summary>按标签向前推动教程（仅当教程正停在该标签的等待句时生效）。</summary>
    private static void TutorialServicePush(string tag)
    {
        HostTutorial.PushToNextSentenceByTag(tag);
    }

    /// <summary>画布被编辑（移动/缩放等）后推进教程：缩放等待句仅在第二张示例图尺寸真的变化时前进。</summary>
    private void AdvanceTutorialOnEdit()
    {
        var tag = HostTutorial.GetCurrentSentenceTag();
        if (tag == "scale" && _tutorialScaleLayerId != null && _canvas.SelectedLayer is { } l &&
            l.Id == _tutorialScaleLayerId)
        {
            var (bw, bh) = _tutorialScaleBaseline ?? (0, 0);
            if (Math.Abs(l.Width - bw) > 0.5 || Math.Abs(l.Height - bh) > 0.5)
            {
                TutorialServicePush("scale");
                return;
            }
        }

        // 旧的「拖动摆位」等待句仍在旧版工程里出现过，保留推进不碍事。
        TutorialServicePush("move");
    }

    /// <summary>教程插入第二张图（非背景、可缩放靠右对齐）时记录其缩放基线。</summary>
    private void RecordTutorialScaleBaseline(WallpaperLayerItem layer)
    {
        _tutorialScaleLayerId = layer.Id;
        _tutorialScaleBaseline = (layer.Width, layer.Height);
    }

    private void WireCanvas()
    {
        _canvas.EditStarted += () =>
        {
            PushUndo();
            _document.MarkDirty();
        };
        _canvas.Edited += () =>
        {
            RefreshLayerList();
            RefreshInspector();
            UpdateStatus();
            // 推进教程的「移动/缩放」等待句：缩放句要等第二张示例图尺寸真的变化才前进。
            AdvanceTutorialOnEdit();
        };
        _canvas.ShapeCreated += () => TutorialServicePush("shape");
        _canvas.TextCreated += () => TutorialServicePush("text");
        _canvas.SelectionChanged += () =>
        {
            RefreshLayerList();
            RefreshInspector();
            UpdateStatus();
            UpdateGroupButtons();
            UpdateLayerActionButtons();
            // 选中变化时同步各滤镜窗口的数值。
            SyncFilterWindows();
        };
        _canvas.IslandChanged += () =>
        {
            RefreshInspector();
            UpdateStatus();
            // 推进教程的「拖动主界面边缘」等待句（非该句时自动忽略）。
            TutorialServicePush("resize");
        };
        _canvas.ImagesChanged += RefreshLayerList;
        _canvas.DeleteRequested += DeleteLayer;
        _canvas.RasterizeRequested += RasterizeSelected;
        _canvas.ColorPicked += OnColorPicked;
        _canvas.ColorPreview += OnColorPreview;
        _canvas.HintRequested += ShowReminder;
        _canvas.ToolChanged += _ =>
        {
            UpdateToolBarSelection();
            // 选中高亮滑块滑动到新工具的位置。
            SlideToolHighlight(true);
            // 切换工具后刷新检查器，显示 / 隐藏「画笔」设置。
            RefreshInspector();
        };
        // 选区创建 / 清除时刷新检查器（显示 / 隐藏「选区」操作组）。
        _canvas.SelectionStateChanged += RefreshInspector;
    }

    private void BuildContent()
    {
        // ---- 顶部命令栏（参考 ClassIsland 档案编辑窗口的 CommandBar）----
        // 层级不再用下拉框：改为直接拖拽图层面板里的「背景图层」行调整（顶部 = 底色之后，底部 = 底色之上）。
        _undoButton = CommandButton("\uE195", "撤销", "撤销上一步操作", Undo);
        _redoButton = CommandButton("\uE121", "重做", "重做已撤销的操作", Redo);
        _undoButton.IsEnabled = false;
        _redoButton.IsEnabled = false;
        _groupButton = CommandButton("\uE92F", "组合", "把选中的多个图层编为一组（Ctrl+G），之后拖动任一组内图层即可整组移动", _canvas.GroupSelection);
        _ungroupButton = CommandButton("\uE931", "取消组合", "把选中图层从所在组中拆出（Ctrl+Shift+G）", _canvas.UngroupSelection);
        _groupButton.IsEnabled = false;
        _ungroupButton.IsEnabled = false;
        var addImageButton = CommandButton("\uE9B4", "添加图片图层", "选择一张图片作为新的底图图层", AddImageLayer);
        // 供教程 TargetSelector 定位（#EditorAddImage）。
        addImageButton.Name = "EditorAddImage";
        var exportVideoButton = CommandButton("\uF3E1", "导入视频编辑器",
            "把当前画布内容合成一张图片，导入视频编辑器作为底层片段（按视频舞台画幅排布）",
            ImportToVideoEditor);
        var saveButton = CommandButton("\uEEB5", "保存并应用", "保存图层并应用到主界面", Save);
        // 供教程 TargetSelector 定位（#EditorSave）。
        saveButton.Name = "EditorSave";
        _hslFilterButton = CommandButton("\uE51E", "色相 / 饱和度", "打开滤镜窗口：逐像素调整选中图片图层的色相、饱和度与明度（含滤镜预设）", OpenHslAdjustWindow);
        _brightnessFilterButton = CommandButton("\uE2BC", "亮度 / 对比度", "打开滤镜窗口：逐像素调整选中图片图层的亮度与对比度（含滤镜预设）", OpenBrightnessContrastWindow);
        _blurFilterButton = CommandButton("\uE20B", "高斯模糊", "打开滤镜窗口：调整选中图片图层的高斯模糊半径", OpenBlurAdjustWindow);
        var commandBar = new CommandBar
        {
            DefaultLabelPosition = CommandBarDefaultLabelPosition.Right,
            PrimaryCommands =
            {
                addImageButton,
                new CommandBarSeparator(),
                _undoButton,
                _redoButton,
                new CommandBarSeparator(),
                _groupButton,
                _ungroupButton,
                new CommandBarSeparator(),
                CommandButton("\uE62F", "重置主界面尺寸", "把主界面预览尺寸恢复为 ClassIsland 实际尺寸", ResetIslandSize),
                CommandButton("\uE92A", "棋盘格配色", "设置画布背景棋盘格：跟随主题自动按深浅色选择，或自定义两种颜色", OpenCheckerboardSettings),
                exportVideoButton,
                new CommandBarSeparator(),
                _hslFilterButton,
                _brightnessFilterButton,
                _blurFilterButton,
                saveButton
            }
        };
        // 入场动画目标；先置 Opacity=0 保证首帧不闪烁，Opened 后再淡入。
        _commandBarHost = commandBar;
        commandBar.Opacity = 0;

        // ---- 右侧：上 = 属性检查器（可滚动），下 = 图层面板（固定在底部），
        // 两区之间用水平手柄分割高度。----
        var layerListHost = new Grid { Children = { _layerStack, _reorderIndicator } };
        var inspector = BuildInspector();
        // 上：属性检查器面板（无边框，仅用背景区分）。
        var inspectorPanel = new Border
        {
            Background = ThemePalette.MicaPanelBackground(),
            Padding = new Thickness(12),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = inspector
            }
        };
        // 供教程 TargetSelector 定位（#EditorInspector）。
        inspectorPanel.Name = "EditorInspector";
        // 下：图层面板（固定在底部，无边框）：图层列表（可滚动）+ 底部固定操作按钮行。
        var layerActions = BuildLayerActions();
        var layerPanel = new Border
        {
            Background = ThemePalette.MicaPanelBackground(),
            Padding = new Thickness(12),
            MaxHeight = 340,
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                Children =
                {
                    new ScrollViewer
                    {
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = layerListHost
                    },
                    layerActions
                }
            }
        };
        // 供教程 TargetSelector 定位（#EditorLayerPanel）。
        layerPanel.Name = "EditorLayerPanel";
        // 入场动画目标；先置 Opacity=0 保证首帧不闪烁，Opened 后再上滑淡入。
        _layerPanelHost = layerPanel;
        layerPanel.Opacity = 0;
        Grid.SetRow(layerActions, 1);
        // 上下区之间的水平分割手柄（高度与横向间隙一致）。
        var rowSplitter = new GridSplitter
        {
            Height = 8,
            ResizeDirection = GridResizeDirection.Rows,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent
        };
        // 两个区域 + 中间手柄。
        var rightColumn = new Grid
        {
            RowDefinitions = new RowDefinitions("*,8,Auto"),
            Children = { inspectorPanel, rowSplitter, layerPanel }
        };
        Grid.SetRow(rowSplitter, 1);
        Grid.SetRow(layerPanel, 2);
        // 入场动画目标；先置 Opacity=0 保证首帧不闪烁，Opened 后再右滑淡入。
        _rightColumn = rightColumn;
        rightColumn.Opacity = 0;

        // 左侧工具栏（Photoshop 式）+ 舞台 + 右侧设置区之间加垂直分割手柄，可左右拖动调整宽度。
        var toolbar = new Border
        {
            Padding = new Thickness(6),
            VerticalAlignment = VerticalAlignment.Top,
            Background = ThemePalette.MicaPanelBackground(),
            Child = BuildToolBar()
        };
        // 供教程 TargetSelector 定位（#EditorToolBar）。
        toolbar.Name = "EditorToolBar";
        // 入场动画目标；先置 Opacity=0 保证首帧不闪烁，Opened 后再左滑淡入。
        _toolbarHost = toolbar;
        toolbar.Opacity = 0;
        var stageHost = new Border
        {
            ClipToBounds = true,
            Background = ThemePalette.MicaPanelBackground(),
            Child = _canvas
        };
        var columnSplitter = new GridSplitter
        {
            Width = 6,
            ResizeDirection = GridResizeDirection.Columns,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent
        };
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,330"),
            Children = { toolbar, stageHost, columnSplitter, rightColumn }
        };
        Grid.SetColumn(toolbar, 0);
        Grid.SetColumn(stageHost, 1);
        Grid.SetColumn(columnSplitter, 2);
        Grid.SetColumn(rightColumn, 3);
        // 统一左右间隙为 8px：左侧 = 工具栏右 margin；右侧 = 舞台右 margin(2) + 分割手柄(6)。
        toolbar.Margin = new Thickness(0, 0, 8, 0);
        stageHost.Margin = new Thickness(0, 0, 2, 0);

        _body = body;
        _contentGrid = new Grid
        {
            Margin = new Thickness(12, 4, 12, 12),
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 6,
            Children = { commandBar, body }
        };
        Grid.SetRow(body, 1);
        // 舞台与右侧栏共用同一个 Grid 行（天然等高）；这里再限制 body 高度不超过
        // 可视区，避免右侧栏内容过高把行撑出窗口导致两侧底部被裁、看起来高度不一。
        _contentGrid.SizeChanged += (_, _) => ConstrainBodyHeight();
        Content = _contentGrid;
        UpdateToolBarSelection();
        UpdateGroupButtons();
        UpdateLayerActionButtons();
    }

    /// <summary>把 body（舞台 + 右侧栏）高度限制在当前可视区内，保证两侧严格等高且都在窗口内。</summary>
    private void ConstrainBodyHeight()
    {
        if (_contentGrid == null || _body == null)
        {
            return;
        }

        var barRow = _contentGrid.RowDefinitions.Count > 0 ? _contentGrid.RowDefinitions[0] : null;
        var barHeight = barRow?.ActualHeight > 0 ? barRow.ActualHeight : 52;
        // RowSpacing(6) + Margin 上 4 下 12
        var available = _contentGrid.Bounds.Height - barHeight - 6 - 16;
        _body.MaxHeight = Math.Max(160, available);
    }

    // ============ 左侧工具栏（Photoshop 式）============

    // 图层面板操作按钮（BuildLayerActions / LayerIconButton / UpdateLayerActionButtons）
    // 已按职责归档到 WallpaperLayerEditor.Layers.cs。

    /// <summary>
    /// 在「配置目录\layers」下创建一个指定尺寸的全透明 PNG 位图文件（DPI 92 基准上按
    /// RenderScaling 放大，保证画笔等逐像素绘制在屏幕上 1:1 清晰）。返回文件路径；
    /// 创建失败（磁盘 / 权限等）返回 null。
    /// </summary>
    private static string? CreateBlankBitmapFile(int widthDip, int heightDip, TopLevel? topLevel)
    {
        // 位图按显示器缩放（DPI）创建，保证画出来的笔迹在屏幕上 1:1 清晰，
        // 不会因为「位图分辨率 = DIP 尺寸」而被系统放大变糊。
        var dpr = topLevel?.RenderScaling ?? 1.0;
        var w = (int)Math.Max(1, Math.Round(widthDip * dpr));
        var h = (int)Math.Max(1, Math.Round(heightDip * dpr));
        var id = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(InjectorRuntime.ConfigDirectory, "layers");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{id}.png");
        try
        {
            // WriteableBitmap 默认清零（全透明）。
            using var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96));
            using var fs = File.Create(path);
            bmp.Save(fs);
        }
        catch
        {
            return null;
        }

        return path;
    }

    private void AddBlankLayer()
    {
        var path = CreateBlankBitmapFile(
            (int)Math.Round(_canvas.IslandWidth), (int)Math.Round(_canvas.IslandHeight), TopLevel.GetTopLevel(this));
        if (path == null)
        {
            return;
        }

        var layer = new WallpaperLayerItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"空白图层 {_layers.Count + 1}",
            Kind = WallpaperLayerKind.Image,
            Source = WallpaperSource.LocalImage,
            Path = path,
            DisplayMode = WallpaperDisplayMode.Stretch,
            SizeMode = WallpaperLayerSizeMode.Custom,
            // 图层显示尺寸保持 DIP（与主界面一致），位图分辨率更高（dpr 倍），
            // 画笔按 BrushRadiusFor 换算后屏幕大小不变、笔迹更清晰。
            Width = _canvas.IslandWidth,
            Height = _canvas.IslandHeight,
            AnchorX = WallpaperLayerAnchorX.Center,
            AnchorY = WallpaperLayerAnchorY.Center
        };
        AddLayer(layer);
    }

    /// <summary>
    /// 新建「画布图层」：铺满整个编辑器画布（主界面 + 四周留白区域）的透明位图，
    /// 用户可以在眼睛所见的任意位置自由绘制（画笔不受主界面边界限制）。
    /// 画布图层不会出现在主界面上，需先「栅格化」裁出主界面区域转为图片图层。
    /// </summary>
    private void AddCanvasLayer()
    {
        var canvasW = _canvas.IslandWidth + WallpaperLayerCanvas.CanvasMargin * 2;
        var canvasH = _canvas.IslandHeight + WallpaperLayerCanvas.CanvasMargin * 2;
        var path = CreateBlankBitmapFile(
            (int)Math.Round(canvasW), (int)Math.Round(canvasH), TopLevel.GetTopLevel(this));
        if (path == null)
        {
            return;
        }

        var layer = new WallpaperLayerItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"画布 {_layers.Count + 1}",
            Kind = WallpaperLayerKind.Image,
            Source = WallpaperSource.LocalImage,
            Path = path,
            DisplayMode = WallpaperDisplayMode.Stretch,
            SizeMode = WallpaperLayerSizeMode.Custom,
            // 左上角锚点 + 负偏移：让位图左上角对齐画布（主界面左上角 - 四周留白）。
            AnchorX = WallpaperLayerAnchorX.Left,
            AnchorY = WallpaperLayerAnchorY.Top,
            OffsetX = -WallpaperLayerCanvas.CanvasMargin,
            OffsetY = -WallpaperLayerCanvas.CanvasMargin,
            Width = canvasW,
            Height = canvasH,
            IsCanvasLayer = true
        };
        AddLayer(layer);
    }

    /// <summary>
    /// 栅格化选中的形状 / 文本 / 画布图层：把矢量内容渲染成 PNG 位图，转为图片图层
    /// （保留尺寸 / 位置 / 旋转等变换，此后按位图处理，不能再编辑矢量）。
    /// 首次弹出警告（可勾选「不再提示」）。
    /// </summary>
    private async void RasterizeSelected()
    {
        // 画布图层（铺满整张画布）与矢量图层（形状 / 文本）都可栅格化。
        var canvasLayers = _canvas.SelectedLayers.Where(l => l.IsCanvasLayer).ToList();
        var vectorLayers = _canvas.SelectedLayers
            .Where(l => !l.IsCanvasLayer && l.Kind != WallpaperLayerKind.Image).ToList();
        if (canvasLayers.Count == 0 && vectorLayers.Count == 0)
        {
            ShowReminder("栅格化需要选中画布图层或形状 / 文本图层。");
            return;
        }

        if (!InjectorRuntime.Settings.RasterizeWarningDismissed)
        {
            var dismiss = new CheckBox { Content = "以后不再提示" };
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                return;
            }

            var hint = canvasLayers.Count > 0 && vectorLayers.Count == 0
                ? "画布图层栅格化后，会保留整张画布内容转为普通图片图层，此后可移动 / 缩放并显示在主界面上（主界面显示其中主界面区域部分）。"
                : "栅格化后将被渲染成位图，从此当作图片图层处理，不能再编辑矢量。";
            var dialog = new ContentDialog
            {
                Title = "栅格化图层",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { TextWrapping = TextWrapping.Wrap, Text = hint },
                        dismiss
                    }
                },
                PrimaryButtonText = "栅格化",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close
            };
            var result = await dialog.ShowAsync(topLevel);
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            if (dismiss.IsChecked == true)
            {
                var settings = InjectorRuntime.Settings;
                settings.BeginUpdate();
                settings.RasterizeWarningDismissed = true;
                settings.EndUpdate();
                InjectorRuntime.SaveAndApply();
            }
        }

        PushUndo();
        foreach (var layer in canvasLayers)
        {
            RasterizeCanvasLayer(layer);
        }

        foreach (var layer in vectorLayers)
        {
            RasterizeLayer(layer);
        }

        _document.MarkDirty();
        // 触发 RefreshImages + SyncImageControls，按新 Path 加载位图。
        _canvas.Layers = _layers;
        RefreshLayerList();
        RefreshInspector();
        UpdateStatus();
    }

    /// <summary>
    /// 把「画布图层」栅格化为普通图片图层：**保留整张画布内容（不裁剪主界面区域）**，
    /// 只解除「画布图层」标记。此后可正常移动 / 缩放并显示在主界面上（主界面显示其
    /// 主界面区域部分，四周留白在主界面外被裁掉）。
    /// </summary>
    private void RasterizeCanvasLayer(WallpaperLayerItem layer)
    {
        layer.IsCanvasLayer = false;
    }

    /// <summary>把单个矢量图层渲染成 PNG 并转为图片图层；失败时保持原图层不变。</summary>
    private void RasterizeLayer(WallpaperLayerItem layer)
    {
        var w = layer.Width > 0 ? layer.Width : _canvas.IslandWidth;
        var h = layer.Height > 0 ? layer.Height : _canvas.IslandHeight;
        if (w < 1 || h < 1)
        {
            return;
        }

        try
        {
            var visual = new WallpaperLayerVisual { Layer = layer, Width = w, Height = h };
            visual.Measure(new Size(w, h));
            visual.Arrange(new Rect(0, 0, w, h));
            using var rtb = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(w), (int)Math.Ceiling(h)));
            rtb.Render(visual);
            var dir = Path.Combine(InjectorRuntime.ConfigDirectory, "rasterized");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{layer.Id}.png");
            using (var fs = File.Create(path))
            {
                rtb.Save(fs);
            }

            layer.Kind = WallpaperLayerKind.Image;
            layer.Source = WallpaperSource.LocalImage;
            layer.Path = path;
            layer.DisplayMode = WallpaperDisplayMode.Stretch;
            layer.SmtcMode = WallpaperLayerSmtcMode.AsImage;
        }
        catch
        {
            // 栅格化失败（渲染 / 写文件异常）时保持原矢量图层不变。
        }
    }

    /// <summary>打开单例窗口：已打开则聚焦现有实例，否则创建并显示。</summary>
    private static void OpenOrActivate<TWindow>(TWindow? current, Func<TWindow> create) where TWindow : Window
    {
        if (current != null)
        {
            current.Activate();
            return;
        }

        create().Show();
    }

    /// <summary>打开背景效果窗口（单例；已打开则聚焦）。</summary>
    private void OpenBackgroundEffects()
    {
        OpenOrActivate(BackgroundEffectsWindow.Current, () => new BackgroundEffectsWindow());
    }

    // ============ 图层滤镜窗口（色相/饱和度、亮度/对比度、高斯模糊）============

    /// <summary>当前选中图层中第一个图片图层（滤镜窗口读取 / 同步用；无则 null）。</summary>
    internal WallpaperLayerItem? FirstSelectedImageLayer =>
        _canvas.SelectedLayers.FirstOrDefault(l => l.Kind == WallpaperLayerKind.Image);

    /// <summary>当前选中的全部图片图层。</summary>
    internal IEnumerable<WallpaperLayerItem> SelectedImageLayers =>
        _canvas.SelectedLayers.Where(l => l.Kind == WallpaperLayerKind.Image);

    /// <summary>滤镜窗口预览改动前压一次撤销（整次会话只压一次，由窗口跟踪）。</summary>
    internal void PushLayerFilterUndo() => PushUndo();

    /// <summary>把滤镜预览应用到选中的图片图层并刷新画布（标记脏，等待确定才最终提交）。
    /// 逐像素计算已委托画布异步后台执行（<see cref="WallpaperLayerCanvas.RequestFilterPreview"/>），
    /// 滑块拖动不再阻塞 UI 线程；本方法只同步更新参数、登记预览图层并做轻量布局刷新。</summary>
    internal void ApplyLayerFilter(Action apply)
    {
        apply();
        _document.MarkDirty();
        // 登记预览图层并启动后台重算（取消上一轮），Refresh 期间 DisplayBitmap 沿用上一结果。
        _canvas.RequestFilterPreview(SelectedImageLayers.ToList());
        _canvas.Refresh();
        RefreshInspector();
        UpdateStatus();
    }

    /// <summary>滤镜「确定」后：标记脏并刷新（值已由窗口写入图层）。
    /// 若图层有像素选区，则把滤镜烘焙进选区像素（而不是整层）。</summary>
    internal void CommitLayerFilter()
    {
        if (_canvas.HasSelection)
        {
            _canvas.BakeAdjustmentsToSelection();
        }

        _canvas.CancelFilterPreview();
        _document.MarkDirty();
        _canvas.Refresh();
        RefreshInspector();
        UpdateStatus();
    }

    /// <summary>滤镜「取消 / 关闭恢复快照」后：只刷新画布，不标记脏。</summary>
    internal void RefreshAfterLayerFilter()
    {
        _canvas.CancelFilterPreview();
        _canvas.Refresh();
        RefreshInspector();
        UpdateStatus();
    }

    /// <summary>同步所有已打开的滤镜窗口（选中变化时调用）。</summary>
    private void SyncFilterWindows()
    {
        HslAdjustWindow.Current?.SyncFromEditor();
        BrightnessContrastWindow.Current?.SyncFromEditor();
        BlurAdjustWindow.Current?.SyncFromEditor();
    }

    /// <summary>打开色相 / 饱和度窗口（单例；已打开则聚焦）。</summary>
    private void OpenHslAdjustWindow()
    {
        OpenOrActivate(HslAdjustWindow.Current, () => new HslAdjustWindow(this));
    }

    /// <summary>打开亮度 / 对比度窗口（单例；已打开则聚焦）。</summary>
    private void OpenBrightnessContrastWindow()
    {
        OpenOrActivate(BrightnessContrastWindow.Current, () => new BrightnessContrastWindow(this));
    }

    /// <summary>打开高斯模糊窗口（单例；已打开则聚焦）。</summary>
    private void OpenBlurAdjustWindow()
    {
        OpenOrActivate(BlurAdjustWindow.Current, () => new BlurAdjustWindow(this));
    }

    /// <summary>窗口级快捷键：撤销 / 重做与 PS 式滤镜快捷键（画布未处理的键冒泡到这里）。</summary>
    private void EditorWindowOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Z when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                // Ctrl+Shift+Z：重做（Photoshop 同款）。
                Redo();
                e.Handled = true;
                break;
            case Key.Z:
                // Ctrl+Z：撤销。
                Undo();
                e.Handled = true;
                break;
            case Key.Y:
                // Ctrl+Y：重做。
                Redo();
                e.Handled = true;
                break;
            case Key.U:
                // Ctrl+U：色相 / 饱和度（Photoshop 同款）。
                OpenHslAdjustWindow();
                e.Handled = true;
                break;
            case Key.M:
                // Ctrl+M：亮度 / 对比度（Photoshop 的曲线 / 色调调整的实用替代）。
                OpenBrightnessContrastWindow();
                e.Handled = true;
                break;
        }
    }

    /// <summary>吸管最终取色：更新状态栏、把颜色设为默认色并自动记忆，同步刷新检查器。</summary>
    private void OnColorPicked(Color color)
    {
        _statusText.Text = $"已取色 RGB({color.R}, {color.G}, {color.B})  {color.ToString()}";
        RememberActiveColor(color);
        // 吸管不自动跳回其它工具，检查器「画笔」组实时展示取到的颜色。
        RefreshInspector();
    }

    /// <summary>吸管悬停预览：状态栏实时汇报 RGB。</summary>
    private void OnColorPreview(Color color)
    {
        _statusText.Text = $"RGB {color.R}, {color.G}, {color.B}  {color.ToString()}";
    }

    /// <summary>
    /// 把用户最后使用的颜色设为默认色并自动记忆（新建形状 / 文本 / 画笔的默认色）。
    /// 直接写设置属性即可：Changed 事件会自动触发保存应用。
    /// </summary>
    private void RememberActiveColor(Color color)
    {
        _canvas.ActiveColor = color;
        InjectorRuntime.Settings.EditorPickedColor = color.ToString();
    }

    /// <summary>
    /// 把当前画布内容导入视频编辑器：合成主界面区域的 PNG 快照（图层相对位置
    /// 按视频舞台画幅等比排布——底图与视频工程的画幅都是主界面比例），
    /// 作为图片覆盖层加入时间轴底层（时长跟随工程）。
    /// </summary>
    private void ImportToVideoEditor()
    {
        // 快照目录放配置目录（部署/清理不影响源文件）。
        var dir = Path.Combine(InjectorRuntime.ConfigDirectory, "video-import");
        var path = Path.Combine(dir, $"wallpaper-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        var exported = _canvas.ExportIslandSnapshot(path);
        if (exported == null)
        {
            ShowReminder("导出画布快照失败，请重试");
            return;
        }

        if (VideoEditorWindow.Current is { } existing)
        {
            existing.Activate();
            existing.ImportImageAsClip(exported);
            _statusText.Text = "已导入到视频编辑器。";
            return;
        }

        var window = new VideoEditorWindow();
        window.Show();
        window.ImportImageAsClip(exported);
        _statusText.Text = "已打开视频编辑器并导入画布快照。";
    }

    /// <summary>
    /// 打开「画布棋盘格配色」对话框：跟随主题自动按深浅色选择，或自定义两种颜色；
    /// 改动实时写回设置并刷新画布。
    /// </summary>
    private async void OpenCheckerboardSettings()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        var settings = InjectorRuntime.Settings;
        var followTheme = new ToggleSwitch
        {
            OnContent = "开",
            OffContent = "关",
            IsChecked = settings.WallpaperCheckerFollowTheme
        };
        var color1 = ColorPicker();
        color1.Color = ReadColor(settings.WallpaperCheckerColor1, Color.FromRgb(45, 47, 52));
        var color2 = ColorPicker();
        color2.Color = ReadColor(settings.WallpaperCheckerColor2, Color.FromRgb(38, 40, 45));
        void SyncColors() => color1.IsEnabled = color2.IsEnabled = followTheme.IsChecked != true;
        followTheme.PropertyChanged += (_, _) => SyncColors();
        SyncColors();

        // 改动实时写回设置并刷新画布棋盘格（EndUpdate 会触发 Changed → 保存应用）。
        void Apply()
        {
            settings.BeginUpdate();
            settings.WallpaperCheckerFollowTheme = followTheme.IsChecked == true;
            settings.WallpaperCheckerColor1 = color1.Color.ToString();
            settings.WallpaperCheckerColor2 = color2.Color.ToString();
            settings.EndUpdate();
            _canvas.ApplyCheckerboardColors();
        }

        followTheme.PropertyChanged += (_, _) => Apply();
        color1.PropertyChanged += (_, e) => { if (e.Property?.Name == "Color") Apply(); };
        color2.PropertyChanged += (_, e) => { if (e.Property?.Name == "Color") Apply(); };

        var panel = new StackPanel
        {
            Spacing = 4,
            Width = 360,
            Children =
            {
                SettingsRow("跟随主题", followTheme),
                SettingsRow("棋盘格颜色 1", color1),
                SettingsRow("棋盘格颜色 2", color2)
            }
        };
        var dialog = new ContentDialog
        {
            Title = "画布棋盘格配色",
            Content = panel,
            PrimaryButtonText = "完成",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        await dialog.ShowAsync(topLevel);
    }

    /// <summary>构建左侧纵向工具栏：移动 / 选择 / 缩放 / 形状 / 文本。</summary>
    private Control BuildToolBar()
    {
        var panel = new StackPanel { Spacing = 2 };
        _toolPanel = panel;
        panel.Children.Add(ToolButton(WallpaperEditorTool.Move, "\uE113", "移动工具（V）：拖拽图层移动；有像素选区时拖动选区内容"));
        panel.Children.Add(ToolButton(WallpaperEditorTool.Hand, "\uE941", "抓手工具（H）：按住拖动平移画布，查看画布任意区域"));
        panel.Children.Add(ToolButton(WallpaperEditorTool.Select, "\uE5BF", "选择工具（S）：点击只选中图层，不拖拽"));
        panel.Children.Add(ToolButton(WallpaperEditorTool.RectSelect, "\uEF07", "矩形选框工具（M）：在当前图层上拖拽框选像素区域（Delete 删除选区像素；移动工具拖动选区内容；检查器可从选区新建图层）"));
        panel.Children.Add(ToolButton(WallpaperEditorTool.Lasso, "\uEA2B", "套索工具（L）：在当前图层上自由圈选像素区域"));
        panel.Children.Add(ToolButton(WallpaperEditorTool.Zoom, "\uF4D1", "缩放工具（Z）：单击放大 / Alt+单击缩小 / 拖拽框选放大；Ctrl + / Ctrl - 也可缩放"));
        panel.Children.Add(ToolButton(WallpaperEditorTool.Crop, "\uE59B", "裁剪工具（C）：在图片图层上拖拽框选要保留的区域，松手即裁剪（裁剪后切回移动工具）"));
        panel.Children.Add(ToolButton(WallpaperEditorTool.Eyedropper, "\uE81D", "吸管工具（I）：拾取屏幕上任意位置的颜色，按住拖拽可在窗口外取色；取到的颜色会成为新建形状 / 文本 / 画笔的默认色并自动记忆"));
        panel.Children.Add(ToolButton(WallpaperEditorTool.Brush, "\uEC4A", "画笔工具（B）：在图片图层上按住拖动绘制（右侧可调颜色 / 大小）"));
        panel.Children.Add(ToolButton(WallpaperEditorTool.Eraser, "\uE7FF", "橡皮擦工具（E）：擦除图片图层的像素（变为透明）"));
        panel.Children.Add(ToolButton(WallpaperEditorTool.Shape, "\uE775", "形状工具（U）：拖拽绘制矩形；创建后可在右侧修改形状类型"));
        panel.Children.Add(ToolButton(WallpaperEditorTool.Text, "\uF1BE", "文本工具（T）：点击插入文本框图层"));
        panel.Children.Add(new Separator { Margin = new Thickness(2, 5) });
        panel.Children.Add(ToolActionButton("\uEBCA", "添加 SMTC 图层", "把当前播放的专辑封面作为新的底图图层（无播放时显示占位封面）", AddSmtcLayer));
        panel.Children.Add(ToolActionButton("\uE7DC", "添加贴纸", "在线获取 Project Sekai 角色贴纸，插入为新的底图图层", OpenStickerPicker));
        // 逻辑运算按钮：对选中的多个矢量形状做布尔运算（点击弹出原生菜单，菜单项带图标）。
        Button booleanButton = null!;
        booleanButton = ToolActionButton("\uE92F", "逻辑运算",
            "对选中的多个矢量形状做布尔运算：结合（并集 A ∪ B）/ 组合（排除重叠 A ⊕ B）/ 拆分 / 相交（A ∩ B）/ 减除（A − B）",
            () => _booleanMenu.ShowAt(booleanButton));
        _booleanMenu = BuildBooleanMenu();
        panel.Children.Add(booleanButton);

        // 选中高亮滑块：独立圆角块，切换工具时滑动到新位置（位于按钮面板之下，按钮透明露出）。
        _toolHighlight = new Border
        {
            IsHitTestVisible = false,
            // 圆角与按钮自带 hover 样式对齐（Fluent ControlCornerRadius = 4）。
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(ThemePalette.AccentColorWithAlpha(190)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Opacity = 0
        };
        // 宿主 Grid：高亮块在前（底层），按钮面板在后（顶层，按钮透明底露出高亮）。
        var host = new Grid { Children = { _toolHighlight, panel } };
        _toolHighlightHost = host;
        // 首次布局完成后定位高亮（此前 Bounds 未测量）。
        host.SizeChanged += (_, _) =>
        {
            if (!_toolHighlightPositioned)
            {
                SlideToolHighlight(false);
            }
        };
        return host;
    }

    private Button ToolButton(WallpaperEditorTool tool, string glyph, string tip)
    {
        var button = new Button
        {
            Content = new IconText { Glyph = glyph, Text = string.Empty },
            Padding = new Thickness(9, 7),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        // 供教程 TargetSelector 定位形状工具按钮（#EditorShapeTool）。
        if (tool == WallpaperEditorTool.Shape)
        {
            button.Name = "EditorShapeTool";
        }
        // 供教程 TargetSelector 定位文本工具按钮（#EditorTextTool）。
        if (tool == WallpaperEditorTool.Text)
        {
            button.Name = "EditorTextTool";
        }

        // 选中态过渡：背景 / 前景切换时平滑渐变（非线性）。
        button.Transitions = new Transitions
        {
            new BrushTransition { Property = Avalonia.Controls.Button.BackgroundProperty, Duration = EditorAnimations.TapDuration },
            new BrushTransition { Property = Avalonia.Controls.Button.ForegroundProperty, Duration = EditorAnimations.TapDuration }
        };
        // 按压缩放反馈（Fluent 风格）。
        EditorAnimations.AddPressFeedback(button);
        // 初始不可见，打开窗口时逐个弹性进场。
        button.Opacity = 0;
        // 悬停：已选中的工具保持透明（主题色高亮块表达选中），其它工具显示半透明悬停底色。
        button.PointerEntered += (_, _) =>
        {
            _toolHovered[tool] = true;
            UpdateToolBarSelection();
        };
        button.PointerExited += (_, _) =>
        {
            _toolHovered[tool] = false;
            UpdateToolBarSelection();
        };

        ToolTip.SetTip(button, tip);
        button.Click += (_, _) =>
        {
            // MenuFlyout 在宿主的 FluentAvalonia 2.4.1 中会在 PointerExited 时抛空引用，
            // 因此此处不再弹出菜单。先直接绘制矩形，创建后可在检查器切换其它形状。
            _canvas.Tool = tool;
        };
        _toolButtons[tool] = button;
        return button;
    }

    /// <summary>工具栏中的一次性命令按钮，不改变当前编辑工具。</summary>
    private static Button ToolActionButton(string glyph, string label, string tip, Action action)
    {
        var button = new Button
        {
            Content = new IconText { Glyph = glyph, Text = string.Empty },
            Padding = new Thickness(9, 7),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(ThemePalette.ForegroundColor())
        };
        // 按压缩放反馈；初始不可见，打开窗口时逐个弹性进场。
        EditorAnimations.AddPressFeedback(button);
        button.Opacity = 0;
        ToolTip.SetTip(button, $"{label}：{tip}");
        button.Click += (_, _) => action();
        return button;
    }



    /// <summary>右上角 Toast 展示一条操作提醒（自动消失）；同一提醒 5 秒内不重复（防刷屏）。
    /// 每次新建一个 Toast 窗口：复用窗口在隐藏后重显示时高度会坍缩。</summary>
    private void ShowReminder(string message)
    {
        var now = DateTime.Now;
        if (message == _lastReminder && (now - _lastReminderAt).TotalSeconds < 5)
        {
            return;
        }

        _lastReminder = message;
        _lastReminderAt = now;
        new ReminderToastWindow().ShowFor(this, message);
    }

    /// <summary>构建逻辑运算菜单（FAMenuFlyout + 带图标的 MenuFlyoutItem，5 种布尔运算）。
    /// 用 FAUI 自带的原生菜单控件，替换早期手写 Popup 面板（外观原生、菜单项带图标）。</summary>
    private FAMenuFlyout BuildBooleanMenu()
    {
        var menu = new FAMenuFlyout();
        AddBooleanOpItem(menu, "\uEF35", "结合（并集 A ∪ B）", WallpaperBooleanOp.Union);
        AddBooleanOpItem(menu, "\uEF2D", "组合（排除重叠 A ⊕ B）", WallpaperBooleanOp.Exclude);
        AddBooleanOpItem(menu, "\uE5C9", "拆分（结合后拆成独立块）", WallpaperBooleanOp.Split);
        AddBooleanOpItem(menu, "\uEF2F", "相交（交集 A ∩ B）", WallpaperBooleanOp.Intersect);
        AddBooleanOpItem(menu, "\uEF33", "减除（差集 A − B）", WallpaperBooleanOp.Subtract);
        return menu;
    }

    private void AddBooleanOpItem(FAMenuFlyout menu, string glyph, string label, WallpaperBooleanOp op)
    {
        var item = new MenuFlyoutItem
        {
            IconSource = new FluentIconSource(glyph),
            Text = label
        };
        item.Click += (_, _) => _canvas.ApplyBooleanOp(op);
        menu.Items.Add(item);
    }

    /// <summary>按当前工具刷新工具栏按钮状态：选中态由高亮滑块表达（按钮透明露底），
    /// 已选中工具悬停保持主题色（高亮块），其它工具悬停显示半透明底色。</summary>
    private void UpdateToolBarSelection()
    {
        var activeTool = _canvas.Tool;
        if (_toolHighlight != null)
        {
            _toolHighlight.Background = new SolidColorBrush(ThemePalette.AccentColorWithAlpha(190));
        }

        foreach (var (tool, button) in _toolButtons)
        {
            var active = tool == activeTool;
            // 选中态背景交给高亮滑块：按钮本身 = 已选中 ? 透明 : (悬停 ? 半透明 : 透明)。
            button.Background = active
                ? Brushes.Transparent
                : _toolHovered.GetValueOrDefault(tool)
                    ? ThemePalette.SubtleFill()
                    : Brushes.Transparent;
            button.Foreground = active
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(ThemePalette.ForegroundColor());
            // 选中工具显示实心图标，未选中显示空心（regular）图标。
            if (button.Content is IconText icon && ToolGlyphs.TryGetValue(tool, out var glyphs))
            {
                icon.Glyph = active ? glyphs.Filled : glyphs.Regular;
            }
        }
    }

    /// <summary>
    /// 把选中高亮滑块移动到当前工具的按钮位置：首次直接放置，之后用弹性缓动滑动。
    /// </summary>
    private void SlideToolHighlight(bool animate)
    {
        if (_toolHighlight == null || !_toolButtons.TryGetValue(_canvas.Tool, out var button))
        {
            return;
        }

        var pos = button.TranslatePoint(new Point(0, 0), _toolHighlightHost);
        if (pos == null || button.Bounds.Height <= 0)
        {
            return; // 尚未布局，稍后由 SizeChanged / 入场动画补齐。
        }

        var targetY = pos.Value.Y;
        if (_toolHighlight.RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform();
            _toolHighlight.RenderTransform = translate;
        }

        // 首次测量时把高度对齐到按钮（与自带 hover 样式同高、同宽：无内缩边距）。
        // 注意：Border.Height 默认是 NaN（Auto），比较必须用 IsNaN。
        if (double.IsNaN(_toolHighlight.Height) || _toolHighlight.Height <= 0)
        {
            _toolHighlight.Height = Math.Max(20, button.Bounds.Height);
        }

        if (animate && _toolHighlightPositioned)
        {
            // 平滑非线性移动（CubicEaseOut），不弹跳。
            EditorAnimations.AnimateValue(v => translate.Y = v, translate.Y, targetY,
                EditorAnimations.InDuration, EditorAnimations.Interaction);
        }
        else
        {
            translate.Y = targetY;
        }

        _toolHighlightPositioned = true;
    }

    /// <summary>左侧工具栏各工具的实心/空心图标码点（FluentSystemIcons，filled/regular 成对）。</summary>
    private static readonly Dictionary<WallpaperEditorTool, (string Filled, string Regular)> ToolGlyphs = new()
    {
        [WallpaperEditorTool.Move] = ("\uE112", "\uE113"),
        [WallpaperEditorTool.Hand] = ("\uE940", "\uE941"),
        [WallpaperEditorTool.Select] = ("\uE5BE", "\uE5BF"),
        [WallpaperEditorTool.RectSelect] = ("\uEF06", "\uEF07"),
        [WallpaperEditorTool.Lasso] = ("\uEA2A", "\uEA2B"),
        [WallpaperEditorTool.Zoom] = ("\uF4D0", "\uF4D1"),
        [WallpaperEditorTool.Shape] = ("\uE774", "\uE775"),
        [WallpaperEditorTool.Text] = ("\uF1BD", "\uF1BE"),
        [WallpaperEditorTool.Crop] = ("\uE59A", "\uE59B"),
        [WallpaperEditorTool.Brush] = ("\uEC49", "\uEC4A"),
        [WallpaperEditorTool.Eraser] = ("\uE7FE", "\uE7FF"),
        [WallpaperEditorTool.Eyedropper] = ("\uE81C", "\uE81D")
    };

    // 右侧检查器的全部职责（BuildInspector / BuildInspectorTabStrip / ActivateInspectorPage /
    // SetPageVisibility / RefreshInspectorSegments / RefreshInspector / RefreshCustomSizePanel /
    // GroupSubtitle / SettingsRow / 显示名映射与颜色解析）已按职责拆分到
    // WallpaperLayerEditor.Inspector.cs。

    private void ResetLayerTransform()
    {
        ApplyToSelected(l =>
        {
            l.SizeMode = WallpaperLayerSizeMode.FillIsland;
            l.Rotation = 0;
            l.Opacity = 1;
            l.AnchorX = WallpaperLayerAnchorX.Center;
            l.AnchorY = WallpaperLayerAnchorY.Center;
            l.OffsetX = 0;
            l.OffsetY = 0;
            l.Width = 0;
            l.Height = 0;
        });
        UpdateStatus();
    }

    /// <summary>对全部选中图层应用修改：压入撤销、置脏、刷新画布与检查器。</summary>
    private void ApplyToSelected(Action<WallpaperLayerItem> edit)
    {
        var layers = _canvas.SelectedLayers;
        if (layers.Count == 0 || _updatingInspector)
        {
            return;
        }

        PushUndo();
        foreach (var layer in layers)
        {
            edit(layer);
        }

        _document.MarkDirty();
        _canvas.Refresh();
        RefreshInspector();
        UpdateStatus();
    }

    // ============ 撤销 / 重做 / 保存 ============

    private void PushUndo()
    {
        _document.Push();
    }

    private void Undo()
    {
        if (_document.Undo())
        {
            _layers = _document.Layers;
        }
    }

    private void Redo()
    {
        if (_document.Redo())
        {
            _layers = _document.Layers;
        }
    }

    private async void Save()
    {
        // 有未栅格化的画布图层时提醒（可勾选「永不弹出」）；用户可取消保存。
        if (!InjectorRuntime.Settings.CanvasRasterizeWarningDismissed)
        {
            var unRasterized = _layers.Count(l => l.IsCanvasLayer);
            if (unRasterized > 0)
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel != null)
                {
                    var dismiss = new CheckBox { Content = "以后不再提醒" };
                    var dialog = new ContentDialog
                    {
                        Title = "存在未栅格化的画布图层",
                        Content = new StackPanel
                        {
                            Spacing = 8,
                            Children =
                            {
                                new TextBlock
                                {
                                    TextWrapping = TextWrapping.Wrap,
                                    Text = $"还有 {unRasterized} 个画布图层未栅格化。画布图层不会显示在主界面上，请先栅格化或删除，否则主界面上看不到它们。"
                                },
                                dismiss
                            }
                        },
                        PrimaryButtonText = "知道了",
                        CloseButtonText = "取消保存",
                        DefaultButton = ContentDialogButton.Primary
                    };
                    var result = await dialog.ShowAsync(topLevel);
                    if (result != ContentDialogResult.Primary)
                    {
                        return;
                    }

                    if (dismiss.IsChecked == true)
                    {
                        var s = InjectorRuntime.Settings;
                        s.BeginUpdate();
                        s.CanvasRasterizeWarningDismissed = true;
                        s.EndUpdate();
                        InjectorRuntime.SaveAndApply();
                    }
                }
            }
        }

        var settings = InjectorRuntime.Settings;
        settings.BeginUpdate();
        settings.WallpaperDesignerEnabled = true;
        settings.WallpaperEnabled = true;
        settings.WallpaperZOrder = _canvas.ZOrder;
        settings.WallpaperLayers = _layers.Select(l => l.Clone()).ToList();
        settings.EndUpdate();
        InjectorRuntime.SaveAndApply();
        _document.MarkSaved();
        _statusText.Text = $"已保存并应用：共 {_layers.Count} 个图片图层 · 层级「{DisplayZOrder(_canvas.ZOrder)}」。";
        UpdateUndoRedoState();
        // 向前推动教程的「保存」等待句。
        TutorialServicePush("save");
    }

    /// <summary>按撤销/重做栈同步命令栏按钮状态。</summary>
    private void UpdateUndoRedoState()
    {
        if (_undoButton == null)
        {
            return;
        }

        _undoButton.IsEnabled = _document.CanUndo;
        _redoButton.IsEnabled = _document.CanRedo;
    }

    /// <summary>按当前选中状态同步「组合 / 取消组合」按钮：单个或无选中 → 都禁用；
    /// 多选且未同组 → 仅「组合」；多选且同组 → 仅「取消组合」。</summary>
    private void UpdateGroupButtons()
    {
        var selected = _canvas.SelectedLayers.Where(l => !_canvas.IsLocked(l.Id)).ToList();
        var sameGroup = selected.Count >= 2 &&
                        !string.IsNullOrEmpty(selected[0].GroupId) &&
                        selected.All(l => l.GroupId == selected[0].GroupId);
        _groupButton.IsEnabled = selected.Count >= 2 && !sameGroup;
        _ungroupButton.IsEnabled = sameGroup;
    }

    // ============ 添加图层 / 主界面重置 ============

    private async void AddImageLayer()
    {
        // 仅在教学引导（教程正停在「加背景图 / 加第二张图」句）时询问图片来源（从文件 / 示例图）；
        // 平时与以前一样，直接打开文件选择器。
        var tag = HostTutorial.GetCurrentSentenceTag();
        if (tag is "add-image" or "add-image-2")
        {
            await AskImageSourceAsync(tag);
            return;
        }

        await PickImageFromFileAsync(TopLevel.GetTopLevel(this));
    }

    /// <summary>教学句对应的内置示例图：加背景图 → 默认背景图；加第二张图 → Airi 贴纸。</summary>
    private string TutorialSamplePath(string tag) => tag == "add-image-2"
        ? Path.Combine(InjectorRuntime.PluginDirectory, "Assets", "Stickers", "Airi_01.png")
        : Path.Combine(InjectorRuntime.PluginDirectory, "Assets", "editorbackground.jpg");

    /// <summary>教学引导中：让用户选择图片来源（从文件选择 / 使用示例图片 / 取消）。
    /// tag=add-image → 背景图（铺满主界面）；tag=add-image-2 → 第二张图（自定义尺寸，可缩放/右对齐）。</summary>
    private async Task AskImageSourceAsync(string tag)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        var isSecond = tag == "add-image-2";
        // 让用户明确选择图片来源：从文件选择，或使用内置示例图片（取消则不添加）。
        var dialog = new ContentDialog
        {
            Title = isSecond ? "添加第二张图片" : "添加背景图片",
            Content = isSecond
                ? "再插入一张图片：从文件选择，或使用内置示例图（Airi 的贴纸）。加好后再把它缩放并靠右对齐。"
                : "先添加一张背景图：从文件选择，或使用内置示例图（插入后会自动铺满整个主界面）。",
            PrimaryButtonText = "从文件选择",
            SecondaryButtonText = "使用示例图片",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await dialog.ShowAsync(topLevel);
        switch (result)
        {
            case ContentDialogResult.Primary:
                await PickImageFromFileAsync(topLevel, p => AddTutorialImage(p, tag));
                break;
            case ContentDialogResult.Secondary:
                AddTutorialImage(TutorialSamplePath(tag), tag);
                break;
            // None（取消）：不添加。
        }
    }

    /// <summary>教程插入图片的公共入口：add-image = 背景（铺满，推 add-image 句）；
    /// add-image-2 = 第二张自定义尺寸图（推 add-image-2 句）。</summary>
    private void AddTutorialImage(string path, string tag)
    {
        if (tag == "add-image-2")
        {
            AddSecondImageTutorialLayer(path);
        }
        else
        {
            AddLayerFromPath(path);
        }
    }

    /// <summary>第二张教学图片：自定义尺寸（高 ≈ 主界面 0.55，宽按图片比例）、垂直水平居中，供用户缩放后右对齐。</summary>
    private void AddSecondImageTutorialLayer(string path)
    {
        var layer = AddLayer(new WallpaperLayerItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"底图图层 {_layers.Count + 1}",
            Source = WallpaperSource.LocalImage,
            Path = path,
            SizeMode = WallpaperLayerSizeMode.Custom,
            DisplayMode = WallpaperDisplayMode.Fit,
            AnchorX = WallpaperLayerAnchorX.Center,
            AnchorY = WallpaperLayerAnchorY.Center
        });
        // 按图片宽高比设定初始尺寸（高 = 主界面 0.55，宽按比例），并居中放置。
        if (_canvas.GetThumbnail(layer.Id) is { } bitmap && bitmap.PixelSize.Height > 0)
        {
            var aspect = bitmap.PixelSize.Width / (double)bitmap.PixelSize.Height;
            var h = _canvas.IslandHeight * 0.55;
            layer.Width = Math.Max(1, h * aspect);
            layer.Height = h;
            _canvas.Refresh();
        }

        // 记录缩放基线，供「缩放」等待句判断尺寸真的变化。
        RecordTutorialScaleBaseline(layer);
        // 向前推动教程的「加第二张图」等待句。
        TutorialServicePush("add-image-2");
    }

    /// <summary>打开系统文件选择器挑选底图图片；取消则不添加。</summary>
    private async Task PickImageFromFileAsync(TopLevel? topLevel, Action<string>? onPick = null)
    {
        if (topLevel?.StorageProvider is not { } provider)
        {
            return;
        }

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择底图图片",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("图片") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"] },
                FilePickerFileTypes.All
            ]
        });
        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (onPick != null)
        {
            onPick(path);
        }
        else
        {
            AddLayerFromPath(path);
        }
    }

    /// <summary>新建图层的公共骨架：压撤销、加入列表、选中新图层。
    /// 画布刷新 / 图层面板 / 检查器 / 状态栏由 <see cref="_document"/> 的 Changed 事件统一处理。</summary>
    private WallpaperLayerItem AddLayer(WallpaperLayerItem layer)
    {
        _document.AddLayer(layer);
        _canvas.Select(layer.Id);
        return layer;
    }

    /// <summary>按本地路径创建一张图片图层并选中（添加成功后会推进教程的 add-image 句）。</summary>
    private void AddLayerFromPath(string path)
    {
        AddLayer(new WallpaperLayerItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"底图图层 {_layers.Count + 1}",
            Source = WallpaperSource.LocalImage,
            Path = path,
            SizeMode = WallpaperLayerSizeMode.FillIsland,
            DisplayMode = WallpaperDisplayMode.Fill
        });
        // 向前推动教程的「添加图片」等待句。
        TutorialServicePush("add-image");
    }

    /// <summary>添加一个 SMTC 专辑封面图层（无播放时画布显示占位封面 album.jpg）。</summary>
    private void AddSmtcLayer()
    {
        AddLayer(new WallpaperLayerItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"SMTC 封面图层 {_layers.Count + 1}",
            Source = WallpaperSource.SmtcAlbum,
            SmtcMode = WallpaperLayerSmtcMode.AsImage,
            SizeMode = WallpaperLayerSizeMode.FillIsland,
            DisplayMode = WallpaperDisplayMode.Fill
        });
    }

    /// <summary>打开在线贴纸选择窗口（单例；已打开则聚焦）。</summary>
    private void OpenStickerPicker()
    {
        OpenOrActivate(StickerPickerWindow.Current, () => new StickerPickerWindow(AddStickerLayer));
    }

    /// <summary>把下载到本地缓存的贴纸插入为新的图片图层（按贴纸比例自动设定初始尺寸）。</summary>
    private void AddStickerLayer(string path, string name)
    {
        var layer = AddLayer(new WallpaperLayerItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Source = WallpaperSource.LocalImage,
            Path = path,
            SizeMode = WallpaperLayerSizeMode.Custom,
            DisplayMode = WallpaperDisplayMode.Fit,
            AnchorX = WallpaperLayerAnchorX.Center,
            AnchorY = WallpaperLayerAnchorY.Center
        });
        // 按贴纸宽高比设定初始尺寸（高 = 主界面 0.8，宽按比例），并居中放置。
        if (_canvas.GetThumbnail(layer.Id) is { } bitmap && bitmap.PixelSize.Height > 0)
        {
            var aspect = bitmap.PixelSize.Width / (double)bitmap.PixelSize.Height;
            var h = _canvas.IslandHeight * 0.8;
            layer.Width = Math.Max(1, h * aspect);
            layer.Height = h;
            _canvas.Refresh();
        }
    }

    private void ResetIslandSize()
    {
        var size = InjectorRuntime.GetCurrentIslandSize();
        _canvas.SetIslandSize(size?.Width > 0 ? size.Value.Width : DefaultIslandWidth,
            size?.Height > 0 ? size.Value.Height : DefaultIslandHeight);
        _statusText.Text = "已把主界面尺寸重置为 ClassIsland 实际尺寸。";
    }

    // ============ 图层面板 ============
    // 图层面板操作方法（RefreshLayerList / 拖拽排序 / 显隐 / 锁定 / 删除）
    // 及 LayerRowControl 已按职责归档到 WallpaperLayerEditor.Layers.cs 

    // 检查器刷新（RefreshCustomSizePanel / IsSmtcDefaultMode / RefreshInspector / ReadColor /
    // InspectorColor / RelativeHintText / OffsetText）已按职责拆分到 WallpaperLayerEditor.Inspector.cs。

    private void UpdateStatus()
    {
        var islandPart = $"主界面 {_canvas.IslandWidth:0} × {_canvas.IslandHeight:0}";
        var unlockPart = _canvas.IslandUnlocked
            ? "· 主界面已解锁：拖动右/下边缘可模拟 ClassIsland 长度变化，观察底图自适应"
            : "· 在右侧图层面板解锁主界面后可拖动边缘测试自适应";
        var selected = _canvas.SelectedLayers;
        var selectedPart = selected.Count switch
        {
            0 => string.Empty,
            1 => $"· 已选「{selected[0].Name}」",
            _ => $"· 已选 {selected.Count} 个图层"
        };
        _statusText.Text = $"{islandPart} {selectedPart} {unlockPart}";
    }

    // ============ 关闭确认 ============

    private async void OnClosingConfirm(object? sender, WindowClosingEventArgs e)
    {
        if (!_document.IsDirty)
        {
            return;
        }

        e.Cancel = true;
        var dialog = new ContentDialog
        {
            Title = "保存更改？",
            Content = "底图图层编辑器中有尚未保存的更改。",
            PrimaryButtonText = "保存",
            SecondaryButtonText = "不保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        // 显式传入本窗口的 TopLevel：多窗口/窗口分离时无参重载可能找不到根而崩溃。
        var topLevel = TopLevel.GetTopLevel(this);
        var result = topLevel != null
            ? await dialog.ShowAsync(topLevel)
            : ContentDialogResult.None;
        if (result == ContentDialogResult.Primary)
        {
            Save();
            _document.MarkSaved();
            Close();
        }
        else if (result == ContentDialogResult.Secondary)
        {
            _document.MarkSaved();
            Close();
        }
    }

    // ============ 小工具 ============

    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>图标 + 文字按钮（如「＋ 添加图片图层」）。</summary>
    private static CommandBarButton CommandButton(string glyph, string label, string tooltip, Action action)
    {
        var button = new CommandBarButton
        {
            IconSource = new FluentIconSource(glyph),
            Label = label
        };
        // 按压缩放反馈（与 FAUI 自带的按压态叠加，更灵动）。
        EditorAnimations.AddPressFeedback(button);
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }

    // 检查器显隐同步、显示名映射、颜色解析与选项枚举已拆分到 WallpaperLayerEditor.Inspector.cs；
    // 图层面板行控件 LayerRowControl 与图层面板操作（刷新 / 拖拽排序 / 显隐 / 锁定 / 删除 / 操作按钮）
    // 已拆分到 WallpaperLayerEditor.Layers.cs。
}
