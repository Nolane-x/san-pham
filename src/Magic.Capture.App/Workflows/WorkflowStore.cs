using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Storage;
using Magic.Capture.Core.Workflows;
using System.Text.Json;

namespace Magic.Capture.App.Workflows;

internal sealed class WorkflowStore
{
    private readonly AppPaths _paths;
    private bool _writeEnabled;

    public WorkflowStore(AppPaths paths) => _paths = paths;

    public async Task<IReadOnlyList<CaptureWorkflow>> LoadAsync(CancellationToken cancellationToken = default)
    {
        _writeEnabled = false;
        var custom = await AtomicJsonFile.ReadAsync<List<CaptureWorkflow>>(
            _paths.WorkflowsFile, cancellationToken, LocalConfigurationLimits.MaximumWorkflowJsonBytes) ?? [];
        LocalConfigurationLimits.ValidateCount(custom.Count, LocalConfigurationLimits.MaximumCustomWorkflows, "Custom workflows");

        var ids = new HashSet<string>(WorkflowCatalog.BuiltIns.Select(workflow => workflow.Id), StringComparer.Ordinal);
        var valid = new List<CaptureWorkflow>(custom.Count);
        foreach (var workflow in custom)
        {
            if (workflow is null || workflow.IsBuiltIn)
                throw new InvalidDataException("Custom workflow storage contains a built-in or null workflow.");
            var validation = WorkflowValidator.Validate(workflow);
            if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
            if (!ids.Add(workflow.Id)) throw new InvalidDataException($"Duplicate workflow id: {workflow.Id}");
            valid.Add(workflow);
        }

        _writeEnabled = true;
        return [.. WorkflowCatalog.BuiltIns, .. valid];
    }

    public async Task SaveCustomAsync(IEnumerable<CaptureWorkflow> workflows, CancellationToken cancellationToken = default)
    {
        if (!_writeEnabled) throw new InvalidOperationException("Workflow storage is not safely loaded; reload it before saving custom workflows.");
        ArgumentNullException.ThrowIfNull(workflows);
        var candidates = workflows.Where(w => w is not null && !w.IsBuiltIn)
            .Take(LocalConfigurationLimits.MaximumCustomWorkflows + 1).ToArray();
        LocalConfigurationLimits.ValidateCount(candidates.Length, LocalConfigurationLimits.MaximumCustomWorkflows, "Custom workflows");

        var ids = new HashSet<string>(WorkflowCatalog.BuiltIns.Select(workflow => workflow.Id), StringComparer.Ordinal);
        foreach (var workflow in candidates)
        {
            var validation = WorkflowValidator.Validate(workflow);
            if (!validation.IsValid) throw new InvalidOperationException(string.Join(" ", validation.Errors));
            if (!ids.Add(workflow.Id)) throw new InvalidDataException($"Duplicate workflow id: {workflow.Id}");
        }
        await AtomicJsonFile.WriteAsync(_paths.WorkflowsFile, candidates, cancellationToken, LocalConfigurationLimits.MaximumWorkflowJsonBytes);
    }

    public async Task<CaptureWorkflow> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > LocalConfigurationLimits.MaximumWorkflowJsonBytes)
            throw new InvalidDataException("Workflow file is missing, empty, or too large.");

        await using var stream = info.OpenRead();
        var workflow = await JsonSerializer.DeserializeAsync<CaptureWorkflow>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Workflow file is invalid.");
        workflow = workflow with { IsBuiltIn = false };
        var validation = WorkflowValidator.Validate(workflow);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
        return workflow;
    }

    public async Task ExportAsync(CaptureWorkflow workflow, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var export = workflow with { IsBuiltIn = false };
        var validation = WorkflowValidator.Validate(export);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, export, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
    }
}
