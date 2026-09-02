using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Ai;
using Magic.Capture.Core.Storage;
using System.Text.Json;

namespace Magic.Capture.App.Ai;

internal sealed class MagicActionStore
{
    private const long MaxImportBytes = 256 * 1024;
    private readonly string _path;
    private bool _writeEnabled;

    public MagicActionStore(AppPaths paths) => _path = paths.MagicActionsFile;

    public async Task<IReadOnlyList<MagicActionDefinition>> LoadAsync(CancellationToken cancellationToken = default)
    {
        _writeEnabled = false;
        var actions = await AtomicJsonFile.ReadAsync<List<MagicActionDefinition>>(
            _path, cancellationToken, LocalConfigurationLimits.MaximumMagicActionJsonBytes) ?? [];
        LocalConfigurationLimits.ValidateCount(actions.Count, LocalConfigurationLimits.MaximumMagicActions, "Custom Magic Actions");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in actions)
        {
            if (action is null || action.IsBuiltIn)
                throw new InvalidDataException("Custom Magic Action storage contains a built-in or null action.");
            var validation = MagicActionValidator.Validate(action);
            if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
            if (!ids.Add(action.Id)) throw new InvalidDataException($"Duplicate Magic Action id: {action.Id}");
        }
        _writeEnabled = true;
        return actions.ToArray();
    }

    public async Task SaveAsync(IReadOnlyList<MagicActionDefinition> actions, CancellationToken cancellationToken = default)
    {
        if (!_writeEnabled) throw new InvalidOperationException("Magic Action storage is not safely loaded; reload it before saving.");
        ArgumentNullException.ThrowIfNull(actions);
        LocalConfigurationLimits.ValidateCount(actions.Count, LocalConfigurationLimits.MaximumMagicActions, "Custom Magic Actions");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in actions)
        {
            if (action is null || action.IsBuiltIn) throw new InvalidDataException("Custom Magic Action storage cannot contain built-in or null actions.");
            var validation = MagicActionValidator.Validate(action);
            if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
            if (!ids.Add(action.Id)) throw new InvalidDataException($"Duplicate Magic Action id: {action.Id}");
        }
        await AtomicJsonFile.WriteAsync(_path, actions, cancellationToken, LocalConfigurationLimits.MaximumMagicActionJsonBytes);
    }

    public async Task<MagicActionDefinition> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > MaxImportBytes) throw new InvalidDataException("Magic Action file is missing or too large.");
        await using var stream = info.OpenRead();
        var action = await JsonSerializer.DeserializeAsync<MagicActionDefinition>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Magic Action file is invalid.");
        action = action with { IsBuiltIn = false };
        var validation = MagicActionValidator.Validate(action);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
        return action;
    }

    public async Task ExportAsync(MagicActionDefinition action, string path, CancellationToken cancellationToken = default)
    {
        var validation = MagicActionValidator.Validate(action);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, action with { IsBuiltIn = false }, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
    }
}
