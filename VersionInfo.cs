using System.Reflection;

namespace ClipboardApp;

/// <summary>
/// 版本信息单一来源：运行时从程序集文件版本读取（由 csproj &lt;Version&gt; 构建时注入），
/// 避免手工维护常量与 csproj 版本不同步。
/// </summary>
public static class VersionInfo
{
    /// <summary>当前程序集版本，如 "1.1.0"。</summary>
    public static string Version
    {
        get
        {
            var raw = Assembly.GetExecutingAssembly()
                              .GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
            if (string.IsNullOrEmpty(raw))
                return "0.0.0";

            // "1.1.0.0" → "1.1.0"（去掉第 4 段 revision）
            var parts = raw.Split('.');
            if (parts.Length > 3)
                parts = parts[..3];
            return string.Join('.', parts);
        }
    }
}
