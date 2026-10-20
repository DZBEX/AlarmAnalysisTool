using System.IO;
using System.Text;
using AlarmAnalysisTool.Models;
using ClosedXML.Excel;

namespace AlarmAnalysisTool.Services;

/// <summary>
/// 数据导出服务：支持导出到Excel(多Sheet格式化)和CSV
/// </summary>
public class ExportService
{
    /// <summary>
    /// 导出完整分析报告到Excel（包含原始数据、频次分析、类别分布、时间分布、持续时间、设备来源、诊断建议）
    /// </summary>
    public void ExportFullReport(
        string filePath,
        List<AlarmRecord> records,
        List<AlarmFrequencyItem> frequency,
        List<CategoryDistributionItem> categories,
        List<HourlyDistributionItem> hourly,
        List<DurationAnalysisItem> duration,
        List<SourceAnalysisItem> sources,
        List<AlarmRecord> unrecovered,
        List<DiagnosticSuggestion> suggestions,
        AnalysisSummary summary)
    {
        using var wb = new XLWorkbook();

        // Sheet1: 概览摘要
        CreateSummarySheet(wb, summary);

        // Sheet2: 原始数据
        CreateRawDataSheet(wb, records);

        // Sheet3: 频次分析
        CreateFrequencySheet(wb, frequency);

        // Sheet4: 类别分布
        CreateCategorySheet(wb, categories);

        // Sheet5: 时间分布
        CreateHourlySheet(wb, hourly);

        // Sheet6: 持续时间分析
        CreateDurationSheet(wb, duration);

        // Sheet7: 设备来源分析
        CreateSourceSheet(wb, sources);

        // Sheet8: 未恢复报警
        CreateUnrecoveredSheet(wb, unrecovered);

        // Sheet9: 智能诊断建议
        CreateSuggestionsSheet(wb, suggestions);

        wb.SaveAs(filePath);
    }

