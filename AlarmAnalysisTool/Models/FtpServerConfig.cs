using System.IO;
using System.Text.Json.Serialization;

namespace AlarmAnalysisTool.Models;

/// <summary>
/// FTP服务器配置（用于远程采集威纶通HMI的event.db报警数据库）
/// 支持从完整FTP URL解析：ftp://uploadhis:111111@192.168.250.4/eventlog/event.db
/// </summary>
public class FtpServerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>配置名称（用户自定义，如"1号线主控HMI"）</summary>
    public string Name { get; set; } = "";

    /// <summary>FTP主机地址</summary>
    public string Host { get; set; } = "";

    /// <summary>FTP端口（默认21）</summary>
    public int Port { get; set; } = 21;

    /// <summary>用户名</summary>
    public string Username { get; set; } = "";

    /// <summary>密码</summary>
    public string Password { get; set; } = "";

    /// <summary>远程文件路径（如 /eventlog/event.db）</summary>
    public string RemotePath { get; set; } = "";

    /// <summary>是否启用此配置</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>最后同步时间</summary>
    public DateTime LastSyncTime { get; set; }

    /// <summary>最后同步记录数</summary>
    public int LastSyncRecordCount { get; set; }

    /// <summary>最后同步状态</summary>
    public string LastSyncStatus { get; set; } = "";

    /// <summary>
    /// 显示用的连接URL（隐藏密码）
    /// </summary>
    [JsonIgnore]
    public string DisplayUrl => $"ftp://{Username}:***@{Host}:{Port}{RemotePath}";

    /// <summary>
    /// 简短显示名（用于列表显示）
    /// </summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrEmpty(Name)
        ? $"{Host}:{Path.GetFileName(RemotePath)}"
        : Name;

    /// <summary>
    /// 最后同步时间显示文本
    /// </summary>
    [JsonIgnore]
    public string LastSyncText => LastSyncTime == DateTime.MinValue
        ? "未同步"
        : LastSyncTime.ToString("MM-dd HH:mm:ss");

    /// <summary>
    /// 从完整FTP URL解析配置
    /// 支持格式: ftp://uploadhis:111111@192.168.250.4/eventlog/event.db
    /// </summary>
    public static FtpServerConfig FromUrl(string url, string name = "")
    {
        var config = new FtpServerConfig();
        var uri = new Uri(url);

        config.Host = uri.Host;
        config.Port = uri.Port == -1 ? 21 : uri.Port;
        config.RemotePath = uri.AbsolutePath;

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            config.Username = parts[0];
            if (parts.Length > 1) config.Password = parts[1];
        }

        config.Name = string.IsNullOrEmpty(name)
            ? $"{config.Host}"
            : name;

        return config;
    }
}

/// <summary>
/// FTP自动采集设置
/// </summary>
public class FtpAutoRefreshSettings
{
    /// <summary>是否启用自动采集</summary>
    public bool IsEnabled { get; set; } = false;

    /// <summary>采集间隔（分钟）</summary>
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>可用的间隔选项</summary>
    [JsonIgnore]
    public static readonly int[] AvailableIntervals = { 1, 2, 5, 10, 15, 30, 60 };
}
