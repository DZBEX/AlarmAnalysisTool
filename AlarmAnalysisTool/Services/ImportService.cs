using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AlarmAnalysisTool.Models;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;

namespace AlarmAnalysisTool.Services;

/// <summary>
/// 数据导入服务：支持CSV和Excel导入，支持自定义列映射
/// </summary>
public class ImportService
{
    static ImportService()
    {
        // 注册中文编码提供程序，使 GB18030/GBK(936) 等编码可用
        // 工业HMI导出的CSV常为GBK编码，而非UTF-8
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // 预编译正则，避免每条记录重复编译模式
    private static readonly Regex CategoryNameRegex = new(@"\[(.+?)\]", RegexOptions.Compiled);
    private static readonly Regex SourceMainRegex = new(@"^(EM\d+)", RegexOptions.Compiled);

    /// <summary>
    /// 从CSV文件导入（自动检测列映射）— 异步版本，不阻塞UI线程
    /// </summary>
    public Task<(List<AlarmRecord> records, ColumnMapping mapping, List<string> headers)> ImportCsvAsync(string filePath, IProgress<string>? progress = null)
        => Task.Run(() => ImportCsv(filePath, progress));

    /// <summary>
    /// 使用指定列映射从CSV导入 — 异步版本
    /// </summary>
    public Task<List<AlarmRecord>> ImportCsvWithMappingAsync(string filePath, ColumnMapping mapping)
        => Task.Run(() => ImportCsvWithMapping(filePath, mapping));

    /// <summary>
    /// 使用指定列映射从Excel导入 — 异步版本
    /// </summary>
    public Task<List<AlarmRecord>> ImportExcelAsync(string filePath, ColumnMapping mapping, List<string> headers, IProgress<string>? progress = null)
        => Task.Run(() => ImportExcel(filePath, mapping, headers, progress));

    /// <summary>
    /// 从CSV文件导入（自动检测列映射）
    /// </summary>
    public (List<AlarmRecord> records, ColumnMapping mapping, List<string> headers) ImportCsv(string filePath, IProgress<string>? progress = null)
    {
        progress?.Report("正在读取文件...");
        var lines = ReadAllLinesAuto(filePath);
        if (lines.Length < 2)
            return (new(), new(), new());

        progress?.Report("正在检测列映射...");
        // 检测分隔符
        char delimiter = DetectDelimiter(lines[0]);
        var headers = ParseCsvLine(lines[0], delimiter);

        // 自动检测列映射
        var mapping = AutoDetectMapping(headers);

        progress?.Report($"正在解析 {lines.Length - 1} 行数据...");
        var records = ParseRecords(lines, delimiter, mapping, headers);
        return (records, mapping, headers);
    }

    /// <summary>
    /// 从Excel文件导入（返回表头用于列映射）
    /// </summary>
    public (List<string> headers, List<List<string>> previewRows, int rowCount) ReadExcelPreview(string filePath, int previewCount = 20)
    {
        using var wb = new XLWorkbook(filePath);
        var ws = wb.Worksheet(1);
        var headers = new List<string>();
        var previewRows = new List<List<string>>();
        int rowCount = 0;

        // 读取表头
        var headerRow = ws.Row(1);
        int colCount = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (int c = 1; c <= colCount; c++)
        {
            headers.Add(headerRow.Cell(c).GetString().Trim());
        }

        // 读取预览行
        rowCount = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (int r = 2; r <= Math.Min(previewCount + 1, rowCount); r++)
        {
            var row = new List<string>();
            for (int c = 1; c <= colCount; c++)
            {
                row.Add(ws.Row(r).Cell(c).GetString().Trim());
            }
            previewRows.Add(row);
        }

        return (headers, previewRows, rowCount - 1);
    }

    /// <summary>
    /// 使用指定列映射从Excel导入
    /// </summary>
    public List<AlarmRecord> ImportExcel(string filePath, ColumnMapping mapping, List<string> headers, IProgress<string>? progress = null)
    {
        progress?.Report("正在打开Excel文件...");
        using var wb = new XLWorkbook(filePath);
        var ws = wb.Worksheet(1);
        var records = new List<AlarmRecord>();

        int colCount = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        int rowCount = ws.LastRowUsed()?.RowNumber() ?? 0;

        // 构建列索引映射
        var colIndex = BuildColumnIndex(mapping, headers);

        progress?.Report($"正在解析 {rowCount - 1} 行数据...");
        for (int r = 2; r <= rowCount; r++)
        {
            var row = ws.Row(r);
            var values = new string[colCount + 1];
            for (int c = 1; c <= colCount; c++)
            {
                values[c] = row.Cell(c).GetString().Trim();
            }

            var record = CreateRecord(values, colIndex);
            if (record != null)
                records.Add(record);
        }

        return records;
    }

    /// <summary>
    /// 使用指定列映射从CSV导入
    /// </summary>
    public List<AlarmRecord> ImportCsvWithMapping(string filePath, ColumnMapping mapping)
    {
        var lines = ReadAllLinesAuto(filePath);
        if (lines.Length < 2) return new();

        char delimiter = DetectDelimiter(lines[0]);
        var headers = ParseCsvLine(lines[0], delimiter);
        return ParseRecords(lines, delimiter, mapping, headers);
    }

    /// <summary>
    /// 从SQLite数据库导入（威纶通HMI event.db格式）— 异步版本
    /// </summary>
    public Task<List<AlarmRecord>> ImportSqliteAsync(string filePath, IProgress<string>? progress = null)
        => Task.Run(() => ImportSqlite(filePath, progress));

    /// <summary>
    /// 从SQLite数据库导入（带时间范围过滤）— 异步版本
    /// </summary>
    public Task<List<AlarmRecord>> ImportSqliteAsync(string filePath, DateTime? startDate, DateTime? endDate, IProgress<string>? progress = null)
        => Task.Run(() => ImportSqlite(filePath, startDate, endDate, progress));

    /// <summary>
    /// 批量导入多个SQLite数据库文件（带时间范围过滤）— 异步版本
    /// </summary>
    public Task<List<AlarmRecord>> ImportMultipleSqliteAsync(string[] filePaths, DateTime? startDate, DateTime? endDate, IProgress<string>? progress = null)
        => Task.Run(() => ImportMultipleSqlite(filePaths, startDate, endDate, progress));

    /// <summary>
    /// 查询SQLite数据库中数据的时间范围（最早/最晚触发时间）
    /// </summary>
    public (DateTime min, DateTime max) GetSqliteDateRange(string filePath)
    {
        using var conn = new SqliteConnection($"Data Source=\"{filePath}\"");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT MIN(e.""trigger_time@timestamp""), MAX(e.""trigger_time@timestamp"")
                            FROM event e
                            JOIN event_log el ON e.event_log_index = el.event_log_index
                            WHERE el.language1 IS NOT NULL AND el.language1 != ''";
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var minTs = reader.IsDBNull(0) ? 0.0 : reader.GetDouble(0);
            var maxTs = reader.IsDBNull(1) ? 0.0 : reader.GetDouble(1);
            return (UnixToLocal(minTs), UnixToLocal(maxTs));
        }
        return (DateTime.MinValue, DateTime.MinValue);
    }