    /// <summary>
    /// 导出原始数据到CSV
    /// </summary>
    public void ExportRawDataCsv(string filePath, List<AlarmRecord> records)
    {
        var sb = new StringBuilder();
        sb.AppendLine("事件,类别,触发日期,触发时间,恢复日期,恢复时间,持续时间(秒),报警码,报警描述,类别详情,设备来源,发生次数,累计时间,是否恢复");

        foreach (var r in records)
        {
            sb.AppendLine(string.Join(",",
                r.Event, r.Category,
                EscapeCsv(r.TriggerDate), EscapeCsv(r.TriggerTime),
                EscapeCsv(r.RecoveryDate), EscapeCsv(r.RecoveryTime),
                r.Duration,
                EscapeCsv(r.AlarmCode), EscapeCsv(r.Description),
                EscapeCsv(r.CategoryDetail), EscapeCsv(r.Source),
                r.OccurrenceCount, r.CumulativeTime,
                r.IsRecovered ? "是" : "否"));
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// 导出频次分析到Excel
    /// </summary>
    public void ExportFrequencyExcel(string filePath, List<AlarmFrequencyItem> frequency)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("频次分析");
        var headers = new[] { "排名", "报警码", "报警描述", "类别", "设备来源", "触发次数", "占比(%)", "累计持续时间" };
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        var headerRange = ws.Range(1, 1, 1, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#4472C4");
        headerRange.Style.Font.FontColor = XLColor.White;
        ws.SheetView.FreezeRows(1);

        for (int i = 0; i < frequency.Count; i++)
        {
            var f = frequency[i];
            int row = i + 2;
            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = f.AlarmCode;
            ws.Cell(row, 3).Value = f.Description;
            ws.Cell(row, 4).Value = f.CategoryName;
            ws.Cell(row, 5).Value = f.Source;
            ws.Cell(row, 6).Value = f.Count;
            ws.Cell(row, 7).Value = Math.Round(f.Percentage, 2);
            ws.Cell(row, 8).Value = f.TotalDurationText;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
    }

    #region 私有方法 - Excel Sheet创建

    private void CreateSummarySheet(XLWorkbook wb, AnalysisSummary summary)
    {
        var ws = wb.AddWorksheet("概览摘要");
        ws.Cell(1, 1).Value = "报警分析报告概览";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        ws.Range(1, 1, 1, 2).Merge();

        var items = new (string, string)[]
        {
            ("总报警数", summary.TotalAlarms.ToString()),
            ("报警类型数", summary.UniqueAlarmTypes.ToString()),
            ("未恢复报警数", summary.UnrecoveredCount.ToString()),
            ("报警类别数", summary.CategoryCount.ToString()),
            ("设备来源数", summary.SourceCount.ToString()),
            ("最高频报警", summary.TopAlarm),
            ("最高频报警次数", summary.TopAlarmCount.ToString()),
            ("最多报警来源", summary.TopSource),
            ("最多报警来源次数", summary.TopSourceCount.ToString()),
            ("累计持续时间", summary.TotalDurationText),
        };

        for (int i = 0; i < items.Length; i++)
        {
            int row = i + 3;
            ws.Cell(row, 1).Value = items[i].Item1;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E2F3");
            ws.Cell(row, 2).Value = items[i].Item2;
        }

        ws.Column(1).Width = 20;
        ws.Column(2).Width = 30;
    }

    private void CreateRawDataSheet(XLWorkbook wb, List<AlarmRecord> records)
    {
        var ws = wb.AddWorksheet("原始数据");
        var headers = new[] { "事件类型", "类别", "触发时间", "恢复时间", "持续时间", "报警码", "报警描述", "类别详情", "设备来源", "发生次数", "累计时间", "是否恢复" };
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        StyleHeader(ws, headers.Length);

        for (int i = 0; i < records.Count; i++)
        {
            var r = records[i];
            int row = i + 2;
            ws.Cell(row, 1).Value = r.EventType;
            ws.Cell(row, 2).Value = r.CategoryName;
            ws.Cell(row, 3).Value = r.TriggerDateTimeText;
            ws.Cell(row, 4).Value = r.RecoveryDateTimeText;
            ws.Cell(row, 5).Value = r.DurationText;
            ws.Cell(row, 6).Value = r.AlarmCode;
            ws.Cell(row, 7).Value = r.Description;
            ws.Cell(row, 8).Value = r.CategoryDetail;
            ws.Cell(row, 9).Value = r.Source;
            ws.Cell(row, 10).Value = r.OccurrenceCount;
            ws.Cell(row, 11).Value = r.CumulativeTime;
            ws.Cell(row, 12).Value = r.IsRecovered ? "是" : "否";

            if (!r.IsRecovered)
            {
                ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");
            }
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    private void CreateFrequencySheet(XLWorkbook wb, List<AlarmFrequencyItem> frequency)
    {
        var ws = wb.AddWorksheet("频次分析");
        var headers = new[] { "排名", "报警码", "报警描述", "类别", "设备来源", "触发次数", "占比(%)", "累计持续时间" };
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];
        StyleHeader(ws, headers.Length);

        for (int i = 0; i < frequency.Count; i++)
        {
            var f = frequency[i];
            int row = i + 2;
            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = f.AlarmCode;
            ws.Cell(row, 3).Value = f.Description;
            ws.Cell(row, 4).Value = f.CategoryName;
            ws.Cell(row, 5).Value = f.Source;
            ws.Cell(row, 6).Value = f.Count;
            ws.Cell(row, 7).Value = Math.Round(f.Percentage, 2);
            ws.Cell(row, 8).Value = f.TotalDurationText;

            // 频次越高颜色越深
            if (i < 3) ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#FCE4D6");
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    private void CreateCategorySheet(XLWorkbook wb, List<CategoryDistributionItem> categories)
    {
        var ws = wb.AddWorksheet("类别分布");
        var headers = new[] { "类别名称", "报警次数", "占比(%)" };
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];
        StyleHeader(ws, headers.Length);

        for (int i = 0; i < categories.Count; i++)
        {
            var c = categories[i];
            int row = i + 2;
            ws.Cell(row, 1).Value = c.CategoryName;
            ws.Cell(row, 2).Value = c.Count;
            ws.Cell(row, 3).Value = Math.Round(c.Percentage, 2);
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    private void CreateHourlySheet(XLWorkbook wb, List<HourlyDistributionItem> hourly)
    {
        var ws = wb.AddWorksheet("时间分布");
        var headers = new[] { "时段", "报警次数" };
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];
        StyleHeader(ws, headers.Length);

        for (int i = 0; i < hourly.Count; i++)
        {
            var h = hourly[i];
            int row = i + 2;
            ws.Cell(row, 1).Value = h.HourText;
            ws.Cell(row, 2).Value = h.Count;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    private void CreateDurationSheet(XLWorkbook wb, List<DurationAnalysisItem> duration)
    {
        var ws = wb.AddWorksheet("持续时间分析");
        var headers = new[] { "报警码", "报警描述", "触发次数", "最长持续", "平均持续", "累计持续" };
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];
        StyleHeader(ws, headers.Length);

        for (int i = 0; i < duration.Count; i++)
        {
            var d = duration[i];
            int row = i + 2;
            ws.Cell(row, 1).Value = d.AlarmCode;
            ws.Cell(row, 2).Value = d.Description;
            ws.Cell(row, 3).Value = d.Occurrences;
            ws.Cell(row, 4).Value = d.MaxDurationText;
            ws.Cell(row, 5).Value = d.AvgDurationText;
            ws.Cell(row, 6).Value = d.TotalDurationText;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    private void CreateSourceSheet(XLWorkbook wb, List<SourceAnalysisItem> sources)
    {
        var ws = wb.AddWorksheet("设备来源分析");
        var headers = new[] { "设备来源", "报警次数", "占比(%)", "报警类型数" };
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];
        StyleHeader(ws, headers.Length);

        for (int i = 0; i < sources.Count; i++)
        {
            var s = sources[i];
            int row = i + 2;
            ws.Cell(row, 1).Value = s.Source;
            ws.Cell(row, 2).Value = s.Count;
            ws.Cell(row, 3).Value = Math.Round(s.Percentage, 2);
            ws.Cell(row, 4).Value = s.UniqueAlarmTypes;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    private void CreateUnrecoveredSheet(XLWorkbook wb, List<AlarmRecord> unrecovered)
    {
        var ws = wb.AddWorksheet("未恢复报警");
        var headers = new[] { "触发时间", "报警码", "报警描述", "类别", "设备来源", "发生次数" };
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];
        StyleHeader(ws, headers.Length);

        for (int i = 0; i < unrecovered.Count; i++)
        {
            var r = unrecovered[i];
            int row = i + 2;
            ws.Cell(row, 1).Value = r.TriggerDateTimeText;
            ws.Cell(row, 2).Value = r.AlarmCode;
            ws.Cell(row, 3).Value = r.Description;
            ws.Cell(row, 4).Value = r.CategoryName;
            ws.Cell(row, 5).Value = r.Source;
            ws.Cell(row, 6).Value = r.OccurrenceCount;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    private void CreateSuggestionsSheet(XLWorkbook wb, List<DiagnosticSuggestion> suggestions)
    {
        var ws = wb.AddWorksheet("智能诊断建议");
        var headers = new[] { "级别", "标题", "描述", "详细分析" };
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];
        StyleHeader(ws, headers.Length);

        for (int i = 0; i < suggestions.Count; i++)
        {
            var s = suggestions[i];
            int row = i + 2;
            ws.Cell(row, 1).Value = s.LevelText;
            ws.Cell(row, 2).Value = s.Title;
            ws.Cell(row, 3).Value = s.Description;
            ws.Cell(row, 4).Value = s.Detail;

            // 按级别着色
            var color = s.Level switch
            {
                SuggestionLevel.Critical => "#FFC7CE",
                SuggestionLevel.Warning => "#FFEB9C",
                _ => "#C6EFCE"
            };
            ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml(color);

            // 详细分析列自动换行
            ws.Cell(row, 4).Style.Alignment.WrapText = true;
        }

        ws.SheetView.FreezeRows(1);
        ws.Column(1).Width = 8;
        ws.Column(2).Width = 25;
        ws.Column(3).Width = 45;
        ws.Column(4).Width = 80;
    }

    private void StyleHeader(IXLWorksheet ws, int colCount)
    {
        var range = ws.Range(1, 1, 1, colCount);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#4472C4");
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    private string EscapeCsv(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }

    #endregion
}
