using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ClassIsland.Core.Controls;

namespace ClassIslandInjector.Views;

/// <summary>
/// 底图图层编辑器的「图层面板」职责分区（与 <see cref="WallpaperLayerEditorWindow"/> 主体同属一个
/// partial 类）。集中承载图层面板行控件的渲染单元 <see cref="LayerRowControl"/>，
/// 主文件聚焦窗口骨架、画布交互与图层重排等主体逻辑。
/// </summary>
internal sealed partial class WallpaperLayerEditorWindow
{
    /// <summary>图层面板的一行：缩略图/图标 + 标题/副标题 + 眼睛/锁/删除 + 拖拽手柄（主界面行带解锁）。</summary>
    private sealed class LayerRowControl : Border
    {
        private Action<bool>? _select;
        private Action? _visibilityAction;
        private Action? _lockAction;
        private Action? _deleteAction;

        public string? LayerId { get; init; }
        public bool IsIsland { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Subtitle { get; init; } = string.Empty;
        public string IconGlyph { get; init; } = string.Empty;
        public bool Visible { get; init; } = true;
        public bool Locked { get; init; }
        public bool Unlocked { get; init; }
        public bool Selected { get; init; }
        public Bitmap? Thumbnail { get; init; }

        /// <summary>拖拽手柄按下（用于图层列表排序）。</summary>
        public event Action<PointerPressedEventArgs>? DragHandlePressed;
        /// <summary>内容区双击（用于唤出背景效果窗口）。</summary>
        public event Action? DoubleTapRequested;

        private DateTime _lastPressUtc = DateTime.MinValue;
        private double _lastPressX;
        private double _lastPressY;
        /// <summary>当前是否悬停（用于新增闪光结束后的底色正确回落）。</summary>
        private bool _hovered;

        public LayerRowControl WithHandlers(Action<bool>? select, Action? visibility, Action? lockAction, Action? delete)
        {
            _select = select;
            _visibilityAction = visibility;
            _lockAction = lockAction;
            _deleteAction = delete;
            Build();
            return this;
        }

        private void Build()
        {
            CornerRadius = new CornerRadius(6);
            // 无边框（参考 ClassIsland 组件库卡片的框子样式），选中仅用底色高亮。
            BorderThickness = new Thickness(0);
            Padding = new Thickness(8, 6);
            // 背景色平滑过渡：悬停微高亮 / 选中强调色 / 新增闪光都渐变（非线性）。
            Transitions = new Transitions
            {
                new BrushTransition { Property = BackgroundProperty, Duration = EditorAnimations.TapDuration }
            };

            // 原生风格：透明底 + 悬停微高亮 + 选中强调色（跟随主题，不手搓深色卡片）。
            ApplyBackground(false);
            PointerEntered += (_, _) => { _hovered = true; ApplyBackground(true); };
            PointerExited += (_, _) => { _hovered = false; ApplyBackground(false); };

            Control preview;
            if (Thumbnail != null)
            {
                preview = new Image
                {
                    Source = Thumbnail,
                    Width = 40,
                    Height = 26,
                    Stretch = Stretch.UniformToFill,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            else
            {
                preview = new Border
                {
                    Width = 40,
                    Height = 26,
                    CornerRadius = new CornerRadius(4),
                    Background = ThemePalette.SubtleFill(),
                    Child = new IconText
                    {
                        Glyph = IconGlyph,
                        Text = string.Empty,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Opacity = 0.55
                    }
                };
            }

            // 标签用 Grid（而非 StackPanel）：StackPanel 会给子项无限宽度，TextBlock 无法省略；
            // Grid 单列会让 TextBlock 在可用宽度内自动以「…」截断长名称/副标题。
            var titleBlock = new TextBlock
            {
                Text = Title,
                FontWeight = FontWeight.SemiBold,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            var subtitleBlock = new TextBlock
            {
                Text = Subtitle,
                FontSize = 11,
                Opacity = 0.6,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            var label = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center,
                RowDefinitions = new RowDefinitions("Auto,Auto"),
                Children = { titleBlock, subtitleBlock }
            };
            Grid.SetRow(subtitleBlock, 1);
            // 内容区用 Grid（而非水平 StackPanel）：StackPanel 会给子项无限宽度，
            // 导致 label 里的 TextBlock 永不截断；Grid 的 * 列会把 label 限制在可用宽度内。
            var contentArea = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 8,
                Cursor = new Cursor(StandardCursorType.Hand),
                Children = { preview, label }
            };
            Grid.SetColumn(label, 1);
            contentArea.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(contentArea).Properties.IsLeftButtonPressed)
                {
                    var now = DateTime.UtcNow;
                    var pos = e.GetPosition(this);
                    // 500ms 内、位移小于 8px 的连续两次左键点击 = 双击。
                    if ((now - _lastPressUtc).TotalMilliseconds < 500 &&
                        Math.Abs(pos.X - _lastPressX) < 8 && Math.Abs(pos.Y - _lastPressY) < 8)
                    {
                        DoubleTapRequested?.Invoke();
                    }

                    _lastPressUtc = now;
                    _lastPressX = pos.X;
                    _lastPressY = pos.Y;
                    var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
                    _select?.Invoke(ctrl);
                }
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                ColumnSpacing = 4,
                Children = { contentArea }
            };
            Grid.SetColumn(contentArea, 1);

            if (IsIsland)
            {
                // 主界面行：左侧拖拽手柄调整背景层级（顶部 = 底色之后，底部 = 底色之上、组件之下）；
                // 右侧为「解锁主界面」按钮（眼睛不可用，主界面始终可见）。
                var dragHandle = new Border
                {
                    Width = 18,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new IconText
                    {
                        // 与 ClassIsland 组件库 TouchDragThumb（compact）同款拖拽手柄图标。
                        Glyph = "\uEE49",
                        Text = string.Empty,
                        FontSize = 18,
                        Opacity = 0.8,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                };
                ToolTip.SetTip(dragHandle, "拖动调整背景层级：放到列表顶部 → 底色之后；放到列表底部 → 底色之上、组件之下");
                dragHandle.PointerPressed += (_, e) =>
                {
                    if (e.GetCurrentPoint(dragHandle).Properties.IsLeftButtonPressed)
                    {
                        DragHandlePressed?.Invoke(e);
                        e.Handled = true;
                    }
                };
                var lockButton = IconButton(Unlocked ? "\uEAF8" : "\uEAF0",
                    Unlocked ? "锁定主界面" : "解锁主界面（可拖动边缘测试自适应）", _lockAction);
                grid.ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto");
                Grid.SetColumn(dragHandle, 0);
                Grid.SetColumn(lockButton, 3);
                grid.Children.Add(dragHandle);
                grid.Children.Add(lockButton);
            }
            else
            {
                var dragHandle = new Border
                {
                    Width = 18,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new IconText
                    {
                        // 与 ClassIsland 组件库 TouchDragThumb（compact）同款拖拽手柄图标。
                        Glyph = "\uEE49",
                        Text = string.Empty,
                        FontSize = 18,
                        Opacity = 0.8,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                };
                ToolTip.SetTip(dragHandle, "拖动调整图层顺序：列表越靠上，显示越靠前");
                dragHandle.PointerPressed += (_, e) =>
                {
                    if (e.GetCurrentPoint(dragHandle).Properties.IsLeftButtonPressed)
                    {
                        DragHandlePressed?.Invoke(e);
                        e.Handled = true;
                    }
                };
                var eye = IconButton(Visible ? "\uE813" : "\uE817", Visible ? "隐藏图层" : "显示图层", _visibilityAction);
                var lockButton = IconButton(Locked ? "\uEAF0" : "\uEAF8", Locked ? "解锁图层" : "锁定图层", _lockAction);
                var delete = IconButton("\uE61D", "删除图层", _deleteAction);
                Grid.SetColumn(dragHandle, 0);
                Grid.SetColumn(eye, 2);
                Grid.SetColumn(lockButton, 3);
                Grid.SetColumn(delete, 4);
                grid.ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto");
                grid.Children.Add(dragHandle);
                grid.Children.Add(eye);
                grid.Children.Add(lockButton);
                grid.Children.Add(delete);
            }

            Child = grid;
        }

        /// <summary>应用底色（透明 / 悬停微高亮 / 选中强调色，跟随主题）。</summary>
        private void ApplyBackground(bool hover)
        {
            Background = Selected
                ? new SolidColorBrush(ThemePalette.AccentColorWithAlpha(70))
                : hover
                    ? ThemePalette.SubtleFill()
                    : Brushes.Transparent;
            BorderBrush = Brushes.Transparent;
        }

        /// <summary>
        /// 新增图层的入场闪光：强调色底色短暂高亮后平滑回落到当前状态（悬停 / 选中）。
        /// 背景渐变由 Transitions 自动完成，不会与悬停 / 选中底色冲突。
        /// </summary>
        public void PlayAddedFlash()
        {
            Background = new SolidColorBrush(ThemePalette.AccentColorWithAlpha(64));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                ApplyBackground(_hovered);
            };
            timer.Start();
        }

        private static Button IconButton(string glyph, string tooltip, Action? action)
        {
            var button = new Button
            {
                Content = new IconText { Glyph = glyph, Text = string.Empty },
                Padding = new Thickness(7, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            EditorAnimations.AddPressFeedback(button);
            ToolTip.SetTip(button, tooltip);
            button.IsEnabled = action != null;
            if (action != null)
            {
                button.Click += (_, _) => action();
            }

            return button;
        }
    }

    // ============ 图层面板 ============

    /// <summary>当前正在拖拽排序的图层（非空表示正在拖拽中）。</summary>
    private WallpaperLayerItem? _reorderLayer;
    /// <summary>当前正在拖拽的背景（主界面）行（与 _reorderLayer 互斥）。</summary>
    private bool _reorderBackground;
    /// <summary>背景行拖拽的目标层级。</summary>
    private WallpaperLayerZOrder _reorderBackgroundTarget;
    /// <summary>拖拽排序：源图层在 _layers 中的索引。</summary>
    private int _reorderSourceIndex;
    /// <summary>拖拽排序：目标插入索引。</summary>
    private int _reorderInsertIndex;
    /// <summary>拖拽排序：插入位置指示线（舞台右上角图层面板内，跟随主题强调色）。</summary>
    private readonly Border _reorderIndicator = new()
    {
        Height = 3,
        CornerRadius = new CornerRadius(1.5),
        Background = new SolidColorBrush(ThemePalette.AccentColor()),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Top,
        IsVisible = false,
        IsHitTestVisible = false,
        ZIndex = 10
    };

    private void RefreshLayerList()
    {
        // 识别本次刷新「新增」的图层（用于弹跳 + 闪光入场；正常刷新不会重播）。
        var addedIds = new HashSet<string>(_layers.Select(l => l.Id));
        addedIds.ExceptWith(_knownLayerIds);
        _knownLayerIds.Clear();
        _knownLayerIds.UnionWith(_layers.Select(l => l.Id));

        _layerStack.Children.Clear();

        var islandRow = new LayerRowControl
        {
            IsIsland = true,
            Title = "ClassIsland 主界面",
            Subtitle = "背景图层",
            IconGlyph = "\uE62F",
            Unlocked = _canvas.IslandUnlocked,
            Selected = _canvas.SelectedLayer == null
        }.WithHandlers(
            _ => _canvas.Select(null),
            null,
            () => ToggleIslandUnlock(),
            null);
        // 背景行可拖拽调整层级：顶部 = 底色之后，底部 = 底色之上、组件之下。
        islandRow.DragHandlePressed += e => BeginBackgroundReorder(islandRow, e);
        // 双击背景行 = 打开背景效果窗口（与 Photoshop 双击图层唤出效果一致）。
        islandRow.DoubleTapRequested += OpenBackgroundEffects;

        // 背景行位置跟随层级：底色之后 → 列表顶部（最前）；其余 → 列表底部（最后）。
        var islandAtTop = _canvas.ZOrder == WallpaperLayerZOrder.BehindBackground;
        if (islandAtTop)
        {
            _layerStack.Children.Add(islandRow);
        }

        for (var i = _layers.Count - 1; i >= 0; i--)
        {
            var layer = _layers[i];
            var captured = layer;
            var row = new LayerRowControl
            {
                LayerId = layer.Id,
                IsIsland = false,
                Title = layer.Name,
                Subtitle = (string.IsNullOrEmpty(layer.GroupId) ? string.Empty : "组 · ")
                    + (layer.Kind == WallpaperLayerKind.Image
                        ? $"{DisplayKind(layer)}{SmtcModeSuffix(layer)} · {DisplayModeName(layer.DisplayMode)}"
                        : $"{DisplayKind(layer)}{SmtcModeSuffix(layer)}"),
                IconGlyph = layer.Kind switch
                {
                    WallpaperLayerKind.Shape => "\uE774",
                    WallpaperLayerKind.Text => "\uF1BD",
                    _ => layer.Source == WallpaperSource.SmtcAlbum ? "\uE021" : "\uE9B2"
                },
                Visible = layer.Visible,
                Locked = _canvas.IsLocked(layer.Id),
                Selected = _canvas.SelectedLayers.Contains(layer),
                Thumbnail = _canvas.GetThumbnail(layer.Id)
            };
            // Ctrl + 点击 = 多选（切换选中）；普通点击 = 单选（若属于组则选中整组）。
            row.WithHandlers(
                ctrl =>
                {
                    if (ctrl)
                    {
                        _canvas.SelectWithToggle(captured.Id);
                    }
                    else
                    {
                        _canvas.SelectWithGroup(captured.Id);
                    }
                },
                () => ToggleLayerVisibility(captured),
                () => ToggleLayerLock(captured),
                () => DeleteLayer(captured));
            row.DragHandlePressed += e => BeginLayerReorder(captured, e);
            _layerStack.Children.Add(row);
        }

        if (!islandAtTop)
        {
            _layerStack.Children.Add(islandRow);
        }

        // 首次构建（窗口尚未打开）：把图层行收集起来，供打开时逐行滑入。
        // 之后刷新：仅对「新增」的图层行做弹跳 + 强调色闪光入场。
        if (_entrancePlayed)
        {
            foreach (var row in _layerStack.Children.OfType<LayerRowControl>())
            {
                if (row.LayerId != null && addedIds.Contains(row.LayerId))
                {
                    row.Opacity = 0;
                    EditorAnimations.PopIn(row, 24, 0, 0.92, EditorAnimations.InDuration, EditorAnimations.Entrance);
                    row.PlayAddedFlash();
                }
            }
        }
        else
        {
            _pendingLayerRows.Clear();
            foreach (var row in _layerStack.Children.OfType<LayerRowControl>())
            {
                row.Opacity = 0;
                _pendingLayerRows.Add(row);
            }
        }
    }

    // ============ 拖拽排序（图层列表）============
    // 平滑拖拽：拖拽期间不重建列表，只显示「插入指示线」+ 源行半透明，释放时一次性重排。

    /// <summary>开始拖拽排序：记录图层、弹出置顶幽灵预览窗口、捕获指针。</summary>
    private void BeginLayerReorder(WallpaperLayerItem layer, PointerPressedEventArgs e)
    {
        if (_reorderLayer != null || _reorderBackground)
        {
            return;
        }

        _reorderLayer = layer;
        _reorderSourceIndex = _layers.IndexOf(layer);
        _reorderInsertIndex = _reorderSourceIndex;
        PushUndo();
        _layerStack.PointerMoved += LayerReorderPointerMoved;
        _layerStack.PointerReleased += LayerReorderPointerReleased;
        _layerStack.PointerCaptureLost += LayerReorderOnCaptureLost;

        // 幽灵预览：独立置顶窗口跟随鼠标（参考「主界面 → 组件」的拖拽预览），
        // 完全不参与本窗口布局，避免破坏右侧面板。先显示预览再捕获指针，
        // 避免 Show 新窗口导致指针捕获被取消。
        foreach (var child in _layerStack.Children)
        {
            if (child is LayerRowControl row && row.LayerId == layer.Id)
            {
                var childOrigin = child.TranslatePoint(new Point(0, 0), this) ?? default;
                var grabScreen = this.PointToScreen(childOrigin);
                var pointerScreen = this.PointToScreen(e.GetPosition(this));
                _reorderGrabOffset = new Point(
                    pointerScreen.X - grabScreen.X,
                    pointerScreen.Y - grabScreen.Y);
                ShowDragPreview(child);
                break;
            }
        }

        UpdateReorderIndicator();
        UpdateDragPreview(e);
        e.Pointer.Capture(_layerStack);
        e.Handled = true;
    }

    /// <summary>
    /// 开始拖拽背景（主界面）行：放到列表顶部 = 底色之后，放到列表底部 = 底色之上、组件之下。
    /// 层级由背景行在图层面板中的上下位置决定，释放时才生效。
    /// </summary>
    private void BeginBackgroundReorder(LayerRowControl row, PointerPressedEventArgs e)
    {
        if (_reorderLayer != null || _reorderBackground)
        {
            return;
        }

        _reorderBackground = true;
        _reorderBackgroundTarget = _canvas.ZOrder;
        _layerStack.PointerMoved += LayerReorderPointerMoved;
        _layerStack.PointerReleased += LayerReorderPointerReleased;
        _layerStack.PointerCaptureLost += LayerReorderOnCaptureLost;

        // 幽灵预览：截取背景行外观跟随鼠标（参考图层行的拖拽预览）。
        var childOrigin = row.TranslatePoint(new Point(0, 0), this) ?? default;
        var grabScreen = this.PointToScreen(childOrigin);
        var pointerScreen = this.PointToScreen(e.GetPosition(this));
        _reorderGrabOffset = new Point(
            pointerScreen.X - grabScreen.X,
            pointerScreen.Y - grabScreen.Y);
        ShowDragPreview(row);
        UpdateReorderIndicator();
        UpdateDragPreview(e);
        e.Pointer.Capture(_layerStack);
        e.Handled = true;
    }

    private void LayerReorderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_reorderBackground)
        {
            var target = ComputeBackgroundDropOrder(e.GetPosition(_layerStack));
            if (target != _reorderBackgroundTarget)
            {
                _reorderBackgroundTarget = target;
                UpdateReorderIndicator();
            }

            UpdateDragPreview(e);
            return;
        }

        if (_reorderLayer == null)
        {
            return;
        }

        var insert = ComputeReorderInsertIndex(e.GetPosition(_layerStack));
        if (insert != _reorderInsertIndex)
        {
            _reorderInsertIndex = insert;
            UpdateReorderIndicator();
        }

        UpdateDragPreview(e);
    }

    private void LayerReorderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndLayerReorder(e);
    }