    /// <summary>
    /// 查询多个SQLite数据库的合并时间范围
    /// </summary>
    public (DateTime min, DateTime max) GetMultipleSqliteDateRange(string[] filePaths)
    {
        DateTime globalMin = DateTime.MaxValue, globalMax = DateTime.MinValue;
        foreach (var file in filePaths)
        {
            try
            {
                var (min, max) = GetSqliteDateRange(file);
                if (min < globalMin) globalMin = min;
                if (max > globalMax) globalMax = max;
            }
            catch { /* 跳过无法读取的文件 */ }
        }
        if (globalMin == DateTime.MaxValue) return (DateTime.MinValue, DateTime.MinValue);
        return (globalMin, globalMax);
    }

    /// <summary>
    /// 批量导入多个CSV文件，合并所有记录 — 异步版本
    /// </summary>
    public Task<(List<AlarmRecord> records, ColumnMapping mapping, List<string> headers, List<string> importedFiles)> ImportMultipleCsvAsync(string[] filePaths, IProgress<string>? progress = null)
        => Task.Run(() => ImportMultipleCsv(filePaths, progress));

    /// <summary>
    /// 批量导入多个SQLite数据库文件，合并所有记录 — 异步版本
    /// </summary>
    public Task<List<AlarmRecord>> ImportMultipleSqliteAsync(string[] filePaths, IProgress<string>? progress = null)
        => Task.Run(() => ImportMultipleSqlite(filePaths, progress));

