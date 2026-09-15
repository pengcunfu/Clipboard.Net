using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ClipboardApp.Services;

/// <summary>
/// 自研更新/回滚器：以 `Clipboard.exe --updater apply|rollback ...` 的第二个进程运行，
/// 不依赖任何外部库。负责等待主进程退出 → 备份旧 exe → 原地替换 → 看门狗校验
/// （新版本启动失败自动回滚并重启旧版）。
/// Windows 文件锁要点：运行中的 exe 可改名不可覆盖，因此先改名腾位再放入新文件。
/// </summary>
public static class Updater
{
    private const string BakSuffix = ".bak";
    private const string MarkerName = ".update_ok";
    private const string LogName = "updater.log";
    private const int WatchdogSeconds = 30;

    /// <summary>入口：args 形如 ["apply", "--newExe", "...", "--pid", "1234", "--to", "1.3.0"]。</summary>
    public static int Run(string[] args)
    {
        string exePath;
        try
        {
            exePath = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位当前 exe 路径");
            Log("更新器启动，exe=" + Path.GetFileName(exePath));
        }
        catch (Exception ex)
        {
            Log("初始化失败: " + ex.Message);
            return 1;
        }

        string bak = exePath + BakSuffix;
        string dir = Path.GetDirectoryName(exePath) ?? "";
        string marker = Path.Combine(dir, MarkerName);

        string mode = args.Length > 0 ? args[0] : "";
        try
        {
            return mode switch
            {
                "apply" => Apply(exePath, bak, marker, args),
                "rollback" => ManualRollback(exePath, bak, marker, args),
                _ => Fail($"未知模式: {mode}")
            };
        }
        catch (Exception ex)
        {
            Log($"更新器异常: {ex}");
            // 兜底：无论替换到哪一步，都尽量恢复可用 exe 并重启，绝不留下「无可用 exe」的死状态
            RestoreBackupAndRelaunch(exePath, bak, relaunchArgs: null);
            return 1;
        }
    }

    private static int Apply(string exePath, string bak, string marker, string[] args)
    {
        string fromVer = string.IsNullOrEmpty(GetArg(args, "--from")) ? VersionInfo.Version : GetArg(args, "--from");
        string toVer = GetArg(args, "--to");
        string newExe = GetArg(args, "--newExe");
        int pid = ParsePid(args);
        Log($"apply: 当前={fromVer} 目标={toVer}");

        WaitForExit(pid);

        // 0. 更新后 exe 名跟随新版本号（Clipboard-1.3.3-win-x64.exe → Clipboard-1.3.4-win-x64.exe）。
        //    避免把新版本二进制写到旧文件名上，导致文件名残留旧版本号。
        string targetExe = VersionedExePath(exePath, toVer);

        // 1. 备份旧 exe（运行中 exe 可改名，不可覆盖）
        if (File.Exists(bak)) File.Delete(bak);
        File.Move(exePath, bak);
        Log($"已备份旧版本 -> {Path.GetFileName(bak)}");

        // 2. 放入新 exe（写往新版本规范名）并清理临时文件
        if (string.IsNullOrEmpty(newExe) || !File.Exists(newExe))
            throw new InvalidOperationException($"找不到新版本文件: {newExe}");
        File.Copy(newExe, targetExe, overwrite: true);
        TryDelete(newExe);
        Log("已就位新版本 " + Path.GetFileName(targetExe));

        // 3. 清除旧的就绪标记并启动新版本
        TryDelete(marker);
        using var proc = StartProcess(targetExe, $"--updated-to {toVer}", out var err);
        if (proc is null)
        {
            Log("启动新版本失败: " + err);
            TryDelete(targetExe);
            RestoreBackupAndRelaunch(exePath, bak, $"--rollback-from {toVer} --rollback-to {fromVer}");
            return 1;
        }
        Log($"已启动新版本 pid={proc.Id}，进入看门狗（最长 {WatchdogSeconds}s）");

        // 4. 看门狗：等待就绪标记（新 exe 启动成功后写入）；进程提前退出视为失败
        var deadline = DateTime.UtcNow.AddSeconds(WatchdogSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (HasExited(proc))
            {
                Log("新版本进程提前退出，回滚");
                TryDelete(targetExe);
                RestoreBackupAndRelaunch(exePath, bak, $"--rollback-from {toVer} --rollback-to {fromVer}");
                return 1;
            }
            if (File.Exists(marker))
            {
                TryDelete(marker);
                Log("新版本就绪标记已出现，更新成功");
                return 0;
            }
            Thread.Sleep(500);
        }

        // 5. 超时未就绪：视为失败，回滚
        Log("看门狗超时未就绪，回滚");
        Kill(proc);
        TryDelete(targetExe);
        RestoreBackupAndRelaunch(exePath, bak, $"--rollback-from {toVer} --rollback-to {fromVer}");
        return 1;
    }

