using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;

namespace Magic.Capture.App.Persistence;

internal sealed class HistoryProcessIconCache
{
    private const int MaximumCachedIcons = 256;
    private const int MaximumIconPngBytes = 2 * 1024 * 1024;
    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HistoryProcessIconCache(AppPaths paths) => _paths = paths;

    public async Task<string?> GetOrCreateAsync(string? executablePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath.Length > 2048 || !IsLocalWindowsExecutablePath(executablePath) || !File.Exists(executablePath)) return null;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(executablePath.Trim().ToUpperInvariant()))).ToLowerInvariant();
        var path = Path.Combine(_paths.HistoryIconCacheRoot, key + ".png");
        if (File.Exists(path)) return path;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(path)) return path;
            byte[] bytes;
            try
            {
                using var icon = Icon.ExtractAssociatedIcon(executablePath);
                if (icon is null) return null;
                using var bitmap = icon.ToBitmap();
                using var stream = new MemoryStream();
                bitmap.Save(stream, ImageFormat.Png);
                if (stream.Length <= 0 || stream.Length > MaximumIconPngBytes) return null;
                bytes = stream.ToArray();
            }
            catch (Exception ex) when (ex is ArgumentException or ExternalException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
            await AtomicFile.WriteBytesAsync(path, bytes, cancellationToken);
            PruneOldIcons();
            return path;
        }
        finally { _gate.Release(); }
    }

    private static bool IsLocalWindowsExecutablePath(string path) =>
        path.Length >= 4 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');

    private void PruneOldIcons()
    {
        try
        {
            var files = Directory.EnumerateFiles(_paths.HistoryIconCacheRoot, "*.png", SearchOption.TopDirectoryOnly)
                .Select(value => new FileInfo(value))
                .OrderByDescending(value => value.LastWriteTimeUtc)
                .Skip(MaximumCachedIcons)
                .ToArray();
            foreach (var file in files) try { file.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