    /// <summary>
    /// 批量导入多个CSV文件并合并记录。
    /// 以第一个文件的列映射为基准，后续文件使用相同映射。
    /// </summary>
    public (List<AlarmRecord> records, ColumnMapping mapping, List<string> headers, List<string> importedFiles) ImportMultipleCsv(string[] filePaths, IProgress<string>? progress = null)
    {
        if (filePaths.Length == 0) return (new(), new(), new(), new());

        var allRecords = new List<AlarmRecord>();
        ColumnMapping? mapping = null;
        List<string> headers = new();
        var importedFiles = new List<string>();

        for (int i = 0; i < filePaths.Length; i++)
        {
            var file = filePaths[i];
            progress?.Report($"[{i + 1}/{filePaths.Length}] 正在导入: {Path.GetFileName(file)}");

            var (records, fileMapping, fileHeaders) = ImportCsv(file);

            if (mapping == null)
            {
                mapping = fileMapping;
                headers = fileHeaders;
            }

            allRecords.AddRange(records);
            importedFiles.Add(file);
            progress?.Report($"[{i + 1}/{filePaths.Length}] {Path.GetFileName(file)}: {records.Count}条");
        }

        // 按时间排序合并后的记录
        allRecords.Sort((a, b) => a.TriggerDateTime.CompareTo(b.TriggerDateTime));

        progress?.Report($"合并完成: 共 {allRecords.Count} 条记录");
        return (allRecords, mapping ?? new(), headers, importedFiles);
    }

    /// <summary>
    /// 批量导入多个SQLite数据库文件并合并记录。
    /// </summary>
    public List<AlarmRecord> ImportMultipleSqlite(string[] filePaths, IProgress<string>? progress = null)
        => ImportMultipleSqlite(filePaths, null, null, progress);

    /// <summary>
    /// 批量导入多个SQLite数据库文件并合并记录（带时间范围过滤）。
    /// </summary>
    public List<AlarmRecord> ImportMultipleSqlite(string[] filePaths, DateTime? startDate, DateTime? endDate, IProgress<string>? progress = null)
    {
        if (filePaths.Length == 0) return new();

        var allRecords = new List<AlarmRecord>();

        for (int i = 0; i < filePaths.Length; i++)
        {
            var file = filePaths[i];
            progress?.Report($"[{i + 1}/{filePaths.Length}] 正在导入: {Path.GetFileName(file)}");

            var records = ImportSqlite(file, startDate, endDate, progress);
            allRecords.AddRange(records);

            progress?.Report($"[{i + 1}/{filePaths.Length}] {Path.GetFileName(file)}: {records.Count}条");
        }

        // 按时间排序合并后的记录
        allRecords.Sort((a, b) => a.TriggerDateTime.CompareTo(b.TriggerDateTime));

        progress?.Report($"合并完成: 共 {allRecords.Count} 条记录");
        return allRecords;
    }

