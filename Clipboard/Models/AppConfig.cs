using System.Text.Json.Serialization;

namespace ClipboardApp.Models;

public sealed class AppConfig
{
    [JsonPropertyName("hotkey_modifiers")]
    public List<string> HotkeyModifiers { get; set; } = ["ctrl"];

    [JsonPropertyName("hotkey_key")]
    public string HotkeyKey { get; set; } = "space";

    [JsonPropertyName("autostart")]
    public bool Autostart { get; set; }
}
