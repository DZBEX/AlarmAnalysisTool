using System;
using System.Windows;

namespace AlarmAnalysisTool.Views;

/// <summary>
/// 时间范围选择对话框：显示数据库中数据的最早/最晚时间，
/// 用户可选择"全部导入"或"按时间范围筛选导入"。
/// </summary>
public partial class DateRangeDialog : Window
{
    /// <summary>用户选择的开始时间（null=不限）</summary>
    public DateTime? SelectedStart { get; private set; }

    /// <summary>用户选择的结束时间（null=不限）</summary>
    public DateTime? SelectedEnd { get; private set; }

    /// <summary>用户是否点击了确认（true=按范围导入, false=关闭/取消）</summary>
    public bool IsConfirmed { get; private set; }

    /// <summary>用户是否选择全部导入</summary>
    public bool IsImportAll { get; private set; }

    public DateRangeDialog(DateTime minDate, DateTime maxDate)
    {
        InitializeComponent();

        // 显示数据范围
        MinDateText.Text = minDate.ToString("yyyy-MM-dd HH:mm:ss");
        MaxDateText.Text = maxDate.ToString("yyyy-MM-dd HH:mm:ss");

        // 默认选中全部范围
        StartDatePicker.SelectedDate = minDate.Date;
        EndDatePicker.SelectedDate = maxDate.Date.AddDays(1).AddSeconds(-1);
    }

    private void ImportAllButton_Click(object sender, RoutedEventArgs e)
    {
        IsImportAll = true;
        IsConfirmed = true;
        SelectedStart = null;
        SelectedEnd = null;
        DialogResult = true;
        Close();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var start = StartDatePicker.SelectedDate;
        var end = EndDatePicker.SelectedDate;

        if (start == null && end == null)
        {
            // 没选日期 = 全部导入
            ImportAllButton_Click(sender, e);
            return;
        }

        if (start != null && end != null && start > end)
        {
            MessageBox.Show("开始时间不能晚于结束时间", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsImportAll = false;
        IsConfirmed = true;
        SelectedStart = start;
        // 结束日期取当天23:59:59
        SelectedEnd = end?.AddDays(1).AddSeconds(-1);
        DialogResult = true;
        Close();
    }
}
