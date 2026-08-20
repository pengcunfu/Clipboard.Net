# 熔岩超级剪贴板 (.NET 10)

由 Python/PySide6 版本迁移而来的 WPF 实现，目标框架 `net10.0-windows`。

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
dotnet publish Clipboard -c Release -r win-x64 --self-contained false -o .\publish
```

## GitHub Actions 自动发布

推送形如 `v1.0.0` 的标签，或在 Actions 页面手动运行 **发布 Windows 版本** 工作流，
即可自动构建并发布到 GitHub Releases：

- 绿色版：`win-x64` / `win-arm64` 单文件 exe，解压即用，数据保存在 exe 旁边的 `data/` 目录
- 安装版：Inno Setup 打包的安装程序（win-x64，按当前用户目录安装，无需管理员权限）

工作流文件：[`.github/workflows/publish.yml`](.github/workflows/publish.yml)，
安装脚本：[`installer/setup.iss`](installer/setup.iss)。
