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
}