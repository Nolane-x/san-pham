using System.Text.Json;

namespace Magic.Capture.App.Persistence;

internal static class AtomicJsonFile
{
    public const long DefaultMaximumJsonBytes = 16L * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static async Task<T?> ReadAsync<T>(
        string path,
        CancellationToken cancellationToken = default,
        long maximumBytes = DefaultMaximumJsonBytes)
    {
        ValidateMaximum(maximumBytes);
        var backup = path + ".bak";
        if (!File.Exists(path))
        {
            if (!File.Exists(backup)) return default;
            var backupOnly = await TryReadBackupAsync<T>(backup, cancellationToken, maximumBytes);
            if (backupOnly.Success) return backupOnly.Value;
            throw new InvalidDataException($"Primary JSON is missing and its backup is not readable: {Path.GetFileName(path)}");
        }

        try
        {
            return await ReadFileAsync<T>(path, cancellationToken, maximumBytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception primaryError) when (primaryError is JsonException or InvalidDataException)
        {
            var recovered = await TryReadBackupAsync<T>(backup, cancellationToken, maximumBytes);
            if (!recovered.Success)
                throw new InvalidDataException($"JSON data is corrupt or exceeds its safe size budget and no valid backup is available: {Path.GetFileName(path)}", primaryError);

            QuarantineCorruptPrimary(path);
            try { File.Copy(backup, path, overwrite: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return recovered.Value;
        }
    }

    public static async Task WriteAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken = default,
        long maximumBytes = DefaultMaximumJsonBytes)
    {
        ValidateMaximum(maximumBytes);
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Target path has no directory.");
        Directory.CreateDirectory(directory);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        var backup = path + ".bak";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
                if (stream.Length > maximumBytes)
                    throw new InvalidDataException($"JSON data exceeds the safe {maximumBytes / (1024 * 1024):N0} MB persistence limit.");
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temp, path, backup, ignoreMetadataErrors: true);
                    return;
                }
                catch (PlatformNotSupportedException) { }
                catch (IOException) { }

                // Platforms without File.Replace may use copy + move, but never overwrite the
                // only readable generation unless a safety backup was created successfully.
                CreateFallbackBackupOrThrow(path, backup);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }


    private static void CreateFallbackBackupOrThrow(string path, string backup)
    {
        try
        {
            File.Copy(path, backup, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Could not create the required safety backup; the existing JSON file was left untouched.", ex);
        }
    }

    private static async Task<T?> ReadFileAsync<T>(string path, CancellationToken cancellationToken, long maximumBytes)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        if (stream.Length <= 0) throw new InvalidDataException($"JSON data is empty: {Path.GetFileName(path)}");
        if (stream.Length > maximumBytes)
            throw new InvalidDataException($"JSON data exceeds the safe {maximumBytes / (1024 * 1024):N0} MB read limit: {Path.GetFileName(path)}");
        var value = await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
        if (value is null) throw new InvalidDataException($"JSON root must not be null: {Path.GetFileName(path)}");
        return value;
    }

    private static async Task<(bool Success, T? Value)> TryReadBackupAsync<T>(
        string backupPath,
        CancellationToken cancellationToken,
        long maximumBytes)
    {
        if (!File.Exists(backupPath)) return (false, default);
        try
        {
            return (true, await ReadFileAsync<T>(backupPath, cancellationToken, maximumBytes));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException)
        {
            return (false, default);
        }
        catch (InvalidDataException)
        {
            return (false, default);
        }
        catch (IOException)
        {
            return (false, default);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, default);
        }
    }

    private static void ValidateMaximum(long maximumBytes)
    {
        if (maximumBytes <= 0 || maximumBytes > 1024L * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
    }

    private static void QuarantineCorruptPrimary(string path)
    {
        if (!File.Exists(path)) return;
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var quarantine = Path.Combine(
            directory,
            $"{Path.GetFileName(path)}.corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        try { File.Move(path, quarantine, overwrite: false); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
