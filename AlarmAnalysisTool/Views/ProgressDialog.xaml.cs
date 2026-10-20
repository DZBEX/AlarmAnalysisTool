using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace AlarmAnalysisTool.Views;

/// <summary>
/// 进度提示弹窗：在导入/分析期间显示不确定进度条和实时状态消息。
/// 禁用主窗口交互，防止用户重复点击按钮。
/// </summary>
public partial class ProgressDialog : Window
{
    /// <summary>
    /// 当前状态消息，通过 DependencyProperty 供 XAML 绑定
    /// </summary>
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(
            nameof(Message),
            typeof(string),
            typeof(ProgressDialog),
            new PropertyMetadata("正在处理..."));

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public ProgressDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 拦截 Alt+F4 和关闭按钮，操作期间不可手动关闭
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        // 仅允许代码调用 Close() 时关闭（通过标记）
        if (!_allowClose)
            e.Cancel = true;
        base.OnClosing(e);
    }

    private bool _allowClose;

    /// <summary>
    /// 由 ViewModel 在操作完成后调用，允许关闭
    /// </summary>
    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }
}
