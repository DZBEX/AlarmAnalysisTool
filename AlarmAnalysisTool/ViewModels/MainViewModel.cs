using AlarmAnalysisTool.Models;
using AlarmAnalysisTool.Services;
using AlarmAnalysisTool.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace AlarmAnalysisTool.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ImportService _importService = new();
    private readonly AnalysisService _analysisService = new();
    private readonly ExportService _exportService = new();
    private readonly ReportExportService _reportExportService = new();
    private readonly FtpService _ftpService = new();
    private readonly FtpConfigService _ftpConfigService = new();
    private DispatcherTimer? _autoRefreshTimer;

    /// <summary>
    /// 当前进度弹窗实例（操作期间非null）
    /// </summary>
    private Views.ProgressDialog? _progressDialog;

    /// <summary>
    /// 显示进度弹窗并禁用主窗口交互
    /// </summary>
    private Views.ProgressDialog ShowProgress(string message)
    {
        _progressDialog = new Views.ProgressDialog { Message = message };
        var mainWindow = System.Windows.Application.Current.MainWindow;
        _progressDialog.Owner = mainWindow;
        mainWindow.IsEnabled = false;
        _progressDialog.Show();
        return _progressDialog;
    }

    /// <summary>
    /// 创建绑定到当前进度弹窗的 IProgress 回调
    /// </summary>
    private IProgress<string> CreateProgress()
    {
        return new Progress<string>(msg =>
        {
            if (_progressDialog != null)
                _progressDialog.Message = msg;
        });
    }

    /// <summary>
    /// 关闭进度弹窗并恢复主窗口交互
    /// </summary>
    private void CloseProgress()
    {
        if (_progressDialog != null)
        {
            var mainWindow = System.Windows.Application.Current.MainWindow;
            mainWindow.IsEnabled = true;
            _progressDialog.ForceClose();
            _progressDialog = null;
        }
    }

    [ObservableProperty]
    private ObservableCollection<AlarmRecord> _records = new();

    [ObservableProperty]
    private ObservableCollection<AlarmRecord> _filteredRecords = new();

    [ObservableProperty]
    private ObservableCollection<AlarmFrequencyItem> _frequencyData = new();

    [ObservableProperty]
    private ObservableCollection<CategoryDistributionItem> _categoryData = new();

    [ObservableProperty]
    private ObservableCollection<HourlyDistributionItem> _hourlyData = new();

    [ObservableProperty]
    private ObservableCollection<DailyDistributionItem> _dailyData = new();

    [ObservableProperty]
    private ObservableCollection<DurationAnalysisItem> _durationData = new();

    [ObservableProperty]
    private ObservableCollection<SourceAnalysisItem> _sourceData = new();

    [ObservableProperty]
    private ObservableCollection<AlarmRecord> _unrecoveredData = new();

    [ObservableProperty]
    private ObservableCollection<DiagnosticSuggestion> _suggestions = new();

    [ObservableProperty]
    private ObservableCollection<AlarmCorrelationItem> _correlationData = new();

    [ObservableProperty]
    private ImportFileInfo? _fileInfo;

    [ObservableProperty]
    private AnalysisSummary? _summary;

    /// <summary>
    /// 是否已过滤提示类记录（用于UI显示过滤信息卡片）
    /// </summary>
    public bool HasFilteredInfo => Summary?.FilteredInfoCount > 0;

    /// <summary>
    /// 过滤提示类记录的摘要文本
    /// </summary>
    public string FilteredInfoSummary => Summary != null
        ? $"已排除 {Summary.FilteredInfoCount} 条 / 共 {Summary.TotalImportedCount} 条"
        : "";

    partial void OnSummaryChanged(AnalysisSummary? value)
    {
        OnPropertyChanged(nameof(HasFilteredInfo));
        OnPropertyChanged(nameof(FilteredInfoSummary));
    }

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    /// <summary>
    /// IsBusy 的反值，供 XAML 按钮 IsEnabled 绑定
    /// </summary>
    public bool IsNotBusy => !IsBusy;

    /// <summary>
    /// FTP采集是否正在进行（独立于历史分析的IsBusy）
    /// </summary>
    [ObservableProperty]
    private bool _isFtpBusy;

    /// <summary>
    /// IsFtpBusy 的反值，供 XAML 按钮 IsEnabled 绑定
    /// </summary>
    public bool IsNotFtpBusy => !IsFtpBusy;

    partial void OnIsFtpBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotFtpBusy));
    }

    /// <summary>
    /// IsAutoRefreshEnabled 的反值，供 XAML Visibility 绑定
    /// </summary>
    public bool IsNotAutoRefreshEnabled => !IsAutoRefreshEnabled;

    [ObservableProperty]
    private string _statusText = "就绪 - 请导入报警日志文件";

    // 筛选
    [ObservableProperty]
    private string _filterKeyword = "";

    [ObservableProperty]
    private DateTime? _filterStartDate;

    [ObservableProperty]
    private DateTime? _filterEndDate;

    [ObservableProperty]
    private ObservableCollection<FilterCheckItem> _availableCategories = new();

    [ObservableProperty]
    private ObservableCollection<FilterCheckItem> _availableSources = new();

    // 图表模型
    [ObservableProperty]
    private PlotModel? _frequencyChart;

    [ObservableProperty]
    private PlotModel? _categoryChart;

    [ObservableProperty]
    private PlotModel? _hourlyChart;

    [ObservableProperty]
    private PlotModel? _dailyChart;

    /// <summary>
    /// 按日趋势图自适应高度：根据天数动态计算，每行约 28px + 标题/边距 70px，上限 800px
    /// </summary>
    [ObservableProperty]
    private double _dailyChartHeight = 320;

    private static double ComputeDailyChartHeight(int dayCount)
    {
        if (dayCount <= 0) return 320;
        // 每天一行柱子约28px高；标题+上下边距预留70px
        double h = dayCount * 28.0 + 70;
        // 上限防止图表过高撑满屏幕（超过上限时让 OxyPlot 自行滚动）
        return Math.Min(h, 800);
    }

    [ObservableProperty]
    private PlotModel? _sourceChart;

    // 当前选中的记录
    [ObservableProperty]
    private AlarmRecord? _selectedRecord;

    // 列映射
    [ObservableProperty]
    private ColumnMapping _columnMapping = new();

    [ObservableProperty]
    private ObservableCollection<string> _sourceColumns = new();

    // FTP采集相关属性
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotAutoRefreshEnabled))]
    private bool _isAutoRefreshEnabled;

    [ObservableProperty]
    private string _ftpSyncStatus = "";

    [ObservableProperty]
    private bool _hasFtpConfigs;

    /// <summary>
    /// 实时报警表集合（每个FTP服务器对应一个实时报警表）
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<RealtimeAlarmTable> _realtimeTables = new();

    /// <summary>
    /// 是否有实时报警表（用于UI显示实时报警Tab）
    /// </summary>
    public bool HasRealtimeTables => RealtimeTables.Count > 0;

    /// <summary>
    /// HasRealtimeTables 的反值（用于显示空状态）
    /// </summary>
    public bool IsNotRealtimeTables => !HasRealtimeTables;

    /// <summary>
    /// 是否未配置FTP（用于显示引导提示）
    /// </summary>
    public bool IsNotFtpConfigured => !HasFtpConfigs;

    /// <summary>
    /// 是否有FTP配置（用于UI显示FTP状态区域）
    /// </summary>
    public bool CanAutoRefresh => HasFtpConfigs;

    partial void OnHasFtpConfigsChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAutoRefresh));
        OnPropertyChanged(nameof(IsNotFtpConfigured));
    }

    /// <summary>
    /// RealtimeTables 变化时通知 HasRealtimeTables
    /// </summary>
    partial void OnRealtimeTablesChanged(ObservableCollection<RealtimeAlarmTable> value)
    {
        OnPropertyChanged(nameof(HasRealtimeTables));
        OnPropertyChanged(nameof(IsNotRealtimeTables));
    }

    /// <summary>
    /// 构造函数：初始化FTP配置和自动采集定时器
    /// </summary>
    public MainViewModel()
    {
        UpdateAutoRefreshSettings();
    }

    [RelayCommand]
    public async Task ImportCsv()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "CSV文件|*.csv|所有文件|*.*",
            Title = "选择报警日志CSV文件（可多选）",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true) return;
        var filePaths = dialog.FileNames;

        IsBusy = true;
        var fileCount = filePaths.Length;
        var titleMsg = fileCount == 1
            ? $"正在导入: {Path.GetFileName(filePaths[0])}"
            : $"正在导入 {fileCount} 个CSV文件...";
        ShowProgress(titleMsg);
        var progress = CreateProgress();
        try
        {
            StatusText = titleMsg + "...";

            List<AlarmRecord> records;
            ColumnMapping mapping;
            List<string> headers;
            List<string> importedFiles;

            if (fileCount == 1)
            {
                var (recs, map, hdrs) = await _importService.ImportCsvAsync(filePaths[0], progress);
                records = recs;
                mapping = map;
                headers = hdrs;
                importedFiles = new List<string> { filePaths[0] };
            }
            else
            {
                var (recs, map, hdrs, files) = await _importService.ImportMultipleCsvAsync(filePaths, progress);
                records = recs;
                mapping = map;
                headers = hdrs;
                importedFiles = files;
            }

            ColumnMapping = mapping;
            SourceColumns = new ObservableCollection<string>(headers);

            await LoadRecordsAsync(records, filePaths[0], progress, importedFiles);
            StatusText = $"导入完成: {records.Count} 条记录 ({fileCount}个文件)";
        }
        catch (Exception ex)
        {
            StatusText = $"导入失败: {ex.Message}";
            MessageBox.Show($"导入失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CloseProgress();
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ImportExcel()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Excel文件|*.xlsx;*.xls|所有文件|*.*",
            Title = "选择报警日志Excel文件"
        };

        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            StatusText = $"正在读取Excel: {Path.GetFileName(dialog.FileName)}...";
            var (headers, previewRows, rowCount) = _importService.ReadExcelPreview(dialog.FileName);

            if (headers.Count == 0)
            {
                MessageBox.Show("未检测到表头行", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 自动检测映射
            var mapping = new ColumnMapping();
            foreach (var header in headers)
            {
                var field = ColumnMapping.AutoDetectField(header);
                if (field != null)
                {
                    var prop = typeof(ColumnMapping).GetProperty(field);
                    prop?.SetValue(mapping, header);
                }
            }

            // 显示列映射对话框（此期间不显示进度弹窗，允许用户交互）
            var mappingDialog = new ColumnMappingDialog(headers, mapping, previewRows)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            if (mappingDialog.ShowDialog() != true) return;

            var finalMapping = mappingDialog.GetMapping();

            // 实际导入阶段：显示进度弹窗
            ShowProgress($"正在导入: {Path.GetFileName(dialog.FileName)}");
            var progress = CreateProgress();
            StatusText = "正在导入数据...";

            // Excel导入在后台线程执行
            var records = await _importService.ImportExcelAsync(dialog.FileName, finalMapping, headers, progress);
            ColumnMapping = finalMapping;
            SourceColumns = new ObservableCollection<string>(headers);

            await LoadRecordsAsync(records, dialog.FileName, progress);
            StatusText = $"导入完成: {records.Count} 条记录";
        }
        catch (Exception ex)
        {
            StatusText = $"导入失败: {ex.Message}";
            MessageBox.Show($"导入失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CloseProgress();
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ImportSqlite()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "SQLite数据库|*.db;*.sqlite;*.sqlite3|所有文件|*.*",
            Title = "选择报警日志SQLite数据库文件（可多选）",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true) return;
        var filePaths = dialog.FileNames;

        // 先查询数据库中数据的时间范围，让用户选择导入范围
        DateTime? filterStart = null;
        DateTime? filterEnd = null;

        ShowProgress("正在读取数据时间范围...");
        var rangeProgress = CreateProgress();
        try
        {
            var (minDate, maxDate) = await Task.Run(() => _importService.GetMultipleSqliteDateRange(filePaths));
            CloseProgress();

            if (minDate != DateTime.MinValue)
            {
                // 弹出日期范围选择对话框
                var dateDialog = new Views.DateRangeDialog(minDate, maxDate)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };

                if (dateDialog.ShowDialog() == true && dateDialog.IsConfirmed && !dateDialog.IsImportAll)
                {
                    filterStart = dateDialog.SelectedStart;
                    filterEnd = dateDialog.SelectedEnd;
                }
                // 如果用户选"全部导入"或关闭对话框，filterStart/filterEnd 保持 null
            }
        }
        catch
        {
            CloseProgress();
            // 查询失败时不阻塞导入流程，直接全部导入
        }

        IsBusy = true;
        var fileCount = filePaths.Length;
        var rangeText = (filterStart.HasValue || filterEnd.HasValue)
            ? $" ({filterStart:yyyy-MM-dd}~{filterEnd:yyyy-MM-dd})"
            : "";
        var titleMsg = fileCount == 1
            ? $"正在导入: {Path.GetFileName(filePaths[0])}{rangeText}"
            : $"正在导入 {fileCount} 个数据库文件{rangeText}...";
        ShowProgress(titleMsg);
        var progress = CreateProgress();
        try
        {
            StatusText = titleMsg + "...";

            List<AlarmRecord> records;
            List<string> importedFiles;

            if (filterStart.HasValue || filterEnd.HasValue)
            {
                // 带时间范围过滤导入
                if (fileCount == 1)
                {
                    records = await _importService.ImportSqliteAsync(filePaths[0], filterStart, filterEnd, progress);
                    importedFiles = new List<string> { filePaths[0] };
                }
                else
                {
                    records = await _importService.ImportMultipleSqliteAsync(filePaths, filterStart, filterEnd, progress);
                    importedFiles = filePaths.ToList();
                }
            }
            else
            {
                // 全部导入
                if (fileCount == 1)
                {
                    records = await _importService.ImportSqliteAsync(filePaths[0], progress);
                    importedFiles = new List<string> { filePaths[0] };
                }
                else
                {
                    records = await _importService.ImportMultipleSqliteAsync(filePaths, progress);
                    importedFiles = filePaths.ToList();
                }
            }

            ColumnMapping = new ColumnMapping();
            SourceColumns = new ObservableCollection<string>();

            await LoadRecordsAsync(records, filePaths[0], progress, importedFiles);
            StatusText = $"导入完成: {records.Count} 条记录 ({fileCount}个文件){rangeText}";
        }
        catch (Exception ex)
        {
            StatusText = $"导入失败: {ex.Message}";
            MessageBox.Show($"导入失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CloseProgress();
            IsBusy = false;
        }
    }

    #region FTP远程采集

    /// <summary>
    /// 从FTP服务器采集报警数据（手动触发）
    /// </summary>
    [RelayCommand]
    public async Task ImportFromFtp()
    {
        await ImportFromFtpCore(isAutoRefresh: false);
    }

    /// <summary>
    /// 打开FTP配置管理对话框
    /// </summary>
    [RelayCommand]
    public void ManageFtpConfigs()
    {
        var dialog = new Views.FtpConfigDialog
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true && dialog.IsImportRequested)
        {
            // 用户点击了"立即采集"
            _ = ImportFromFtp();
        }

        // 对话框关闭后更新自动采集设置
        UpdateAutoRefreshSettings();
    }

    /// <summary>
    /// FTP采集核心逻辑（手动/自动共用）
    /// 仅更新实时报警表，不覆盖历史分析数据
    /// </summary>
    private async Task ImportFromFtpCore(bool isAutoRefresh)
    {
        var configs = _ftpConfigService.LoadConfigs().Where(c => c.IsEnabled).ToList();

        if (configs.Count == 0)
        {
            if (!isAutoRefresh)
            {
                MessageBox.Show("没有已启用的FTP配置，请先配置FTP服务器",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                OpenFtpConfigDialog();
            }
            return;
        }

        IsFtpBusy = true;

        // 自动采集时不显示模态进度弹窗，只更新状态文本
        if (!isAutoRefresh)
        {
            ShowProgress($"正在从 {configs.Count} 个FTP服务器采集数据...");
        }
        IProgress<string> progress = new Progress<string>(msg =>
        {
            if (isAutoRefresh)
                FtpSyncStatus = msg;
            else if (_progressDialog != null)
                _progressDialog.Message = msg;
        });

        try
        {
            var successCount = 0;
            var totalRecords = 0;

            for (int i = 0; i < configs.Count; i++)
            {
                var config = configs[i];
                progress.Report($"[{i + 1}/{configs.Count}] 正在连接 {config.DisplayName}...");

                try
                {
                    // 从FTP下载.db文件到本地
                    var localPath = await _ftpService.DownloadFileAsync(
                        config.Host, config.Port, config.Username, config.Password,
                        config.RemotePath, progress);

                    progress.Report($"[{i + 1}/{configs.Count}] 正在读取 {config.DisplayName}...");

                    // 读取数据库中的报警记录
                    var records = await _importService.ImportSqliteAsync(localPath, progress);
                    successCount++;
                    totalRecords += records.Count;

                    // 更新同步状态
                    _ftpConfigService.UpdateSyncStatus(config, records.Count, "成功");

                    // 创建或更新该服务器的实时报警表（不合并到历史分析数据）
                    UpdateRealtimeTable(config, records, isOnline: true, "采集成功");

                    progress.Report($"[{i + 1}/{configs.Count}] {config.DisplayName}: {records.Count} 条");
                }
                catch (Exception ex)
                {
                    _ftpConfigService.UpdateSyncStatus(config, 0, $"失败: {ex.Message}");

                    // 更新实时报警表为离线状态（保留之前的数据）
                    UpdateRealtimeTable(config, new List<AlarmRecord>(), isOnline: false, $"采集失败: {ex.Message}");

                    progress.Report($"[{i + 1}/{configs.Count}] {config.DisplayName} 采集失败: {ex.Message}");
                }
            }

            // 清理不再存在的服务器实时报警表（配置被删除或禁用）
            CleanupRealtimeTables(configs);

            // 只更新FTP采集状态，不影响历史分析区
            var timeStr = DateTime.Now.ToString("HH:mm:ss");
            if (totalRecords > 0)
            {
                FtpSyncStatus = $"最后采集: {timeStr} ({totalRecords}条, {successCount}/{configs.Count}个服务器)";
                StatusText = $"FTP采集完成: {totalRecords} 条记录 ({successCount}/{configs.Count}个服务器)";
            }
            else
            {
                FtpSyncStatus = $"采集完成: 未获取到数据 ({timeStr})";
                StatusText = "FTP采集完成: 未获取到数据";
                if (!isAutoRefresh)
                    MessageBox.Show("未采集到任何数据，请检查FTP配置和网络连接",
                        "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"FTP采集失败: {ex.Message}";
            FtpSyncStatus = $"采集失败: {DateTime.Now:HH:mm:ss}";
            if (!isAutoRefresh)
                MessageBox.Show($"FTP采集失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (!isAutoRefresh)
                CloseProgress();
            IsFtpBusy = false;
        }
    }

    /// <summary>
    /// 打开FTP配置对话框
    /// </summary>
    private void OpenFtpConfigDialog()
    {
        var dialog = new Views.FtpConfigDialog
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        dialog.ShowDialog();
        UpdateAutoRefreshSettings();
    }

    /// <summary>
    /// 创建或更新某个FTP服务器的实时报警表
    /// </summary>
    private void UpdateRealtimeTable(FtpServerConfig config, List<AlarmRecord> records, bool isOnline, string statusText)
    {
        // 查找已有的实时报警表
        var table = RealtimeTables.FirstOrDefault(t => t.ConfigId == config.Id);

        if (table == null)
        {
            // 创建新的实时报警表
            table = new RealtimeAlarmTable();
            table.Initialize(config.Id, config.DisplayName, config.Host);

            // 如果有记录才更新（无记录时只设置离线状态）
            if (records.Count > 0)
            {
                table.UpdateRecords(records, isOnline, statusText);
            }
            else
            {
                table.IsOnline = isOnline;
                table.StatusText = statusText;
                table.LastSyncTime = DateTime.Now;
                table.RecordCount = 0;
            }

            RealtimeTables.Add(table);
        }
        else
        {
            // 更新已有的实时报警表
            if (records.Count > 0)
            {
                table.UpdateRecords(records, isOnline, statusText);
            }
            else
            {
                // 采集失败，只更新状态，保留之前的数据
                table.IsOnline = isOnline;
                table.StatusText = statusText;
                table.LastSyncTime = DateTime.Now;
                table.RefreshDerivedProperties();
            }
        }

        // 更新服务器名称（用户可能修改了配置名称）
        if (table.ServerName != config.DisplayName)
        {
            table.ServerName = config.DisplayName;
        }

        OnPropertyChanged(nameof(HasRealtimeTables));
        OnPropertyChanged(nameof(IsNotRealtimeTables));
    }

    /// <summary>
    /// 清理不再存在的服务器实时报警表（配置被删除或禁用）
    /// </summary>
    private void CleanupRealtimeTables(List<FtpServerConfig> activeConfigs)
    {
        var activeIds = activeConfigs.Select(c => c.Id).ToHashSet();
        var toRemove = RealtimeTables.Where(t => !activeIds.Contains(t.ConfigId)).ToList();
        foreach (var table in toRemove)
        {
            RealtimeTables.Remove(table);
        }
        if (toRemove.Count > 0)
        {
            OnPropertyChanged(nameof(HasRealtimeTables));
            OnPropertyChanged(nameof(IsNotRealtimeTables));
        }
    }

    /// <summary>
    /// 从配置服务加载设置，并启动/停止自动采集定时器
    /// </summary>
    private void UpdateAutoRefreshSettings()
    {
        var settings = _ftpConfigService.LoadAutoRefreshSettings();
        var configs = _ftpConfigService.LoadConfigs();

        IsAutoRefreshEnabled = settings.IsEnabled;
        HasFtpConfigs = configs.Any(c => c.IsEnabled);

        if (settings.IsEnabled && HasFtpConfigs)
        {
            StartAutoRefreshTimer(settings.IntervalMinutes);
        }
        else
        {
            StopAutoRefreshTimer();
        }
    }

    /// <summary>
    /// 启动自动采集定时器
    /// </summary>
    private void StartAutoRefreshTimer(int intervalMinutes)
    {
        StopAutoRefreshTimer();
        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(intervalMinutes)
        };
        _autoRefreshTimer.Tick += async (s, e) =>
        {
            // 如果正在执行FTP采集或历史分析，跳过本次自动采集
            if (!IsFtpBusy && !IsBusy)
            {
                await ImportFromFtpCore(isAutoRefresh: true);
            }
        };
        _autoRefreshTimer.Start();
    }

    /// <summary>
    /// 停止自动采集定时器
    /// </summary>
    private void StopAutoRefreshTimer()
    {
        if (_autoRefreshTimer != null)
        {
            _autoRefreshTimer.Stop();
            _autoRefreshTimer = null;
        }
    }

    #endregion

    [RelayCommand]
    public void ExportExcel()
    {
        if (!HasData)
        {
            MessageBox.Show("没有可导出的数据", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Excel文件|*.xlsx",
            DefaultExt = ".xlsx",
            FileName = $"报警分析报告_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            Title = "导出分析报告"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            StatusText = "正在导出...";
            _exportService.ExportFullReport(
                dialog.FileName,
                Records.ToList(),
                FrequencyData.ToList(),
                CategoryData.ToList(),
                HourlyData.ToList(),
                DurationData.ToList(),
                SourceData.ToList(),
                UnrecoveredData.ToList(),
                Suggestions.ToList(),
                Summary!);

            StatusText = $"导出完成: {dialog.FileName}";
            MessageBox.Show($"导出成功!\n文件路径: {dialog.FileName}", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = $"导出失败: {ex.Message}";
            MessageBox.Show($"导出失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void ExportCsv()
    {
        if (!HasData)
        {
            MessageBox.Show("没有可导出的数据", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV文件|*.csv",
            DefaultExt = ".csv",
            FileName = $"报警数据_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            Title = "导出原始数据"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            _exportService.ExportRawDataCsv(dialog.FileName, FilteredRecords.ToList());
            StatusText = $"导出完成: {dialog.FileName}";
            MessageBox.Show($"导出成功!\n文件路径: {dialog.FileName}", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = $"导出失败: {ex.Message}";
            MessageBox.Show($"导出失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task ExportPdf()
    {
        if (!HasData || Summary == null)
        {
            MessageBox.Show("没有可导出的数据，请先导入并分析数据", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PDF文件|*.pdf",
            DefaultExt = ".pdf",
            FileName = $"报警分析报告_{DateTime.Now:yyyyMMdd_HHmmss}.pdf",
            Title = "导出PDF分析报告"
        };

        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        ShowProgress("正在生成PDF报告...");
        try
        {
            StatusText = "正在生成PDF报告...";
            var reportData = BuildReportData();
            await Task.Run(() => _reportExportService.ExportPdf(dialog.FileName, reportData));

            StatusText = $"PDF导出完成: {dialog.FileName}";
            MessageBox.Show($"PDF报告导出成功!\n文件路径: {dialog.FileName}", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = $"导出失败: {ex.Message}";
            MessageBox.Show($"PDF导出失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CloseProgress();
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ExportWord()
    {
        if (!HasData || Summary == null)
        {
            MessageBox.Show("没有可导出的数据，请先导入并分析数据", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Word文档|*.docx",
            DefaultExt = ".docx",
            FileName = $"报警分析报告_{DateTime.Now:yyyyMMdd_HHmmss}.docx",
            Title = "导出Word分析报告"
        };

        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        ShowProgress("正在生成Word报告...");
        try
        {
            StatusText = "正在生成Word报告...";
            var reportData = BuildReportData();
            await Task.Run(() => _reportExportService.ExportWord(dialog.FileName, reportData));

            StatusText = $"Word导出完成: {dialog.FileName}";
            MessageBox.Show($"Word报告导出成功!\n文件路径: {dialog.FileName}", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = $"导出失败: {ex.Message}";
            MessageBox.Show($"Word导出失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CloseProgress();
            IsBusy = false;
        }
    }

    /// <summary>
    /// 构建报告导出所需的数据对象
    /// </summary>
    private ReportData BuildReportData()
    {
        return new ReportData
        {
            FileInfo = FileInfo,
            Summary = Summary,
            Frequency = FrequencyData.ToList(),
            Category = CategoryData.ToList(),
            Hourly = HourlyData.ToList(),
            Daily = DailyData.ToList(),
            Duration = DurationData.ToList(),
            Source = SourceData.ToList(),
            Unrecovered = UnrecoveredData.ToList(),
            Correlation = CorrelationData.ToList(),
            Suggestions = Suggestions.ToList(),
            FrequencyChart = FrequencyChart,
            CategoryChart = CategoryChart,
            HourlyChart = HourlyChart,
            DailyChart = DailyChart,
            SourceChart = SourceChart
        };
    }

    [RelayCommand]
    public async Task Analyze()
    {
        if (!HasData) return;

        IsBusy = true;
        ShowProgress("正在分析报警数据...");
        var progress = CreateProgress();
        try
        {
            StatusText = "正在分析...";
            await AnalyzeCoreAsync(progress);
            var filterMsg = Summary?.FilteredInfoCount > 0
                ? $" (已过滤 {Summary.FilteredInfoCount} 条提示类)"
                : "";
            StatusText = $"分析完成 - {FrequencyData.Count} 种报警类型, {Suggestions.Count} 条诊断建议{filterMsg}";
        }
        finally
        {
            CloseProgress();
            IsBusy = false;
        }
    }

    /// <summary>
    /// 分析核心逻辑（不含IsBusy/状态文本管理），可被导入流程直接调用
    /// </summary>
    private async Task AnalyzeCoreAsync(IProgress<string>? progress = null)
    {
        var records = Records.ToList();

        // 全部分析在后台线程一次性完成（原实现分8次调用+GetSummary+GenerateDiagnosticSuggestions重复计算）
        var result = await Task.Run(() => _analysisService.AnalyzeAll(records, progress));

        // 更新分析结果与图表，并重建筛选项
        UpdateAnalysisFromResult(result, rebuildFilters: true);

        // 刷新原始数据表格（默认筛选条件 = 全选）
        RefreshFilteredRecords();
    }

    /// <summary>
    /// 将分析结果应用到UI数据（统计表 + 图表），可选是否重建筛选项
    /// </summary>
    private void UpdateAnalysisFromResult(AnalysisResult result, bool rebuildFilters)
    {
        // 更新UI数据
        FrequencyData = new ObservableCollection<AlarmFrequencyItem>(result.Frequency);
        CategoryData = new ObservableCollection<CategoryDistributionItem>(result.Category);
        HourlyData = new ObservableCollection<HourlyDistributionItem>(result.Hourly);
        DailyData = new ObservableCollection<DailyDistributionItem>(result.Daily);
        DailyChartHeight = ComputeDailyChartHeight(result.Daily.Count);
        DurationData = new ObservableCollection<DurationAnalysisItem>(result.Duration);
        SourceData = new ObservableCollection<SourceAnalysisItem>(result.Source);
        UnrecoveredData = new ObservableCollection<AlarmRecord>(result.Unrecovered);
        CorrelationData = new ObservableCollection<AlarmCorrelationItem>(result.Correlation);
        Suggestions = new ObservableCollection<DiagnosticSuggestion>(result.Suggestions);
        Summary = result.Summary;

        // 生成图表
        FrequencyChart = CreateFrequencyChart(result.Frequency.Take(15).ToList());
        CategoryChart = CreateCategoryChart(result.Category);
        HourlyChart = CreateHourlyChart(result.Hourly);
        DailyChart = CreateDailyChart(result.Daily);
        SourceChart = CreateSourceChart(result.Source);

        // 更新筛选项（默认全选）；筛选后重新分析时不重建，以保留用户的多选状态
        if (rebuildFilters)
        {
            AvailableCategories = new ObservableCollection<FilterCheckItem>(
                result.Category.Select(c => new FilterCheckItem(c.CategoryName, true)));
            AvailableSources = new ObservableCollection<FilterCheckItem>(
                result.Source.Select(s => new FilterCheckItem(s.Source, true)));
        }
    }

    /// <summary>
    /// 根据当前筛选条件计算筛选后的记录（纯筛选，不触发重新分析）
    /// </summary>
    private IEnumerable<AlarmRecord> GetFilteredRecords()
    {
        var query = Records.AsEnumerable();

        // 关键字筛选
        if (!string.IsNullOrWhiteSpace(FilterKeyword))
        {
            var kw = FilterKeyword.Trim();
            query = query.Where(r =>
                r.Description.Contains(kw) ||
                r.AlarmCode.Contains(kw) ||
                r.RawMessage.Contains(kw));
        }

        // 时间段筛选
        if (FilterStartDate.HasValue)
            query = query.Where(r => r.TriggerDateTime >= FilterStartDate.Value);
        if (FilterEndDate.HasValue)
            query = query.Where(r => r.TriggerDateTime < FilterEndDate.Value.AddDays(1));

        // 报警类别多选筛选（部分选中时才过滤，全选不过滤）
        var selectedCategories = AvailableCategories.Where(c => c.IsSelected).Select(c => c.Name).ToHashSet();
        if (selectedCategories.Count > 0 && selectedCategories.Count < AvailableCategories.Count)
            query = query.Where(r => selectedCategories.Contains(r.CategoryName));

        // 设备来源多选筛选（部分选中时才过滤，全选不过滤）
        var selectedSources = AvailableSources.Where(s => s.IsSelected).Select(s => s.Name).ToHashSet();
        if (selectedSources.Count > 0 && selectedSources.Count < AvailableSources.Count)
            query = query.Where(r => selectedSources.Contains(r.SourceMain));

        return query;
    }

    /// <summary>
    /// 根据当前筛选条件仅刷新原始数据表格（不重新分析）
    /// </summary>
    private void RefreshFilteredRecords()
    {
        FilteredRecords = new ObservableCollection<AlarmRecord>(GetFilteredRecords().ToList());
    }

    [RelayCommand]
    public void ApplyFilter() => _ = ApplyFilterAsync();

    /// <summary>
    /// 点击筛选后：先按条件过滤原始数据，再基于筛选后的记录重新分析（图表 + 概览）
    /// rebuildFilters=false 以保留用户的多选状态
    /// </summary>
    private async Task ApplyFilterAsync()
    {
        if (IsBusy) return;

        // 1. 先在前台快速过滤原始数据并更新表格
        var filtered = GetFilteredRecords().ToList();
        FilteredRecords = new ObservableCollection<AlarmRecord>(filtered);

        // 2. 在后台线程基于筛选后的记录重新分析（不弹模态窗，避免影响实时采集区）
        IsBusy = true;
        StatusText = "正在按筛选条件重新分析报警数据...";
        try
        {
            var result = await Task.Run(() => _analysisService.AnalyzeAll(filtered));
            UpdateAnalysisFromResult(result, rebuildFilters: false);
            StatusText = $"筛选分析完成 - 共 {filtered.Count} 条记录, {FrequencyData.Count} 种报警类型";
        }
        catch (Exception ex)
        {
            StatusText = $"筛选分析失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void ClearFilter()
    {
        FilterKeyword = "";
        FilterStartDate = null;
        FilterEndDate = null;
        foreach (var c in AvailableCategories) c.IsSelected = true;
        foreach (var s in AvailableSources) s.IsSelected = true;
        ApplyFilter();
    }

    /// <summary>
    /// 切换报警类别全选/全不选
    /// </summary>
    [RelayCommand]
    public void ToggleAllCategories()
    {
        var allSelected = AvailableCategories.All(c => c.IsSelected);
        foreach (var c in AvailableCategories) c.IsSelected = !allSelected;
    }

    /// <summary>
    /// 切换设备来源全选/全不选
    /// </summary>
    [RelayCommand]
    public void ToggleAllSources()
    {
        var allSelected = AvailableSources.All(s => s.IsSelected);
        foreach (var s in AvailableSources) s.IsSelected = !allSelected;
    }

    private async Task LoadRecordsAsync(List<AlarmRecord> records, string filePath, IProgress<string>? progress = null, List<string>? importedFiles = null)
    {
        Records = new ObservableCollection<AlarmRecord>(records);
        FilteredRecords = new ObservableCollection<AlarmRecord>(records);
        HasData = records.Count > 0;

        if (HasData)
        {
            // 过滤掉日期无效的记录，避免MinValue污染日期范围显示
            var validDates = records.Where(r => r.TriggerDateTime > DateTime.MinValue).ToList();
            var dateSource = validDates.Count > 0 ? validDates : records;
            var minDate = dateSource.Min(r => r.TriggerDateTime);
            var maxDate = dateSource.Max(r => r.TriggerDateTime);
            FileInfo = new ImportFileInfo
            {
                FileName = Path.GetFileName(filePath),
                FilePath = filePath,
                RecordCount = records.Count,
                ImportTime = DateTime.Now,
                DateRange = $"{minDate:yyyy-MM-dd HH:mm} ~ {maxDate:yyyy-MM-dd HH:mm}",
                ImportedFiles = importedFiles ?? new List<string> { filePath }
            };
            await AnalyzeCoreAsync(progress);
        }
    }

    #region 图表创建

    // —— 图表美化配色 ——
    private static readonly OxyColor TitleColor = OxyColor.FromRgb(0x2C, 0x3E, 0x50);
    private static readonly OxyColor AxisTextColor = OxyColor.FromRgb(0x59, 0x6A, 0x7A);
    private static readonly OxyColor GridColor = OxyColor.FromRgb(0xE6, 0xEA, 0xF0);
    private static readonly OxyColor PlotBg = OxyColor.FromRgb(0xFB, 0xFC, 0xFE);
    // 蓝色渐变（频次 / 时间 / 趋势）
    private static readonly OxyColor BarBlueStart = OxyColor.FromRgb(0x6F, 0xA8, 0xDC);
    private static readonly OxyColor BarBlueEnd = OxyColor.FromRgb(0x2F, 0x55, 0x97);
    // 橙色渐变（设备来源）
    private static readonly OxyColor BarOrangeStart = OxyColor.FromRgb(0xF6, 0xB2, 0x6B);
    private static readonly OxyColor BarOrangeEnd = OxyColor.FromRgb(0xC6, 0x5A, 0x12);
    // 饼图调色板（清新柔和）
    private static readonly OxyColor[] PiePalette =
    {
        OxyColor.FromRgb(0x4E, 0x79, 0xA7),
        OxyColor.FromRgb(0xF2, 0x8C, 0x5B),
        OxyColor.FromRgb(0x59, 0xA1, 0x4B),
        OxyColor.FromRgb(0xE1, 0x57, 0x59),
        OxyColor.FromRgb(0xB0, 0x7A, 0xA3),
        OxyColor.FromRgb(0xED, 0xC9, 0x4B),
        OxyColor.FromRgb(0x76, 0xB7, 0xB2),
        OxyColor.FromRgb(0x9C, 0x6B, 0x4F)
    };

    /// <summary>
    /// 在两种颜色间按索引线性插值，生成从浅到深的渐变序列
    /// （OxyPlot 2.2.0 无 Interpolate 静态方法，手动实现）
    /// </summary>
    private static OxyColor GradientColor(OxyColor a, OxyColor b, int index, int count)
    {
        if (count <= 1) return b;
        double t = (double)index / (count - 1);
        byte r = (byte)(a.R + (b.R - a.R) * t);
        byte g = (byte)(a.G + (b.G - a.G) * t);
        byte bl = (byte)(a.B + (b.B - a.B) * t);
        return OxyColor.FromRgb(r, g, bl);
    }

    /// <summary>
    /// 统一设置 PlotModel 的字体和渲染属性，确保高 DPI 下清晰且美观
    /// </summary>
    private static void ApplyChartStyle(PlotModel model, string title)
    {
        model.Title = title;
        model.TitleFontSize = 15;
        model.TitleFontWeight = OxyPlot.FontWeights.Bold;
        model.TitleColor = TitleColor;
        model.DefaultFont = "Microsoft YaHei UI";
        model.DefaultFontSize = 12;
        model.Background = OxyColors.White;
        model.PlotAreaBackground = PlotBg;
        model.Padding = new OxyThickness(10);
        model.PlotAreaBorderThickness = new OxyThickness(0);
        model.PlotAreaBorderColor = GridColor;
    }

    /// <summary>
    /// 统一设置数值轴样式（淡色网格 + 无刻度线，干净现代）
    /// </summary>
    private static void StyleValueAxis(LinearAxis axis, string title)
    {
        axis.Position = AxisPosition.Bottom;
        axis.Title = title;
        axis.FontSize = 12;
        axis.TitleFontSize = 13;
        axis.TextColor = AxisTextColor;
        axis.TitleColor = AxisTextColor;
        axis.MajorGridlineStyle = LineStyle.Solid;
        axis.MajorGridlineColor = GridColor;
        axis.MajorGridlineThickness = 1;
        axis.MinorGridlineStyle = LineStyle.None;
        axis.AxislineColor = AxisTextColor;
        axis.AxislineStyle = LineStyle.Solid;
        axis.AxislineThickness = 1;
        axis.TickStyle = TickStyle.None;
        axis.Font = "Microsoft YaHei UI";
        axis.IsZoomEnabled = false;
        axis.IsPanEnabled = false;
        // 收紧轴范围：最小值从 0 起无额外填充；最大值仅留 6% 呼吸空间
        // 避免 OxyPlot 默认的大 padding + 整刻度对齐导致右侧大片空白
        axis.MinimumPadding = 0;
        axis.MaximumPadding = 0.06;
    }

    /// <summary>
    /// 统一设置类别轴样式
    /// </summary>
    private static void StyleCategoryAxis(CategoryAxis axis, string title)
    {
        axis.Position = AxisPosition.Left;
        axis.Title = title;
        axis.FontSize = 12;
        axis.TitleFontSize = 13;
        axis.TextColor = AxisTextColor;
        axis.TitleColor = AxisTextColor;
        axis.GapWidth = 0.25;
        axis.TickStyle = TickStyle.None;
        axis.Font = "Microsoft YaHei UI";
        axis.AxisTitleDistance = 8;
        axis.IsZoomEnabled = false;
        axis.IsPanEnabled = false;
    }

    private PlotModel CreateFrequencyChart(List<AlarmFrequencyItem> data)
    {
        var model = new PlotModel();
        ApplyChartStyle(model, "报警频次排行 (Top 15)");
        model.PlotMargins = new OxyThickness(220, 10, 20, 40);

        var series = new BarSeries
        {
            LabelPlacement = LabelPlacement.Inside,
            LabelFormatString = "{0}",
            TextColor = OxyColors.White,
            FillColor = BarBlueEnd,
            StrokeColor = OxyColors.White,
            StrokeThickness = 1.5,
            FontSize = 12,
            Font = "Microsoft YaHei UI"
        };

        // 倒序添加（从下到上显示排名1到N）；排名越高(原index越小)颜色越深
        int fn = data.Count;
        for (int i = data.Count - 1; i >= 0; i--)
        {
            var item = data[i];
            // 原index越小(排名越高)颜色越深：用 BarBlueEnd→BarBlueStart 反向插值
            series.Items.Add(new BarItem { Value = item.Count, Color = GradientColor(BarBlueEnd, BarBlueStart, i, fn)
            });
        }

        model.Series.Add(series);

        var categoryAxis = new CategoryAxis();
        StyleCategoryAxis(categoryAxis, "");
        foreach (var item in data)
        {
            categoryAxis.Labels.Add(TruncateLabel($"{item.AlarmCode}-{item.Description}", 25));
        }
        // 倒序标签
        categoryAxis.Labels.Reverse();

        var valueAxis = new LinearAxis();
        StyleValueAxis(valueAxis, "次数");

        model.Axes.Add(categoryAxis);
        model.Axes.Add(valueAxis);

        return model;
    }

    private PlotModel CreateCategoryChart(List<CategoryDistributionItem> data)
    {
        var model = new PlotModel();
        ApplyChartStyle(model, "报警类别分布");
        model.IsLegendVisible = true;
        model.Legends.Add(new Legend
        {
            LegendPosition = LegendPosition.RightMiddle,
            LegendPlacement = LegendPlacement.Outside,
            LegendBorderThickness = 0
        });
        model.Padding = new OxyThickness(10, 10, 6, 10);

        var series = new PieSeries
        {
            InnerDiameter = 0.45,
            LegendFormat = "{0}: {1} ({2:0.0}%)",
            OutsideLabelFormat = "{2:0.0}%",
            InsideLabelFormat = "{0}",
            TickDistance = 2,
            FontSize = 13,
            Font = "Microsoft YaHei UI",
            TextColor = OxyColors.White,
            Stroke = OxyColors.White,
            StrokeThickness = 2,
            ExplodedDistance = 0.06,
            AngleSpan = 360,
            StartAngle = 0
        };

        // 找出最大占比，用于"爆开"突出
        int maxIdx = -1;
        double maxVal = -1;
        for (int i = 0; i < data.Count; i++)
            if (data[i].Count > maxVal) { maxVal = data[i].Count; maxIdx = i; }

        for (int i = 0; i < data.Count; i++)
        {
            series.Slices.Add(new PieSlice(data[i].CategoryName, data[i].Count)
            {
                Fill = PiePalette[i % PiePalette.Length],
                IsExploded = i == maxIdx
            });
        }

        model.Series.Add(series);
        return model;
    }

    private PlotModel CreateHourlyChart(List<HourlyDistributionItem> data)
    {
        var model = new PlotModel();
        ApplyChartStyle(model, "报警时间分布 (24小时)");
        model.PlotMargins = new OxyThickness(60, 10, 30, 40);

        var series = new BarSeries
        {
            FillColor = BarBlueEnd,
            StrokeColor = OxyColors.White,
            StrokeThickness = 1.5,
            LabelPlacement = LabelPlacement.Outside,
            LabelFormatString = "{0}",
            FontSize = 11,
            Font = "Microsoft YaHei UI"
        };

        // 倒序添加以匹配类别轴；按时间顺序做由浅到深渐变
        int hn = data.Count;
        for (int i = data.Count - 1; i >= 0; i--)
        {
            int rank = data.Count - 1 - i; // 0=最早时段(浅) → 最深
            series.Items.Add(new BarItem { Value = data[i].Count, Color = GradientColor(BarBlueStart, BarBlueEnd, rank, hn)
            });
        }

        model.Series.Add(series);

        var categoryAxis = new CategoryAxis();
        StyleCategoryAxis(categoryAxis, "时段");
        categoryAxis.GapWidth = 0.15;
        foreach (var item in data)
        {
            categoryAxis.Labels.Add($"{item.Hour:00}时");
        }
        categoryAxis.Labels.Reverse();

        var valueAxis = new LinearAxis();
        StyleValueAxis(valueAxis, "报警次数");

        model.Axes.Add(categoryAxis);
        model.Axes.Add(valueAxis);

        return model;
    }

    private PlotModel CreateDailyChart(List<DailyDistributionItem> data)
    {
        var model = new PlotModel();
        var title = data.Count > 1
            ? $"按日报警趋势 ({data.Count}天)"
            : "按日报警趋势";
        ApplyChartStyle(model, title);
        // left=110 给"07-01 周二"日期标签留足空间; right=80 给柱子外侧数字标签(最大4位数)留足空间
        model.PlotMargins = new OxyThickness(110, 10, 30, 50);

        var barSeries = new BarSeries
        {
            Title = "报警次数",
            FillColor = BarBlueEnd,
            StrokeColor = OxyColors.White,
            StrokeThickness = 1.5,
            LabelPlacement = LabelPlacement.Outside,
            LabelFormatString = "{0}",
            FontSize = 11,
            Font = "Microsoft YaHei UI"
        };

        // 倒序添加以匹配类别轴（从下到上显示日期从早到晚）；按日期顺序由浅到深渐变
        int dn = data.Count;
        for (int i = data.Count - 1; i >= 0; i--)
        {
            int rank = data.Count - 1 - i;
            barSeries.Items.Add(new BarItem { Value = data[i].Count, Color = GradientColor(BarBlueStart, BarBlueEnd, rank, dn)
            });
        }

        model.Series.Add(barSeries);

        var categoryAxis = new CategoryAxis();
        StyleCategoryAxis(categoryAxis, "日期");
        categoryAxis.GapWidth = 0.15;
        foreach (var item in data)
        {
            categoryAxis.Labels.Add($"{item.DateText} {item.DayOfWeek}");
        }
        categoryAxis.Labels.Reverse();

        var valueAxis = new LinearAxis();
        StyleValueAxis(valueAxis, "报警次数");

        model.Axes.Add(categoryAxis);
        model.Axes.Add(valueAxis);

        return model;
    }

    private PlotModel CreateSourceChart(List<SourceAnalysisItem> data)
    {
        var model = new PlotModel();
        ApplyChartStyle(model, "设备来源分布");
        model.PlotMargins = new OxyThickness(80, 10, 30, 40);

        var series = new BarSeries
        {
            FillColor = BarOrangeEnd,
            StrokeColor = OxyColors.White,
            StrokeThickness = 1.5,
            LabelPlacement = LabelPlacement.Outside,
            LabelFormatString = "{0}",
            FontSize = 12,
            Font = "Microsoft YaHei UI"
        };

        // 倒序添加；按数量排序后的顺序由浅到深渐变
        int sn = data.Count;
        for (int i = data.Count - 1; i >= 0; i--)
        {
            int rank = data.Count - 1 - i;
            series.Items.Add(new BarItem { Value = data[i].Count, Color = GradientColor(BarOrangeStart, BarOrangeEnd, rank, sn)
            });
        }

        model.Series.Add(series);

        var categoryAxis = new CategoryAxis();
        StyleCategoryAxis(categoryAxis, "设备");
        categoryAxis.GapWidth = 0.3;
        foreach (var item in data)
        {
            categoryAxis.Labels.Add(item.Source);
        }
        categoryAxis.Labels.Reverse();

        var valueAxis = new LinearAxis();
        StyleValueAxis(valueAxis, "报警次数");

        model.Axes.Add(categoryAxis);
        model.Axes.Add(valueAxis);

        return model;
    }

    private string TruncateLabel(string label, int maxLen)
    {
        return label.Length > maxLen ? label.Substring(0, maxLen) + "..." : label;
    }

    #endregion
}
