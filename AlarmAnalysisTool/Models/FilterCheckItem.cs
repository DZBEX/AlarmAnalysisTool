using CommunityToolkit.Mvvm.ComponentModel;

namespace AlarmAnalysisTool.Models;

/// <summary>
/// 筛选项（带选中状态的复选项，用于多选筛选器）
/// </summary>
public partial class FilterCheckItem : ObservableObject
{
    /// <summary>选项名称</summary>
    public string Name { get; set; } = "";

    /// <summary>是否选中</summary>
    [ObservableProperty]
    private bool _isSelected = true;

    public FilterCheckItem() { }

    public FilterCheckItem(string name, bool isSelected = true)
    {
        Name = name;
        IsSelected = isSelected;
    }

    public override string ToString() => Name;
}