    private void LayerReorderOnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndLayerReorder(null);
    }

    /// <summary>创建（如需要）并显示拖拽幽灵预览窗口，截取源行外观。</summary>
    private void ShowDragPreview(Control sourceRow)
    {
        if (_dragPreviewWindow == null || _dragPreviewHost == null)
        {
            var host = new Border
            {
                CornerRadius = new CornerRadius(6),
                Opacity = 0.65,
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 12, Color = Color.FromArgb(120, 0, 0, 0) })
            };
            _dragPreviewHost = host;
            _dragPreviewWindow = new Window
            {
                SystemDecorations = SystemDecorations.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                CanResize = false,
                Topmost = true,
                TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                Background = Brushes.Transparent,
                Content = host
            };
        }

        _dragPreviewHost!.Width = sourceRow.Bounds.Width > 0 ? sourceRow.Bounds.Width : 240;
        _dragPreviewHost.Height = sourceRow.Bounds.Height > 0 ? sourceRow.Bounds.Height : 40;
        _dragPreviewHost.Background = new VisualBrush(sourceRow);
        _dragPreviewWindow.Width = _dragPreviewHost.Width;
        _dragPreviewWindow.Height = _dragPreviewHost.Height;
        _dragPreviewWindow.Show();
    }

    /// <summary>把幽灵预览窗口移动到指针位置（屏幕坐标，保留抓取偏移）。</summary>
    private void UpdateDragPreview(PointerEventArgs e)
    {
        if (_dragPreviewWindow is not { IsVisible: true })
        {
            return;
        }

        var screen = this.PointToScreen(e.GetPosition(this));
        _dragPreviewWindow.Position = new PixelPoint(
            (int)(screen.X - _reorderGrabOffset.X),
            (int)(screen.Y - _reorderGrabOffset.Y));
    }

    private void EndLayerReorder(PointerEventArgs? e)
    {
        if (_reorderBackground)
        {
            var target = _reorderBackgroundTarget;
            _reorderBackground = false;
            _layerStack.PointerMoved -= LayerReorderPointerMoved;
            _layerStack.PointerReleased -= LayerReorderPointerReleased;
            _layerStack.PointerCaptureLost -= LayerReorderOnCaptureLost;
            if (e != null)
            {
                e.Pointer.Capture(null);
            }

            _dragPreviewWindow?.Hide();
            _reorderIndicator.IsVisible = false;
            if (target != _canvas.ZOrder)
            {
                // 背景层级拖拽也会改变状态：补压撤销，与图层拖拽排序保持一致。
                PushUndo();
                _canvas.ZOrder = target;
                _document.MarkDirty();
                RefreshLayerList();
            }

            UpdateStatus();
            return;
        }

        if (_reorderLayer == null)
        {
            return;
        }

        var layer = _reorderLayer;
        var sourceIndex = _reorderSourceIndex;
        var insertIndex = _reorderInsertIndex;
        _reorderLayer = null;
        _layerStack.PointerMoved -= LayerReorderPointerMoved;
        _layerStack.PointerReleased -= LayerReorderPointerReleased;
        _layerStack.PointerCaptureLost -= LayerReorderOnCaptureLost;
        if (e != null)
        {
            e.Pointer.Capture(null);
        }

        _dragPreviewWindow?.Hide();
        _reorderIndicator.IsVisible = false;
        if (insertIndex != sourceIndex)
        {
            _document.MarkDirty();
            _layers.RemoveAt(sourceIndex);
            var adjusted = insertIndex > sourceIndex ? insertIndex - 1 : insertIndex;
            _layers.Insert(Math.Clamp(adjusted, 0, _layers.Count), layer);
        }

        RefreshLayerList();
        _canvas.Refresh();
        UpdateStatus();
    }

    /// <summary>
    /// 计算指针位置对应的插入索引（_layers 中的位置，0 = 最底，Count = 最前）。
    /// 图层面板自上而下 = 从前到后：指针在列表上方 → 更靠前，下方 → 更靠后。
    /// 背景行固定在一端（顶部 = 底色之后 / 底部 = 底色之上），图层不能越过它。
    /// </summary>
    private int ComputeReorderInsertIndex(Point pos)
    {
        foreach (var child in _layerStack.Children)
        {
            if (child is not LayerRowControl row)
            {
                continue;
            }

            var b = child.Bounds;
            if (pos.Y >= b.Y && pos.Y <= b.Y + b.Height)
            {
                if (row.IsIsland)
                {
                    // 背景行：顶部（底色之后）→ 图层只能落在其下（最前）；底部 → 只能落在其上（最底）。
                    return _canvas.ZOrder == WallpaperLayerZOrder.BehindBackground ? _layers.Count : 0;
                }

                var index = _layers.FindIndex(l => l.Id == row.LayerId);
                // 上半 → 落在此行上方（更前）；下半 → 落在此行下方（更后）。
                return pos.Y < b.Y + b.Height / 2 ? index : Math.Max(0, index - 1);
            }

            if (pos.Y < b.Y)
            {
                // 指针在整列上方 → 最前。
                return _layers.Count;
            }
        }

        // 指针在整列下方 → 最底。
        return 0;
    }

    /// <summary>背景行拖拽：按指针在面板中的上下位置决定目标层级（上 = 底色之后，下 = 底色之上、组件之下）。</summary>
    private WallpaperLayerZOrder ComputeBackgroundDropOrder(Point pos)
    {
        var height = _layerStack.Bounds.Height > 0 ? _layerStack.Bounds.Height : 200;
        return pos.Y < height / 2
            ? WallpaperLayerZOrder.BehindBackground
            : WallpaperLayerZOrder.AboveBackground;
    }

    /// <summary>插入索引对应的指示线 Y 坐标（图层面板坐标系）。</summary>
    private double GetReorderIndicatorY(int insertIndex)
    {
        var rows = _layerStack.Children.OfType<LayerRowControl>().Where(r => r.LayerId != null).ToArray();
        if (rows.Length == 0)
        {
            return 0;
        }

        if (insertIndex >= _layers.Count)
        {
            return rows[0].Bounds.Y; // 顶部之上
        }

        if (insertIndex <= 0)
        {
            return rows[^1].Bounds.Bottom; // 底部之下
        }

        var target = rows.FirstOrDefault(r => r.LayerId == _layers[insertIndex].Id);
        return target?.Bounds.Y ?? rows[0].Bounds.Y;
    }

    private void UpdateReorderIndicator()
    {
        if (_reorderBackground)
        {
            // 背景行：顶部 = 底色之后，底部 = 底色之上、组件之下。
            var atTop = _reorderBackgroundTarget == WallpaperLayerZOrder.BehindBackground;
            _reorderIndicator.Margin = new Thickness(0,
                atTop ? 0 : Math.Max(0, _layerStack.Bounds.Height - 3), 0, 0);
            _reorderIndicator.IsVisible = true;
            return;
        }

        if (_reorderLayer == null)
        {
            _reorderIndicator.IsVisible = false;
            return;
        }

        _reorderIndicator.Margin = new Thickness(0, Math.Max(0, GetReorderIndicatorY(_reorderInsertIndex) - 1.5), 0, 0);
        _reorderIndicator.IsVisible = true;
    }

    private void ToggleIslandUnlock()
    {
        var newState = !_canvas.IslandUnlocked;
        _canvas.IslandUnlocked = newState;
        RefreshLayerList();
        UpdateStatus();
        // 教程推进：区分「正常解锁」与「已解锁用户先锁回去再解锁」两种分支，
        // 保证已解锁时教程不会卡在后续的拖动步骤上。
        var tag = HostTutorial.GetCurrentSentenceTag();
        if (newState)
        {
            // 点击后处于解锁状态：解锁完成，推进对应等待句（unlock 或 unlock-reset）。
            if (tag is "unlock" or "unlock-reset")
            {
                TutorialServicePush(tag);
            }
        }
        else if (tag == "unlock")
        {
            // 点击后处于锁定状态：用户把原本已解锁的主界面锁回去了。
            // 跳到「吃惊 + 重新解锁」分支句，让用户再解锁一次。
            HostTutorial.PushToNextSentence();
        }
    }

    private void ToggleLayerVisibility(WallpaperLayerItem layer)
    {
        PushUndo();
        layer.Visible = !layer.Visible;
        _document.MarkDirty();
        _canvas.Refresh();
        RefreshLayerList();
    }

    private void ToggleLayerLock(WallpaperLayerItem layer)
    {
        _canvas.ToggleLock(layer.Id);
        RefreshLayerList();
    }

    private void DeleteLayer(WallpaperLayerItem layer)
    {
        if (_canvas.IsLocked(layer.Id))
        {
            return;
        }

        // 多选时删除整个选中集（跳过锁定）；仅单个触发时只删该图层。
        var toDelete = _canvas.SelectedLayers.Count > 0 && _canvas.SelectedLayers.Contains(layer)
            ? _canvas.SelectedLayers.Where(l => !_canvas.IsLocked(l.Id)).ToList()
            : [layer];
        PushUndo();

        // 画布元素移除动画（淡出 + 缩小）。
        foreach (var l in toDelete)
        {
            _canvas.AnimateLayerOut(l.Id);
        }

        // 图层面板行移除动画（淡出 + 上滑），动画结束后再真正删除。
        var rows = _layerStack.Children.OfType<LayerRowControl>()
            .Where(r => r.LayerId != null && toDelete.Any(l => l.Id == r.LayerId)).ToList();
        foreach (var row in rows)
        {
            EditorAnimations.FadeIn(row, 1, 0, EditorAnimations.InDuration, EditorAnimations.Interaction);
            EditorAnimations.SlideOut(row, 0, -12, EditorAnimations.InDuration, EditorAnimations.Interaction);
        }

        if (rows.Count > 0)
        {
            EditorAnimations.After(TimeSpan.FromMilliseconds(240), () => FinishDelete(toDelete));
        }
        else
        {
            FinishDelete(toDelete);
        }
    }

    /// <summary>实际执行删除：从列表移除、刷新画布与检查器（在移除动画之后调用）。</summary>
    private void FinishDelete(List<WallpaperLayerItem> toDelete)
    {
        foreach (var l in toDelete)
        {
            _layers.Remove(l);
        }

        _document.MarkDirty();
        _canvas.Layers = _layers;
        _canvas.Select(null);
        RefreshLayerList();
        RefreshInspector();
        UpdateStatus();
    }


    /// <summary>图层面板底部的操作按钮行（纯图标 + 提示，固定在面板底边）。</summary>
    private Control BuildLayerActions()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _newBlankLayerButton = LayerIconButton("\uE010", "新建空白图层（透明，与主界面同尺寸，可用画笔 / 橡皮擦绘制）", AddBlankLayer);
        _newLayerButton = LayerIconButton("\uE20C", "新建画布图层：铺满整个画布（主界面 + 四周留白），可在眼睛所见的任意位置自由绘制；要放到主界面上需先栅格化为图片", AddCanvasLayer);
        _duplicateButton = LayerIconButton("\uE58B", "复制图层（Ctrl+J）", () => _canvas.DuplicateSelection());
        _deleteButton = LayerIconButton("\uE61D", "删除图层", () =>
        {
            var layer = _canvas.SelectedLayer;
            if (layer != null)
            {
                DeleteLayer(layer);
            }
        });
        _effectButton = LayerIconButton("\uF42F", "效果选项（只作用于 ClassIsland 背景，需先点击背景行）", OpenBackgroundEffects);
        _rasterizeButton = LayerIconButton("\uE928", "栅格化图层（Ctrl+Shift+R）：画布图层 → 裁出主界面区域转为图片；形状 / 文本 → 渲染成位图后当作图片图层处理（不可再编辑矢量）", RasterizeSelected);
        panel.Children.Add(_newBlankLayerButton);
        panel.Children.Add(_newLayerButton);
        panel.Children.Add(_duplicateButton);
        panel.Children.Add(_deleteButton);
        panel.Children.Add(_rasterizeButton);
        panel.Children.Add(_effectButton);
        return panel;
    }

    /// <summary>图层面板操作按钮（纯图标，提示文字放 ToolTip）。</summary>
    private static Button LayerIconButton(string glyph, string tooltip, Action action)
    {
        var button = new Button
        {
            Content = new IconText { Glyph = glyph, Text = string.Empty },
            Padding = new Thickness(8, 5)
        };
        EditorAnimations.AddPressFeedback(button);
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>按当前选中状态同步图层面板操作按钮：复制/删除仅选中图层可用；栅格化仅选中形状/文本图层可用；效果仅背景（无选中）可用。命令栏滤镜按钮仅选中图片图层时可用。</summary>
    private void UpdateLayerActionButtons()
    {
        var hasSelection = _canvas.SelectedLayer != null;
        var hasVector = _canvas.SelectedLayers.Any(l => l.Kind != WallpaperLayerKind.Image);
        var hasCanvas = _canvas.SelectedLayers.Any(l => l.IsCanvasLayer);
        var hasImageLayer = _canvas.SelectedLayers.Any(l => l.Kind == WallpaperLayerKind.Image);
        _duplicateButton.IsEnabled = hasSelection;
        _deleteButton.IsEnabled = hasSelection;
        _rasterizeButton.IsEnabled = hasVector || hasCanvas;
        _effectButton.IsEnabled = !hasSelection;
        _hslFilterButton.IsEnabled = hasImageLayer;
        _brightnessFilterButton.IsEnabled = hasImageLayer;
        _blurFilterButton.IsEnabled = hasImageLayer;
    }
}