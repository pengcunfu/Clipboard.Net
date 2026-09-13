using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using ClipboardApp.Dialogs;

namespace ClipboardApp.Services;

/// <summary>
/// 自研版本自更新服务：检查 GitHub Releases → 下载 exe（SHA256 校验）→ 交给内嵌更新器替换并重启。
/// 不依赖任何第三方库。更新源可用环境变量 CLIPBOARD_UPDATE_API 覆盖（本地联调用）。
/// </summary>
public sealed class UpdateService
{
    private const string Repo = "pengcunfu/Clipboard.Net";
    private const string ApiEnvVar = "CLIPBOARD_UPDATE_API";
    private const string AssetPrefix = "Clipboard-{0}-win-x64.exe";
    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>检查更新；已是最新返回 null。异常向上抛，由 UI 转友好提示。</summary>
    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        var api = Environment.GetEnvironmentVariable(ApiEnvVar);
        if (string.IsNullOrWhiteSpace(api))
            api = $"https://api.github.com/repos/{Repo}/releases/latest";

        using var resp = await Http.GetAsync(api);
        resp.EnsureSuccessStatusCode();
        var release = JsonSerializer.Deserialize<GitHubRelease>(await resp.Content.ReadAsStringAsync(), JsonOpts)
            ?? throw new InvalidOperationException("无法解析 GitHub 最新版本信息");

        var newVer = (release.TagName ?? "").TrimStart('v');
        if (newVer.Length == 0)
            throw new InvalidOperationException("GitHub 返回的版本号为空");

        if (!Version.TryParse(newVer, out var nv) || !Version.TryParse(VersionInfo.Version, out var cv))
            throw new InvalidOperationException($"版本号无法比较：{newVer} vs {VersionInfo.Version}");
        if (nv <= cv)
            return null;

        var exeName = string.Format(AssetPrefix, newVer);
        var exeAsset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, exeName, StringComparison.OrdinalIgnoreCase));
        if (exeAsset is null)
            throw new InvalidOperationException($"GitHub Release 中未找到资产 {exeName}");

        var shaAsset = release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, exeName + ".sha256", StringComparison.OrdinalIgnoreCase));

        var downloadUrl = exeAsset.BrowserDownloadUrl
            ?? throw new InvalidOperationException($"资产 {exeName} 缺少下载地址");
        return new UpdateInfo(newVer, downloadUrl, shaAsset?.BrowserDownloadUrl);
    }

    /// <summary>下载新版本 exe 到临时目录，SHA256 校验通过后返回本地路径；progress 为 0..100。</summary>
    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<int>? progress, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Clipboard-update");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, string.Format(AssetPrefix, info.Version));

        // 先拉取 sha256 期望值；拉取失败则不强制校验（不阻断更新）
        string? expectedHash = null;
        if (!string.IsNullOrEmpty(info.Sha256Url))
        {
            try
            {
                var text = await Http.GetStringAsync(info.Sha256Url, ct);
                expectedHash = text.Split([' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
            }
            catch { /* 忽略，走无校验路径 */ }
        }

        // 下载 exe 并报告进度
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
        if (!string.IsNullOrEmpty(expectedHash))
        {
            string actual;
            await using (var fs = File.OpenRead(dest))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();

            if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(dest);
                throw new InvalidOperationException($"SHA256 校验失败：期望 {expectedHash}，实际 {actual}");
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
        var bak = Path.Combine(AppContext.BaseDirectory, "Clipboard.exe.bak");
        if (!File.Exists(bak))
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Clipboard-Update/1.0"); // GitHub API 要求 UA
        return client;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}

/// <summary>一次可用的更新：新版本号 + exe 下载地址 + 可选 sha256 校验地址。</summary>
public sealed record UpdateInfo(string Version, string DownloadUrl, string? Sha256Url);
