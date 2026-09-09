namespace ClassIslandInjector;

/// <summary>
/// 通用的撤销 / 重做历史记录。
/// <para>
/// 采用「快照」模型：每次 <see cref="Push"/> 通过 <paramref name="snapshot"/> 捕获一份当前状态；
/// 约定 <c>snapshot().CloneList()</c> 返回状态的一份深拷贝，<see cref="Undo"/>/<see cref="Redo"/>
/// 时把栈中的快照取回。状态合并策略（合并离散 < 500ms 的连续编辑）由 <paramref name="mergeWindow"/> 提供，
/// 供重复派生的高频变更（滑块拖动 / 连续输入 / 方向键长按）聚合为一条历史。
/// </summary>
/// <typeparam name="TSnapshot">一条历史快照的类型。</typeparam>
public sealed class WallpaperUndoHistory<TSnapshot>
{
    private readonly int _capacity;
    private readonly Func<TSnapshot> _snapshot;
    private readonly List<TSnapshot> _undo = [];
    private readonly List<TSnapshot> _redo = [];

    /// <summary>上一次压入历史的时间（UTC），用于合并离散操作。</summary>
    private DateTime _lastPushAt = DateTime.MinValue;

    /// <summary>
    /// 状态合并判据：返回 <c>true</c> 表示上一次 <see cref="Push"/> 到本次之间间隔很短，
    /// 应把本次变更合并进同一条历史（不再新增快照）。未指定时默认使用 500ms 时间窗。
    /// </summary>
    private readonly Func<DateTime, bool>? _shouldMerge;

    /// <summary>历史变化事件（撤销 / 重做数量变化时触发，供 UI 启停「撤销 / 重做」按钮）。</summary>
    public event Action? Changed;

    public WallpaperUndoHistory(int capacity, Func<TSnapshot> snapshot, Func<DateTime, bool>? shouldMerge = null)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
        _snapshot = snapshot;
        _shouldMerge = shouldMerge;
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>当前状态是否为「已修改」（具备可撤销历史）。</summary>
    public bool IsDirty => _undo.Count > 0;

    /// <summary>
    /// 捕获当前状态并入栈。若与上一次 <see cref="Push"/> 被视为同一编辑会话，则仅刷新时间戳、
    /// 不新增快照（避免一次操作压入几十个快照挤掉早期历史）。
    /// <para>注意：本方法默认使用调用方提供的 <paramref name="snapshot"/> 与合并策略。
    /// 传入的 now 仅供合并判据使用。</para>
    /// </summary>
    public void Push(DateTime? now = null)
    {
        var ts = now ?? DateTime.UtcNow;
        if (CanUndo && IsMerge(ts))
        {
            _lastPushAt = ts;
            return;
        }

        _lastPushAt = ts;
        _undo.Add(_snapshot());
        if (_undo.Count > _capacity)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// 无条件捕获当前状态并入栈（用于显式「新建 / 删除 / 贴纸」等离散操作，
    /// 不受合并窗口影响）。会清空重做栈。
    /// </summary>
    public void PushDiscrete(DateTime? now = null)
    {
        _lastPushAt = now ?? DateTime.UtcNow;
        _undo.Add(_snapshot());
        if (_undo.Count > _capacity)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>撤销一步，返回撤销前的状态（供调用方落回画布）。无可撤销历史时返回 null。</summary>
    public TSnapshot? Undo(Func<TSnapshot> snapshot)
    {
        if (_undo.Count == 0)
        {
            return default;
        }

        _redo.Add(snapshot());
        var target = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Changed?.Invoke();
        return target;
    }

    /// <summary>重做一步，返回重做后的状态。无可重做历史时返回 null。</summary>
    public TSnapshot? Redo(Func<TSnapshot> snapshot)
    {
        if (_redo.Count == 0)
        {
            return default;
        }

        _undo.Add(snapshot());
        var target = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Changed?.Invoke();
        return target;
    }

    private bool IsMerge(DateTime now)
    {
        if (_shouldMerge != null)
        {
            return _shouldMerge(now);
        }

        return (now - _lastPushAt).TotalMilliseconds < 500 && _lastPushAt != DateTime.MinValue;
    }
}