    /// <summary>
    /// 从威纶通HMI的SQLite数据库(event.db)导入报警记录。
    /// </summary>
    public List<AlarmRecord> ImportSqlite(string filePath, IProgress<string>? progress = null)
        => ImportSqlite(filePath, null, null, progress);

    /// <summary>
    /// 从威纶通HMI的SQLite数据库(event.db)导入报警记录（带时间范围过滤）。
    /// event表存储事件实例（Unix时间戳/发生次数/持续时间），event_log表存储报警定义（消息文本language1），
    /// 通过 event_log_index 关联。language1格式与CSV的Message列一致。
    /// </summary>
    public List<AlarmRecord> ImportSqlite(string filePath, DateTime? startDate, DateTime? endDate, IProgress<string>? progress = null)
    {
        progress?.Report("正在打开数据库...");
        using var conn = new SqliteConnection($"Data Source=\"{filePath}\"");
        conn.Open();

        // 构建带时间范围过滤的SQL
        var whereClauses = new List<string> { "el.language1 IS NOT NULL AND el.language1 != ''" };
        if (startDate.HasValue)
            whereClauses.Add($"e.\"trigger_time@timestamp\" >= {new DateTimeOffset(startDate.Value).ToUnixTimeSeconds()}");
        if (endDate.HasValue)
            whereClauses.Add($"e.\"trigger_time@timestamp\" <= {new DateTimeOffset(endDate.Value).ToUnixTimeSeconds()}");
        var whereSql = string.Join(" AND ", whereClauses);

        // 先查总数用于进度报告
        int total;
        using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = $@"SELECT COUNT(*) FROM event e
                                      JOIN event_log el ON e.event_log_index = el.event_log_index
                                      WHERE {whereSql}";
            total = Convert.ToInt32(countCmd.ExecuteScalar());
        }

        if (total == 0) return new();

