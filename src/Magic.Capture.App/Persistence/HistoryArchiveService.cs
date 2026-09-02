using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.History;
using Magic.Capture.Core.Imaging;
using Magic.Capture.Core.Portability;
using Magic.Capture.Core.Settings;

namespace Magic.Capture.App.Persistence;

internal sealed record HistoryArchiveImportResult(int Imported, int Failed);

internal sealed class HistoryArchiveService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HistoryStore _history;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HistoryArchiveService(HistoryStore history) => _history = history;

    public async Task<int> ExportAsync(
        string destinationPath,
        IEnumerable<Guid>? selectedIds,
        string sourceAppVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var all = await _history.ListAsync(cancellationToken);
            var selection = selectedIds?.Distinct().Take(PortableArchivePolicy.MaximumHistoryCapturesPerArchive + 1).ToHashSet();
            var items = (selection is null ? all : all.Where(item => selection.Contains(item.Id)))
                .Take(PortableArchivePolicy.MaximumHistoryCapturesPerArchive + 1).ToArray();
            if (items.Length == 0) throw new InvalidOperationException("There are no History captures to export.");
            if (items.Length > PortableArchivePolicy.MaximumHistoryCapturesPerArchive)
                throw new InvalidDataException($"A single History archive supports at most {PortableArchivePolicy.MaximumHistoryCapturesPerArchive:N0} captures. Export in smaller batches.");

            var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(items, JsonOptions);
            if (metadataBytes.LongLength > PortableArchivePolicy.MaximumHistoryMetadataBytes)
                throw new InvalidDataException("History metadata exceeds the archive safety budget.");

            long projectedBytes = metadataBytes.LongLength;
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new FileInfo(_history.GetAbsolutePath(item));
                if (!info.Exists) throw new InvalidDataException($"History capture is missing: {item.Id:N}");
                ImageWorkloadLimits.ValidateEncodedLength(info.Length);
                projectedBytes = SaturatingAdd(projectedBytes, info.Length);
                if (projectedBytes > PortableArchivePolicy.MaximumHistoryArchiveBytes)
                    throw new InvalidDataException("Selected History captures exceed the cumulative archive safety budget.");
            }

            var inventory = new List<PortableArchiveEntry>(items.Length + 1)
            {
                new(PortableArchivePolicy.HistoryMetadataEntryName, metadataBytes.LongLength, Sha256(metadataBytes))
            };
            var temp = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 256 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
                {
                    await WriteBytesEntryAsync(archive, PortableArchivePolicy.HistoryMetadataEntryName, metadataBytes, cancellationToken);
                    foreach (var item in items)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entryName = $"images/{item.Id:N}.png";
                        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                        await using var output = entry.Open();
                        await using var input = new FileStream(_history.GetAbsolutePath(item), FileMode.Open, FileAccess.Read, FileShare.Read,
                            256 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        ImageWorkloadLimits.ValidateEncodedLength(input.Length);
                        var (length, hash) = await CopyAndHashAsync(input, output, ImageWorkloadLimits.MaximumEncodedBytes, cancellationToken);
                        inventory.Add(new PortableArchiveEntry(entryName, length, hash));
                    }

                    var manifest = new PortableArchiveManifest(
                        PortableArchivePolicy.CurrentSchemaVersion,
                        PortableArchivePolicy.ProductName,
                        sourceAppVersion,
                        DateTimeOffset.UtcNow,
                        PortableArchiveKind.History,
                        inventory);
                    ThrowIfInvalid(manifest);
                    var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
                    if (manifestBytes.LongLength > PortableArchivePolicy.MaximumManifestBytes)
                        throw new InvalidDataException("History archive manifest exceeds the safety budget.");
                    await WriteBytesEntryAsync(archive, PortableArchivePolicy.ManifestEntryName, manifestBytes, cancellationToken);
                }
                File.Move(temp, destinationPath, overwrite: true);
            }
            finally
            {
                TryDelete(temp);
            }
            return items.Length;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HistoryArchiveImportResult> ImportAsync(string sourcePath, AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var info = new FileInfo(sourcePath);
            if (!info.Exists || info.Length <= 0) throw new InvalidDataException("History archive is missing or empty.");
            if (info.Length > PortableArchivePolicy.MaximumHistoryArchiveBytes)
                throw new InvalidDataException("History archive compressed file exceeds the safety budget.");

            await using var file = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count > PortableArchivePolicy.MaximumHistoryPayloadEntries + 1)
                throw new InvalidDataException("History archive contains too many ZIP entries.");
            var entries = RequireUniqueCanonicalEntries(archive);
            if (!entries.TryGetValue(PortableArchivePolicy.ManifestEntryName, out var manifestEntry))
                throw new InvalidDataException("History archive is missing manifest.json.");
            var manifestBytes = await ReadEntryAsync(manifestEntry, PortableArchivePolicy.MaximumManifestBytes, cancellationToken);
            var manifest = JsonSerializer.Deserialize<PortableArchiveManifest>(manifestBytes, JsonOptions)
                ?? throw new InvalidDataException("History archive manifest is invalid.");
            if (manifest.Kind != PortableArchiveKind.History)
                throw new InvalidDataException("Archive is not a Magic Capture Desktop History archive.");
            ThrowIfInvalid(manifest);

            var expectedNames = manifest.Entries.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
            var actualNames = entries.Keys.Where(name => name != PortableArchivePolicy.ManifestEntryName).ToHashSet(StringComparer.Ordinal);
            if (!expectedNames.SetEquals(actualNames))
                throw new InvalidDataException("History archive ZIP payloads do not exactly match the manifest inventory.");
            var inventory = manifest.Entries.ToDictionary(entry => entry.Name, StringComparer.Ordinal);

            var metadataInventory = inventory[PortableArchivePolicy.HistoryMetadataEntryName];
            var metadataEntry = entries[PortableArchivePolicy.HistoryMetadataEntryName];
            ValidateLength(metadataEntry, metadataInventory);
            var metadataBytes = await ReadEntryAsync(metadataEntry, PortableArchivePolicy.MaximumHistoryMetadataBytes, cancellationToken);
            ValidateHash(metadataBytes, metadataInventory);
            var items = JsonSerializer.Deserialize<List<HistoryItem>>(metadataBytes, JsonOptions)
                ?? throw new InvalidDataException("History archive metadata is invalid.");
            if (items.Count == 0 || items.Count > PortableArchivePolicy.MaximumHistoryCapturesPerArchive)
                throw new InvalidDataException("History archive contains an invalid capture count.");
            if (items.Any(item => item is null || item.Id == Guid.Empty) || items.Select(item => item.Id).Distinct().Count() != items.Count)
                throw new InvalidDataException("History archive metadata contains null, empty, or duplicate capture ids.");

            var expectedImageNames = items.Select(item => $"images/{item.Id:N}.png").ToHashSet(StringComparer.Ordinal);
            var manifestImageNames = manifest.Entries.Where(entry => PortableArchivePolicy.IsHistoryImageEntry(entry.Name, out _)).Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
            if (!expectedImageNames.SetEquals(manifestImageNames))
                throw new InvalidDataException("History metadata rows do not exactly match the image inventory.");

            // Validate every image payload before mutating local History. This intentionally performs
            // a second bounded read during the import loop: archive integrity wins over saving I/O,
            // and retaining all decoded payloads would violate the archive memory budget.
            await PreflightHistoryPayloadsAsync(items, entries, inventory, cancellationToken);

            var imported = 0;
            var failed = 0;
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    ImageWorkloadLimits.ValidateDimensions(item.Width, item.Height);
                    var name = $"images/{item.Id:N}.png";
                    var manifestImage = inventory[name];
                    var zipImage = entries[name];
                    ValidateLength(zipImage, manifestImage);
                    var bytes = await ReadEntryAsync(zipImage, ImageWorkloadLimits.MaximumEncodedBytes, cancellationToken);
                    ValidateHash(bytes, manifestImage);
                    if (!PngDimensions.TryRead(bytes, out var width, out var height) || width != item.Width || height != item.Height)
                        throw new InvalidDataException("History image dimensions do not match archive metadata.");
                    var added = await _history.ImportPortableAsync(item, bytes, settings, cancellationToken);
                    if (added is null) failed++; else imported++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
                {
                    failed++;
                }
            }

            await _history.ApplyRetentionAsync(settings, cancellationToken);
            return new HistoryArchiveImportResult(imported, failed);
        }
        finally
        {
            _gate.Release();
        }
    }


    private static async Task PreflightHistoryPayloadsAsync(
        IReadOnlyList<HistoryItem> items,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        IReadOnlyDictionary<string, PortableArchiveEntry> inventory,
        CancellationToken cancellationToken)
    {
        long cumulativeBytes = 0;
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImageWorkloadLimits.ValidateDimensions(item.Width, item.Height);
            var name = $"images/{item.Id:N}.png";
            if (!entries.TryGetValue(name, out var zipImage) || !inventory.TryGetValue(name, out var manifestImage))
                throw new InvalidDataException($"History archive is missing the image payload for {item.Id:N}.");

            ValidateLength(zipImage, manifestImage);
            cumulativeBytes = checked(cumulativeBytes + zipImage.Length);
            if (cumulativeBytes > PortableArchivePolicy.MaximumHistoryArchiveBytes)
                throw new InvalidDataException("History archive image payloads exceed the cumulative safety budget.");

            var bytes = await ReadEntryAsync(zipImage, ImageWorkloadLimits.MaximumEncodedBytes, cancellationToken);
            ValidateHash(bytes, manifestImage);
            if (!PngDimensions.TryRead(bytes, out var width, out var height) || width != item.Width || height != item.Height)
                throw new InvalidDataException($"History image dimensions do not match archive metadata for {item.Id:N}.");
        }
    }

    private static Dictionary<string, ZipArchiveEntry> RequireUniqueCanonicalEntries(ZipArchive archive)
    {
        var result = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (!PortableArchivePolicy.IsCanonicalEntryName(entry.FullName))
                throw new InvalidDataException($"Archive contains unsafe ZIP entry name: {entry.FullName}");
            if (!result.TryAdd(entry.FullName, entry))
                throw new InvalidDataException($"Archive contains duplicate ZIP entry name: {entry.FullName}");
        }
        return result;
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, long maximum, CancellationToken cancellationToken)
    {
        if (entry.Length <= 0 || entry.Length > maximum) throw new InvalidDataException($"Archive entry exceeds its safety budget: {entry.FullName}");
        await using var stream = entry.Open();
        return await BoundedStreamReader.ReadExactAsync(stream, entry.Length, maximum, cancellationToken);
    }

    private static async Task WriteBytesEntryAsync(ZipArchive archive, string name, byte[] bytes, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private static async Task<(long Length, string Sha256)> CopyAndHashAsync(Stream input, Stream output, long maximum, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total = checked(total + read);
            if (total > maximum) throw new InvalidDataException("History image exceeds the archive image safety budget.");
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (total <= 0) throw new InvalidDataException("History image payload is empty.");
        return (total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static void ValidateLength(ZipArchiveEntry entry, PortableArchiveEntry inventory)
    {
        if (entry.Length != inventory.Length) throw new InvalidDataException($"Archive payload length mismatch: {inventory.Name}");
    }

    private static void ValidateHash(byte[] bytes, PortableArchiveEntry inventory)
    {
        if (!string.Equals(Sha256(bytes), inventory.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Archive payload SHA-256 mismatch: {inventory.Name}");
    }

    private static void ThrowIfInvalid(PortableArchiveManifest manifest)
    {
        var validation = PortableArchivePolicy.ValidateManifest(manifest);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static long SaturatingAdd(long left, long right) => long.MaxValue - left < right ? long.MaxValue : left + right;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
