using Magic.Capture.Core.History;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Magic.Capture.App.ViewModels;

public sealed class HistoryDisplayItem
{
    public required HistoryItem Item { get; init; }
    public required BitmapImage Thumbnail { get; init; }
    public BitmapImage? ProcessIcon { get; init; }
    public string SourceKind => Item.SourceKind;
    public string DisplayTitle => string.IsNullOrWhiteSpace(Item.Title) ? (Item.SourceDisplayName ?? Item.SourceKind) : Item.Title;
    public string FavoriteGlyph => Item.IsFavorite ? "★" : string.Empty;
    public string TagsText => Item.Tags is { Count: > 0 } ? string.Join("  ·  ", Item.Tags.Select(tag => $"#{tag}")) : string.Empty;
    public string RelativePath => Item.RelativePath;
    public string? OcrPreview => Item.OcrPreview;
    public DateTimeOffset CreatedUtc => Item.CreatedUtc;
    public string Dimensions => $"{Item.Width} × {Item.Height}";
    public string SessionText => string.IsNullOrWhiteSpace(Item.SessionId) ? string.Empty : $"Session {(Item.SessionId.Length <= 12 ? Item.SessionId : Item.SessionId[^12..])}";
    public string FileSizeText => Item.FileBytes >= 1024 * 1024 ? $"{Item.FileBytes / (1024d * 1024d):F1} MB" : $"{Math.Max(1, Item.FileBytes / 1024d):F0} KB";
    public string SourceMetadataText
    {
        get
        {
            var parts = new[] { Item.ProcessName, Item.WindowTitle, Item.MonitorName }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
            return parts.Length == 0 ? string.Empty : string.Join(" · ", parts);
        }
    }

    internal static async Task<HistoryDisplayItem> CreateAsync(HistoryItem item, string imagePath, Persistence.HistoryProcessIconCache? iconCache = null)
    {
        var image = new BitmapImage { DecodePixelWidth = 180 };
        using var stream = await FileRandomAccessStream.OpenAsync(imagePath, Windows.Storage.FileAccessMode.Read);
        await image.SetSourceAsync(stream);
        BitmapImage? processIcon = null;
        if (iconCache is not null && await iconCache.GetOrCreateAsync(item.ExecutablePath) is { } iconPath && File.Exists(iconPath))
        {
            try
            {
                processIcon = new BitmapImage { DecodePixelWidth = 24 };
                using var iconStream = await FileRandomAccessStream.OpenAsync(iconPath, Windows.Storage.FileAccessMode.Read);
                await processIcon.SetSourceAsync(iconStream);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { processIcon = null; }
        }
        return new HistoryDisplayItem { Item = item, Thumbnail = image, ProcessIcon = processIcon };
    }
}