    private static int ManualRollback(string exePath, string bak, string marker, string[] args)
    {
        Log("手动回滚开始");
        WaitForExit(ParsePid(args));

        // 备份名带旧版本号（Clipboard-1.3.3-win-x64.exe.bak），不能用 exePath+".bak" 推断，
        // 需在目录里定位最新的 .bak（可能残留历史备份）。
        var dir = Path.GetDirectoryName(exePath) ?? "";
        var found = FindBackup(dir);
        if (found is null)
        {
            Log("没有可回滚的备份");
            return 1;
        }
        string oldExe = found[..^BakSuffix.Length]; // 去掉 ".bak" = 旧版本 exe 名

        // 当前 exe 正被本进程运行，先改名腾位，再把备份恢复到旧版本 exe 名
        TryDelete(marker);
        string tmp = exePath + ".rollback";
        if (File.Exists(tmp)) File.Delete(tmp);
        File.Move(exePath, tmp);
        File.Move(found, oldExe);
        TryDelete(tmp); // 尽力清理；失败时残留文件由下次 apply 覆盖
        Log("已恢复备份 -> " + Path.GetFileName(oldExe));

        StartProcess(oldExe, "--manual-rollback", out _);
        return 0;
    }

    /// <summary>
    /// 兜底：恢复可用的 exe 并重启。
    /// 若 exePath 可被删除（已被替换为损坏新版，本进程运行在 .bak 上）→ 恢复备份；
    /// 若 exePath 不可删（仍是本进程运行的完好旧版，替换前就失败了）→ 直接重启旧版。
    /// </summary>
    private static void RestoreBackupAndRelaunch(string exePath, string bak, string? relaunchArgs)
    {
        try
        {
            if (File.Exists(bak) && TryDeleteFile(exePath))
            {
                File.Move(bak, exePath);
                Log("已恢复备份到 " + Path.GetFileName(exePath));
            }
        }
        catch (Exception ex)
        {
            Log("恢复备份失败: " + ex.Message);
        }
        finally
        {
            StartProcess(exePath, relaunchArgs ?? "", out var err);
            if (!string.IsNullOrEmpty(err)) Log("重启失败: " + err);
        }
    }

    // ---------- 工具 ----------

    private static void WaitForExit(int pid)
    {
        if (pid <= 0) return;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited) return;
            }
            catch (ArgumentException)
            {
                return; // 进程已不存在
            }
            catch (Exception ex)
            {
                Log("等待主进程异常: " + ex.Message);
                return;
            }
            Thread.Sleep(300);
        }
        Log("等待主进程退出超时，继续尝试替换");
    }

    private static bool HasExited(Process p)
    {
        try { return p.HasExited; }
        catch { return true; }
    }

    private static void Kill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch { }
    }

    private static Process? StartProcess(string file, string arguments, out string error)
    {
        error = "";
        try
        {
            var psi = new ProcessStartInfo(file, arguments) { UseShellExecute = false };
            return Process.Start(psi);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static string GetArg(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return "";
    }

    private static int ParsePid(string[] args)
        => int.TryParse(GetArg(args, "--pid"), out var pid) ? pid : 0;

    /// <summary>更新后 exe 名跟随新版本号：把文件名里的版本号片段换为 toVer（Clipboard-1.3.3-win-x64.exe → Clipboard-1.3.4-win-x64.exe）。
    /// 用正则替换而非 fromVer（兼容历史陈旧文件名：之前 bug 会把新版本写到旧名上，文件名不含 fromVer）。</summary>
    private static string VersionedExePath(string currentExe, string toVer)
    {
        string dir = Path.GetDirectoryName(currentExe) ?? "";
        string fn = Path.GetFileName(currentExe);
        string newFn = Regex.Replace(fn, @"-\d+(\.\d+)+-", $"-{toVer}-");
        return Path.Combine(dir, newFn);
    }

    /// <summary>定位目录下最新（按修改时间）的 .bak 备份，即上一版本 exe 备份。</summary>
    private static string? FindBackup(string dir)
    {
        string[] files;
        try { files = Directory.GetFiles(dir, "*" + BakSuffix); }
        catch { return null; }
        return files.Length == 0
            ? null
            : files.OrderByDescending(File.GetLastWriteTimeUtc).First();
    }

    private static int Fail(string message)
    {
        Log(message);
        return 1;
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) { File.Delete(path); return !File.Exists(path); }
            return true;
        }
        catch { return false; }
    }

    private static void TryDelete(string path)
    {
        TryDeleteFile(path);
    }

    private static void Log(string message)
    {
        try
        {
            string dir = AppContext.BaseDirectory;
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(dir, LogName), line, Encoding.UTF8);
        }
        catch { /* 日志失败不影响更新流程 */ }
    }
}
