# Clipboard (.NET 10)

剪贴板历史管理工具（由 Python/PySide6 版本迁移而来的 WPF 实现），目标框架 `net10.0-windows`。

## 功能

- 文本 / 图片剪贴板历史
- 搜索与分类过滤
- 系统托盘（关闭窗口隐藏到托盘）
- 全局热键唤起主窗口
- 开机自启动
- 历史导出、按时间范围清空

## 开发

```powershell
cd Clipboard.Net
dotnet build
dotnet run --project Clipboard
```

## 数据目录

用户数据写在文档目录下的 `FNSoftware/Clipboard/`（例如 `C:\Users\<用户>\Documents\FNSoftware\Clipboard`）：

- `config.json` — 热键与自启动配置
- `clipboard_history.json` — 历史记录
- `clipboard_images/` — 图片文件

启动时会自动把旧位置（程序目录或程序目录下的 `data/`）中的上述文件迁移过去。

## 发布

```powershell
.\scripts\publish.ps1
```

默认生成单文件 exe（不包含 .NET 运行时，体积小；目标机器需已安装 .NET Desktop Runtime）。
如需把 .NET 运行时也打进 exe（体积更大，但目标机器无需安装），加 `-SelfContained` 参数：

```powershell
.\scripts\publish.ps1 -SelfContained
```

版本号单一维护在 `Clipboard/Clipboard.csproj` 的 `<Version>`（语义化版本），由 git tag 驱动。

## GitHub Actions 自动发布

推送形如 `v1.0.0` 的标签，或在 Actions 页面手动运行 **build-release** 工作流，
即可自动构建并发布到 GitHub Releases（Velopack 打包）：

- 安装版：`Clipboard-win-Setup.exe`，安装后支持**应用内自动更新**（增量下载、自动重启）
- 便携版：`Clipboard-win-Portable.zip`，解压即用（便携版同样支持检查更新）
- 更新包：`Clipboard-{版本}-full.nupkg` / `-delta.nupkg` + `RELEASES` 更新清单

应用内通过托盘菜单或「关于」对话框的 **检查更新** 检查并安装新版本，更新源即 GitHub Releases。
增量更新需要上一版本，首次发布无 delta 增量包属正常。

工作流文件：[`.github/workflows/build-release.yml`](.github/workflows/build-release.yml)。

> 注意：从旧版 Inno Setup 安装版升级的用户，需重新运行一次新的 `Clipboard-win-Setup.exe`
> 才能开启自动更新。
