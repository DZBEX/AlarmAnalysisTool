using System.Collections.Generic;
using System.Linq;
using System.Text;
using AlarmAnalysisTool.Models;

namespace AlarmAnalysisTool.Services;

/// <summary>
/// 全量分析结果（一次性计算，避免重复遍历）
/// </summary>
public class AnalysisResult
{
    public List<AlarmFrequencyItem> Frequency { get; set; } = new();
    public List<CategoryDistributionItem> Category { get; set; } = new();
    public List<HourlyDistributionItem> Hourly { get; set; } = new();
    public List<DailyDistributionItem> Daily { get; set; } = new();
    public List<DurationAnalysisItem> Duration { get; set; } = new();
    public List<SourceAnalysisItem> Source { get; set; } = new();
    public List<AlarmRecord> Unrecovered { get; set; } = new();
    public List<AlarmCorrelationItem> Correlation { get; set; } = new();
    public List<DiagnosticSuggestion> Suggestions { get; set; } = new();
    public AnalysisSummary Summary { get; set; } = new();
}

/// <summary>
/// 报警智能分析服务：频次统计、类别分布、时间分布、持续时间、设备来源、关联分析、智能诊断
/// </summary>
public class AnalysisService
{
    /// <summary>
    /// 一次性完成全部分析，内部复用排序结果和中间数据，避免重复计算。
    /// 原实现中 Analyze() 调用 8 个分析方法 + GetSummary + GenerateDiagnosticSuggestions，
    /// 而后两者内部又重复调用 AnalyzeFrequency/Source/Duration/Correlation 等，
    /// 导致大部分分析执行 2~3 次。本方法消除所有冗余。
    /// </summary>
    public AnalysisResult AnalyzeAll(List<AlarmRecord> records, IProgress<string>? progress = null)
    {
        var result = new AnalysisResult();
        if (records.Count == 0) return result;

        // 过滤提示类记录（CategoryName含"提示类"的操作提示，如"等待复位"、"来料拉带未提升到位"等）
        // 这类记录不是真正的设备报警，不参与频次/类别/时间/持续/来源/关联等分析
        int totalImported = records.Count;
        var alarmRecords = records.Where(r => !r.IsInfoType).ToList();
        int filteredInfoCount = totalImported - alarmRecords.Count;

        if (alarmRecords.Count == 0)
        {
            // 全部是提示类，返回空结果但保留过滤统计
            result.Summary = new AnalysisSummary
            {
                TotalImportedCount = totalImported,
                FilteredInfoCount = filteredInfoCount
            };
            return result;
        }

        progress?.Report($"正在排序数据...（已过滤 {filteredInfoCount} 条提示类记录）");
        // 预排序一次，供关联分析和风暴检测复用（避免各自内部再排序）
        var sortedAsc = alarmRecords.OrderBy(r => r.TriggerDateTime).ToList();

        progress?.Report("正在分析报警频次...");
        result.Frequency = AnalyzeFrequency(alarmRecords);

        progress?.Report("正在分析类别分布...");
        result.Category = AnalyzeCategoryDistribution(alarmRecords);

        progress?.Report("正在分析时间分布...");
        result.Hourly = AnalyzeHourlyDistribution(alarmRecords);

        progress?.Report("正在分析按日趋势...");
        result.Daily = AnalyzeDailyDistribution(alarmRecords);

        progress?.Report("正在分析持续时间...");
        result.Duration = AnalyzeDuration(alarmRecords);

        progress?.Report("正在分析设备来源...");
        result.Source = AnalyzeSource(alarmRecords);

        progress?.Report("正在检测未恢复报警...");
        result.Unrecovered = GetUnrecoveredAlarms(alarmRecords);

        progress?.Report("正在分析报警关联...");
        result.Correlation = AnalyzeCorrelationCore(sortedAsc, alarmRecords);

        progress?.Report("正在生成诊断建议...");
        result.Suggestions = GenerateDiagnosticSuggestionsFromResult(result, alarmRecords, sortedAsc);

        progress?.Report("正在生成分析摘要...");
        result.Summary = BuildSummary(alarmRecords, result.Frequency, result.Source, result.Category, result.Unrecovered);
        result.Summary.TotalImportedCount = totalImported;
        result.Summary.FilteredInfoCount = filteredInfoCount;

        return result;
    }