        var rangeDesc = (startDate.HasValue || endDate.HasValue)
            ? $" ({startDate:yyyy-MM-dd} ~ {endDate:yyyy-MM-dd})"
            : "";
        progress?.Report($"正在读取 {total} 条记录{rangeDesc}...");
        var records = new List<AlarmRecord>(total);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT e.""trigger_time@timestamp"", e.""recover_time@timestamp"",
                                   e.occurrence_count, e.elapsed_time,
                                   el.language1, el.category, el.priority
                            FROM event e
                            JOIN event_log el ON e.event_log_index = el.event_log_index
                            WHERE {whereSql}
                            ORDER BY e.""trigger_time@timestamp""";

        using var reader = cmd.ExecuteReader();
        int count = 0;
        while (reader.Read())
        {
            var record = CreateRecordFromSqlite(reader);
            if (record != null)
                records.Add(record);

            count++;
            if (count % 3000 == 0)
                progress?.Report($"已读取 {count}/{total} 条...");
        }

        progress?.Report($"读取完成，共 {records.Count} 条有效记录");
        return records;
    }

    #region 私有方法

    /// <summary>
    /// 自动检测编码并读取所有行。
    /// 优先识别BOM；无BOM时尝试严格UTF-8解码，失败则回退GB18030(GBK超集)。
    /// 解决工业HMI导出的GBK编码CSV被当作UTF-8读取导致表头乱码、列映射失败的问题。
    /// </summary>
    private static string[] ReadAllLinesAuto(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var encoding = DetectEncoding(bytes, out int bomLength);
        var text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
        return text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
    }

    /// <summary>
    /// 检测字节流的编码并返回BOM长度
    /// </summary>
    private static Encoding DetectEncoding(byte[] bytes, out int bomLength)
    {
        bomLength = 0;

        // UTF-8 BOM: EF BB BF
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            bomLength = 3;
            return new UTF8Encoding(false);
        }
        // UTF-16 LE BOM: FF FE
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            bomLength = 2;
            return Encoding.Unicode;
        }
        // UTF-16 BE BOM: FE FF
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            bomLength = 2;
            return Encoding.BigEndianUnicode;
        }

        // 无BOM：用严格UTF-8解码试探，若出现非法字节序列则判定为GBK系
        try
        {
            var utf8Strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
            utf8Strict.GetString(bytes);
            return new UTF8Encoding(false);
        }
        catch
        {
            // GB18030 是 GBK/GB2312 的超集，兼容性最好
            return Encoding.GetEncoding("GB18030");
        }
    }

    private List<AlarmRecord> ParseRecords(string[] lines, char delimiter, ColumnMapping mapping, List<string> headers)
    {
        // 预分配容量，避免大列表多次扩容
        var records = new List<AlarmRecord>(Math.Max(0, lines.Length - 1));
        var colIndex = BuildColumnIndex(mapping, headers);

        // 用于检测重复表头行（HMI在文件末尾可能追加重复的表头文本）
        var firstHeader = headers.Count > 0 ? headers[0].Trim() : "";

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            // 跳过重复表头行（行首与表头第一列相同且不是数字开头）
            if (!string.IsNullOrEmpty(firstHeader))
            {
                var lineStart = lines[i].AsSpan().TrimStart();
                if (lineStart.StartsWith(firstHeader) && lineStart.Length > firstHeader.Length)
                    continue;
            }

            var values = ParseCsvLine(lines[i], delimiter);
            // 补齐到headers长度
            while (values.Count < headers.Count) values.Add("");

            var record = CreateRecord(values, colIndex);
            if (record != null)
                records.Add(record);
        }
        return records;
    }

    private Dictionary<string, int> BuildColumnIndex(ColumnMapping mapping, List<string> headers)
    {
        var dict = new Dictionary<string, int>();
        foreach (var field in ColumnMapping.StandardFields.Keys)
        {
            var colName = mapping.GetColumn(field);
            if (!string.IsNullOrEmpty(colName))
            {
                int idx = headers.IndexOf(colName);
                if (idx >= 0) dict[field] = idx;
            }
        }
        return dict;
    }

    private AlarmRecord? CreateRecord(IList<string> values, Dictionary<string, int> colIndex)
    {
        string GetString(string field) =>
            colIndex.TryGetValue(field, out int idx) && idx < values.Count ? values[idx] : "";

        int GetInt(string field, int def = 0)
        {
            var s = GetString(field).Trim();
            return int.TryParse(s, out int v) ? v : def;
        }

        long GetLong(string field, long def = 0)
        {
            var s = GetString(field).Trim();
            return long.TryParse(s, out long v) ? v : def;
        }

        var msg = GetString("Message");
        if (string.IsNullOrWhiteSpace(msg)) return null;

        var record = new AlarmRecord
        {
            Event = GetInt("Event"),
            Category = GetInt("Category"),
            TriggerDate = GetString("TriggerDate").Trim(),
            TriggerTime = GetString("TriggerTime").Trim(),
            RecoveryDate = GetString("RecoveryDate").Trim(),
            RecoveryTime = GetString("RecoveryTime").Trim(),
            Duration = GetInt("Duration"),
            RawMessage = msg,
            OccurrenceCount = GetInt("OccurrenceCount"),
            CumulativeTime = GetLong("CumulativeTime")
        };

        // 解析信息字段
        ParseMessage(record);

        // 解析日期时间
        record.TriggerDateTime = ParseDateTime(record.TriggerDate, record.TriggerTime) ?? DateTime.MinValue;
        record.RecoveryDateTime = ParseDateTime(record.RecoveryDate, record.RecoveryTime);

        // 跳过日期解析失败的记录（如HMI在文件末尾追加的重复表头垃圾行）
        // 这类记录TriggerDateTime=MinValue会导致按日趋势分析日期范围爆炸
        if (record.TriggerDateTime == DateTime.MinValue)
            return null;

        return record;
    }

    /// <summary>
    /// 解析信息字段: "3062 - 陀螺仪倾角超限 - Cat.06 / [10_设备故障类] - EM03:EM03"
    /// </summary>
    private void ParseMessage(AlarmRecord record)
    {
        var msg = record.RawMessage;
        var parts = msg.Split(" - ", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1) record.AlarmCode = parts[0].Trim();
        if (parts.Length >= 2) record.Description = parts[1].Trim();
        if (parts.Length >= 3)
        {
            record.CategoryDetail = parts[2].Trim();
            // 提取类别名称 [10_设备故障类]
            var match = CategoryNameRegex.Match(parts[2]);
            if (match.Success) record.CategoryName = match.Groups[1].Value;
            else record.CategoryName = parts[2].Trim();
        }
        if (parts.Length >= 4)
        {
            record.Source = parts[3].Trim();
            // 提取主设备 EM03
            var srcMatch = SourceMainRegex.Match(parts[3]);
            if (srcMatch.Success) record.SourceMain = srcMatch.Groups[1].Value;
            else record.SourceMain = parts[3].Trim();
        }
    }

    /// <summary>
    /// 从SQLite数据行创建AlarmRecord。
    /// 威纶通HMI event.db：时间为Unix时间戳（秒，浮点），language1格式同CSV的Message列。
    /// </summary>
    private AlarmRecord? CreateRecordFromSqlite(SqliteDataReader reader)
    {
        var triggerTs = reader.IsDBNull(0) ? 0.0 : reader.GetDouble(0);
        double? recoverTs = reader.IsDBNull(1) ? null : reader.GetDouble(1);
        var occCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
        var elapsed = reader.IsDBNull(3) ? 0L : reader.GetInt64(3);
        var msg = reader.IsDBNull(4) ? "" : reader.GetString(4);
        var category = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
        var priority = reader.IsDBNull(6) ? 0 : reader.GetInt32(6);

        if (string.IsNullOrWhiteSpace(msg)) return null;

        // Unix时间戳 → 本地时间
        var triggerDt = UnixToLocal(triggerTs);
        DateTime? recoverDt = recoverTs.HasValue ? UnixToLocal(recoverTs.Value) : null;

        // 持续时间 = 恢复时间 - 触发时间（秒）
        int duration = (recoverTs.HasValue && recoverTs.Value > triggerTs)
            ? (int)(recoverTs.Value - triggerTs) : 0;

        var record = new AlarmRecord
        {
            Event = priority,
            Category = category,
            TriggerDate = triggerDt.ToString("yyyy-MM-dd"),
            TriggerTime = triggerDt.ToString("HH:mm:ss"),
            RecoveryDate = recoverDt?.ToString("yyyy-MM-dd") ?? "",
            RecoveryTime = recoverDt?.ToString("HH:mm:ss") ?? "",
            Duration = duration,
            RawMessage = msg,
            OccurrenceCount = occCount,
            CumulativeTime = elapsed,
            TriggerDateTime = triggerDt,
            RecoveryDateTime = recoverDt
        };

        ParseMessage(record);
        return record;
    }

    /// <summary>
    /// Unix时间戳（秒，浮点）转本地DateTime
    /// </summary>
    private static DateTime UnixToLocal(double timestamp)
    {
        var seconds = (long)timestamp;
        var fraction = timestamp - seconds;
        // HMI event.db 中存储的是 CST 壁钟时间（非UTC），直接构造 DateTime 避免时区转换
        return DateTime.SpecifyKind(
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
                .AddSeconds(seconds)
                .AddTicks((long)(fraction * TimeSpan.TicksPerSecond)),
            DateTimeKind.Unspecified);
    }

    /// <summary>
    /// 解析日期和时间，处理 " 00:06: 16" 这种带空格的格式。
    /// 用手动字符清理替代 Regex.Replace（每条记录2次调用，大数据量下差异显著），
    /// 并对最常见格式 yyyy-MM-dd HH:mm:ss 做快速路径优先匹配。
    /// </summary>
    private DateTime? ParseDateTime(string date, string time)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;

        var cleanDate = date.Trim();
        var cleanTime = CleanTimeString(time);

        var dtStr = cleanTime.Length > 0 ? $"{cleanDate} {cleanTime}" : cleanDate;

        // 快速路径：工业HMI最常见格式优先尝试
        if (DateTime.TryParseExact(dtStr, _fmt1, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;
        if (DateTime.TryParseExact(dtStr, _fmt2, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2))
            return dt2;

        // 回退：尝试其余格式
        for (int i = 2; i < _formats.Length; i++)
        {
            if (DateTime.TryParseExact(dtStr, _formats[i], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtN))
                return dtN;
        }

        if (DateTime.TryParse(dtStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtFallback))
            return dtFallback;

        return null;
    }

    // 常见格式数组，前两个做快速路径单独引用以减少数组遍历
    private static readonly string _fmt1 = "yyyy-MM-dd HH:mm:ss";
    private static readonly string _fmt2 = "yyyy-MM-dd HH:mm";
    private static readonly string[] _formats = {
        _fmt1, _fmt2,
        "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd HH:mm",
        "yyyy-MM-dd", "yyyy/MM/dd",
        "M/d/yyyy h:mm:ss", "M/d/yyyy h:mm"
    };

    /// <summary>
    /// 手动移除时间字符串中的空格，替代 Regex.Replace(@"\s+","")。
    /// 使用 stackalloc 避免堆分配。
    /// </summary>
    private static string CleanTimeString(string? time)
    {
        if (string.IsNullOrEmpty(time)) return "";
        ReadOnlySpan<char> src = time.AsSpan().Trim();
        if (src.IsEmpty) return "";

        // 快速检测是否含空格
        bool hasSpace = false;
        foreach (var c in src)
            if (c == ' ') { hasSpace = true; break; }
        if (!hasSpace) return src.ToString();

        // 移除空格
        Span<char> buf = stackalloc char[src.Length];
        int j = 0;
        foreach (var c in src)
            if (c != ' ') buf[j++] = c;
        return buf[..j].ToString();
    }

    private char DetectDelimiter(string headerLine)
    {
        int comma = headerLine.Count(c => c == ',');
        int semicolon = headerLine.Count(c => c == ';');
        int tab = headerLine.Count(c => c == '\t');
        int max = Math.Max(comma, Math.Max(semicolon, tab));
        if (max == 0) return ',';
        if (max == comma) return ',';
        if (max == semicolon) return ';';
        return '\t';
    }

    private List<string> ParseCsvLine(string line, char delimiter)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuote = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuote && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else inQuote = !inQuote;
            }
            else if (c == delimiter && !inQuote)
            {
                result.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else sb.Append(c);
        }
        result.Add(sb.ToString().Trim());
        return result;
    }

    private ColumnMapping AutoDetectMapping(List<string> headers)
    {
        var mapping = new ColumnMapping();
        foreach (var prop in typeof(ColumnMapping).GetProperties())
        {
            if (prop.Name == "StandardFields") continue;
            // 尝试精确匹配
            var match = headers.FirstOrDefault(h => h.Trim() == prop.Name);
            if (match != null) { prop.SetValue(mapping, match); continue; }

            // 尝试中文显示名匹配
            if (ColumnMapping.StandardFields.TryGetValue(prop.Name, out var displayName))
            {
                match = headers.FirstOrDefault(h => h.Trim() == displayName);
                if (match != null) { prop.SetValue(mapping, match); continue; }
            }

            // 尝试自动检测
            foreach (var h in headers)
            {
                if (ColumnMapping.AutoDetectField(h) == prop.Name)
                {
                    prop.SetValue(mapping, h);
                    break;
                }
            }
        }
        return mapping;
    }

    #endregion
}
