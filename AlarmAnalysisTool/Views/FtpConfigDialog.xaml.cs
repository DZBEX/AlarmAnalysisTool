using System.Collections.ObjectModel;
using System.Windows;
using AlarmAnalysisTool.Models;
using AlarmAnalysisTool.Services;

namespace AlarmAnalysisTool.Views;

/// <summary>
/// FTP远程采集配置对话框。
/// 管理多个FTP服务器配置，支持从URL快速添加、手动编辑、测试连接、立即采集。
/// </summary>
public partial class FtpConfigDialog : Window
{
    private readonly FtpService _ftpService = new();
    private readonly FtpConfigService _configService = new();

    /// <summary>当前编辑的配置列表</summary>
    public ObservableCollection<FtpServerConfig> Configs { get; private set; }

    /// <summary>自动采集设置</summary>
    public FtpAutoRefreshSettings AutoRefreshSettings { get; private set; }

    /// <summary>用户是否点击了"立即采集"</summary>
    public bool IsImportRequested { get; private set; }

    public FtpConfigDialog()
    {
        InitializeComponent();

        // 加载已保存的配置
        var configs = _configService.LoadConfigs();
        Configs = new ObservableCollection<FtpServerConfig>(configs);

        AutoRefreshSettings = _configService.LoadAutoRefreshSettings();

        // 绑定数据
        ConfigDataGrid.ItemsSource = Configs;
        AutoRefreshCheckBox.IsChecked = AutoRefreshSettings.IsEnabled;

        // 设置间隔下拉框
        var intervals = FtpAutoRefreshSettings.AvailableIntervals;
        var idx = Array.IndexOf(intervals, AutoRefreshSettings.IntervalMinutes);
        IntervalComboBox.SelectedIndex = idx >= 0 ? idx : 2; // 默认5分钟

        UpdateLastSyncDisplay();
    }

    /// <summary>
    /// 从URL快速添加配置（支持自定义名称）
    /// </summary>
    private void AddFromUrlButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlInputBox.Text.Trim();
        var customName = ServerNameInputBox.Text.Trim();

