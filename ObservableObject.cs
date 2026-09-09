using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClassIslandInjector;

/// <summary>
/// MVVM 可观察对象基类：提供 <see cref="INotifyPropertyChanged"/> 实现与
/// <see cref="Set{T}"/> 属性回写助手、<see cref="BeginUpdate"/>/<see cref="EndUpdate"/>
/// 批量更新抑制（连续属性变更只发一次通知，配合编辑器高频输入）。
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    private int _suspendCount;

    /// <summary>属性变更事件。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>是否正在批量更新（<see cref="BeginUpdate"/> 后、<see cref="EndUpdate"/> 前）。</summary>
    protected bool IsUpdating => _suspendCount > 0;

    /// <summary>进入批量更新：抑制后续逐条属性通知，直到对应的 <see cref="EndUpdate"/>。</summary>
    public void BeginUpdate() => _suspendCount++;

    /// <summary>结束一次批量更新；回到最外层时发出一次 <c>nameof(Item)</c> 全量刷新通知。</summary>
    public void EndUpdate()
    {
        if (_suspendCount == 0)
        {
            return;
        }

        _suspendCount--;
        if (_suspendCount == 0)
        {
            OnPropertyChanged(string.Empty);
        }
    }

    /// <summary>回写属性并（非批量期间）发出变更通知；值未变化时不发通知。返回是否发生了变更。</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>触发属性变更通知（非批量期间；<c>propertyName</c> 为 null 或空表示全量刷新）。</summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (IsUpdating)
        {
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// 图层的可观察记录（LayerItem 的值字段只读快照），供 UI 数据绑定在集合项上订阅
/// 单项变更通知。编辑操作仍以 <see cref="WallpaperLayerDocument"/> 为入口。
/// </summary>
public abstract class ObservableLayerItem : ObservableObject
{
}