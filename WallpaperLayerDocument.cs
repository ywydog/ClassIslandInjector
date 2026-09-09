namespace ClassIslandInjector;

/// <summary>
/// 图层文档（MVVM 领域的统一模型 / 单一数据源）。
/// <para>
/// 统一持有「当前图层列表 + 撤销/重做历史 + 未保存脏标记」，并对外发出统一的变更事件。
/// 编辑器的所有编辑操作都应以本对象为入口：修改 <see cref="Layers"/> 中图层的属性后调用
/// <see cref="MarkDirty"/>，离散操作调用 <see cref="Push"/>（含自动合并高频变更），
/// 撤销 / 重做调用 <see cref="Undo"/> / <see cref="Redo"/>（内部替换 Layers 引用并发出通知）。
/// 这样把原先分散在窗口类里的手动刷新链（RefreshLayerList/RefreshInspector/UpdateStatus）
/// 收敛为对 <see cref="Changed"/> 事件的单一订阅。
/// </summary>
public sealed class WallpaperLayerDocument
{
    /// <summary>撤销 / 重做历史：捕获当前图层列表的深拷贝作为快照；连续高频变更（500ms）合并为一条。</summary>
    private readonly WallpaperUndoHistory<List<WallpaperLayerItem>> _history;

    /// <summary>当前图层列表（可变引用；Undo/Redo 时可能被整体替换为新快照）。</summary>
    private List<WallpaperLayerItem> _layers;

    /// <summary>是否存在未保存的修改（编辑器关闭确认时据此提示）。</summary>
    private bool _dirty;

    /// <summary>上一次 Push 的时间（合并高频编辑用）。</summary>
    private DateTime? _lastPushAt;

    /// <summary>当前图层列表（单一数据源）。修改其中元素的属性后请调用 <see cref="MarkDirty"/>。</summary>
    public List<WallpaperLayerItem> Layers
    {
        get => _layers;
        private set => _layers = value;
    }

    /// <summary>是否存在未保存的修改。</summary>
    public bool IsDirty => _dirty;

    /// <summary>是否存在可撤销历史。</summary>
    public bool CanUndo => _history.CanUndo;

    /// <summary>是否存在可重做历史。</summary>
    public bool CanRedo => _history.CanRedo;

    /// <summary>文档变更事件：图层增删 / 属性编辑 / 撤销 / 重做 / 脏标记变化时触发，供 UI 统一刷新。</summary>
    public event Action? Changed;

    /// <summary>撤销/重做状态变化事件（供命令栏按钮启停）。</summary>
    public event Action? UndoRedoChanged;

    public WallpaperLayerDocument(List<WallpaperLayerItem> initialLayers)
    {
        _layers = initialLayers;
        _history = new WallpaperUndoHistory<List<WallpaperLayerItem>>(
            capacity: 100,
            snapshot: () => _layers.Select(l => l.Clone()).ToList());
        _history.Changed += () =>
        {
            MarkDirty();
            UndoRedoChanged?.Invoke();
        };
    }

    /// <summary>声明发生了一次编辑（属性变更 / 增删），标记为未保存并发出 <see cref="Changed"/>。</summary>
    public void MarkDirty()
    {
        if (_dirty)
        {
            return;
        }

        _dirty = true;
        Changed?.Invoke();
    }

    /// <summary>压入撤销点并标记未保存（拥有自动合并高频变更的 Push）。若与上次间隔很短则合并。</summary>
    public void Push()
    {
        var now = DateTime.UtcNow;
        if (CanUndo && _lastPushAt != null && (now - _lastPushAt.Value).TotalMilliseconds < 500)
        {
            _lastPushAt = now;
            return;
        }

        _lastPushAt = now;
        _history.PushDiscrete(now);
    }

    /// <summary>无条件压入撤销点（用于新建 / 删除 / 贴纸等离散操作，不受合并窗口影响）。</summary>
    public void PushDiscrete()
    {
        _lastPushAt = DateTime.UtcNow;
        _history.PushDiscrete();
    }

    /// <summary>撤销一步；成功后把图层列表替换为撤销前快照并发出变更事件。无可撤销时返回 false。</summary>
    public bool Undo()
    {
        var target = _history.Undo(() => _layers.Select(l => l.Clone()).ToList());
        if (target == null)
        {
            return false;
        }

        _layers = target;
        MarkDirty();
        Changed?.Invoke();
        return true;
    }

    /// <summary>重做一步；成功后把图层列表替换为重做快照并发出变更事件。无可重做时返回 false。</summary>
    public bool Redo()
    {
        var target = _history.Redo(() => _layers.Select(l => l.Clone()).ToList());
        if (target == null)
        {
            return false;
        }

        _layers = target;
        MarkDirty();
        Changed?.Invoke();
        return true;
    }

    /// <summary>保存已完成：清除未保存标记并发出变更事件（撤销历史保留，可继续撤销/重做）。</summary>
    public void MarkSaved()
    {
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        Changed?.Invoke();
    }
}