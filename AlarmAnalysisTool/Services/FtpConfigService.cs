using System.IO;
using System.Text.Json;
using AlarmAnalysisTool.Models;

namespace AlarmAnalysisTool.Services;

/// <summary>
/// FTP配置持久化服务：将FTP服务器配置和自动采集设置保存到
/// %APPDATA%/AlarmAnalysisTool/ftp_configs.json
/// </summary>
public class FtpConfigService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AlarmAnalysisTool");

    private static readonly string ConfigFile = Path.Combine(ConfigDir, "ftp_configs.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 加载所有FTP配置
    /// </summary>
    public List<FtpServerConfig> LoadConfigs()
    {
        try
        {
            if (!File.Exists(ConfigFile)) return new();
            var json = File.ReadAllText(ConfigFile);
            var data = JsonSerializer.Deserialize<FtpConfigData>(json, JsonOptions);
            return data?.Configs ?? new();
        }
        catch
        {
            return new();
        }
    }

    /// <summary>
    /// 加载自动采集设置
    /// </summary>
    public FtpAutoRefreshSettings LoadAutoRefreshSettings()
    {
        try
        {
            if (!File.Exists(ConfigFile)) return new();
            var json = File.ReadAllText(ConfigFile);
            var data = JsonSerializer.Deserialize<FtpConfigData>(json, JsonOptions);
            return data?.AutoRefresh ?? new();
        }
        catch
        {
            return new();
        }
    }

    /// <summary>
    /// 保存FTP配置和自动采集设置
    /// </summary>
    public void Save(List<FtpServerConfig> configs, FtpAutoRefreshSettings autoRefresh)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var data = new FtpConfigData
            {
                Configs = configs,
                AutoRefresh = autoRefresh
            };
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(ConfigFile, json);
        }
        catch
        {
            // 静默失败，配置保存失败不应影响主流程
        }
    }

    /// <summary>
    /// 更新单个配置的最后同步信息
    /// </summary>
    public void UpdateSyncStatus(FtpServerConfig config, int recordCount, string status)
    {
        var configs = LoadConfigs();
        var target = configs.FirstOrDefault(c => c.Id == config.Id);
        if (target != null)
        {
            target.LastSyncTime = DateTime.Now;
            target.LastSyncRecordCount = recordCount;
            target.LastSyncStatus = status;
            var autoRefresh = LoadAutoRefreshSettings();
            Save(configs, autoRefresh);
        }
    }

    /// <summary>
    /// 序列化容器
    /// </summary>
    private class FtpConfigData
    {
        public List<FtpServerConfig> Configs { get; set; } = new();
        public FtpAutoRefreshSettings AutoRefresh { get; set; } = new();
    }
}
