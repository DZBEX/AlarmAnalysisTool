using System.ComponentModel;
using System.IO;

namespace AlarmAnalysisTool.Models;

/// <summary>
/// 单条报警记录（对应CSV/Excel中的一行）
/// </summary>
public class AlarmRecord : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public int Event { get; set; }               // 事件类型: 0=提示, 2=报警
    public int Category { get; set; }            // 类别: 6=报警, 20=提示
    public string TriggerDate { get; set; } = "";
    public string TriggerTime { get; set; } = "";
    public string RecoveryDate { get; set; } = "";
    public string RecoveryTime { get; set; } = "";
    public int Duration { get; set; }            // 持续时间(秒)
    public string RawMessage { get; set; } = "";  // 原始信息字段

    // 解析后的字段
    public string AlarmCode { get; set; } = "";       // 报警码, 如 "3062"
    public string Description { get; set; } = "";     // 报警描述, 如 "陀螺仪倾角超限"
    public string CategoryDetail { get; set; } = "";  // 类别详情, 如 "Cat.06 / [10_设备故障类]"
    public string CategoryName { get; set; } = "";    // 类别名称, 如 "10_设备故障类"
    public string Source { get; set; } = "";          // 设备来源, 如 "EM03:EM03"
    public string SourceMain { get; set; } = "";      // 主设备, 如 "EM03"

    public int OccurrenceCount { get; set; }    // 发生次数
    public long CumulativeTime { get; set; }     // 累计时间(秒)

    // 计算字段
    public DateTime TriggerDateTime { get; set; }
    public DateTime? RecoveryDateTime { get; set; }
    public bool IsRecovered => RecoveryDateTime.HasValue;
    public string EventType => Event switch
    {
        0 => "提示",
        2 => "报警",
        _ => $"未知({Event})"
    };
    public string DurationText => Duration > 0 ? FormatDuration(Duration) : "-";

    /// <summary>
    /// 是否为提示类报警（CategoryName含"提示类"的记录不参与分析）
    /// 威纶通HMI中 Cat=10/Cat=20 对应 [89_提示类]，如"等待复位操作"、"来料拉带未提升到位"等操作提示
    /// </summary>
    public bool IsInfoType => CategoryName.Contains("提示类");

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
    }

    public string TriggerDateTimeText => TriggerDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    public string RecoveryDateTimeText => RecoveryDateTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
    public string RecoveredText => IsRecovered ? "是" : "否";

    public static string FormatDuration(int seconds)
    {
        if (seconds < 60) return $"{seconds}秒";
        if (seconds < 3600) return $"{seconds / 60}分{seconds % 60}秒";
        return $"{seconds / 3600}时{(seconds % 3600) / 60}分{seconds % 60}秒";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 报警频次统计项
/// </summary>
public class AlarmFrequencyItem
{
    public int Rank { get; set; }
    public string AlarmCode { get; set; } = "";
    public string Description { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public string Source { get; set; } = "";
    public int Count { get; set; }
    public int TotalDuration { get; set; }
    public string TotalDurationText => AlarmRecord.FormatDuration(TotalDuration);
    public double Percentage { get; set; }
}

/// <summary>
/// 类别分布统计项
/// </summary>
public class CategoryDistributionItem
{
    public string CategoryName { get; set; } = "";
    public int Count { get; set; }
    public double Percentage { get; set; }
}

/// <summary>
/// 时间分布统计项
/// </summary>
public class HourlyDistributionItem
{
    public int Hour { get; set; }
    public string HourText => $"{Hour:00}:00 - {Hour:00}:59";
    public int Count { get; set; }
}

/// <summary>
/// 按日分布统计项（多日趋势分析）
/// </summary>
public class DailyDistributionItem
{
    public DateTime Date { get; set; }
    public string DateText => Date.ToString("MM-dd");
    public string DateFullText => Date.ToString("yyyy-MM-dd");
    public int Count { get; set; }
    public int UniqueAlarmTypes { get; set; }
    public int UnrecoveredCount { get; set; }
    public string DayOfWeek => Date.DayOfWeek switch
    {
        System.DayOfWeek.Monday => "周一",
        System.DayOfWeek.Tuesday => "周二",
        System.DayOfWeek.Wednesday => "周三",
        System.DayOfWeek.Thursday => "周四",
        System.DayOfWeek.Friday => "周五",
        System.DayOfWeek.Saturday => "周六",
        System.DayOfWeek.Sunday => "周日",
        _ => ""
    };
}

/// <summary>
/// 持续时间分析项
/// </summary>
public class DurationAnalysisItem
{
    public string AlarmCode { get; set; } = "";
    public string Description { get; set; } = "";
    public int Occurrences { get; set; }
    public int MaxDuration { get; set; }
    public int AvgDuration { get; set; }
    public int TotalDuration { get; set; }
    public string MaxDurationText => AlarmRecord.FormatDuration(MaxDuration);
    public string AvgDurationText => AlarmRecord.FormatDuration(AvgDuration);
    public string TotalDurationText => AlarmRecord.FormatDuration(TotalDuration);
}

/// <summary>
/// 设备来源统计项
/// </summary>
public class SourceAnalysisItem
{
    public string Source { get; set; } = "";
    public int Count { get; set; }
    public double Percentage { get; set; }
    public int UniqueAlarmTypes { get; set; }
}

/// <summary>
/// 智能诊断建议
/// </summary>
public class DiagnosticSuggestion
{
    public SuggestionLevel Level { get; set; }
    public string LevelText => Level switch
    {
        SuggestionLevel.Critical => "严重",
        SuggestionLevel.Warning => "警告",
        SuggestionLevel.Info => "提示",
        _ => ""
    };
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Detail { get; set; } = "";
}

public enum SuggestionLevel
{
    Info,
    Warning,
    Critical
}

/// <summary>
/// 报警关联分析项
/// </summary>
public class AlarmCorrelationItem
{
    public string Alarm1 { get; set; } = "";
    public string Alarm2 { get; set; } = "";
    public int CoOccurrenceCount { get; set; }
    public double Confidence { get; set; }  // P(Alarm2 | Alarm1)
}

/// <summary>
/// 列映射配置
/// </summary>
public class ColumnMapping
{
    public string Event { get; set; } = "";
    public string Category { get; set; } = "";
    public string TriggerDate { get; set; } = "";
    public string TriggerTime { get; set; } = "";
    public string RecoveryDate { get; set; } = "";
    public string RecoveryTime { get; set; } = "";
    public string Duration { get; set; } = "";
    public string Message { get; set; } = "";
    public string OccurrenceCount { get; set; } = "";
    public string CumulativeTime { get; set; } = "";

    /// <summary>
    /// 所有标准字段名及其显示名
    /// </summary>
    public static readonly Dictionary<string, string> StandardFields = new()
    {
        { "Event", "事件" },
        { "Category", "类别" },
        { "TriggerDate", "触发日期" },
        { "TriggerTime", "触发时间" },
        { "RecoveryDate", "恢复日期" },
        { "RecoveryTime", "恢复时间" },
        { "Duration", "持续时间(秒)" },
        { "Message", "信息" },
        { "OccurrenceCount", "发生次数" },
        { "CumulativeTime", "累计时间" }
    };

    /// <summary>
    /// 根据源列名自动猜测映射
    /// </summary>
    public static string? AutoDetectField(string sourceColumn)
    {
        var lower = sourceColumn.ToLowerInvariant().Trim();
        if (lower.Contains("事件") || lower.Contains("event")) return "Event";
        if (lower.Contains("类别") || lower.Contains("category") || lower.Contains("cat")) return "Category";
        if (lower.Contains("触发日期") || (lower.Contains("trigger") && lower.Contains("date"))) return "TriggerDate";
        if (lower.Contains("触发时间") || (lower.Contains("trigger") && lower.Contains("time"))) return "TriggerTime";
        if (lower.Contains("恢复日期") || (lower.Contains("recovery") && lower.Contains("date"))) return "RecoveryDate";
        if (lower.Contains("恢复时间") || (lower.Contains("recovery") && lower.Contains("time"))) return "RecoveryTime";
        if (lower.Contains("持续") || lower.Contains("duration")) return "Duration";
        if (lower.Contains("信息") || lower.Contains("message") || lower.Contains("info")) return "Message";
        if (lower.Contains("发生次数") || lower.Contains("occurrence") || lower.Contains("count")) return "OccurrenceCount";
        if (lower.Contains("累计") || lower.Contains("cumulative")) return "CumulativeTime";
        return null;
    }

    public string GetColumn(string fieldName) => fieldName switch
    {
        "Event" => Event,
        "Category" => Category,
        "TriggerDate" => TriggerDate,
        "TriggerTime" => TriggerTime,
        "RecoveryDate" => RecoveryDate,
        "RecoveryTime" => RecoveryTime,
        "Duration" => Duration,
        "Message" => Message,
        "OccurrenceCount" => OccurrenceCount,
        "CumulativeTime" => CumulativeTime,
        _ => ""
    };
}

/// <summary>
/// 导入文件信息
/// </summary>
public class ImportFileInfo
{
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int RecordCount { get; set; }
    public DateTime ImportTime { get; set; }
    public string DateRange { get; set; } = "";

    /// <summary>
    /// 已导入的文件列表（支持多文件导入）
    /// </summary>
    public List<string> ImportedFiles { get; set; } = new();

    /// <summary>
    /// 文件数量
    /// </summary>
    public int FileCount => ImportedFiles.Count > 0 ? ImportedFiles.Count : 1;

    /// <summary>
    /// 显示用的文件名摘要（多文件时显示"文件A +2个"格式）
    /// </summary>
    public string FileNameDisplay => FileCount <= 1
        ? FileName
        : $"{Path.GetFileName(ImportedFiles[0])} +{FileCount - 1}个";
}
