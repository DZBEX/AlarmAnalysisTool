using System.IO;
using AlarmAnalysisTool.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OxyPlot;
using OxyPlot.SkiaSharp;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using Bold = DocumentFormat.OpenXml.Wordprocessing.Bold;
using Color = DocumentFormat.OpenXml.Wordprocessing.Color;
using Table = DocumentFormat.OpenXml.Wordprocessing.Table;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;
using PageSize = QuestPDF.Helpers.PageSize;

namespace AlarmAnalysisTool.Services;

/// <summary>
/// QuestPDF 扩展方法
/// </summary>
public static class QuestPdfExtensions
{
    /// <summary>
    /// 添加带底部边框的章节标题
    /// </summary>
    public static void SectionHeading(this IContainer container, string text)
    {
        container
            .PaddingBottom(4)
            .BorderBottom(2)
            .BorderColor("#4472C4")
            .Text(text)
            .FontSize(14)
            .Bold()
            .FontColor("#2F5496");
    }
}

/// <summary>
/// 报告导出所需的数据集合
/// </summary>
public class ReportData
{
    public ImportFileInfo? FileInfo { get; set; }
    public AnalysisSummary? Summary { get; set; }
    public List<AlarmFrequencyItem> Frequency { get; set; } = new();
    public List<CategoryDistributionItem> Category { get; set; } = new();
    public List<HourlyDistributionItem> Hourly { get; set; } = new();
    public List<DailyDistributionItem> Daily { get; set; } = new();
    public List<DurationAnalysisItem> Duration { get; set; } = new();
    public List<SourceAnalysisItem> Source { get; set; } = new();
    public List<AlarmRecord> Unrecovered { get; set; } = new();
    public List<AlarmCorrelationItem> Correlation { get; set; } = new();
    public List<DiagnosticSuggestion> Suggestions { get; set; } = new();
    public PlotModel? FrequencyChart { get; set; }
    public PlotModel? CategoryChart { get; set; }
    public PlotModel? HourlyChart { get; set; }
    public PlotModel? DailyChart { get; set; }
    public PlotModel? SourceChart { get; set; }
}

/// <summary>
/// PDF / Word 分析报告导出服务
/// </summary>
public class ReportExportService
{
    static ReportExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // ══════════════════════════════════════════════════════════════
    //  图表 PNG 导出
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 将 OxyPlot PlotModel 导出为 PNG 字节数组
    /// </summary>
    public static byte[] ChartToPng(PlotModel? model, int width, int height)
    {
        if (model == null) return Array.Empty<byte>();
        var exporter = new PngExporter { Width = width, Height = height };
        using var ms = new MemoryStream();
        exporter.Export(model, ms);
        return ms.ToArray();
    }

    // ══════════════════════════════════════════════════════════════
    //  PDF 报告导出
    // ══════════════════════════════════════════════════════════════

    public void ExportPdf(string filePath, ReportData data)
    {
        // 预导出图表 PNG
        var freqPng = ChartToPng(data.FrequencyChart, 1600, 800);
        var catPng = ChartToPng(data.CategoryChart, 1400, 800);
        var hourPng = ChartToPng(data.HourlyChart, 1600, 700);
        var dailyPng = ChartToPng(data.DailyChart, 1600, 700);
        var srcPng = ChartToPng(data.SourceChart, 1400, 700);

        var summary = data.Summary ?? new AnalysisSummary();
        var fileInfo = data.FileInfo;

        QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36, Unit.Point);
                page.DefaultTextStyle(x => x
                    .FontFamily("Microsoft YaHei UI")
                    .FontSize(10)
                    .LineHeight(1.4f));

