using System;
using System.Windows;
using System.Windows.Input;
using AlarmAnalysisTool.ViewModels;

namespace AlarmAnalysisTool;

public partial class MainWindow : Window
{
    // 手动最大化状态（WindowStyle=None 时不能用 WindowState.Maximized，否则会遮挡任务栏）
    private bool _isMaximized;
    private Rect _restoreBounds;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        SourceInitialized += MainWindow_SourceInitialized;
        UpdateMaximizeButton();
    }

    /// <summary>
    /// 窗口句柄创建后、首次显示前：若 XAML 要求启动最大化，则按工作区（排除任务栏）最大化
    /// </summary>
    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Maximized) return;

        // 记录 XAML 定义的正常尺寸（1400x850 居中）作为还原基准，避免把全屏记为还原尺寸
        const double normalW = 1400, normalH = 850;
        var wa = SystemParameters.WorkArea;
        _restoreBounds = new Rect(
            wa.Left + (wa.Width - normalW) / 2,
            wa.Top + (wa.Height - normalH) / 2,
            normalW, normalH);
        MaximizeToWorkArea();
    }

    /// <summary>
    /// 标题栏拖动移动窗体 + 双击最大化/还原
    /// </summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        DragMove();
    }

    /// <summary>
    /// 最小化
    /// </summary>
    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// 最大化/还原切换
    /// </summary>
    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    /// <summary>
    /// 关闭窗体
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximize()
    {
        if (_isMaximized)
            RestoreFromMaximize();
        else
            MaximizeToWorkArea();
    }

    /// <summary>
    /// 根据当前是否最大化，更新最大化按钮图标（□=最大化 / ▣=还原）
    /// </summary>
    private void UpdateMaximizeButton()
    {
        if (MaximizeButton != null)
            MaximizeButton.Content = _isMaximized ? "▣" : "□";
    }

    /// <summary>
    /// 手动最大化到工作区（自动排除底部任务栏）
    /// </summary>
    private void MaximizeToWorkArea()
    {
        if (_isMaximized) return;
        if (WindowState != WindowState.Maximized)
            _restoreBounds = new Rect(Left, Top, Width, Height);

        _isMaximized = true;
        WindowState = WindowState.Normal; // WindowStyle=None 下保持 Normal 才能自由设定尺寸
        Left = SystemParameters.WorkArea.Left;
        Top = SystemParameters.WorkArea.Top;
        Width = SystemParameters.WorkArea.Width;
        Height = SystemParameters.WorkArea.Height;
        UpdateMaximizeButton();
    }

    /// <summary>
    /// 从最大化还原为记录的正常尺寸
    /// </summary>
    private void RestoreFromMaximize()
    {
        _isMaximized = false;
        Left = _restoreBounds.Left;
        Top = _restoreBounds.Top;
        Width = _restoreBounds.Width;
        Height = _restoreBounds.Height;
        UpdateMaximizeButton();
    }
}
