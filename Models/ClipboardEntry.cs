using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace ClipboardApp.Models;

public sealed class ClipboardEntry
{
    [JsonIgnore]
    public long Id { get; set; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("image_path")]
    public string? ImagePath { get; set; }

    [JsonIgnore]
    public bool IsImage => string.Equals(Type, "image", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            if (IsImage)
            {
                var name = string.IsNullOrWhiteSpace(ImagePath)
                    ? "unknown.png"
                    : Path.GetFileName(ImagePath);
                return $"{Timestamp}\n[图片] {name}";
            }

            var preview = string.Join(' ', (Text ?? string.Empty).Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries));
            if (preview.Length > 300)
                preview = preview[..300] + "...";
            return $"{Timestamp}\n{preview}";
        }
    }

    [JsonIgnore]
    public BitmapImage? Thumbnail { get; set; }
}

