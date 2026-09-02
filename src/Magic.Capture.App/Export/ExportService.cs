using System.Drawing.Imaging;
using Magic.Capture.App.Capture;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Export;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Magic.Capture.App.Export;

internal sealed class ExportService
{
    public async Task<StorageFile?> SaveImageAsAsync(Window owner, CaptureAsset asset, string format, int jpegQuality, string fileNameTemplate)
    {
        var normalized = format.Trim().ToLowerInvariant();
        var extension = ExtensionFor(normalized);
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeChoices.Add(normalized.ToUpperInvariant(), new List<string> { extension });
        picker.SuggestedFileName = FileNameTemplate.Render(fileNameTemplate, DateTimeOffset.Now, 1);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(owner));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return null;

        var bytes = EncodeForFormat(asset, normalized, jpegQuality);
        await FileIO.WriteBytesAsync(file, bytes);
        return file;
    }

    public async Task<StorageFolder?> PickImageOutputFolderAsync(Window owner)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(owner));
        return await picker.PickSingleFolderAsync();
    }

    public async Task<StorageFile> SaveImageToFolderAsync(
        StorageFolder folder,
        CaptureAsset asset,
        string format,
        int jpegQuality,
        string fileNameTemplate,
        int sequence)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(asset);
        var normalized = format.Trim().ToLowerInvariant();
        var extension = ExtensionFor(normalized);
        var baseName = FileNameTemplate.Render(fileNameTemplate, DateTimeOffset.Now, Math.Max(1, sequence));
        var file = await folder.CreateFileAsync(baseName + extension, CreationCollisionOption.GenerateUniqueName);
        var bytes = EncodeForFormat(asset, normalized, jpegQuality);
        await FileIO.WriteBytesAsync(file, bytes);
        return file;
    }

    private static string ExtensionFor(string normalized) => normalized switch
    {
        "png" => ".png",
        "jpg" or "jpeg" => ".jpg",
        "bmp" => ".bmp",
        "tif" or "tiff" => ".tiff",
        "pdf" => ".pdf",
        _ => ".png"
    };

    private static byte[] EncodeForFormat(CaptureAsset asset, string normalized, int jpegQuality) => normalized switch
    {
        "png" => asset.PngBytes,
        "jpg" or "jpeg" => BitmapCodec.Transcode(asset.PngBytes, ImageFormat.Jpeg, jpegQuality),
        "bmp" => BitmapCodec.Transcode(asset.PngBytes, ImageFormat.Bmp),
        "tif" or "tiff" => BitmapCodec.Transcode(asset.PngBytes, ImageFormat.Tiff),
        "pdf" => PdfImageDocumentWriter.Write([new PdfJpegPage(BitmapCodec.Transcode(asset.PngBytes, ImageFormat.Jpeg, jpegQuality), asset.Width, asset.Height)]),
        _ => asset.PngBytes
    };

    public async Task<StorageFile?> SaveBytesAsAsync(Window owner, byte[] bytes, string description, string extension, string suggestedFileName)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!extension.StartsWith('.')) extension = "." + extension;
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = suggestedFileName };
        picker.FileTypeChoices.Add(description, new List<string> { extension });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(owner));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return null;
        await FileIO.WriteBytesAsync(file, bytes);
        return file;
    }

    public async Task<StorageFile?> SaveTextAsAsync(Window owner, string content, string description, string extension)
    {
        if (!extension.StartsWith('.')) extension = "." + extension;
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = "Magic Capture Desktop" };
        picker.FileTypeChoices.Add(description, new List<string> { extension });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Platform.WindowHelpers.GetWindowHandle(owner));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return null;
        await FileIO.WriteTextAsync(file, content);
        return file;
    }
}
