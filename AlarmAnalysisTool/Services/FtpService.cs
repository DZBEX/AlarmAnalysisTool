using System.IO;
using System.Net;

namespace AlarmAnalysisTool.Services;

/// <summary>
/// FTP远程采集服务：从远程FTP服务器下载威纶通HMI的event.db数据库文件。
/// 使用内置 FtpWebRequest 实现，无需额外NuGet依赖。
/// </summary>
public class FtpService
{
    /// <summary>
    /// 测试FTP连接是否可达，并返回远程文件大小
    /// 注意：使用 GetDateTimestamp 替代 GetFileSize，避免 pyftpdlib 等服务器
    /// 在 ASCII 模式下拒绝 SIZE 命令（550 SIZE not allowed in ASCII mode）
    /// </summary>
    /// <returns>(是否成功, 文件大小字节(可能为-1), 错误信息)</returns>
    public async Task<(bool success, long fileSize, string error)> TestConnectionAsync(
        string host, int port, string username, string password, string remotePath)
    {
        try
        {
            // 先用 GetDateTimestamp 测试连通性（ASCII模式兼容性好）
            var request = CreateFtpRequest(host, port, username, password, remotePath,
                WebRequestMethods.Ftp.GetDateTimestamp, 10000);

            using var response = (FtpWebResponse)await request.GetResponseAsync();

            // 连通后再尝试获取文件大小（此时已确认二进制模式可用）
            long size = -1;
            try
            {
                var sizeRequest = CreateFtpRequest(host, port, username, password, remotePath,
                    WebRequestMethods.Ftp.GetFileSize, 10000);
                using var sizeResponse = (FtpWebResponse)await sizeRequest.GetResponseAsync();
                size = sizeResponse.ContentLength;
            }
            catch
            {
                // 部分FTP服务器在ASCII模式下拒绝SIZE命令，忽略此错误
                // 文件存在性已由上面的 GetDateTimestamp 确认
                size = -1;
            }

            return (true, size, "");
        }
        catch (Exception ex)
        {
            return (false, 0, GetFtpErrorMessage(ex));
        }
    }

    /// <summary>
    /// 从FTP下载文件到本地临时路径，返回本地文件路径
    /// </summary>
    public async Task<string> DownloadFileAsync(
        string host, int port, string username, string password, string remotePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fileName = Path.GetFileName(remotePath);
        if (string.IsNullOrEmpty(fileName)) fileName = "event.db";

        // 使用时间戳唯一文件名，避免SQLite未释放文件锁导致覆盖失败
        // 每次下载生成新文件，并清理超过1小时的旧缓存文件
        var localDir = Path.Combine(Path.GetTempPath(), "AlarmAnalysisTool_FtpCache");
        Directory.CreateDirectory(localDir);
        CleanupOldCacheFiles(localDir, maxAgeHours: 1);
        var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var localPath = Path.Combine(localDir, $"{host}_{ts}_{fileName}");

        progress?.Report($"正在连接FTP服务器 {host}:{port}...");

        var request = CreateFtpRequest(host, port, username, password, remotePath,
            WebRequestMethods.Ftp.DownloadFile, 30000);

        using var response = (FtpWebResponse)await request.GetResponseAsync();
        using var responseStream = response.GetResponseStream();
        using var fileStream = File.Create(localPath);

        var totalBytes = response.ContentLength;
        var buffer = new byte[81920];
        long totalDownloaded = 0;
        int bytesRead;

        progress?.Report(totalBytes > 0
            ? $"正在下载 {fileName} ({FormatSize(totalBytes)})..."
            : $"正在下载 {fileName}...");

        while ((bytesRead = await responseStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalDownloaded += bytesRead;

            // 每1MB报告一次进度
            if (totalBytes > 0 && totalDownloaded % (1024 * 1024) < 81920)
            {
                var percent = (int)(totalDownloaded * 100 / totalBytes);
                progress?.Report($"下载中... {percent}% ({FormatSize(totalDownloaded)}/{FormatSize(totalBytes)})");
            }
        }

        progress?.Report($"下载完成: {FormatSize(totalDownloaded)}");
        return localPath;
    }

    /// <summary>
    /// 创建FtpWebRequest，统一处理已过时API的警告抑制
    /// </summary>
    private static FtpWebRequest CreateFtpRequest(
        string host, int port, string username, string password,
        string remotePath, string method, int timeout)
    {
#pragma warning disable SYSLIB0014
        var request = (FtpWebRequest)WebRequest.Create($"ftp://{host}:{port}{remotePath}");
        request.Credentials = new NetworkCredential(username, password);
        request.Method = method;
        request.Timeout = timeout;
        request.ReadWriteTimeout = Math.Max(timeout, 30000);
        request.UseBinary = true;
        request.KeepAlive = false;
        return request;
#pragma warning restore SYSLIB0014
    }

    /// <summary>
    /// 格式化文件大小显示
    /// </summary>
    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    /// <summary>
    /// 提取FTP错误的友好信息
    /// </summary>
    private static string GetFtpErrorMessage(Exception ex)
    {
        if (ex is WebException webEx && webEx.Response is FtpWebResponse ftpResponse)
        {
            return $"{ftpResponse.StatusCode}: {ftpResponse.StatusDescription}".Trim();
        }
        return ex.Message;
    }

    /// <summary>
    /// 清理超过指定时间的FTP缓存文件，避免临时目录无限膨胀
    /// </summary>
    private static void CleanupOldCacheFiles(string cacheDir, int maxAgeHours = 1)
    {
        try
        {
            var cutoff = DateTime.Now.AddHours(-maxAgeHours);
            var files = Directory.GetFiles(cacheDir);
            int cleaned = 0;
            foreach (var file in files)
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                        cleaned++;
                    }
                }
                catch { /* 跳过被锁定的文件 */ }
            }
            if (cleaned > 0)
                System.Diagnostics.Debug.WriteLine($"[FtpService] 已清理 {cleaned} 个过期缓存文件");
        }
        catch { /* 清理失败不影响主流程 */ }
    }
}