                #region 页眉页脚
                page.Header().BorderBottom(1).BorderColor("#CCCCCC").PaddingBottom(5).Row(row =>
                {
                    row.RelativeItem().Text("威纶通HMI报警日志分析报告").FontSize(9).FontColor("#888888");
                    row.ConstantItem(200).AlignRight().Text($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(9).FontColor("#888888");
                });

                page.Footer().BorderTop(1).BorderColor("#CCCCCC").PaddingTop(5).Row(row =>
                {
                    row.ConstantItem(150).Text($"深圳市信宇人科技股份有限公司").FontSize(8).FontColor("#AAAAAA");
                    row.RelativeItem().AlignCenter().Text(text =>
                    {
                        text.CurrentPageNumber().FontSize(9);
                        text.Span(" / ").FontSize(9).FontColor("#AAAAAA");
                        text.TotalPages().FontSize(9);
                    });
                    row.ConstantItem(100).AlignRight().Text($"第 {DateTime.Now:yyyy-MM-dd} 期").FontSize(8).FontColor("#AAAAAA");
                });
                #endregion

                page.Content().PaddingVertical(10).Column(column =>
                {
                    column.Spacing(12);

                    #region 1. 报告标题
                    column.Item().Background("#4472C4").Padding(20).Column(col =>
                    {
                        col.Item().AlignCenter().Text("报警日志智能分析报告")
                            .FontSize(24).Bold().FontColor("#FFFFFF");
                        col.Item().AlignCenter().PaddingTop(8).Text("威纶通HMI报警数据多维度分析诊断")
                            .FontSize(12).FontColor("#CCD6F0");

                        if (fileInfo != null)
                        {
                            col.Item().PaddingTop(12).Row(row =>
                            {
                                row.RelativeItem().AlignCenter().Text($"数据文件: {fileInfo.FileName}").FontSize(10).FontColor("#CCD6F0");
                            });
                            col.Item().AlignCenter().PaddingTop(4).Text($"数据时间范围: {fileInfo.DateRange}")
                                .FontSize(10).FontColor("#CCD6F0");
                            col.Item().AlignCenter().PaddingTop(4).Text($"记录总数: {fileInfo.RecordCount} 条")
                                .FontSize(10).FontColor("#CCD6F0");
                        }
                    });
                    #endregion

                    #region 2. 概览摘要
                    column.Item().SectionHeading("一、概览摘要");
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        AddMetricCell(table, "总报警次数", summary.TotalAlarms.ToString(), "#4472C4");
                        AddMetricCell(table, "报警类型数", summary.UniqueAlarmTypes.ToString(), "#70AD47");
                        AddMetricCell(table, "未恢复报警", summary.UnrecoveredCount.ToString(), "#ED7D31");
                        AddMetricCell(table, "设备来源数", summary.SourceCount.ToString(), "#FFC000");
                    });

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        AddInfoRow(table, "最高频报警", $"{summary.TopAlarm} ({summary.TopAlarmCount} 次)");
                        AddInfoRow(table, "最多报警来源", $"{summary.TopSource} ({summary.TopSourceCount} 次)");
                        AddInfoRow(table, "报警类别数", summary.CategoryCount.ToString());
                        AddInfoRow(table, "累计持续时间", summary.TotalDurationText);
                    });
                    #endregion

                    #region 3. 报警频次分析
                    column.Item().PageBreak();
                    column.Item().SectionHeading("二、报警频次分析 (Top 20)");

                    if (freqPng.Length > 0)
                    {
                        column.Item().Image(freqPng);
                    }

                    column.Item().PaddingTop(8).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(36);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn();
                            columns.ConstantColumn(50);
                            columns.ConstantColumn(50);
                            columns.ConstantColumn(55);
                        });

                        // Header
                        AddHeaderCell(table, "排名");
                        AddHeaderCell(table, "报警描述");
                        AddHeaderCell(table, "类别");
                        AddHeaderCell(table, "来源");
                        AddHeaderCell(table, "次数");
                        AddHeaderCell(table, "占比%");

                        // Data
                        var topFreq = data.Frequency.Take(20).ToList();
                        for (int i = 0; i < topFreq.Count; i++)
                        {
                            var f = topFreq[i];
                            var bg = i % 2 == 0 ? "#F7F9FC" : "#FFFFFF";
                            AddDataCell(table, f.Rank.ToString(), bg, alignCenter: true);
                            AddDataCell(table, $"[{f.AlarmCode}] {f.Description}", bg);
                            AddDataCell(table, f.CategoryName, bg);
                            AddDataCell(table, f.Source, bg, alignCenter: true);
                            AddDataCell(table, f.Count.ToString(), bg, alignCenter: true, bold: i < 3);
                            AddDataCell(table, f.Percentage.ToString("F1"), bg, alignCenter: true);
                        }
                    });
                    #endregion

                    #region 4. 类别分布
                    column.Item().PageBreak();
                    column.Item().SectionHeading("三、报警类别分布");

                    if (catPng.Length > 0)
                    {
                        column.Item().Image(catPng);
                    }

                    column.Item().PaddingTop(8).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(80);
                        });

                        AddHeaderCell(table, "类别名称");
                        AddHeaderCell(table, "报警次数");
                        AddHeaderCell(table, "占比%");

                        for (int i = 0; i < data.Category.Count; i++)
                        {
                            var c = data.Category[i];
                            var bg = i % 2 == 0 ? "#F7F9FC" : "#FFFFFF";
                            AddDataCell(table, c.CategoryName, bg);
                            AddDataCell(table, c.Count.ToString(), bg, alignCenter: true);
                            AddDataCell(table, c.Percentage.ToString("F1"), bg, alignCenter: true);
                        }
                    });
                    #endregion

                    #region 5. 时间分布
                    column.Item().PageBreak();
                    column.Item().SectionHeading("四、报警时间分布 (24小时)");

                    if (hourPng.Length > 0)
                    {
                        column.Item().Image(hourPng);
                    }

                    // 高峰时段表（只显示有报警的时段）
                    var activeHours = data.Hourly.Where(h => h.Count > 0).OrderByDescending(h => h.Count).Take(12).ToList();
                    if (activeHours.Count > 0)
                    {
                        column.Item().PaddingTop(8).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.ConstantColumn(80);
                            });

                            AddHeaderCell(table, "时段");
                            AddHeaderCell(table, "报警次数");

                            for (int i = 0; i < activeHours.Count; i++)
                            {
                                var h = activeHours[i];
                                var bg = i % 2 == 0 ? "#F7F9FC" : "#FFFFFF";
                                AddDataCell(table, h.HourText, bg, alignCenter: true);
                                AddDataCell(table, h.Count.ToString(), bg, alignCenter: true, bold: i < 3);
                            }
                        });
                    }
                    #endregion

                    #region 5b. 按日趋势分析
                    if (data.Daily.Count > 1)
                    {
                        column.Item().PageBreak();
                        column.Item().SectionHeading("四-b、按日报警趋势分析");

                        if (dailyPng.Length > 0)
                        {
                            column.Item().Image(dailyPng);
                        }

                        column.Item().PaddingTop(8).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.ConstantColumn(60);
                                columns.ConstantColumn(80);
                                columns.ConstantColumn(80);
                                columns.ConstantColumn(70);
                            });

                            AddHeaderCell(table, "日期");
                            AddHeaderCell(table, "星期");
                            AddHeaderCell(table, "报警次数");
                            AddHeaderCell(table, "报警类型数");
                            AddHeaderCell(table, "未恢复数");

                            for (int i = 0; i < data.Daily.Count; i++)
                            {
                                var d = data.Daily[i];
                                var bg = i % 2 == 0 ? "#F7F9FC" : "#FFFFFF";
                                AddDataCell(table, d.DateFullText, bg);
                                AddDataCell(table, d.DayOfWeek, bg, alignCenter: true);
                                AddDataCell(table, d.Count.ToString(), bg, alignCenter: true, bold: d.Count > 0);
                                AddDataCell(table, d.UniqueAlarmTypes.ToString(), bg, alignCenter: true);
                                AddDataCell(table, d.UnrecoveredCount.ToString(), bg, alignCenter: true);
                            }
                        });
                    }
                    #endregion

                    #region 6. 设备来源分析
                    column.Item().PageBreak();
                    column.Item().SectionHeading("五、设备来源分析");

                    if (srcPng.Length > 0)
                    {
                        column.Item().Image(srcPng);
                    }

                    column.Item().PaddingTop(8).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);
                            columns.ConstantColumn(70);
                            columns.ConstantColumn(60);
                            columns.ConstantColumn(70);
                        });

                        AddHeaderCell(table, "设备来源");
                        AddHeaderCell(table, "报警次数");
                        AddHeaderCell(table, "占比%");
                        AddHeaderCell(table, "报警类型数");

                        for (int i = 0; i < data.Source.Count; i++)
                        {
                            var s = data.Source[i];
                            var bg = i % 2 == 0 ? "#F7F9FC" : "#FFFFFF";
                            AddDataCell(table, s.Source, bg, bold: i == 0);
                            AddDataCell(table, s.Count.ToString(), bg, alignCenter: true);
                            AddDataCell(table, s.Percentage.ToString("F1"), bg, alignCenter: true);
                            AddDataCell(table, s.UniqueAlarmTypes.ToString(), bg, alignCenter: true);
                        }
                    });
                    #endregion

                    #region 7. 持续时间分析
                    column.Item().PageBreak();
                    column.Item().SectionHeading("六、持续时间分析 (Top 20)");

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.ConstantColumn(50);
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(80);
                        });

                        AddHeaderCell(table, "报警描述");
                        AddHeaderCell(table, "次数");
                        AddHeaderCell(table, "最长持续");
                        AddHeaderCell(table, "平均持续");
                        AddHeaderCell(table, "累计持续");

                        var topDur = data.Duration.Take(20).ToList();
                        for (int i = 0; i < topDur.Count; i++)
                        {
                            var d = topDur[i];
                            var bg = i % 2 == 0 ? "#F7F9FC" : "#FFFFFF";
                            AddDataCell(table, $"[{d.AlarmCode}] {d.Description}", bg);
                            AddDataCell(table, d.Occurrences.ToString(), bg, alignCenter: true);
                            AddDataCell(table, d.MaxDurationText, bg, alignCenter: true);
                            AddDataCell(table, d.AvgDurationText, bg, alignCenter: true);
                            AddDataCell(table, d.TotalDurationText, bg, alignCenter: true, bold: i == 0);
                        }
                    });
                    #endregion

                    #region 8. 未恢复报警
                    if (data.Unrecovered.Count > 0)
                    {
                        column.Item().PaddingTop(10).SectionHeading("七、未恢复报警列表");

                        column.Item().Background("#FFF3CD").Padding(8)
                            .Text($"当前共有 {data.Unrecovered.Count} 条报警未记录恢复时间，可能仍处于持续状态。")
                            .FontSize(9).FontColor("#856404");

                        column.Item().PaddingTop(6).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(120);
                                columns.ConstantColumn(55);
                                columns.RelativeColumn(3);
                                columns.ConstantColumn(80);
                                columns.ConstantColumn(55);
                            });

                            AddHeaderCell(table, "触发时间");
                            AddHeaderCell(table, "报警码");
                            AddHeaderCell(table, "报警描述");
                            AddHeaderCell(table, "来源");
                            AddHeaderCell(table, "次数");

                            var topUnrec = data.Unrecovered.Take(30).ToList();
                            for (int i = 0; i < topUnrec.Count; i++)
                            {
                                var r = topUnrec[i];
                                var bg = i % 2 == 0 ? "#FFF9E6" : "#FFFFFF";
                                AddDataCell(table, r.TriggerDateTimeText, bg, fontSize: 8);
                                AddDataCell(table, r.AlarmCode, bg, alignCenter: true);
                                AddDataCell(table, r.Description, bg);
                                AddDataCell(table, r.Source, bg, alignCenter: true);
                                AddDataCell(table, r.OccurrenceCount.ToString(), bg, alignCenter: true);
                            }
                        });
                    }
                    #endregion

                    #region 9. 关联分析
                    if (data.Correlation.Count > 0)
                    {
                        column.Item().PageBreak();
                        column.Item().SectionHeading("八、报警关联分析");

                        column.Item().Text("检测在2分钟时间窗口内同时出现的报警对，揭示报警之间的共现关系。消除根因可同时减少多个报警。")
                            .FontSize(9).FontColor("#666666");

                        column.Item().PaddingTop(6).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(3);
                                columns.ConstantColumn(70);
                                columns.ConstantColumn(60);
                            });

                            AddHeaderCell(table, "报警1");
                            AddHeaderCell(table, "报警2");
                            AddHeaderCell(table, "共现次数");
                            AddHeaderCell(table, "置信度%");

                            for (int i = 0; i < data.Correlation.Count; i++)
                            {
                                var c = data.Correlation[i];
                                var bg = i % 2 == 0 ? "#F7F9FC" : "#FFFFFF";
                                AddDataCell(table, c.Alarm1, bg, fontSize: 8);
                                AddDataCell(table, c.Alarm2, bg, fontSize: 8);
                                AddDataCell(table, c.CoOccurrenceCount.ToString(), bg, alignCenter: true, bold: i == 0);
                                AddDataCell(table, c.Confidence.ToString("F1"), bg, alignCenter: true);
                            }
                        });
                    }
                    #endregion

                    #region 10. 智能诊断建议
                    if (data.Suggestions.Count > 0)
                    {
                        column.Item().PageBreak();
                        column.Item().SectionHeading("九、智能诊断建议");

                        column.Item().Text("基于报警频次、持续时间、关联模式、时间分布等多维度分析，自动生成的诊断建议。")
                            .FontSize(9).FontColor("#666666");

                        column.Spacing(8);
                        for (int i = 0; i < data.Suggestions.Count; i++)
                        {
                            var s = data.Suggestions[i];
                            var (bgColor, borderColor) = s.Level switch
                            {
                                SuggestionLevel.Critical => ("#FFC7CE", "#DC3545"),
                                SuggestionLevel.Warning => ("#FFEB9C", "#FFC107"),
                                _ => ("#C6EFCE", "#28A745")
                            };

                            column.Item().Background(bgColor).Border(1).BorderColor(borderColor).Padding(10).Column(col =>
                            {
                                col.Item().Row(row =>
                                {
                                    row.ConstantItem(50).Background(borderColor).Padding(3)
                                        .AlignCenter().AlignMiddle().Text(s.LevelText)
                                        .FontSize(9).Bold().FontColor("#FFFFFF");
                                    row.RelativeItem().PaddingLeft(8).AlignMiddle().Text(s.Title)
                                        .FontSize(12).Bold().FontColor("#333333");
                                });

                                col.Item().PaddingTop(6).Text(s.Description).FontSize(10).FontColor("#444444");

                                col.Item().PaddingTop(6).Background("#FFFFFF").Padding(8)
                                    .Text(s.Detail).FontSize(9).FontColor("#555555");
                            });
                        }
                    }
                    #endregion

                    #region 报告尾注
                    column.Item().PaddingTop(20).AlignCenter().BorderColor("#CCCCCC")
                        .BorderTop(1).PaddingTop(8)
                        .Text("本报告由深圳市信宇人科技股份有限公司\nHMI报警日志智能分析工具自动生成\n数据仅供参考，实际维护请结合现场情况判断")
                        .FontSize(8).FontColor("#999999");
                    #endregion
                });
            });
        }).GeneratePdf(filePath);
    }

    // ══════════════════════════════════════════════════════════════
    //  PDF 辅助方法
    // ══════════════════════════════════════════════════════════════

    private static void AddMetricCell(QuestPDF.Fluent.TableDescriptor table, string label, string value, string color)
    {
        table.Cell().Background(color).Padding(10).Column(col =>
        {
            col.Item().Text(label).FontSize(9).FontColor("#FFFFFF");
            col.Item().Text(value).FontSize(22).Bold().FontColor("#FFFFFF");
        });
    }

    private static void AddInfoRow(QuestPDF.Fluent.TableDescriptor table, string label, string value)
    {
        table.Cell().Background("#F2F6FC").Border(1).BorderColor("#E0E8F0").Padding(6)
            .Text(text =>
            {
                text.Span($"{label}: ").Bold().FontSize(10).FontColor("#333333");
                text.Span(value).FontSize(10).FontColor("#555555");
            });
    }

    private static void AddHeaderCell(QuestPDF.Fluent.TableDescriptor table, string text)
    {
        table.Cell().Background("#4472C4").Border(1).BorderColor("#4472C4").Padding(5)
            .AlignCenter().Text(text).FontSize(9).Bold().FontColor("#FFFFFF");
    }

    private static void AddDataCell(QuestPDF.Fluent.TableDescriptor table, string text, string bgColor,
        bool alignCenter = false, bool bold = false, int fontSize = 9)
    {
        var cell = table.Cell().Background(bgColor).Border(1).BorderColor("#D9D9D9").Padding(4);
        if (alignCenter) cell = cell.AlignCenter();
        var txt = cell.Text(text).FontSize(fontSize).FontColor("#333333");
        if (bold) txt = txt.Bold();
    }

    // ══════════════════════════════════════════════════════════════
    //  Word (.docx) 报告导出
    // ══════════════════════════════════════════════════════════════

    public void ExportWord(string filePath, ReportData data)
    {
        var summary = data.Summary ?? new AnalysisSummary();
        var fileInfo = data.FileInfo;

        // 预导出图表 PNG
        var freqPng = ChartToPng(data.FrequencyChart, 1600, 800);
        var catPng = ChartToPng(data.CategoryChart, 1400, 800);
        var hourPng = ChartToPng(data.HourlyChart, 1600, 700);
        var dailyPng = ChartToPng(data.DailyChart, 1600, 700);
        var srcPng = ChartToPng(data.SourceChart, 1400, 700);

        using var wordDoc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = wordDoc.AddMainDocumentPart();
        mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new Body());
        var body = mainPart.Document.Body!;

        // 页面设置 (A4)
        var sectPr = new SectionProperties(
            new DocumentFormat.OpenXml.Wordprocessing.PageSize { Width = 11906, Height = 16838 },
            new PageMargin { Top = 1134, Right = 1134, Bottom = 1134, Left = 1134, Header = 720, Footer = 720 }
        );
        body.AppendChild(sectPr);

        int imgCounter = 0;

        #region 1. 报告标题
        body.AppendChild(MakeParagraph("威纶通HMI报警日志智能分析报告", bold: true, fontSize: 36, color: "4472C4", align: JustificationValues.Center));
        body.AppendChild(MakeParagraph("—— 报警数据多维度分析诊断报告 ——", fontSize: 18, color: "888888", align: JustificationValues.Center, spaceAfter: 200));

        if (fileInfo != null)
        {
            body.AppendChild(MakeInfoTable(new[]
            {
                ("数据文件", fileInfo.FileName),
                ("记录总数", $"{fileInfo.RecordCount} 条"),
                ("时间范围", fileInfo.DateRange),
                ("导入时间", fileInfo.ImportTime.ToString("yyyy-MM-dd HH:mm")),
                ("报告生成", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            }));
            body.AppendChild(MakeEmptyParagraph(100));
        }
        #endregion

        #region 2. 概览摘要
        body.AppendChild(MakeHeading("一、概览摘要", 1));

        body.AppendChild(MakeKeyValueTable(new[,]
        {
            { "总报警次数", summary.TotalAlarms.ToString(), "报警类型数", summary.UniqueAlarmTypes.ToString() },
            { "未恢复报警", summary.UnrecoveredCount.ToString(), "设备来源数", summary.SourceCount.ToString() },
            { "报警类别数", summary.CategoryCount.ToString(), "累计持续时间", summary.TotalDurationText },
            { "最高频报警", $"{summary.TopAlarm} ({summary.TopAlarmCount}次)", "最多来源", $"{summary.TopSource} ({summary.TopSourceCount}次)" },
        }));
        body.AppendChild(MakeEmptyParagraph(100));
        #endregion

        #region 3. 报警频次分析
        body.AppendChild(MakeHeading("二、报警频次分析 (Top 20)", 1));

        if (freqPng.Length > 0)
        {
            body.AppendChild(MakeImageParagraph(mainPart, freqPng, 5486400, 2743200, ++imgCounter));
            body.AppendChild(MakeEmptyParagraph(60));
        }

        var freqHeaders = new[] { "排名", "报警描述", "类别", "来源", "次数", "占比%" };
        var freqColWidths = new[] { 600, 3500, 1800, 1000, 700, 700 };
        var freqRows = data.Frequency.Take(20).Select(f => new[]
        {
            f.Rank.ToString(),
            $"[{f.AlarmCode}] {f.Description}",
            f.CategoryName,
            f.Source,
            f.Count.ToString(),
            f.Percentage.ToString("F1")
        }).ToList();
        body.AppendChild(MakeTable(freqHeaders, freqRows, freqColWidths));
        body.AppendChild(MakeEmptyParagraph(100));
        #endregion

        #region 4. 类别分布
        body.AppendChild(MakeHeading("三、报警类别分布", 1));

        if (catPng.Length > 0)
        {
            body.AppendChild(MakeImageParagraph(mainPart, catPng, 5486400, 3135600, ++imgCounter));
            body.AppendChild(MakeEmptyParagraph(60));
        }

        var catHeaders = new[] { "类别名称", "报警次数", "占比%" };
        var catRows = data.Category.Select(c => new[]
        {
            c.CategoryName, c.Count.ToString(), c.Percentage.ToString("F1")
        }).ToList();
        body.AppendChild(MakeTable(catHeaders, catRows, new[] { 4500, 1800, 1500 }));
        body.AppendChild(MakeEmptyParagraph(100));
        #endregion

        #region 5. 时间分布
        body.AppendChild(MakeHeading("四、报警时间分布 (24小时)", 1));

        if (hourPng.Length > 0)
        {
            body.AppendChild(MakeImageParagraph(mainPart, hourPng, 5486400, 2400300, ++imgCounter));
            body.AppendChild(MakeEmptyParagraph(60));
        }

        var activeHours = data.Hourly.Where(h => h.Count > 0).OrderByDescending(h => h.Count).Take(12).ToList();
        if (activeHours.Count > 0)
        {
            var hourHeaders = new[] { "时段", "报警次数" };
            var hourRows = activeHours.Select(h => new[] { h.HourText, h.Count.ToString() }).ToList();
            body.AppendChild(MakeTable(hourHeaders, hourRows, new[] { 4000, 2000 }));
        }
        body.AppendChild(MakeEmptyParagraph(100));
        #endregion

        #region 5b. 按日趋势分析
        if (data.Daily.Count > 1)
        {
            body.AppendChild(MakeHeading("四-b、按日报警趋势分析", 1));

            if (dailyPng.Length > 0)
            {
                body.AppendChild(MakeImageParagraph(mainPart, dailyPng, 5486400, 2400300, ++imgCounter));
                body.AppendChild(MakeEmptyParagraph(60));
            }

            var dailyHeaders = new[] { "日期", "星期", "报警次数", "类型数", "未恢复" };
            var dailyRows = data.Daily.Select(d => new[]
            {
                d.DateFullText, d.DayOfWeek, d.Count.ToString(), d.UniqueAlarmTypes.ToString(), d.UnrecoveredCount.ToString()
            }).ToList();
            body.AppendChild(MakeTable(dailyHeaders, dailyRows, new[] { 2000, 800, 1200, 1000, 1000 }));
            body.AppendChild(MakeEmptyParagraph(100));
        }
        #endregion

        #region 6. 设备来源分析
        body.AppendChild(MakeHeading("五、设备来源分析", 1));

        if (srcPng.Length > 0)
        {
            body.AppendChild(MakeImageParagraph(mainPart, srcPng, 5486400, 2743200, ++imgCounter));
            body.AppendChild(MakeEmptyParagraph(60));
        }

        var srcHeaders = new[] { "设备来源", "报警次数", "占比%", "类型数" };
        var srcRows = data.Source.Select(s => new[]
        {
            s.Source, s.Count.ToString(), s.Percentage.ToString("F1"), s.UniqueAlarmTypes.ToString()
        }).ToList();
        body.AppendChild(MakeTable(srcHeaders, srcRows, new[] { 2500, 1500, 1200, 1200 }));
        body.AppendChild(MakeEmptyParagraph(100));
        #endregion

        #region 7. 持续时间分析
        body.AppendChild(MakeHeading("六、持续时间分析 (Top 20)", 1));

        var durHeaders = new[] { "报警描述", "次数", "最长持续", "平均持续", "累计持续" };
        var durColWidths = new[] { 3000, 700, 1200, 1200, 1200 };
        var durRows = data.Duration.Take(20).Select(d => new[]
        {
            $"[{d.AlarmCode}] {d.Description}",
            d.Occurrences.ToString(),
            d.MaxDurationText,
            d.AvgDurationText,
            d.TotalDurationText
        }).ToList();
        body.AppendChild(MakeTable(durHeaders, durRows, durColWidths));
        body.AppendChild(MakeEmptyParagraph(100));
        #endregion

        #region 8. 未恢复报警
        if (data.Unrecovered.Count > 0)
        {
            body.AppendChild(MakeHeading("七、未恢复报警列表", 1));
            body.AppendChild(MakeParagraph($"当前共有 {data.Unrecovered.Count} 条报警未记录恢复时间，可能仍处于持续状态。",
                fontSize: 20, color: "856404", spaceAfter: 100));

            var unrecHeaders = new[] { "触发时间", "报警码", "报警描述", "来源", "次数" };
            var unrecColWidths = new[] { 1800, 700, 2800, 1200, 700 };
            var unrecRows = data.Unrecovered.Take(30).Select(r => new[]
            {
                r.TriggerDateTimeText,
                r.AlarmCode,
                r.Description,
                r.Source,
                r.OccurrenceCount.ToString()
            }).ToList();
            body.AppendChild(MakeTable(unrecHeaders, unrecRows, unrecColWidths, highlightFirst: true));
            body.AppendChild(MakeEmptyParagraph(100));
        }
        #endregion

        #region 9. 关联分析
        if (data.Correlation.Count > 0)
        {
            body.AppendChild(MakeHeading("八、报警关联分析", 1));
            body.AppendChild(MakeParagraph("检测在2分钟时间窗口内同时出现的报警对，揭示报警之间的共现关系。消除根因可同时减少多个报警。",
                fontSize: 18, color: "666666", spaceAfter: 100));

            var corrHeaders = new[] { "报警1", "报警2", "共现次数", "置信度%" };
            var corrColWidths = new[] { 2500, 2500, 1000, 1000 };
            var corrRows = data.Correlation.Select(c => new[]
            {
                c.Alarm1, c.Alarm2, c.CoOccurrenceCount.ToString(), c.Confidence.ToString("F1")
            }).ToList();
            body.AppendChild(MakeTable(corrHeaders, corrRows, corrColWidths));
            body.AppendChild(MakeEmptyParagraph(100));
        }
        #endregion

        #region 10. 智能诊断建议
        if (data.Suggestions.Count > 0)
        {
            body.AppendChild(MakeHeading("九、智能诊断建议", 1));
            body.AppendChild(MakeParagraph("基于报警频次、持续时间、关联模式、时间分布等多维度分析，自动生成的诊断建议。",
                fontSize: 18, color: "666666", spaceAfter: 150));

            for (int i = 0; i < data.Suggestions.Count; i++)
            {
                var s = data.Suggestions[i];
                var (bgColor, borderColor) = s.Level switch
                {
                    SuggestionLevel.Critical => ("FFC7CE", "DC3545"),
                    SuggestionLevel.Warning => ("FFEB9C", "FFC107"),
                    _ => ("C6EFCE", "28A745")
                };

                // 级别 + 标题
                body.AppendChild(MakeSuggestionHeader(s.LevelText, s.Title, bgColor, borderColor));
                // 描述
                body.AppendChild(MakeParagraph(s.Description, fontSize: 20, color: "444444", spaceAfter: 60));
                // 详细分析
                foreach (var line in s.Detail.Split('\n'))
                {
                    body.AppendChild(MakeParagraph(line, fontSize: 18, color: "555555", spaceAfter: 20));
                }
                body.AppendChild(MakeEmptyParagraph(120));
            }
        }
        #endregion

        #region 报告尾注
        body.AppendChild(MakeEmptyParagraph(200));
        body.AppendChild(MakeParagraph("本报告由威纶通HMI报警日志智能分析工具自动生成", fontSize: 16, color: "999999", align: JustificationValues.Center, spaceAfter: 40));
        body.AppendChild(MakeParagraph("数据仅供参考，实际维护请结合现场情况判断", fontSize: 16, color: "999999", align: JustificationValues.Center));
        #endregion

        mainPart.Document.Save();
    }

    // ══════════════════════════════════════════════════════════════
    //  Word 辅助方法
    // ══════════════════════════════════════════════════════════════

    private static readonly string FontName = "Microsoft YaHei UI";

    private static RunProperties MakeRunProps(bool bold = false, int fontSizeHalfPt = 22, string color = "333333")
    {
        var props = new RunProperties
        {
            FontSize = new FontSize { Val = fontSizeHalfPt.ToString() },
            Color = new Color { Val = color },
            RunFonts = new RunFonts { Ascii = FontName, EastAsia = FontName, HighAnsi = FontName }
        };
        if (bold) props.Bold = new Bold();
        return props;
    }

    private static Paragraph MakeParagraph(string text, bool bold = false, int fontSize = 22,
        string color = "333333", JustificationValues? align = null, int spaceAfter = 80)
    {
        var paraProps = new ParagraphProperties
        {
            SpacingBetweenLines = new SpacingBetweenLines { After = spaceAfter.ToString(), Line = "276", LineRule = LineSpacingRuleValues.Auto }
        };
        if (align.HasValue)
            paraProps.Justification = new Justification { Val = align.Value };

        return new Paragraph(
            paraProps,
            new Run(MakeRunProps(bold, fontSize, color), new Text(text) { Space = SpaceProcessingModeValues.Preserve })
        );
    }

    private static Paragraph MakeEmptyParagraph(int spaceAfter = 80)
    {
        return new Paragraph(
            new ParagraphProperties
            {
                SpacingBetweenLines = new SpacingBetweenLines { After = spaceAfter.ToString() }
            }
        );
    }

    private static Paragraph MakeHeading(string text, int level)
    {
        var (fontSize, color) = level switch
        {
            1 => (28, "2F5496"),
            2 => (24, "2F5496"),
            _ => (22, "333333")
        };
        return new Paragraph(
            new ParagraphProperties
            {
                SpacingBetweenLines = new SpacingBetweenLines { Before = "240", After = "120", Line = "276", LineRule = LineSpacingRuleValues.Auto },
                KeepNext = new KeepNext()
            },
            new Run(MakeRunProps(true, fontSize, color), new Text(text))
        );
    }

    /// <summary>
    /// 创建信息键值对表格（2列）
    /// </summary>
    private static Table MakeInfoTable((string label, string value)[] items)
    {
        var table = new Table(
            MakeTableProperties(),
            new TableGrid(new GridColumn { Width = "2400" }, new GridColumn { Width = "4800" })
        );

        foreach (var (label, value) in items)
        {
            var row = new TableRow();
            row.AppendChild(MakeTableCell(label, "D9E2F3", bold: true, fontSize: 20, width: 2400));
            row.AppendChild(MakeTableCell(value, "FFFFFF", fontSize: 20, width: 4800));
            table.AppendChild(row);
        }
        return table;
    }

    /// <summary>
    /// 创建 2x2 键值对网格表格
    /// </summary>
    private static Table MakeKeyValueTable(string[,] grid)
    {
        var rows = grid.GetLength(0);
        var table = new Table(
            MakeTableProperties(),
            new TableGrid(
                new GridColumn { Width = "2000" },
                new GridColumn { Width = "2600" },
                new GridColumn { Width = "2000" },
                new GridColumn { Width = "2600" }
            )
        );

        for (int r = 0; r < rows; r++)
        {
            var row = new TableRow();
            row.AppendChild(MakeTableCell(grid[r, 0], "D9E2F3", bold: true, fontSize: 20, width: 2000));
            row.AppendChild(MakeTableCell(grid[r, 1], "FFFFFF", fontSize: 20, width: 2600));
            row.AppendChild(MakeTableCell(grid[r, 2], "D9E2F3", bold: true, fontSize: 20, width: 2000));
            row.AppendChild(MakeTableCell(grid[r, 3], "FFFFFF", fontSize: 20, width: 2600));
            table.AppendChild(row);
        }
        return table;
    }

    /// <summary>
    /// 创建带表头的数据表格
    /// </summary>
    private static Table MakeTable(string[] headers, List<string[]> rows, int[] colWidths, bool highlightFirst = false)
    {
        var table = new Table(
            MakeTableProperties(),
            new TableGrid(colWidths.Select(w => new GridColumn { Width = w.ToString() }).Cast<GridColumn>().ToArray())
        );

        // Header row
        var headerRow = new TableRow();
        var headerRowProps = new TableRowProperties();
        headerRowProps.AppendChild(new TableHeader());
        headerRow.AppendChild(headerRowProps);
        for (int c = 0; c < headers.Length; c++)
        {
            headerRow.AppendChild(MakeTableCell(headers[c], "4472C4", bold: true, fontSize: 20, color: "FFFFFF", width: colWidths[c], alignCenter: true));
        }
        table.AppendChild(headerRow);

        // Data rows
        for (int i = 0; i < rows.Count; i++)
        {
            var row = new TableRow();
            var bg = i % 2 == 0 ? "F2F6FC" : "FFFFFF";
            for (int c = 0; c < rows[i].Length; c++)
            {
                var cellBg = highlightFirst && i == 0 ? "FFF3CD" : bg;
                var bold = highlightFirst && i == 0;
                row.AppendChild(MakeTableCell(rows[i][c], cellBg, bold: bold, fontSize: 18, width: colWidths[c]));
            }
            table.AppendChild(row);
        }
        return table;
    }

    private static TableProperties MakeTableProperties()
    {
        return new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 1, Color = "CCCCCC" },
                new BottomBorder { Val = BorderValues.Single, Size = 1, Color = "CCCCCC" },
                new LeftBorder { Val = BorderValues.Single, Size = 1, Color = "CCCCCC" },
                new RightBorder { Val = BorderValues.Single, Size = 1, Color = "CCCCCC" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 1, Color = "CCCCCC" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 1, Color = "CCCCCC" }
            ),
            new TableLayout { Type = TableLayoutValues.Fixed }
        );
    }

    private static TableCell MakeTableCell(string text, string bgColor, bool bold = false,
        int fontSize = 18, string color = "333333", int width = 0, bool alignCenter = false)
    {
        var cellProps = new TableCellProperties();
        cellProps.AppendChild(new Shading { Fill = bgColor, Val = ShadingPatternValues.Clear, Color = "auto" });

        var margins = new TableCellMargin();
        margins.AppendChild(new TopMargin { Width = "30", Type = TableWidthUnitValues.Dxa });
        margins.AppendChild(new BottomMargin { Width = "30", Type = TableWidthUnitValues.Dxa });
        margins.AppendChild(new LeftMargin { Width = "60", Type = TableWidthUnitValues.Dxa });
        margins.AppendChild(new RightMargin { Width = "60", Type = TableWidthUnitValues.Dxa });
        cellProps.AppendChild(margins);

        if (width > 0)
            cellProps.AppendChild(new TableCellWidth { Width = width.ToString(), Type = TableWidthUnitValues.Dxa });

        var paraProps = new ParagraphProperties();
        paraProps.AppendChild(new SpacingBetweenLines { Before = "0", After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto });
        if (alignCenter)
            paraProps.AppendChild(new Justification { Val = JustificationValues.Center });

        var cell = new TableCell(cellProps);
        cell.AppendChild(new Paragraph(
            paraProps,
            new Run(MakeRunProps(bold, fontSize, color), new Text(text) { Space = SpaceProcessingModeValues.Preserve })
        ));
        return cell;
    }

    /// <summary>
    /// 创建诊断建议标题行（级别标签 + 标题文本）作为 Table 返回
    /// </summary>
    private static Table MakeSuggestionHeader(string level, string title, string bgColor, string borderColor)
    {
        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 1, Color = borderColor },
                new BottomBorder { Val = BorderValues.Single, Size = 1, Color = borderColor },
                new LeftBorder { Val = BorderValues.Single, Size = 1, Color = borderColor },
                new RightBorder { Val = BorderValues.Single, Size = 1, Color = borderColor }
            )
        ));
        table.AppendChild(new TableGrid(new GridColumn { Width = "900" }, new GridColumn { Width = "4100" }));

        var row = new TableRow();

        // Level cell
        var cell1 = new TableCell();
        var props1 = new TableCellProperties();
        props1.AppendChild(new Shading { Fill = borderColor, Val = ShadingPatternValues.Clear, Color = "auto" });
        var margins1 = new TableCellMargin();
        margins1.AppendChild(new TopMargin { Width = "40", Type = TableWidthUnitValues.Dxa });
        margins1.AppendChild(new BottomMargin { Width = "40", Type = TableWidthUnitValues.Dxa });
        margins1.AppendChild(new LeftMargin { Width = "60", Type = TableWidthUnitValues.Dxa });
        margins1.AppendChild(new RightMargin { Width = "60", Type = TableWidthUnitValues.Dxa });
        props1.AppendChild(margins1);
        props1.AppendChild(new TableCellWidth { Width = "900", Type = TableWidthUnitValues.Dxa });
        cell1.AppendChild(props1);
        cell1.AppendChild(new Paragraph(
            new ParagraphProperties(
                new Justification { Val = JustificationValues.Center },
                new SpacingBetweenLines { Before = "0", After = "0" }
            ),
            new Run(MakeRunProps(true, 18, "FFFFFF"), new Text(level))
        ));
        row.AppendChild(cell1);

        // Title cell
        var cell2 = new TableCell();
        var props2 = new TableCellProperties();
        props2.AppendChild(new Shading { Fill = bgColor, Val = ShadingPatternValues.Clear, Color = "auto" });
        var margins2 = new TableCellMargin();
        margins2.AppendChild(new TopMargin { Width = "40", Type = TableWidthUnitValues.Dxa });
        margins2.AppendChild(new BottomMargin { Width = "40", Type = TableWidthUnitValues.Dxa });
        margins2.AppendChild(new LeftMargin { Width = "100", Type = TableWidthUnitValues.Dxa });
        margins2.AppendChild(new RightMargin { Width = "60", Type = TableWidthUnitValues.Dxa });
        props2.AppendChild(margins2);
        props2.AppendChild(new TableCellWidth { Width = "4100", Type = TableWidthUnitValues.Dxa });
        cell2.AppendChild(props2);
        cell2.AppendChild(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" }),
            new Run(MakeRunProps(true, 24, "333333"), new Text(title))
        ));
        row.AppendChild(cell2);

        table.AppendChild(row);
        return table;
    }

    /// <summary>
    /// 创建包含内嵌图片的段落
    /// </summary>
    private static Paragraph MakeImageParagraph(MainDocumentPart mainPart, byte[] imageBytes,
        long widthEmu, long heightEmu, int imageId)
    {
        var imagePart = mainPart.AddImagePart(ImagePartType.Png);
        using (var ms = new MemoryStream(imageBytes))
        {
            imagePart.FeedData(ms);
        }
        var rId = mainPart.GetIdOfPart(imagePart);

        var drawing = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = widthEmu, Cy = heightEmu },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = (UInt32Value)(uint)imageId, Name = $"Chart {imageId}" },
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = (UInt32Value)(uint)imageId, Name = $"Chart {imageId}" },
                                new PIC.NonVisualPictureDrawingProperties()
                            ),
                            new PIC.BlipFill(
                                new A.Blip { Embed = rId },
                                new A.Stretch(new A.FillRectangle())
                            ),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = widthEmu, Cy = heightEmu }
                                ),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }
                            )
                        )
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
                )
            )
        );

        return new Paragraph(
            new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
            new Run(drawing)
        );
    }
}
