using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using Windows.Storage;

namespace Magic.Capture.App.Platform;

internal sealed class ClipboardService
{
    public async Task CopyImageAsync(byte[] pngBytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(pngBytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
        }
        stream.Seek(0);
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    public async Task CopyFileAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("File path is required.", nameof(path));
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetStorageItems([file]);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    public void CopyText(string text)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text ?? string.Empty);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }
}
