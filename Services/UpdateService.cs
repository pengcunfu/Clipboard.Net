using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using ClipboardApp.Dialogs;

namespace ClipboardApp.Services;

/// <summary>
/// 自研版本自更新服务：读取 GitHub Releases 的 latest.json 清单 → 下载 exe（SHA256 校验）
/// → 交给内嵌更新器替换并重启。
/// 更新源走 github.com 的 /releases/latest/download/ 静态地址，**不调用 api.github.com**，
/// 避免未认证 API 的 60 次/小时限流。可用环境变量 CLIPBOARD_UPDATE_MANIFEST 覆盖清单地址（本地联调用）。
/// </summary>
public sealed class UpdateService
{
    private const string LatestManifestUrl = "https://github.com/pengcunfu/Clipboard.Net/releases/latest/download/latest.json";
    private const string ManifestEnvVar = "CLIPBOARD_UPDATE_MANIFEST";
    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>检查更新；已是最新返回 null。异常向上抛，由 UI 转友好提示。</summary>
    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        var url = Environment.GetEnvironmentVariable(ManifestEnvVar);
        if (string.IsNullOrWhiteSpace(url))
            url = LatestManifestUrl;

        // 网络/HTTP 错误（HttpRequestException）或 JSON 解析失败（JsonException）统一转为友好中文提示，
        // 避免把原始英文异常（如“Response status code does not indicate success: 404”）抛给 UI。
        UpdateManifest manifest;
        try
        {
            var json = await Http.GetStringAsync(url);
            manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOpts)
                ?? throw new JsonException("清单内容为 null");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            throw new InvalidOperationException($"无法解析更新清单：{url}", ex);
        }

        var newVer = manifest.Version?.Trim() ?? "";
        if (newVer.Length == 0)
            throw new InvalidOperationException("更新清单缺少版本号");

        if (!Version.TryParse(newVer, out var nv) || !Version.TryParse(VersionInfo.Version, out var cv))
            throw new InvalidOperationException($"版本号无法比较：{newVer} vs {VersionInfo.Version}");
        if (nv <= cv)
            return null;

        if (string.IsNullOrEmpty(manifest.Url))
            throw new InvalidOperationException("更新清单缺少下载地址");

        return new UpdateInfo(newVer, manifest.Url, manifest.Sha256);
    }

    /// <summary>下载新版本 exe 到临时目录，SHA256 校验通过后返回本地路径；progress 为 0..100。</summary>
    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<int>? progress, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Clipboard-update");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, $"Clipboard-{info.Version}-win-x64.exe");

        using (var resp = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength;
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            await using var fs = File.Create(dest);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total.HasValue && total.Value > 0)
                    progress?.Report((int)(read * 100 / total.Value));
            }
        }

        // 校验完整性
        if (!string.IsNullOrEmpty(info.Sha256))
        {
            string actual;
            await using (var fs = File.OpenRead(dest))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();

            var expected = info.Sha256.Trim().ToLowerInvariant();
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                TryDelete(dest);
                throw new InvalidOperationException($"SHA256 校验失败：期望 {expected}，实际 {actual}");
            }
        }

        return dest;
    }

    /// <summary>启动内嵌更新器（旧 exe 的第二个进程）执行替换，随后请求本进程干净退出。</summary>
    public void ApplyAndRestart(string stagedPath, string newVer)
    {
        var args = $"--updater apply --newExe \"{stagedPath}\" --pid {Environment.ProcessId} --to {newVer} --from {VersionInfo.Version}";
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, args) { UseShellExecute = false });
        App.RequestExit();
    }

    /// <summary>启动内嵌更新器恢复上一版本备份，随后请求本进程干净退出；无备份时提示并返回 false。</summary>
    public bool RollbackAndRestart()
    {
        // 备份名带版本号（Clipboard-1.3.3-win-x64.exe.bak），不能用固定 "Clipboard.exe.bak" 判断
        var baks = Directory.GetFiles(AppContext.BaseDirectory, "*.exe.bak");
        if (baks.Length == 0)
        {
            MessageBox.Show(UiText.NoRollbackBackup, UiText.Tip, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var args = $"--updater rollback --pid {Environment.ProcessId}";
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, args) { UseShellExecute = false });
        App.RequestExit();
        return true;
    }

    /// <summary>
    /// 共享的「检查 → 确认 → 下载 → 替换重启」流程，托盘菜单 / 关于对话框 / 启动自动检查三处复用。
    /// silent=true 时（启动自动检查）对「已最新 / 出错」静默，只在新版本时弹确认框。
    /// WPF SynchronizationContext 保证 await 后续回到 UI 线程。
    /// </summary>
    public static async Task CheckAndApplyAsync(bool silent, Window? owner = null)
    {
        try
        {
            var updater = new UpdateService();
            var info = await updater.CheckForUpdatesAsync();
            if (info is null)
            {
                if (!silent)
                    MessageBox.Show(UiText.UpdateUpToDate, UiText.Tip, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ask = MessageBox.Show(
                string.Format(UiText.UpdateAvailable, info.Version, VersionInfo.Version),
                UiText.Tip,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes)
                return;

            UpdateProgressDialog? progressDlg = null;
            try
            {
                progressDlg = new UpdateProgressDialog { Owner = owner };
                var downloadTask = updater.DownloadAsync(info, new Progress<int>(p => progressDlg.Report(p)));
                progressDlg.Show();
                var staged = await downloadTask;
                progressDlg.Close();

                updater.ApplyAndRestart(staged, info.Version);
            }
            catch (Exception ex)
            {
                progressDlg?.Close();
                if (!silent)
                    MessageBox.Show(UiText.UpdateDownloadFailed + ex.Message, UiText.UpdateError, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            if (!silent)
                MessageBox.Show(UiText.UpdateCheckFailed + ex.Message, UiText.UpdateError, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------- 内部 ----------

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Clipboard-Update/1.0");
        return client;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private sealed class UpdateManifest
    {
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    }
}

/// <summary>一次可用的更新：新版本号 + exe 下载地址 + 期望 SHA256（来自 latest.json 清单）。</summary>
public sealed record UpdateInfo(string Version, string DownloadUrl, string? Sha256);
