using Velopack;
using Velopack.Sources;

namespace ClipboardApp.Services;

/// <summary>
/// 版本自更新服务：封装 Velopack UpdateManager 的检查 / 下载 / 应用更新生命周期。
/// 更新源默认 GitHub Releases；可用环境变量 CLIPBOARD_UPDATE_FEED 覆盖为本地目录
/// 或 HTTP 地址（本地全流程联调用）。
/// </summary>
public sealed class UpdateService
{
    private const string GithubRepo = "https://github.com/pengcunfu/Clipboard.Net";
    private const string UpdateFeedEnvVar = "CLIPBOARD_UPDATE_FEED";

    private readonly UpdateManager _manager;

    public UpdateService()
    {
        string? overrideFeed = Environment.GetEnvironmentVariable(UpdateFeedEnvVar);
        _manager = string.IsNullOrWhiteSpace(overrideFeed)
            ? new UpdateManager(new GithubSource(GithubRepo, accessToken: null, prerelease: false))
            : new UpdateManager(overrideFeed);
    }

    /// <summary>当前是否为 Velopack 安装（含便携版）；调试运行或旧版安装器为 false。</summary>
    public bool IsUpdateCapable => _manager.IsInstalled;

    /// <summary>已安装版本；非 Velopack 安装态为 null（由调用方回退自身版本显示）。</summary>
    public SemanticVersion? CurrentVersion => _manager.CurrentVersion;

    /// <summary>检查更新；已是最新返回 null。异常向上抛，由 UI 转友好提示。</summary>
    public Task<UpdateInfo?> CheckForUpdatesAsync()
        => _manager.CheckForUpdatesAsync();

    /// <summary>下载更新（优先 delta，失败自动回退 full 包）；progress 为 0..100。</summary>
    public async Task<UpdateInfo> DownloadUpdatesAsync(
        UpdateInfo info, IProgress<int>? progress, CancellationToken ct = default)
    {
        var callback = progress is null ? null : new Action<int>(p => progress.Report(p));
        await _manager.DownloadUpdatesAsync(info, callback, ct).ConfigureAwait(true);
        return info;
    }

    /// <summary>立即退出当前进程、应用更新并以相同参数重启（单实例互斥锁随进程终止自动释放）。</summary>
    public void RestartAndApply(UpdateInfo info)
        => _manager.ApplyUpdatesAndRestart(info.TargetFullRelease);

    /// <summary>温和模式：启动更新进程后退出应用，由更新进程应用并（可选）重启。</summary>
    public void ExitAndApply(UpdateInfo info, bool restart, params string[] args)
        => _manager.WaitExitThenApplyUpdates(info.TargetFullRelease, silent: true, restart, args);
}
