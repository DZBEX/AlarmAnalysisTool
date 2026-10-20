using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AlarmAnalysisTool.Models;

/// <summary>
/// 实时报警表（对应一个FTP服务器的报警数据快照）
/// 每个FTP服务器对应一个实时报警表，表标题为"{服务器名称}实时报警"
/// </summary>
public partial class RealtimeAlarmTable : ObservableObject
{
    /// <summary>FTP服务器配置ID（用于关联配置）</summary>
    public string ConfigId { get; set; } = "";

    /// <summary>服务器名称（用户自定义的FTP配置名称）</summary>
    [ObservableProperty]
    private string _serverName = "";

    /// <summary>FTP主机地址</summary>
    public string FtpHost { get; set; } = "";

    /// <summary>报警记录列表（按时间倒序，最新在前）</summary>
    [ObservableProperty]
    private ObservableCollection<AlarmRecord> _records = new();

    /// <summary>最后采集时间</summary>
    [ObservableProperty]
    private DateTime _lastSyncTime;

    /// <summary>最后采集记录数</summary>
    [ObservableProperty]
    private int _recordCount;

    /// <summary>是否在线（最近一次采集是否成功）</summary>
    [ObservableProperty]
    private bool _isOnline = true;

    /// <summary>最后采集状态信息</summary>
    [ObservableProperty]
    private string _statusText = "";

    /// <summary>新增报警数（与上次采集相比）</summary>
    [ObservableProperty]
    private int _newAlarmCount;

    /// <summary>是否有新增报警（用于UI可见性绑定）</summary>
    public bool HasNewAlarms => NewAlarmCount > 0;

    /// <summary>
    /// 表标题："{服务器名称}实时报警"
    /// </summary>
    public string TableTitle => $"{ServerName}实时报警";

    /// <summary>
    /// 最后采集时间显示文本
    /// </summary>
    public string LastSyncText => LastSyncTime == DateTime.MinValue
        ? "未采集"
        : LastSyncTime.ToString("MM-dd HH:mm:ss");

    /// <summary>
    /// 在线状态指示色（绿色=在线, 红色=离线）
    /// </summary>
    public string StatusColor => IsOnline ? "#27AE60" : "#E74C3C";

    /// <summary>
    /// 状态摘要文本
    /// </summary>
    public string StatusSummary
    {
        get
        {
            var parts = new List<string>();
            parts.Add($"{RecordCount} 条报警");
            if (NewAlarmCount > 0)
                parts.Add($"新增 {NewAlarmCount}");
            if (LastSyncTime != DateTime.MinValue)
                parts.Add($"更新: {LastSyncText}");
            return string.Join(" | ", parts);
        }
    }

    /// <summary>
    /// 更新记录并刷新相关属性
    /// </summary>
    public void UpdateRecords(List<AlarmRecord> records, bool isOnline, string statusText)
    {
        var previousCount = RecordCount;

        // 只保留当天未恢复的真实报警（过滤提示类 + 已恢复 + 非当天），按时间倒序排列
        var today = DateTime.Today;
        var alarmRecords = records
            .Where(r => !r.IsInfoType)
            .Where(r => !r.IsRecovered)
            .Where(r => r.TriggerDateTime.Date >= today)
            .OrderByDescending(r => r.TriggerDateTime)
            .ToList();

        // 限制最多1000条，避免UI性能问题
        if (alarmRecords.Count > 1000)
            alarmRecords = alarmRecords.Take(1000).ToList();

        Records = new ObservableCollection<AlarmRecord>(alarmRecords);
        RecordCount = alarmRecords.Count;
        IsOnline = isOnline;
        StatusText = statusText;
        LastSyncTime = DateTime.Now;

        // 计算新增报警数（仅当本次采集记录数大于上次时）
        NewAlarmCount = alarmRecords.Count > previousCount
            ? alarmRecords.Count - previousCount
            : 0;

        // 触发派生属性通知
        OnPropertyChanged(nameof(LastSyncText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(StatusSummary));
        OnPropertyChanged(nameof(TableTitle));
        OnPropertyChanged(nameof(HasNewAlarms));
    }

    /// <summary>
    /// 初始化时设置服务器信息
    /// </summary>
    public void Initialize(string configId, string serverName, string ftpHost)
    {
        ConfigId = configId;
        ServerName = serverName;
        FtpHost = ftpHost;
        OnPropertyChanged(nameof(TableTitle));
    }

    /// <summary>
    /// 刷新派生属性通知（供外部调用）
    /// </summary>
    public void RefreshDerivedProperties()
    {
        OnPropertyChanged(nameof(LastSyncText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(StatusSummary));
        OnPropertyChanged(nameof(TableTitle));
        OnPropertyChanged(nameof(HasNewAlarms));
    }
}
