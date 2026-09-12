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
dotnet run
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

发布产物为单一 `Clipboard.exe`，输出到仓库根目录的 `publish\`（可用 `-OutputDir` 指定其他位置，
如 `-OutputDir D:\dist\clipboard`）。
默认生成单文件 exe（不包含 .NET 运行时，体积小；目标机器需已安装 .NET Desktop Runtime）。
如需把 .NET 运行时也打进 exe（体积更大，但目标机器无需安装），加 `-SelfContained` 参数：

```powershell
.\scripts\publish.ps1 -SelfContained
```

版本号单一维护在 `Clipboard.csproj` 的 `<Version>`（语义化版本），由 git tag 驱动。

## GitHub Actions 自动发布

推送形如 `v1.0.0` 的标签，或在 Actions 页面手动运行 **build-release** 工作流，
即可自动构建并发布单个 exe 到 GitHub Releases：

- 产物：`Clipboard-{版本}-win-x64.exe`（框架依赖单文件，体积小；目标机器需已安装 .NET Desktop Runtime）

**升级方式为纯手动替换**：从 Releases 页面下载新版本 exe，退出正在运行的 Clipboard，
用新文件覆盖旧的 `Clipboard.exe` 即可。程序本身不含自更新机制。

工作流文件：[`.github/workflows/build-release.yml`](.github/workflows/build-release.yml)。
