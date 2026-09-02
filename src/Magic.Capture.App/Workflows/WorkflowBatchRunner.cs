using Magic.Capture.App.Capture;
using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Workflows;

namespace Magic.Capture.App.Workflows;

internal sealed record WorkflowBatchItemResult(Guid AssetId, bool Succeeded, string? Message);

internal sealed record WorkflowBatchExecutionResult(
    int Requested,
    int Completed,
    int Failed,
    IReadOnlyList<WorkflowBatchItemResult> Items);

internal sealed class WorkflowBatchRunner
{
    private readonly WorkflowExecutor _executor;
    private readonly WorkflowTraceStore _traces;
    private readonly LocalLog _log;
    private readonly HistoryLibraryStore _historyLibrary;

    public WorkflowBatchRunner(WorkflowExecutor executor, WorkflowTraceStore traces, LocalLog log, HistoryLibraryStore historyLibrary)
    {
        _executor = executor;
        _traces = traces;
        _log = log;
        _historyLibrary = historyLibrary;
    }

    public async Task<WorkflowBatchExecutionResult> ExecuteAsync(
        CaptureWorkflow workflow,
        IReadOnlyList<Func<CancellationToken, Task<CaptureAsset?>>> assetLoaders,
        Func<CaptureAsset, WorkflowExecutionContext> contextFactory,
        bool stopOnFailure = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(assetLoaders);
        ArgumentNullException.ThrowIfNull(contextFactory);
        if (assetLoaders.Count == 0) return new WorkflowBatchExecutionResult(0, 0, 0, []);
        if (assetLoaders.Count > WorkflowRuntimePolicy.MaximumBatchAssets)
            throw new InvalidDataException($"Workflow batch cannot exceed {WorkflowRuntimePolicy.MaximumBatchAssets:N0} captures.");

        IReadOnlyDictionary<string, string>? sharedParameters = null;
        var items = new List<WorkflowBatchItemResult>(assetLoaders.Count);
        var completed = 0;
        var failed = 0;

        foreach (var loader in assetLoaders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CaptureAsset? asset;
            try { asset = await loader(cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex))
            {
                failed++;
                _log.Error("WorkflowBatchLoad", ex);
                items.Add(new WorkflowBatchItemResult(Guid.Empty, false, "Capture could not be loaded."));
                if (stopOnFailure) break;
                continue;
            }
            if (asset is null)
            {
                failed++;
                items.Add(new WorkflowBatchItemResult(Guid.Empty, false, "Capture could not be loaded."));
                if (stopOnFailure) break;
                continue;
            }

            var context = contextFactory(asset);
            try
            {
                sharedParameters ??= await _executor.ResolveParameterValuesAsync(workflow, context, cancellationToken);
                context = context with { InitialVariables = MergeVariables(context.InitialVariables, sharedParameters) };
                await RecordWorkflowStartBestEffortAsync(asset.Id, workflow, cancellationToken);
                var result = await _executor.ExecuteAsync(workflow, context, cancellationToken);
                await RecordAiActionsBestEffortAsync(asset.Id, workflow, result, cancellationToken);
                await TryAppendTraceAsync(workflow, result, asset.Id, cancellationToken);
                if (result.Succeeded)
                {
                    completed++;
                    items.Add(new WorkflowBatchItemResult(asset.Id, true, null));
                }
                else
                {
                    failed++;
                    var failure = result.Steps.LastOrDefault(step => !step.Succeeded);
                    items.Add(new WorkflowBatchItemResult(asset.Id, false, failure?.Message ?? "Workflow stopped."));
                    if (stopOnFailure) break;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex))
            {
                failed++;
                _log.Error("WorkflowBatch", ex);
                await TryAppendFailureTraceAsync(workflow, context.DryRun, asset.Id, cancellationToken);
                items.Add(new WorkflowBatchItemResult(asset.Id, false, ex.Message));
                if (sharedParameters is null || stopOnFailure) break;
            }
        }

        return new WorkflowBatchExecutionResult(assetLoaders.Count, completed, failed, items);
    }

    private async Task RecordWorkflowStartBestEffortAsync(Guid assetId, CaptureWorkflow workflow, CancellationToken cancellationToken)
    {
        try { await _historyLibrary.RecordWorkflowAsync(assetId, workflow.Id, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex)) { _log.Error("HistoryWorkflowActivity", ex); }
    }

    private async Task RecordAiActionsBestEffortAsync(Guid assetId, CaptureWorkflow workflow, WorkflowExecutionResult result, CancellationToken cancellationToken)
    {
        try
        {
            var attemptedStepIds = result.Steps
                .Where(step => step.Status is not WorkflowStepStatus.Skipped and not WorkflowStepStatus.WouldRun)
                .Select(step => step.StepId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var actionId in workflow.Steps
                .Where(step => step.IsEnabled != false && step.Kind == WorkflowStepKind.RunMagicAction && attemptedStepIds.Contains(step.Id) && !string.IsNullOrWhiteSpace(step.Argument))
                .Select(step => step.Argument!)
                .Distinct(StringComparer.Ordinal))
                await _historyLibrary.RecordAiActionAsync(assetId, actionId, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex)) { _log.Error("HistoryAiActionActivity", ex); }
    }

    private async Task TryAppendTraceAsync(CaptureWorkflow workflow, WorkflowExecutionResult result, Guid assetId, CancellationToken cancellationToken)
    {
        try { await _traces.AppendAsync(workflow, result, assetId, cancellationToken: cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex)) { _log.Error("WorkflowTrace", ex); }
    }

    private async Task TryAppendFailureTraceAsync(CaptureWorkflow workflow, bool dryRun, Guid assetId, CancellationToken cancellationToken)
    {
        try { await _traces.AppendFailureAsync(workflow, dryRun, assetId, cancellationToken: cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex)) { _log.Error("WorkflowTraceFailure", ex); }
    }

    private static IReadOnlyDictionary<string, string>? MergeVariables(
        IReadOnlyDictionary<string, string>? initial,
        IReadOnlyDictionary<string, string> resolvedParameters)
    {
        if ((initial is null || initial.Count == 0) && resolvedParameters.Count == 0) return null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (initial is not null)
            foreach (var (name, value) in initial) values[name] = value;
        foreach (var (name, value) in resolvedParameters) values[name] = value;
        return values;
    }
}
