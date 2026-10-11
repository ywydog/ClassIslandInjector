using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Threading;

namespace ClassIslandInjector.Controls;

/// <summary>
/// 设置内容懒加载宿主：把重型设置页内容的构建推迟到控件首次挂载后。
/// 用 <see cref="ContentFactory"/> 提供真正的内容构建逻辑（仅在首次呈现时执行一次），
/// 期间先显示加载指示器，内容就绪后渐隐加载层、渐入内容层，避免把重型构建阻塞在
/// 设置导航的同一个 UI 帧里。与 ClassIsland / SystemTools 的设置页懒加载体验一致。
/// </summary>
public sealed class SettingsPageLazy : Grid
{
    /// <summary>内容构建工厂；首次懒加载时只调用一次。</summary>
    public Func<Control?>? ContentFactory { get; set; }

    private readonly ContentPresenter _content;
    private readonly Border _loader;
    private bool _started;
    private DispatcherTimer? _fade;

    /// <summary>单次渐入/渐隐时长（毫秒）。</summary>
    private const int FadeMs = 200;

    public SettingsPageLazy()
    {
        _content = new ContentPresenter
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Opacity = 0
        };

        _loader = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Avalonia.Media.Brushes.Transparent,
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 10,
                Children =
                {
                    new ProgressBar
                    {
                        IsIndeterminate = true,
                        Width = 140,
                        Height = 4
                    },
                    new TextBlock
                    {
                        Text = "正在载入设置…",
                        FontSize = 12,
                        Opacity = 0.7,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };

        Children.Add(_content);
        Children.Add(_loader);

        Loaded += (_, _) => EnsureLoadedOnce();
    }

    /// <summary>首次挂载时延迟一帧构建并呈现内容，避免与首个导航帧抢时间片。</summary>
    private void EnsureLoadedOnce()
    {
        if (_started || !IsLoaded)
        {
            return;
        }

        _started = true;
        Dispatcher.UIThread.Post(() =>
        {
            _content.Content = ContentFactory?.Invoke();
            CrossFade();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 用单个 DispatcherTimer 同时驱动加载层渐隐与内容层渐入，完成后隐藏加载层。
    /// （不能对两层各起一个定时器：它们共享同一 _fade 字段，后者会把前者取消掉，
    /// 导致加载层永远停在全透明度状态、进度条常驻。）
    /// </summary>
    private void CrossFade()
    {
        _fade?.Stop();
        _loader.Opacity = 1;
        _content.Opacity = 0;
        var startedAt = Environment.TickCount64;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var p = Math.Min(1.0, (Environment.TickCount64 - startedAt) / Math.Max(1.0, FadeMs));
            _loader.Opacity = 1 - p;
            _content.Opacity = p;
            if (p >= 1.0)
            {
                _loader.Opacity = 0;
                _content.Opacity = 1;
                _loader.IsVisible = false;
                timer.Stop();
            }
        };
        _fade = timer;
        timer.Start();
    }
}