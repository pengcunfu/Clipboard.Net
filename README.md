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

运行后数据写在可执行文件旁的 `data/`：

- `config.json` — 热键与自启动配置
- `clipboard_history.json` — 历史记录
- `clipboard_images/` — 图片文件

格式与原 Python 版兼容，可将原 `data/` 目录复制到输出目录旁直接使用。

## 发布

```powershell
dotnet publish Clipboard -c Release -r win-x64 --self-contained false -o .\publish
```
