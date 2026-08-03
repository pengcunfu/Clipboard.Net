using System.IO;
using System.Text.Json;
using ClipboardApp.Models;

namespace ClipboardApp.Services;

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public AppConfig Config { get; private set; } = new();

    public ConfigService()
    {
        Load();
    }

    public void Load()
    {
        var path = AppPaths.ConfigFile;
        if (!File.Exists(path))
        {
            Config = new AppConfig();
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            Config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载配置失败: {ex.Message}");
            Config = new AppConfig();
        }
    }

    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.ConfigFile)!);
            var json = JsonSerializer.Serialize(Config, JsonOptions);
            File.WriteAllText(AppPaths.ConfigFile, json);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存配置失败: {ex.Message}");
            return false;
        }
    }
}
