using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Ai;
using Magic.Capture.Core.Destinations;
using Magic.Capture.Core.LocalActions;
using Magic.Capture.Core.Portability;
using Magic.Capture.Core.Settings;
using Magic.Capture.Core.Storage;
using Magic.Capture.Core.Workflows;

namespace Magic.Capture.App.Persistence;

internal sealed record ConfigurationArchiveImportResult(int ImportedFiles, AppSettings ImportedSettings, string? Warning = null);

internal sealed class ConfigurationArchiveService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ConfigurationArchiveService(AppPaths paths) => _paths = paths;

    public async Task ExportAsync(string destinationPath, AppSettings currentSettings, string sourceAppVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(currentSettings);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["settings.json"] = SerializeBounded(AppSettingsRules.NormalizeForRuntime(currentSettings), LocalConfigurationLimits.MaximumSettingsJsonBytes)
            };

            foreach (var (name, path, maximum) in ConfigurationFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (name == "settings.json" || !File.Exists(path)) continue;
                payloads[name] = await ReadFileBoundedAsync(path, maximum, cancellationToken);
            }

            var inventory = payloads.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new PortableArchiveEntry(pair.Key, pair.Value.LongLength, Sha256(pair.Value)))
                .ToArray();
            var manifest = new PortableArchiveManifest(
                PortableArchivePolicy.CurrentSchemaVersion,
                PortableArchivePolicy.ProductName,
                sourceAppVersion,
                DateTimeOffset.UtcNow,
                PortableArchiveKind.Configuration,
                inventory);
            ThrowIfInvalid(manifest);

            var temp = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
                {
                    foreach (var pair in payloads.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                        await WriteEntryAsync(archive, pair.Key, pair.Value, cancellationToken);
                    await WriteEntryAsync(archive, PortableArchivePolicy.ManifestEntryName,
                        SerializeBounded(manifest, PortableArchivePolicy.MaximumManifestBytes), cancellationToken);
                }
                File.Move(temp, destinationPath, overwrite: true);
            }
            finally
            {
                TryDelete(temp);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ConfigurationArchiveImportResult> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var info = new FileInfo(sourcePath);
            if (!info.Exists || info.Length <= 0) throw new InvalidDataException("Configuration archive is missing or empty.");
            if (info.Length > PortableArchivePolicy.MaximumConfigurationPayloadBytes * 2)
                throw new InvalidDataException("Configuration archive compressed file exceeds the safety budget.");

            Dictionary<string, byte[]> payloads;
            PortableArchiveManifest manifest;
            await using (var file = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false))
            {
                if (archive.Entries.Count > PortableArchivePolicy.MaximumConfigurationEntries + 1)
                    throw new InvalidDataException("Configuration archive contains too many ZIP entries.");
                var entries = RequireUniqueCanonicalEntries(archive);
                if (!entries.TryGetValue(PortableArchivePolicy.ManifestEntryName, out var manifestEntry))
                    throw new InvalidDataException("Configuration archive is missing manifest.json.");
                var manifestBytes = await ReadZipEntryAsync(manifestEntry, PortableArchivePolicy.MaximumManifestBytes, cancellationToken);
                manifest = JsonSerializer.Deserialize<PortableArchiveManifest>(manifestBytes, JsonOptions)
                    ?? throw new InvalidDataException("Configuration archive manifest is invalid.");
                if (manifest.Kind != PortableArchiveKind.Configuration)
                    throw new InvalidDataException("Archive is not a Magic Capture Desktop configuration archive.");
                ThrowIfInvalid(manifest);

                var expectedNames = manifest.Entries.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
                var actualNames = entries.Keys.Where(name => name != PortableArchivePolicy.ManifestEntryName).ToHashSet(StringComparer.Ordinal);
                if (!expectedNames.SetEquals(actualNames))
                    throw new InvalidDataException("Configuration archive ZIP payloads do not exactly match the manifest inventory.");

                payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                foreach (var inventoryEntry in manifest.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var zipEntry = entries[inventoryEntry.Name];
                    if (zipEntry.Length != inventoryEntry.Length)
                        throw new InvalidDataException($"Configuration payload length does not match manifest: {inventoryEntry.Name}");
                    var maximum = MaximumForConfigurationEntry(inventoryEntry.Name);
                    var bytes = await ReadZipEntryAsync(zipEntry, maximum, cancellationToken);
                    if (!string.Equals(Sha256(bytes), inventoryEntry.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Configuration payload SHA-256 mismatch: {inventoryEntry.Name}");
                    ValidatePayload(inventoryEntry.Name, bytes);
                    payloads[inventoryEntry.Name] = bytes;
                }
            }

            if (!payloads.TryGetValue("settings.json", out var settingsBytes))
                throw new InvalidDataException("Configuration archive must contain settings.json.");
            var importedSettings = JsonSerializer.Deserialize<AppSettings>(settingsBytes, JsonOptions)
                ?? throw new InvalidDataException("Imported settings are invalid.");
            importedSettings = AppSettingsRules.NormalizeForRuntime(importedSettings);

            await CommitTransactionAsync(payloads, cancellationToken);
            return new ConfigurationArchiveImportResult(payloads.Count, importedSettings);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CommitTransactionAsync(IReadOnlyDictionary<string, byte[]> payloads, CancellationToken cancellationToken)
    {
        var transactionId = Guid.NewGuid().ToString("N");
        var stageRoot = Path.Combine(_paths.Root, "config-import-stage-" + transactionId);
        Directory.CreateDirectory(stageRoot);
        var committed = new List<(string Destination, string? Backup)>();
        try
        {
            foreach (var pair in payloads)
            {
                var stage = Path.Combine(stageRoot, pair.Key);
                await AtomicFile.WriteBytesAsync(stage, pair.Value, cancellationToken);
            }

            foreach (var pair in payloads.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = DestinationForConfigurationEntry(pair.Key);
                var backup = File.Exists(destination) ? destination + ".import-backup-" + transactionId : null;
                if (backup is not null) File.Copy(destination, backup, overwrite: false);
                var stage = Path.Combine(stageRoot, pair.Key);
                File.Move(stage, destination, overwrite: true);
                committed.Add((destination, backup));
            }

            foreach (var item in committed)
            {
                TryDelete(item.Backup);
                TryDelete(item.Destination + ".bak");
            }
        }
        catch
        {
            foreach (var item in committed.AsEnumerable().Reverse())
            {
                try
                {
                    if (item.Backup is not null && File.Exists(item.Backup)) File.Copy(item.Backup, item.Destination, overwrite: true);
                    else TryDelete(item.Destination);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }
        finally
        {
            foreach (var item in committed) TryDelete(item.Backup);
            try { if (Directory.Exists(stageRoot)) Directory.Delete(stageRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void ValidatePayload(string name, byte[] bytes)
    {
        switch (name)
        {
            case "settings.json":
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(bytes, JsonOptions) ?? throw new InvalidDataException("settings.json is invalid.");
                if (!AppSettingsRules.IsPersistenceSchemaSupported(settings.PersistenceSchemaVersion))
                    throw new InvalidDataException($"settings.json uses unsupported persistence schema {settings.PersistenceSchemaVersion}.");
                _ = AppSettingsRules.NormalizeForRuntime(settings);
                break;
            }
            case "workflows.json":
            {
                var workflows = JsonSerializer.Deserialize<List<CaptureWorkflow>>(bytes, JsonOptions) ?? throw new InvalidDataException("workflows.json is invalid.");
                LocalConfigurationLimits.ValidateCount(workflows.Count, LocalConfigurationLimits.MaximumCustomWorkflows, "Custom workflows");
                EnsureUnique(workflows, workflow => workflow.Id, workflow => workflow is not null && !workflow.IsBuiltIn && WorkflowValidator.Validate(workflow).IsValid, "workflow");
                break;
            }
            case "destinations.json":
            {
                var destinations = JsonSerializer.Deserialize<List<CustomHttpDestination>>(bytes, JsonOptions) ?? throw new InvalidDataException("destinations.json is invalid.");
                LocalConfigurationLimits.ValidateCount(destinations.Count, LocalConfigurationLimits.MaximumDestinations, "Destinations");
                EnsureUnique(destinations, item => item.Id, item => item is not null && DestinationValidator.Validate(item).IsValid, "destination");
                break;
            }
            case "local-actions.json":
            {
                var actions = JsonSerializer.Deserialize<List<LocalActionProfile>>(bytes, JsonOptions) ?? throw new InvalidDataException("local-actions.json is invalid.");
                LocalConfigurationLimits.ValidateCount(actions.Count, LocalConfigurationLimits.MaximumLocalActions, "Local Actions");
                EnsureUnique(actions, item => item.Id, item => item is not null && LocalActionProfileValidator.Validate(item).IsValid, "Local Action");
                break;
            }
            case "magic-actions.json":
            {
                var actions = JsonSerializer.Deserialize<List<MagicActionDefinition>>(bytes, JsonOptions) ?? throw new InvalidDataException("magic-actions.json is invalid.");
                LocalConfigurationLimits.ValidateCount(actions.Count, LocalConfigurationLimits.MaximumMagicActions, "Custom Magic Actions");
                EnsureUnique(actions, item => item.Id, item => item is not null && !item.IsBuiltIn && MagicActionValidator.Validate(item).IsValid, "Magic Action");
                break;
            }
            case "magic-recipes.json":
            {
                var recipes = JsonSerializer.Deserialize<List<MagicRecipe>>(bytes, JsonOptions) ?? throw new InvalidDataException("magic-recipes.json is invalid.");
                LocalConfigurationLimits.ValidateCount(recipes.Count, LocalConfigurationLimits.MaximumMagicRecipes, "Magic Recipes");
                EnsureUnique(recipes, item => item.Id, item => item is not null && MagicRecipeValidator.Validate(item).IsValid, "Magic Recipe");
                break;
            }
            default:
                throw new InvalidDataException($"Configuration payload is not allowlisted: {name}");
        }
    }

    private static void EnsureUnique<T>(IEnumerable<T> items, Func<T, string> id, Func<T, bool> valid, string label)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null || !valid(item)) throw new InvalidDataException($"Imported {label} storage contains an invalid item.");
            var value = id(item);
            if (string.IsNullOrWhiteSpace(value) || !ids.Add(value)) throw new InvalidDataException($"Imported {label} storage contains a missing or duplicate id.");
        }
    }

    private IEnumerable<(string Name, string Path, long Maximum)> ConfigurationFiles()
    {
        yield return ("settings.json", _paths.SettingsFile, LocalConfigurationLimits.MaximumSettingsJsonBytes);
        yield return ("workflows.json", _paths.WorkflowsFile, LocalConfigurationLimits.MaximumWorkflowJsonBytes);
        yield return ("destinations.json", _paths.DestinationsFile, LocalConfigurationLimits.MaximumDestinationJsonBytes);
        yield return ("local-actions.json", _paths.LocalActionsFile, LocalConfigurationLimits.MaximumLocalActionJsonBytes);
        yield return ("magic-actions.json", _paths.MagicActionsFile, LocalConfigurationLimits.MaximumMagicActionJsonBytes);
        yield return ("magic-recipes.json", _paths.AiRecipesFile, LocalConfigurationLimits.MaximumMagicRecipeJsonBytes);
    }

    private string DestinationForConfigurationEntry(string name) => name switch
    {
        "settings.json" => _paths.SettingsFile,
        "workflows.json" => _paths.WorkflowsFile,
        "destinations.json" => _paths.DestinationsFile,
        "local-actions.json" => _paths.LocalActionsFile,
        "magic-actions.json" => _paths.MagicActionsFile,
        "magic-recipes.json" => _paths.AiRecipesFile,
        _ => throw new InvalidDataException($"Configuration entry is not allowlisted: {name}")
    };

    private static long MaximumForConfigurationEntry(string name) => name switch
    {
        "settings.json" => LocalConfigurationLimits.MaximumSettingsJsonBytes,
        "workflows.json" => LocalConfigurationLimits.MaximumWorkflowJsonBytes,
        "destinations.json" => LocalConfigurationLimits.MaximumDestinationJsonBytes,
        "local-actions.json" => LocalConfigurationLimits.MaximumLocalActionJsonBytes,
        "magic-actions.json" => LocalConfigurationLimits.MaximumMagicActionJsonBytes,
        "magic-recipes.json" => LocalConfigurationLimits.MaximumMagicRecipeJsonBytes,
        _ => throw new InvalidDataException($"Configuration entry is not allowlisted: {name}")
    };

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

    private static async Task<byte[]> ReadFileBoundedAsync(string path, long maximum, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await BoundedStreamReader.ReadExactAsync(stream, stream.Length, maximum, cancellationToken);
    }

    private static async Task<byte[]> ReadZipEntryAsync(ZipArchiveEntry entry, long maximum, CancellationToken cancellationToken)
    {
        if (entry.Length <= 0 || entry.Length > maximum) throw new InvalidDataException($"Archive entry exceeds its safety budget: {entry.FullName}");
        await using var stream = entry.Open();
        return await BoundedStreamReader.ReadExactAsync(stream, entry.Length, maximum, cancellationToken);
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, byte[] bytes, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private static byte[] SerializeBounded<T>(T value, long maximum)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (bytes.LongLength <= 0 || bytes.LongLength > maximum) throw new InvalidDataException("Serialized configuration payload exceeds its safety budget.");
        return bytes;
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void ThrowIfInvalid(PortableArchiveManifest manifest)
    {
        var validation = PortableArchivePolicy.ValidateManifest(manifest);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
