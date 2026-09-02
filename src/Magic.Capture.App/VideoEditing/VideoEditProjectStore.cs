using System.Text.Json;
using Magic.Capture.App.Persistence;
using Magic.Capture.Core.VideoEditing;

namespace Magic.Capture.App.VideoEditing;

internal sealed record VideoEditProjectLoadResult(
    VideoEditProject? Project,
    bool IsReadOnly,
    string? Warning);

internal sealed class VideoEditProjectStore
{
    public const long MaximumProjectBytes = 4L * 1024 * 1024;

    public async Task<VideoEditProjectLoadResult> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Clip project path must be fully qualified.", nameof(path));
        if (!File.Exists(path)) return new VideoEditProjectLoadResult(null, false, null);

        var schema = await ProbeSchemaAsync(path, cancellationToken);
        if (schema is null)
            throw new InvalidDataException("Clip project schema could not be read.");
        if (!VideoEditProjectSchema.CanRead(schema.Value))
            throw new InvalidDataException($"Clip project schema {schema.Value} is unsupported.");

        var project = await AtomicJsonFile.ReadAsync<VideoEditProject>(path, cancellationToken, MaximumProjectBytes)
            ?? throw new InvalidDataException("Clip project is empty or invalid.");

        var future = project.SchemaVersion > VideoEditProjectSchema.CurrentVersion;
        if (!future)
        {
            project = VideoEditProjectMigration.UpgradeToCurrent(project);
            var errors = VideoEditRules.ValidateProject(project);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
        }

        return new VideoEditProjectLoadResult(
            project,
            future,
            future ? $"Clip project schema {project.SchemaVersion} is newer than schema {VideoEditProjectSchema.CurrentVersion}; it is open read-only." : null);
    }

    public async Task SaveAsync(VideoEditProject project, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Clip project path must be fully qualified.", nameof(path));
        if (!VideoEditProjectSchema.CanWrite(project.SchemaVersion))
            throw new InvalidOperationException("A future clip-project schema is read-only in this version.");

        var errors = VideoEditRules.ValidateProject(project);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));

        var existingSchema = await ProbeSchemaAsync(path, cancellationToken);
        if (existingSchema is > VideoEditProjectSchema.CurrentVersion)
            throw new InvalidOperationException("The existing clip project was written by a newer Magic Capture Desktop version and will not be overwritten.");

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Clip project has no parent directory."));
        await AtomicJsonFile.WriteAsync(path, project, cancellationToken, MaximumProjectBytes);
    }

    private static async Task<int?> ProbeSchemaAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        var info = new FileInfo(path);
        if (info.Length == 0) return null; // FileSavePicker creates an empty placeholder before SaveAsync writes atomically.
        if (info.Length > MaximumProjectBytes) throw new InvalidDataException("Clip project exceeds the 4 MiB limit.");

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase)) continue;
                return property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var value) ? value : null;
            }
            return 0;
        }
        catch (OperationCanceledException) { throw; }
        catch (JsonException ex) { throw new InvalidDataException("Clip project JSON is invalid.", ex); }
    }
}