    /// <summary>
    /// 获取同一条报警的真实发生次数。
    /// HMI日志中"发生次数"是累计计数器（running counter），同一报警多次触发时该值递增，
    /// 取组内最大值即为真实总次数。若发生次数为0或未映射，回退到行数。
    /// </summary>
    private static int GetTrueCount(IEnumerable<AlarmRecord> group)
    {
        int maxOcc = 0;
        int rowCount = 0;
        foreach (var r in group)
        {
            rowCount++;
            if (r.OccurrenceCount > maxOcc) maxOcc = r.OccurrenceCount;
        }
        return maxOcc > 0 ? maxOcc : rowCount;
    }

    /// <summary>
    /// 报警频次分析（按报警码+描述分组统计）
    /// 发生次数取同一条报警的最大累计值，而非行数累加
    /// </summary>
    public List<AlarmFrequencyItem> AnalyzeFrequency(List<AlarmRecord> records)
    {
        var groups = records
            .GroupBy(r => new { r.AlarmCode, r.Description })
            .Select(g => new AlarmFrequencyItem
            {
                AlarmCode = g.Key.AlarmCode,
                Description = g.Key.Description,
                CategoryName = g.First().CategoryName,
                Source = g.First().SourceMain,
                Count = GetTrueCount(g),
                TotalDuration = g.Sum(r => r.Duration),
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        var total = groups.Sum(x => x.Count);
        for (int i = 0; i < groups.Count; i++)
        {
            groups[i].Rank = i + 1;
            groups[i].Percentage = total > 0 ? (double)groups[i].Count / total * 100 : 0;
        }
        return groups;
    }

    /// <summary>
    /// 类别分布分析（按真实发生次数统计）
    /// </summary>
    public List<CategoryDistributionItem> AnalyzeCategoryDistribution(List<AlarmRecord> records)
    {
        // 预计算每条报警的真实次数
        var alarmTrueCounts = records
            .GroupBy(r => new { r.AlarmCode, r.Description })
            .ToDictionary(
                g => (g.Key.AlarmCode, g.Key.Description),
                g => GetTrueCount(g));
        var total = alarmTrueCounts.Values.Sum();

        var result = records
            .GroupBy(r => r.CategoryName)
            .Select(g => new CategoryDistributionItem
            {
                CategoryName = g.Key,
                Count = g.GroupBy(r => new { r.AlarmCode, r.Description })
                         .Sum(subg => alarmTrueCounts.GetValueOrDefault((subg.Key.AlarmCode, subg.Key.Description)))
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        foreach (var item in result)
            item.Percentage = total > 0 ? (double)item.Count / total * 100 : 0;

        return result;
    }

    /// <summary>
    /// 按小时统计时间分布
    /// </summary>
    public List<HourlyDistributionItem> AnalyzeHourlyDistribution(List<AlarmRecord> records)
    {
        var result = new List<HourlyDistributionItem>(24);
        for (int h = 0; h < 24; h++)
            result.Add(new HourlyDistributionItem { Hour = h, Count = 0 });

        foreach (var r in records)
            result[r.TriggerDateTime.Hour].Count++;

        return result;
    }

    /// <summary>
    /// 按日统计报警趋势（多日分析核心）
    /// 按触发日期分组，统计每日报警行数、独立报警类型数、未恢复数。
    /// 自动填充缺失日期（无报警的天也显示0），确保趋势图连续。
    /// </summary>
    public List<DailyDistributionItem> AnalyzeDailyDistribution(List<AlarmRecord> records)
    {
        if (records.Count == 0) return new();

        // 过滤掉日期无效的记录（TriggerDateTime=MinValue），防止日期范围爆炸
        var validRecords = records.Where(r => r.TriggerDateTime > DateTime.MinValue).ToList();
        if (validRecords.Count == 0) return new();

        var minDate = validRecords.Min(r => r.TriggerDateTime).Date;
        var maxDate = validRecords.Max(r => r.TriggerDateTime).Date;

        // 安全上限：日期范围超过366天时，截断到最近366天（防止异常数据导致百万次循环）
        if ((maxDate - minDate).Days > 366)
        {
            minDate = maxDate.AddDays(-365);
            validRecords = validRecords.Where(r => r.TriggerDateTime.Date >= minDate).ToList();
        }

        // 按日期分组统计
        var grouped = validRecords
            .GroupBy(r => r.TriggerDateTime.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 填充日期范围（含无报警的天）
        var result = new List<DailyDistributionItem>();
        for (var date = minDate; date <= maxDate; date = date.AddDays(1))
        {
            if (grouped.TryGetValue(date, out var dayRecords))
            {
                result.Add(new DailyDistributionItem
                {
                    Date = date,
                    Count = dayRecords.Count,
                    UniqueAlarmTypes = dayRecords.Select(r => r.AlarmCode).Distinct().Count(),
                    UnrecoveredCount = dayRecords.Count(r => !r.IsRecovered)
                });
            }
            else
            {
                result.Add(new DailyDistributionItem
                {
                    Date = date,
                    Count = 0,
                    UniqueAlarmTypes = 0,
                    UnrecoveredCount = 0
                });
            }
        }

        return result;
    }

    /// <summary>
    /// 持续时间分析（仅统计有恢复时间的记录）
    /// </summary>
    public List<DurationAnalysisItem> AnalyzeDuration(List<AlarmRecord> records)
    {
        return records
            .Where(r => r.IsRecovered && r.Duration > 0)
            .GroupBy(r => new { r.AlarmCode, r.Description })
            .Select(g => new DurationAnalysisItem
            {
                AlarmCode = g.Key.AlarmCode,
                Description = g.Key.Description,
                Occurrences = g.Count(),
                MaxDuration = g.Max(r => r.Duration),
                AvgDuration = (int)g.Average(r => r.Duration),
                TotalDuration = g.Sum(r => r.Duration)
            })
            .OrderByDescending(x => x.TotalDuration)
            .ToList();
    }

    /// <summary>
    /// 设备来源分析（按真实发生次数统计）
    /// </summary>
    public List<SourceAnalysisItem> AnalyzeSource(List<AlarmRecord> records)
    {
        // 预计算每条报警的真实次数
        var alarmTrueCounts = records
            .GroupBy(r => new { r.AlarmCode, r.Description })
            .ToDictionary(
                g => (g.Key.AlarmCode, g.Key.Description),
                g => GetTrueCount(g));
        var total = alarmTrueCounts.Values.Sum();

        var result = records
            .GroupBy(r => r.SourceMain)
            .Select(g => new SourceAnalysisItem
            {
                Source = g.Key,
                Count = g.GroupBy(r => new { r.AlarmCode, r.Description })
                         .Sum(subg => alarmTrueCounts.GetValueOrDefault((subg.Key.AlarmCode, subg.Key.Description))),
                UniqueAlarmTypes = g.Select(r => r.AlarmCode).Distinct().Count()
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        foreach (var item in result)
            item.Percentage = total > 0 ? (double)item.Count / total * 100 : 0;

        return result;
    }

    /// <summary>
    /// 未恢复报警列表
    /// </summary>
    public List<AlarmRecord> GetUnrecoveredAlarms(List<AlarmRecord> records)
    {
        return records
            .Where(r => !r.IsRecovered)
            .OrderByDescending(r => r.TriggerDateTime)
            .ToList();
    }

    /// <summary>
    /// 报警关联分析（同时发生的报警）
    /// 公开入口：自行排序后委托给核心方法
    /// </summary>
    public List<AlarmCorrelationItem> AnalyzeCorrelation(List<AlarmRecord> records, int timeWindowSeconds = 120)
    {
        var sorted = records.OrderBy(r => r.TriggerDateTime).ToList();
        return AnalyzeCorrelationCore(sorted, records, timeWindowSeconds);
    }

    /// <summary>
    /// 关联分析核心：接受预排序列表，使用 Dictionary O(1) 查找替代原 List.FirstOrDefault O(m) 线性搜索。
    /// 原实现复杂度 O(n²·m)，优化后 O(n²)（内层因 break 时间窗口实际为 O(n·w)，w 为窗口内平均记录数）。
    /// </summary>
    private List<AlarmCorrelationItem> AnalyzeCorrelationCore(List<AlarmRecord> sorted, List<AlarmRecord> records, int timeWindowSeconds = 120)
    {
        var pairMap = new Dictionary<(string, string), AlarmCorrelationItem>();
        int n = sorted.Count;

        for (int i = 0; i < n; i++)
        {
            var ti = sorted[i].TriggerDateTime;
            for (int j = i + 1; j < n; j++)
            {
                var diff = (sorted[j].TriggerDateTime - ti).TotalSeconds;
                if (diff > timeWindowSeconds) break;
                if (diff == 0) continue; // 完全同时的跳过
                if (sorted[i].AlarmCode == sorted[j].AlarmCode) continue;

                var key1 = $"{sorted[i].AlarmCode}-{sorted[i].Description}";
                var key2 = $"{sorted[j].AlarmCode}-{sorted[j].Description}";
                var key = (key1, key2);

                if (pairMap.TryGetValue(key, out var existing))
                    existing.CoOccurrenceCount++;
                else
                    pairMap[key] = new AlarmCorrelationItem
                    {
                        Alarm1 = key1,
                        Alarm2 = key2,
                        CoOccurrenceCount = 1
                    };
            }
        }

        // 频次表（用于置信度计算）
        var freqMap = new Dictionary<string, int>();
        foreach (var r in records)
        {
            freqMap.TryGetValue(r.AlarmCode, out int c);
            freqMap[r.AlarmCode] = c + 1;
        }

        var result = new List<AlarmCorrelationItem>(pairMap.Count);
        foreach (var item in pairMap.Values)
        {
            var code1 = item.Alarm1.AsSpan();
            var dashIdx = code1.IndexOf('-');
            var code1Str = dashIdx >= 0 ? code1[..dashIdx].ToString() : item.Alarm1;
            if (freqMap.TryGetValue(code1Str, out int count1) && count1 > 0)
                item.Confidence = (double)item.CoOccurrenceCount / count1 * 100;
            if (item.CoOccurrenceCount >= 2)
                result.Add(item);
        }

        return result.OrderByDescending(x => x.CoOccurrenceCount).Take(15).ToList();
    }

    /// <summary>
    /// 智能诊断建议生成（核心智能分析功能）
    /// 公开入口：自行计算全部分析后委托
    /// </summary>
    public List<DiagnosticSuggestion> GenerateDiagnosticSuggestions(List<AlarmRecord> records)
    {
        if (records.Count == 0) return new();
        var sortedAsc = records.OrderBy(r => r.TriggerDateTime).ToList();
        var result = new AnalysisResult
        {
            Frequency = AnalyzeFrequency(records),
            Source = AnalyzeSource(records),
            Duration = AnalyzeDuration(records),
            Unrecovered = GetUnrecoveredAlarms(records),
            Hourly = AnalyzeHourlyDistribution(records),
            Correlation = AnalyzeCorrelationCore(sortedAsc, records)
        };
        return GenerateDiagnosticSuggestionsFromResult(result, records, sortedAsc);
    }

    /// <summary>
    /// 基于预计算结果生成诊断建议，避免重复分析
    /// </summary>
    private List<DiagnosticSuggestion> GenerateDiagnosticSuggestionsFromResult(
        AnalysisResult result, List<AlarmRecord> records, List<AlarmRecord> sortedAsc)
    {
        var suggestions = new List<DiagnosticSuggestion>();
        if (records.Count == 0) return suggestions;

        var freq = result.Frequency;
        var source = result.Source;
        var duration = result.Duration;
        var unrecovered = result.Unrecovered;
        var hourly = result.Hourly;
        var correlation = result.Correlation;
        var total = records.Count;               // 日志记录行数（用于按行统计的对比）
        var trueTotal = freq.Sum(x => x.Count);   // 真实报警总次数（基于发生次数最大值）

        // 1. 高频报警诊断
        var topAlarm = freq.FirstOrDefault();
        if (topAlarm != null && topAlarm.Count >= 10)
        {
            var level = topAlarm.Percentage > 30 ? SuggestionLevel.Critical : SuggestionLevel.Warning;
            suggestions.Add(new DiagnosticSuggestion
            {
                Level = level,
                Title = $"高频报警: {topAlarm.Description}",
                Description = $"报警码 {topAlarm.AlarmCode} 在当前周期内触发 {topAlarm.Count} 次，占总报警量的 {topAlarm.Percentage:F1}%",
                Detail = GenerateHighFreqSuggestion(topAlarm)
            });
        }

        // 2. 通讯异常群组诊断
        var commAlarms = freq.Where(x => x.Description.Contains("通讯异常")).ToList();
        if (commAlarms.Count >= 2)
        {
            var totalComm = commAlarms.Sum(x => x.Count);
            suggestions.Add(new DiagnosticSuggestion
            {
                Level = SuggestionLevel.Warning,
                Title = "通讯异常群发",
                Description = $"检测到 {commAlarms.Count} 种通讯异常类报警，合计 {totalComm} 次，占总量的 {(double)totalComm / trueTotal * 100:F1}%",
                Detail = "多个设备同时出现通讯异常，通常指向共同的根因：\n" +
                        "  - 通讯总线/网络硬件故障（交换机、网线、接头）\n" +
                        "  - 通讯电源不稳定或接地不良\n" +
                        "  - EMC干扰导致通讯中断\n" +
                        "  - 上位机/PLC通讯模块过载\n" +
                        "建议：检查EM03相关通讯链路，测量通讯信号质量，排查接地与屏蔽。"
            });
        }

        // 3. 陀螺仪/传感器异常诊断
        var gyroAlarms = freq.Where(x => x.Description.Contains("陀螺仪") || x.Description.Contains("倾角")).ToList();
        if (gyroAlarms.Sum(x => x.Count) >= 15)
        {
            suggestions.Add(new DiagnosticSuggestion
            {
                Level = SuggestionLevel.Warning,
                Title = "陀螺仪/倾角传感器异常",
                Description = $"陀螺仪相关报警累计 {gyroAlarms.Sum(x => x.Count)} 次",
                Detail = "陀螺仪倾角超限频繁触发可能原因：\n" +
                        "  - 传感器安装松动或支架振动\n" +
                        "  - 传感器标定漂移，需重新校准\n" +
                        "  - 阈值设置过于敏感\n" +
                        "  - 现场存在持续振动源\n" +
                        "建议：检查传感器物理安装状态，必要时重新标定或调整报警阈值。"
            });
        }

        // 4. 持续时间最长的报警
        var longestDuration = duration.FirstOrDefault();
        if (longestDuration != null && longestDuration.TotalDuration >= 300)
        {
            suggestions.Add(new DiagnosticSuggestion
            {
                Level = longestDuration.TotalDuration >= 1800 ? SuggestionLevel.Critical : SuggestionLevel.Warning,
                Title = $"持续时间最长: {longestDuration.Description}",
                Description = $"累计持续 {longestDuration.TotalDurationText}（{longestDuration.Occurrences}次触发）",
                Detail = "长时间持续报警通常表明：\n" +
                        "  - 故障未得到有效处理即复位\n" +
                        "  - 设备处于异常工作状态\n" +
                        "  - 工艺参数偏离正常范围\n" +
                        "建议：排查根因后再复位，避免带病运行。"
            });
        }

        // 5. 未恢复报警诊断
        if (unrecovered.Count > 0)
        {
            var unrecoveredGroups = unrecovered.GroupBy(r => r.Description)
                .OrderByDescending(g => g.Count()).Take(3).ToList();
            var desc = string.Join("\n", unrecoveredGroups.Select(g => $"  - {g.Key}: {g.Count()}次未恢复"));

            suggestions.Add(new DiagnosticSuggestion
            {
                Level = unrecovered.Count > 10 ? SuggestionLevel.Warning : SuggestionLevel.Info,
                Title = $"未恢复报警: {unrecovered.Count}条",
                Description = $"当前有 {unrecovered.Count} 条报警未记录恢复时间",
                Detail = "未恢复报警可能表明：\n" +
                        "  - 报警仍在持续中\n" +
                        "  - 恢复事件未被HMI正确记录\n" +
                        "  - 报警被手动确认但未自动恢复\n\n" +
                        $"主要未恢复报警:\n{desc}"
            });
        }

        // 6. 设备来源集中诊断
        var topSource = source.FirstOrDefault();
        if (topSource != null && topSource.Percentage > 50)
        {
            suggestions.Add(new DiagnosticSuggestion
            {
                Level = SuggestionLevel.Info,
                Title = $"报警来源集中: {topSource.Source}",
                Description = $"{topSource.Source} 产生报警 {topSource.Count} 次，占总量的 {topSource.Percentage:F1}%，涉及 {topSource.UniqueAlarmTypes} 种报警类型",
                Detail = "单一设备产生大量报警时建议：\n" +
                        "  - 检查该设备的运行状态和健康度\n" +
                        "  - 评估该设备的维护周期是否需要缩短\n" +
                        "  - 分析是否为关联报警（一个根因引发多个报警）"
            });
        }

        // 7. 时间段集中诊断
        var peakHour = hourly.OrderByDescending(x => x.Count).First();
        if (peakHour.Count > total * 0.15)
        {
            suggestions.Add(new DiagnosticSuggestion
            {
                Level = SuggestionLevel.Info,
                Title = $"报警高峰时段: {peakHour.HourText}",
                Description = $"在 {peakHour.HourText} 时段内产生 {peakHour.Count} 次报警，占总量 {peakHour.Count * 100.0 / total:F1}%",
                Detail = "报警时段集中可能关联：\n" +
                        "  - 换班/交接班操作\n" +
                        "  - 特定工艺阶段（如升温、真空抽气）\n" +
                        "  - 环境因素变化（温度、湿度）\n" +
                        "建议：结合工艺时序分析该时段的特殊操作。"
            });
        }

        // 8. 报警关联诊断
        if (correlation.Count > 0)
        {
            var topCorr = correlation.First();
            suggestions.Add(new DiagnosticSuggestion
            {
                Level = SuggestionLevel.Info,
                Title = "报警关联分析",
                Description = $"检测到 [{topCorr.Alarm1}] 与 [{topCorr.Alarm2}] 在 {topCorr.CoOccurrenceCount} 次事件中同时出现",
                Detail = "报警关联分析揭示了报警之间的共现关系：\n" +
                        "  - 共现报警通常指向同一根因\n" +
                        "  - 消除根因可同时减少多个报警\n" +
                        "  - 关联报警可用于构建报警抑制规则\n\n" +
                        $"置信度: {topCorr.Confidence:F1}%（当第一个报警发生时，第二个报警出现的概率）"
            });
        }

        // 9. 报警风暴检测（复用预排序列表）
        var stormDetected = DetectAlarmStorm(sortedAsc);
        if (stormDetected != null)
        {
            suggestions.Add(stormDetected);
        }

        // 10. 多日趋势诊断（数据跨多天时才触发）
        if (result.Daily.Count > 1)
        {
            var dailySuggestion = AnalyzeDailyTrend(result.Daily);
            if (dailySuggestion != null)
                suggestions.Add(dailySuggestion);
        }

        return suggestions;
    }

    /// <summary>
    /// 多日报警趋势分析诊断
    /// </summary>
    private DiagnosticSuggestion? AnalyzeDailyTrend(List<DailyDistributionItem> daily)
    {
        if (daily.Count < 2) return null;

        var avgCount = daily.Average(x => x.Count);
        var maxDay = daily.OrderByDescending(x => x.Count).First();
        var minDay = daily.OrderBy(x => x.Count).First();

        // 趋势判断：后半段均值 vs 前半段均值
        var halfPoint = daily.Count / 2;
        var firstHalfAvg = daily.Take(halfPoint).Average(x => x.Count);
        var secondHalfAvg = daily.Skip(halfPoint).Average(x => x.Count);

        var trendDesc = "";
        var trendDetail = "";

        if (secondHalfAvg > firstHalfAvg * 1.3)
        {
            trendDesc = "报警次数呈上升趋势";
            trendDetail = "后半月报警均值显著高于前半月，建议重点关注近期新增的报警类型和设备状态变化。";
        }
        else if (firstHalfAvg > secondHalfAvg * 1.3)
        {
            trendDesc = "报警次数呈下降趋势";
            trendDetail = "后半月报警均值显著低于前半月，说明前期问题可能已得到处理或自行缓解。";
        }
        else
        {
            trendDesc = "报警次数整体平稳";
            trendDetail = "前后段报警均值接近，系统运行状态相对稳定。";
        }

        var level = maxDay.Count > avgCount * 3 ? SuggestionLevel.Warning : SuggestionLevel.Info;

        return new DiagnosticSuggestion
        {
            Level = level,
            Title = $"多日趋势分析 ({daily.Count}天): {trendDesc}",
            Description = $"日均报警 {avgCount:F0} 次, 最高 {maxDay.DateText}({maxDay.Count}次), 最低 {minDay.DateText}({minDay.Count}次)",
            Detail = $"{trendDetail}\n\n" +
                    $"统计概况:\n" +
                    $"  - 分析天数: {daily.Count} 天\n" +
                    $"  - 日均报警: {avgCount:F1} 次\n" +
                    $"  - 报警最多: {maxDay.DateFullText} ({maxDay.DayOfWeek}) - {maxDay.Count}次, {maxDay.UniqueAlarmTypes}种类型\n" +
                    $"  - 报警最少: {minDay.DateFullText} ({minDay.DayOfWeek}) - {minDay.Count}次\n" +
                    $"  - 未恢复总数: {daily.Sum(x => x.UnrecoveredCount)} 次\n\n" +
                    "建议:\n" +
                    "  - 对比高日报警与低日报警的差异，识别影响报警频次的关键因素\n" +
                    "  - 关注特定星期几的报警规律（如周末值班期间报警是否偏高）\n" +
                    "  - 持续上升趋势时应安排专项排查"
        };
    }

    private string GenerateHighFreqSuggestion(AlarmFrequencyItem item)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"报警码: {item.AlarmCode}");
        sb.AppendLine($"描述: {item.Description}");
        sb.AppendLine($"类别: {item.CategoryName}");
        sb.AppendLine($"来源: {item.Source}");
        sb.AppendLine($"触发次数: {item.Count} 次 ({item.Percentage:F1}%)");
        sb.AppendLine($"累计持续时间: {item.TotalDurationText}");
        sb.AppendLine();
        sb.AppendLine("建议措施:");

        if (item.Description.Contains("通讯"))
        {
            sb.AppendLine("  1. 检查通讯线缆连接和信号质量");
            sb.AppendLine("  2. 排查通讯电源和接地");
            sb.AppendLine("  3. 检查设备通讯模块状态");
        }
        else if (item.Description.Contains("超限") || item.Description.Contains("超时"))
        {
            sb.AppendLine("  1. 检查传感器精度和标定状态");
            sb.AppendLine("  2. 评估报警阈值是否合理");
            sb.AppendLine("  3. 排查工艺参数波动原因");
        }
        else if (item.Description.Contains("阀"))
        {
            sb.AppendLine("  1. 检查阀门机械动作是否顺畅");
            sb.AppendLine("  2. 检查气源压力和电磁阀状态");
            sb.AppendLine("  3. 排查阀门反馈信号链路");
        }
        else
        {
            sb.AppendLine("  1. 检查相关设备的运行状态");
            sb.AppendLine("  2. 查阅设备维护手册排查故障");
            sb.AppendLine("  3. 联系设备厂家技术支持");
        }

        if (item.Percentage > 30)
            sb.AppendLine("\n⚠ 该报警占比超过30%，建议优先处理以减少报警噪音。");

        return sb.ToString();
    }

    /// <summary>
    /// 报警风暴检测：短时间内大量报警同时触发
    /// 接受预排序列表，避免重复排序；用索引范围替代 Skip/Take 分配
    /// </summary>
    private DiagnosticSuggestion? DetectAlarmStorm(List<AlarmRecord> sorted)
    {
        int windowSeconds = 60;
        int threshold = 5;
        int n = sorted.Count;

        for (int i = 0; i <= n - threshold; i++)
        {
            var windowEnd = sorted[i].TriggerDateTime.AddSeconds(windowSeconds);
            int j = i;
            while (j < n && sorted[j].TriggerDateTime <= windowEnd)
                j++;
            int count = j - i;

            if (count >= threshold)
            {
                var stormStart = sorted[i].TriggerDateTime;
                // 收集不重复的报警描述，避免 Skip/Take/Select/Distinct 链式分配
                var descSet = new HashSet<string>();
                var descList = new List<string>();
                for (int k = i; k < j && descList.Count < 5; k++)
                {
                    if (descSet.Add(sorted[k].Description))
                        descList.Add(sorted[k].Description);
                }
                return new DiagnosticSuggestion
                {
                    Level = SuggestionLevel.Critical,
                    Title = "报警风暴检测",
                    Description = $"在 {stormStart:HH:mm:ss} 前后 {windowSeconds}秒 内触发了 {count} 条报警",
                    Detail = "报警风暴通常表明：\n" +
                            "  - 系统级故障导致多个设备同时报警\n" +
                            "  - 电源波动或通讯中断\n" +
                            "  - 操作失误引发连锁反应\n\n" +
                            "涉及报警:\n" + string.Join("\n", descList.Select(a => $"  - {a}"))
                };
            }
        }
        return null;
    }

    /// <summary>
    /// 生成分析报告摘要
    /// 公开入口：委托给 BuildSummary，复用预计算结果
    /// </summary>
    public AnalysisSummary GetSummary(List<AlarmRecord> records)
    {
        var freq = AnalyzeFrequency(records);
        var unrecovered = GetUnrecoveredAlarms(records);
        var source = AnalyzeSource(records);
        var categories = AnalyzeCategoryDistribution(records);
        return BuildSummary(records, freq, source, categories, unrecovered);
    }

    /// <summary>
    /// 基于预计算结果构建摘要，避免重复分析
    /// </summary>
    private static AnalysisSummary BuildSummary(
        List<AlarmRecord> records,
        List<AlarmFrequencyItem> freq,
        List<SourceAnalysisItem> source,
        List<CategoryDistributionItem> categories,
        List<AlarmRecord> unrecovered)
    {
        long totalDuration = 0;
        foreach (var r in records)
            if (r.Duration > 0) totalDuration += r.Duration;

        // 计算覆盖天数
        int dayCount = 0;
        if (records.Count > 0)
        {
            var minDate = records.Min(r => r.TriggerDateTime).Date;
            var maxDate = records.Max(r => r.TriggerDateTime).Date;
            dayCount = (maxDate - minDate).Days + 1;
        }

        var topFreq = freq.Count > 0 ? freq[0] : null;
        var topSrc = source.Count > 0 ? source[0] : null;

        return new AnalysisSummary
        {
            TotalAlarms = freq.Sum(x => x.Count),
            UniqueAlarmTypes = freq.Count,
            UnrecoveredCount = unrecovered.Count,
            CategoryCount = categories.Count,
            SourceCount = source.Count,
            DayCount = dayCount,
            TopAlarm = topFreq?.Description ?? "-",
            TopAlarmCount = topFreq?.Count ?? 0,
            TopSource = topSrc?.Source ?? "-",
            TopSourceCount = topSrc?.Count ?? 0,
            TotalDurationSeconds = totalDuration
        };
    }
}

public class AnalysisSummary
{
    public int TotalAlarms { get; set; }
    public int UniqueAlarmTypes { get; set; }
    public int UnrecoveredCount { get; set; }
    public int CategoryCount { get; set; }
    public int SourceCount { get; set; }
    public int DayCount { get; set; }
    public string TopAlarm { get; set; } = "";
    public int TopAlarmCount { get; set; }
    public string TopSource { get; set; } = "";
    public int TopSourceCount { get; set; }
    public long TotalDurationSeconds { get; set; }
    public string TotalDurationText => AlarmRecord.FormatDuration((int)TotalDurationSeconds);

    /// <summary>
    /// 被过滤掉的提示类记录数（不参与分析）
    /// </summary>
    public int FilteredInfoCount { get; set; }

    /// <summary>
    /// 原始导入总记录数（含提示类）
    /// </summary>
    public int TotalImportedCount { get; set; }
}