        if (string.IsNullOrEmpty(url))
        {
            MessageBox.Show("请输入FTP地址", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // 尝试解析URL，传入自定义名称
            var config = FtpServerConfig.FromUrl(url, customName);

            // 检查是否已存在相同主机和路径的配置
            if (Configs.Any(c => c.Host == config.Host && c.RemotePath == config.RemotePath))
            {
                MessageBox.Show($"已存在相同配置: {config.Host}{config.RemotePath}",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Configs.Add(config);
            UrlInputBox.Clear();
            ServerNameInputBox.Clear();
            SaveConfigs();

            MessageBox.Show($"已添加: {config.DisplayName}\n\n将在主页面显示为\"{config.Name}实时报警\"",
                "添加成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"URL解析失败:\n{ex.Message}\n\n正确格式: ftp://用户名:密码@主机地址/路径/文件.db",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 手动添加空配置（使用自定义名称或默认"新配置"）
    /// </summary>
    private void AddManualButton_Click(object sender, RoutedEventArgs e)
    {
        var customName = ServerNameInputBox.Text.Trim();
        var config = new FtpServerConfig
        {
            Name = string.IsNullOrEmpty(customName) ? "新配置" : customName,
            Host = "192.168.x.x",
            Port = 21,
            Username = "",
            Password = "",
            RemotePath = "/eventlog/event.db",
            IsEnabled = true
        };
        Configs.Add(config);
        ConfigDataGrid.SelectedItem = config;
        ConfigDataGrid.ScrollIntoView(config);
        ServerNameInputBox.Clear();
        SaveConfigs();
    }

    /// <summary>
    /// 删除选中的配置
    /// </summary>
    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ConfigDataGrid.SelectedItem is not FtpServerConfig selected)
        {
            MessageBox.Show("请先选择要删除的配置", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show($"确定删除配置 \"{selected.DisplayName}\" 吗？",
            "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            Configs.Remove(selected);
            SaveConfigs();
        }
    }

    /// <summary>
    /// 测试选中的配置连接
    /// </summary>
    private async void TestSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (ConfigDataGrid.SelectedItem is not FtpServerConfig selected)
        {
            MessageBox.Show("请先选择要测试的配置", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await TestConnectionAsync(selected);
    }

    /// <summary>
    /// 测试所有已启用的配置连接
    /// </summary>
    private async void TestAllButton_Click(object sender, RoutedEventArgs e)
    {
        var enabledConfigs = Configs.Where(c => c.IsEnabled).ToList();
        if (enabledConfigs.Count == 0)
        {
            MessageBox.Show("没有已启用的FTP配置", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TestAllButton.IsEnabled = false;
        TestAllButton.Content = "测试中...";

        var results = new List<(FtpServerConfig config, bool success, string error)>();
        foreach (var config in enabledConfigs)
        {
            var (success, fileSize, error) = await _ftpService.TestConnectionAsync(
                config.Host, config.Port, config.Username, config.Password, config.RemotePath);
            results.Add((config, success, success
                ? (fileSize > 0 ? $"{fileSize / 1024.0:F1} KB" : "连接成功")
                : error));
        }

        TestAllButton.IsEnabled = true;
        TestAllButton.Content = "🔌 测试全部连接";

        var successCount = results.Count(r => r.success);
        var failCount = results.Count - successCount;

        var msg = $"测试完成: {successCount} 成功, {failCount} 失败\n\n";
        foreach (var (config, success, info) in results)
        {
            msg += $"  {(success ? "✓" : "✗")} {config.DisplayName}: {info}\n";
        }

        MessageBox.Show(msg, "连接测试结果", MessageBoxButton.OK,
            failCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    /// <summary>
    /// 测试单个配置的FTP连接
    /// </summary>
    private async Task TestConnectionAsync(FtpServerConfig config)
    {
        var button = TestSelectedButton;
        button.IsEnabled = false;
        var originalContent = button.Content;
        button.Content = "测试中...";

        try
        {
            var (success, fileSize, error) = await _ftpService.TestConnectionAsync(
                config.Host, config.Port, config.Username, config.Password, config.RemotePath);

            if (success)
            {
                var sizeText = fileSize > 0 ? $" ({fileSize / 1024.0:F1} KB)" : "";
                MessageBox.Show($"连接成功!\n\n服务器: {config.Host}:{config.Port}\n文件: {config.RemotePath}{sizeText}",
                    "连接成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"连接失败:\n\n{error}",
                    "连接失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = originalContent;
        }
    }

    /// <summary>
    /// 立即采集按钮 - 保存配置并通知主窗口开始下载
    /// </summary>
    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var enabledConfigs = Configs.Where(c => c.IsEnabled).ToList();
        if (enabledConfigs.Count == 0)
        {
            MessageBox.Show("没有已启用的FTP配置，请先添加并启用配置",
                "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SaveConfigs();
        IsImportRequested = true;
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// 关闭按钮
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigs();
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// 保存配置和自动采集设置
    /// </summary>
    private void SaveConfigs()
    {
        // 从UI收集自动采集设置
        AutoRefreshSettings.IsEnabled = AutoRefreshCheckBox.IsChecked ?? false;
        var intervals = FtpAutoRefreshSettings.AvailableIntervals;
        var idx = IntervalComboBox.SelectedIndex;
        AutoRefreshSettings.IntervalMinutes = idx >= 0 && idx < intervals.Length
            ? intervals[idx] : 5;

        _configService.Save(Configs.ToList(), AutoRefreshSettings);
    }

    /// <summary>
    /// 更新最后同步时间显示
    /// </summary>
    private void UpdateLastSyncDisplay()
    {
        var lastSync = Configs
            .Where(c => c.LastSyncTime != DateTime.MinValue)
            .Select(c => c.LastSyncTime)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

        LastSyncTextBlock.Text = lastSync != DateTime.MinValue
            ? $"最后采集: {lastSync:HH:mm:ss}"
            : "";
    }

    /// <summary>
    /// 窗口关闭时自动保存
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        SaveConfigs();
        base.OnClosed(e);
    }
}
