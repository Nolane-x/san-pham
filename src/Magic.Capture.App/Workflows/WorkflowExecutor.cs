using Magic.Capture.App.Ai;
using Magic.Capture.App.Analysis;
using Magic.Capture.App.Capture;
using Magic.Capture.App.Commerce;
using Magic.Capture.App.Platform;
using Magic.Capture.App.Destinations;
using Magic.Capture.App.LocalActions;
using Magic.Capture.App.Utilities;
using Magic.Capture.Core.Ai;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Commerce;
using Magic.Capture.Core.Signals;
using Magic.Capture.Core.Tables;
using Magic.Capture.Core.Workflows;
using Magic.Capture.Core.Platform;
using Magic.Capture.Core.LocalActions;

namespace Magic.Capture.App.Workflows;

internal sealed class WorkflowExecutor
{
    private readonly ClipboardService _clipboard;
    private readonly ITextRecognitionService _ocr;
    private readonly BarcodeService _barcodes;
    private readonly ImageUtilityService _images;
    private readonly MetadataService _metadata;
    private readonly MagicActionService _magic;
    private readonly MagicActionStore _actionStore;
    private readonly EntitlementService _entitlements;
    private readonly Func<Magic.Capture.Core.Settings.AppSettings> _settings;
    private readonly DestinationProfileStore _destinations;
    private readonly CustomHttpDestinationClient _destinationClient;
    private readonly LocalActionProfileStore _localActions;
    private readonly LocalActionRunner _localActionRunner;

    public WorkflowExecutor(
        ClipboardService clipboard,
        ITextRecognitionService ocr,
        BarcodeService barcodes,
        ImageUtilityService images,
        MetadataService metadata,
        MagicActionService magic,
        MagicActionStore actionStore,
        EntitlementService entitlements,
        Func<Magic.Capture.Core.Settings.AppSettings> settings,
        DestinationProfileStore destinations,
        CustomHttpDestinationClient destinationClient,
        LocalActionProfileStore localActions,
        LocalActionRunner localActionRunner)
    {
        _clipboard = clipboard;
        _ocr = ocr;
        _barcodes = barcodes;
        _images = images;
        _metadata = metadata;
        _magic = magic;
        _actionStore = actionStore;
        _entitlements = entitlements;
        _settings = settings;
        _destinations = destinations;
        _destinationClient = destinationClient;
        _localActions = localActions;
        _localActionRunner = localActionRunner;
    }

    public async Task<WorkflowExecutionResult> ExecuteAsync(CaptureWorkflow workflow, WorkflowExecutionContext context, CancellationToken cancellationToken = default)
    {
        var validation = WorkflowValidator.Validate(workflow);
        if (!validation.IsValid) throw new InvalidOperationException(string.Join(" ", validation.Errors));
        if (!TierAllows(_entitlements.Current.Tier, workflow.RequiredTier))
            throw new InvalidOperationException($"Workflow '{workflow.Name}' requires {workflow.RequiredTier}.");

        var executionStartedUtc = DateTimeOffset.UtcNow;
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["asset"] = context.Asset,
            ["image"] = context.Asset.PngBytes,
            ["workflow"] = workflow.Name,
            ["filename"] = "capture.png",
            ["width"] = context.Asset.Width,
            ["height"] = context.Asset.Height,
            ["size"] = context.Asset.PngBytes.LongLength,
            ["source"] = context.Asset.SourceDisplayName ?? context.Asset.SourceKind.ToString()
        };
        ApplyInitialVariables(workflow, context, values);
        var resolvedParameters = await ResolveParameterValuesAsync(workflow, context, cancellationToken);
        foreach (var (name, value) in resolvedParameters) values[name] = value;

        var callStack = context.WorkflowCallStack is { Count: > 0 }
            ? context.WorkflowCallStack
            : new[] { workflow.Id };
        context = context with { WorkflowCallStack = callStack };
        var results = new List<WorkflowStepResult>();

        foreach (var step in workflow.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stepStartedUtc = DateTimeOffset.UtcNow;
            if (step.IsEnabled == false)
            {
                results.Add(NewStepResult(step, WorkflowStepStatus.Skipped, "Skipped: step disabled.", 0, stepStartedUtc));
                continue;
            }
            if (!WorkflowConditionEvaluator.Evaluate(step.Condition, values))
            {
                results.Add(NewStepResult(step, WorkflowStepStatus.Skipped, "Skipped: condition not met.", 0, stepStartedUtc));
                continue;
            }

            if (context.IsResume && context.ResumeCompletedSideEffectStepIds?.Contains(step.Id) == true)
            {
                if (!WorkflowRuntimePolicy.IsResumeSkippableSideEffect(step.Kind))
                    throw new InvalidOperationException($"Resume cannot skip non-replayable step '{step.Id}'.");
                results.Add(NewStepResult(step, WorkflowStepStatus.Skipped, "Resume: completed side effect not repeated.", 0, stepStartedUtc));
                continue;
            }

            if (context.DryRun && (WorkflowRuntimePolicy.IsSideEffecting(step.Kind) || WorkflowRuntimePolicy.IsInteractive(step.Kind) || step.Kind == WorkflowStepKind.Delay))
            {
                results.Add(NewStepResult(step, WorkflowStepStatus.WouldRun, "Dry-run: action suppressed.", 0, stepStartedUtc));
                continue;
            }

            Exception? failure = null;
            var attempts = 0;
            for (var attempt = 1; attempt <= step.MaxAttempts; attempt++)
            {
                attempts = attempt;
                cancellationToken.ThrowIfCancellationRequested();
                using var timeout = step.TimeoutMilliseconds > 0
                    ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                    : null;
                if (timeout is not null) timeout.CancelAfter(step.TimeoutMilliseconds);
                var stepToken = timeout?.Token ?? cancellationToken;
                try
                {
                    await ExecuteStepAsync(step, context, values, stepToken);
                    failure = null;
                    break;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout?.IsCancellationRequested == true)
                {
                    failure = new TimeoutException($"Step {step.Id} timed out after {step.TimeoutMilliseconds} ms.");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
                {
                    failure = ex;
                }

                if (failure is not null && attempt < step.MaxAttempts && step.RetryDelayMilliseconds > 0)
                    await Task.Delay(step.RetryDelayMilliseconds, cancellationToken);
            }

            if (failure is null)
            {
                results.Add(NewStepResult(step, WorkflowStepStatus.Succeeded, null, attempts, stepStartedUtc));
                continue;
            }

            results.Add(NewStepResult(step, WorkflowStepStatus.Failed, failure.Message, attempts, stepStartedUtc));
            if (step.Required)
                return new WorkflowExecutionResult(false, results, values, executionStartedUtc, DateTimeOffset.UtcNow, context.DryRun);
        }
        return new WorkflowExecutionResult(true, results, values, executionStartedUtc, DateTimeOffset.UtcNow, context.DryRun);
    }

    private static WorkflowStepResult NewStepResult(WorkflowStep step, WorkflowStepStatus status, string? message, int attempts, DateTimeOffset startedUtc) =>
        new(step.Id, step.Kind, status, message, step.OutputKey, attempts, startedUtc, DateTimeOffset.UtcNow);

    internal async Task<IReadOnlyDictionary<string, string>> ResolveParameterValuesAsync(
        CaptureWorkflow workflow,
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var resolution = WorkflowParameterResolver.ResolveKnown(workflow.Parameters, workflow.Variables, context.InitialVariables);
        if (resolution.Errors.Count > 0) throw new InvalidOperationException(string.Join(" ", resolution.Errors));
        var resolved = new Dictionary<string, string>(resolution.Values, StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in resolution.Missing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.DryRun)
                throw new InvalidOperationException($"Dry-run requires a supplied/default value for parameter '{parameter.Name}'.");

            string? value = parameter.Kind switch
            {
                WorkflowParameterKind.Text when context.PromptTextAsync is not null =>
                    await context.PromptTextAsync(parameter.Prompt, parameter.DefaultValue, cancellationToken),
                WorkflowParameterKind.Choice when context.PromptChoiceAsync is not null =>
                    await context.PromptChoiceAsync(parameter.Prompt, parameter.Choices ?? [], parameter.DefaultValue, cancellationToken),
                WorkflowParameterKind.Boolean when context.ConfirmStepAsync is not null =>
                    (await context.ConfirmStepAsync(parameter.Prompt, cancellationToken))?.ToString().ToLowerInvariant(),
                _ => throw new InvalidOperationException($"Workflow parameter '{parameter.Name}' requires interactive input that this host does not provide.")
            };

            if (value is null) throw new InvalidOperationException($"Workflow parameter '{parameter.Name}' was canceled.");
            var error = WorkflowParameterResolver.ValidateResolvedValue(parameter, value);
            if (error is not null) throw new InvalidOperationException(error);
            resolved[parameter.Name] = value;
        }

        return resolved;
    }

    private async Task ExecuteStepAsync(WorkflowStep step, WorkflowExecutionContext context, Dictionary<string, object?> values, CancellationToken cancellationToken)
    {
        switch (step.Kind)
        {
            case WorkflowStepKind.CopyImage:
                await _clipboard.CopyImageAsync(CurrentImage(context, values));
                break;
            case WorkflowStepKind.CopyText:
                _clipboard.CopyText(CurrentText(values, step.Argument));
                break;
            case WorkflowStepKind.SaveImage:
                if (context.SaveImageAsync is null) throw new InvalidOperationException("This workflow host cannot save interactively.");
                await context.SaveImageAsync(context.Asset.WithPng(CurrentImage(context, values)), cancellationToken);
                break;
            case WorkflowStepKind.PinImage:
                if (context.PinImage is null) throw new InvalidOperationException("This workflow host cannot pin images.");
                context.PinImage(context.Asset.WithPng(CurrentImage(context, values)));
                break;
            case WorkflowStepKind.OpenEditor:
                if (context.OpenEditor is null) throw new InvalidOperationException("This workflow host cannot open the editor.");
                context.OpenEditor(context.Asset.WithPng(CurrentImage(context, values)));
                break;
            case WorkflowStepKind.RunOcr:
            {
                var ocr = await _ocr.RecognizeAsync(CurrentImage(context, values), _settings().PreferredOcrLanguage, cancellationToken);
                values[step.OutputKey ?? "ocr"] = ocr;
                values["text"] = ocr.Text;
                break;
            }
            case WorkflowStepKind.ExtractTable:
            {
                var ocr = await EnsureOcrAsync(context, values, cancellationToken);
                var table = TableExtractor.TryExtract(ocr) ?? throw new InvalidOperationException("No table was detected in this capture.");
                values[step.OutputKey ?? "table"] = table;
                values["text"] = TableSerializers.ToCsv(table);
                break;
            }
            case WorkflowStepKind.ScanBarcode:
            {
                var hits = _barcodes.Decode(CurrentImage(context, values));
                values[step.OutputKey ?? "barcodes"] = hits;
                values["text"] = string.Join(Environment.NewLine, hits.Select(h => h.Text));
                break;
            }
            case WorkflowStepKind.ExtractSignals:
            {
                var ocr = await EnsureOcrAsync(context, values, cancellationToken);
                var signals = TextSignalExtractor.Extract(ocr);
                values[step.OutputKey ?? "signals"] = signals;
                break;
            }
            case WorkflowStepKind.BeautifyImage:
                SetCurrentImage(values, _images.Beautify(CurrentImage(context, values), new Magic.Capture.Core.Utilities.BeautifyOptions()));
                break;
            case WorkflowStepKind.StripMetadata:
                SetCurrentImage(values, _images.StripMetadata(CurrentImage(context, values)));
                break;
            case WorkflowStepKind.ComputeHashes:
                values[step.OutputKey ?? "metadata"] = _metadata.Inspect(CurrentImage(context, values));
                break;
            case WorkflowStepKind.RunMagicAction:
            {
                if (!_entitlements.CanUse(ProductFeature.MagicActions)) throw new InvalidOperationException("Magic Actions require Pro Lifetime.");
                var actionId = step.Argument ?? throw new InvalidOperationException("Magic Action id is required.");
                var action = BuiltInMagicActions.All.FirstOrDefault(a => string.Equals(a.Id, actionId, StringComparison.Ordinal))
                    ?? (await _actionStore.LoadAsync(cancellationToken)).FirstOrDefault(a => string.Equals(a.Id, actionId, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException($"Magic Action '{actionId}' was not found.");
                var currentAsset = context.Asset.WithPng(CurrentImage(context, values));
                var magicRequest = new MagicActionExecutionRequest(currentAsset, action, null, context.AiContext ?? []);
                var preview = await _magic.PreviewAsync(magicRequest, cancellationToken);
                if (!preview.ProviderIsLocal)
                {
                    if (context.ConfirmCloudMagicActionAsync is null)
                        throw new InvalidOperationException("Cloud AI inside a workflow requires explicit user confirmation. Open the workflow interactively or choose a local AI provider.");
                    if (!await context.ConfirmCloudMagicActionAsync(magicRequest, preview, cancellationToken))
                        throw new InvalidOperationException("Cloud AI workflow step was canceled.");
                }
                var execution = await _magic.ExecuteAsync(magicRequest, cancellationToken);
                values[step.OutputKey ?? "magic"] = execution;
                values["text"] = execution.Result.Markdown;
                break;
            }
            case WorkflowStepKind.ExportText:
                values["text"] = CurrentText(values, step.Argument);
                break;
            case WorkflowStepKind.CustomHttpDestination:
            {
                if (!_entitlements.CanUse(ProductFeature.CustomDestinations)) throw new InvalidOperationException("Custom destinations require Pro Lifetime.");
                var id = step.Argument ?? throw new InvalidOperationException("Destination id is required.");
                var profile = (await _destinations.LoadAsync(cancellationToken)).FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException($"Destination '{id}' was not found.");
                var currentAsset = context.Asset.WithPng(CurrentImage(context, values));
                var textValues = BuildDestinationValues(currentAsset, values);
                var response = await _destinationClient.SendAsync(profile, new DestinationRequestContext(currentAsset, textValues["filename"], textValues), cancellationToken);
                values[step.OutputKey ?? "destination"] = response;
                if (!string.IsNullOrWhiteSpace(response.ResultUrl)) values["text"] = response.ResultUrl;
                break;
            }
            case WorkflowStepKind.RunLocalAction:
            {
                if (!_entitlements.CanUse(ProductFeature.AdvancedWorkflows))
                    throw new InvalidOperationException("Local Actions require Plus trial or Pro Lifetime.");
                var id = step.Argument ?? throw new InvalidOperationException("Local Action id is required.");
                var profile = (await _localActions.LoadAsync(cancellationToken)).FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException($"Local Action '{id}' was not found.");
                if (profile.Arguments.Any(argument => LocalActionTemplate.References(argument, "ocrText")) && !values.ContainsKey("text"))
                    await EnsureOcrAsync(context, values, cancellationToken);

                var currentAsset = context.Asset.WithPng(CurrentImage(context, values));
                var execution = await _localActionRunner.ExecuteAsync(
                    profile,
                    new LocalActionExecutionContext(currentAsset, values, context.ConfirmLocalActionApprovalAsync),
                    cancellationToken);
                values[step.OutputKey ?? "localAction"] = execution;
                values["stdout"] = execution.Stdout;
                values["stderr"] = execution.Stderr;
                if (execution.OutputBytes is not null) values["output"] = execution.OutputBytes;
                if (execution.OutputText is not null)
                {
                    values["output"] = execution.OutputText;
                    values["text"] = execution.OutputText;
                }
                if (execution.OutputBytes is not null && PngDimensions.TryRead(execution.OutputBytes, out _, out _))
                    SetCurrentImage(values, execution.OutputBytes);
                break;
            }
            case WorkflowStepKind.PromptText:
            {
                if (context.PromptTextAsync is null) throw new InvalidOperationException("This workflow host cannot prompt for text.");
                var defaultValue = Option(step.Options, "default");
                var value = await context.PromptTextAsync(step.Argument ?? "Input", defaultValue, cancellationToken);
                if (value is null) throw new InvalidOperationException("Text input was canceled.");
                if (value.Length > WorkflowRuntimePolicy.MaximumParameterValueLength) throw new InvalidDataException("Workflow text input is too long.");
                values[step.OutputKey ?? throw new InvalidOperationException("PromptText output key is required.")] = value;
                break;
            }
            case WorkflowStepKind.PromptChoice:
            {
                if (context.PromptChoiceAsync is null) throw new InvalidOperationException("This workflow host cannot prompt for a choice.");
                var choices = WorkflowRuntimePolicy.ParseChoices(step.Options);
                var value = await context.PromptChoiceAsync(step.Argument ?? "Choose", choices, Option(step.Options, "default"), cancellationToken);
                if (value is null) throw new InvalidOperationException("Choice input was canceled.");
                if (!choices.Contains(value, StringComparer.Ordinal)) throw new InvalidDataException("Workflow choice is not one of the declared values.");
                values[step.OutputKey ?? throw new InvalidOperationException("PromptChoice output key is required.")] = value;
                break;
            }
            case WorkflowStepKind.Confirm:
            {
                if (context.ConfirmStepAsync is null) throw new InvalidOperationException("This workflow host cannot show workflow confirmation.");
                var confirmed = await context.ConfirmStepAsync(step.Argument ?? "Continue?", cancellationToken);
                if (confirmed is null) throw new InvalidOperationException("Confirmation was canceled.");
                values[step.OutputKey ?? "confirmed"] = confirmed.Value;
                if (!confirmed.Value && step.Required) throw new InvalidOperationException("Confirmation was declined.");
                break;
            }
            case WorkflowStepKind.Delay:
                await Task.Delay(WorkflowRuntimePolicy.ParseDelayMilliseconds(step.Argument), cancellationToken);
                break;
            case WorkflowStepKind.ForEachImage:
            {
                if (context.ResolveWorkflowAsync is null) throw new InvalidOperationException("This workflow host cannot resolve image-loop child workflows.");
                var workflowId = step.Argument ?? throw new InvalidOperationException("ForEachImage child workflow id is required.");
                var child = await context.ResolveWorkflowAsync(workflowId, cancellationToken)
                    ?? throw new InvalidOperationException($"Image-loop child workflow '{workflowId}' was not found.");
                var callStack = context.WorkflowCallStack ?? [];
                if (!WorkflowRuntimePolicy.CanEnterSubworkflow(callStack, child.Id))
                    throw new InvalidOperationException($"Image-loop child workflow '{child.Id}' would create a cycle or exceed the nesting limit.");

                var assets = context.LoopAssets is { Count: > 0 } supplied ? supplied : new[] { context.Asset };
                if (assets.Count > WorkflowRuntimePolicy.MaximumLoopImages)
                    throw new InvalidDataException($"ForEachImage cannot exceed {WorkflowRuntimePolicy.MaximumLoopImages} images.");
                var continueOnError = WorkflowRuntimePolicy.ParseLoopContinueOnError(step.Options);
                var succeeded = 0;
                var failed = 0;
                for (var index = 0; index < assets.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var loopAsset = assets[index];
                    var initial = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (name, value) in StringValues(values)) initial[name] = value;
                    initial["loop.index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    initial["loop.number"] = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    initial["loop.count"] = assets.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var childContext = context with
                    {
                        Asset = loopAsset,
                        InitialVariables = initial,
                        WorkflowCallStack = [.. callStack, child.Id],
                        LoopAssets = new[] { loopAsset },
                        IsResume = false,
                        ResumeCompletedSideEffectStepIds = null
                    };
                    WorkflowExecutionResult? childResult = null;
                    Exception? loopChildFailure = null;
                    try
                    {
                        childResult = await ExecuteAsync(child, childContext, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
                    {
                        loopChildFailure = ex;
                    }

                    if (childResult?.Succeeded == true)
                    {
                        succeeded++;
                        continue;
                    }

                    failed++;
                    if (!continueOnError)
                    {
                        var failure = childResult?.Steps.LastOrDefault(result => !result.Succeeded);
                        var message = failure?.Message ?? loopChildFailure?.Message ?? "unknown failure";
                        throw new InvalidOperationException($"Image-loop child workflow '{child.Name}' failed at image {index + 1}: {message}", loopChildFailure);
                    }
                }

                if (!string.IsNullOrWhiteSpace(step.OutputKey))
                    values[step.OutputKey] = new WorkflowLoopSummary(assets.Count, succeeded, failed);
                break;
            }
            case WorkflowStepKind.RunWorkflow:
            {
                if (context.ResolveWorkflowAsync is null) throw new InvalidOperationException("This workflow host cannot resolve subworkflows.");
                var workflowId = step.Argument ?? throw new InvalidOperationException("Subworkflow id is required.");
                var child = await context.ResolveWorkflowAsync(workflowId, cancellationToken)
                    ?? throw new InvalidOperationException($"Subworkflow '{workflowId}' was not found.");
                var callStack = context.WorkflowCallStack ?? [];
                if (!WorkflowRuntimePolicy.CanEnterSubworkflow(callStack, child.Id))
                    throw new InvalidOperationException($"Subworkflow '{child.Id}' would create a cycle or exceed the nesting limit.");

                var childContext = context with
                {
                    Asset = context.Asset.WithPng(CurrentImage(context, values)),
                    InitialVariables = StringValues(values),
                    WorkflowCallStack = [.. callStack, child.Id]
                };
                var childResult = await ExecuteAsync(child, childContext, cancellationToken);
                if (!childResult.Succeeded)
                {
                    var failure = childResult.Steps.LastOrDefault(result => !result.Succeeded);
                    throw new InvalidOperationException($"Subworkflow '{child.Name}' failed: {failure?.Message ?? "unknown failure"}");
                }
                if (childResult.Values.TryGetValue("image", out var image) && image is byte[] bytes) SetCurrentImage(values, bytes);
                if (childResult.Values.TryGetValue("text", out var text) && text is not null) values["text"] = text;
                if (!string.IsNullOrWhiteSpace(step.OutputKey)) values[step.OutputKey] = childResult;
                break;
            }
            default:
                throw new InvalidOperationException($"Unsupported workflow step: {step.Kind}.");
        }
    }

    private static void ApplyInitialVariables(CaptureWorkflow workflow, WorkflowExecutionContext context, Dictionary<string, object?> values)
    {
        if (workflow.Variables is not null)
        {
            foreach (var (key, value) in workflow.Variables) values[key] = value;
        }

        if (context.InitialVariables is null) return;
        var errors = WorkflowVariables.Validate(context.InitialVariables, "Runtime");
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        foreach (var (key, value) in context.InitialVariables) values[key] = value;
    }

    private async Task<Magic.Capture.Core.Ocr.OcrDocument> EnsureOcrAsync(WorkflowExecutionContext context, Dictionary<string, object?> values, CancellationToken cancellationToken)
    {
        if (values.TryGetValue("ocr", out var existing) && existing is Magic.Capture.Core.Ocr.OcrDocument ocr) return ocr;
        ocr = await _ocr.RecognizeAsync(CurrentImage(context, values), _settings().PreferredOcrLanguage, cancellationToken);
        values["ocr"] = ocr;
        values["text"] = ocr.Text;
        return ocr;
    }

    private static byte[] CurrentImage(WorkflowExecutionContext context, IReadOnlyDictionary<string, object?> values) =>
        values.TryGetValue("image", out var value) && value is byte[] bytes ? bytes : context.Asset.PngBytes;

    private static void SetCurrentImage(Dictionary<string, object?> values, byte[] pngBytes)
    {
        values["image"] = pngBytes;
        if (PngDimensions.TryRead(pngBytes, out var width, out var height))
        {
            values["width"] = width;
            values["height"] = height;
            values["size"] = pngBytes.LongLength;
        }
    }

    private static string CurrentText(IReadOnlyDictionary<string, object?> values, string? format)
    {
        if (values.TryGetValue("table", out var tableValue) && tableValue is DetectedTable table)
        {
            return (format ?? string.Empty).ToLowerInvariant() switch
            {
                "tsv" => TableSerializers.ToTsv(table),
                "markdown" or "md" => TableSerializers.ToMarkdown(table),
                "html" => TableSerializers.ToHtml(table),
                "json" => TableSerializers.ToJson(table),
                _ => TableSerializers.ToCsv(table)
            };
        }
        return values.TryGetValue("text", out var text) ? text?.ToString() ?? string.Empty : string.Empty;
    }

    private static IReadOnlyDictionary<string, string> BuildDestinationValues(CaptureAsset asset, IReadOnlyDictionary<string, object?> values)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["filename"] = values.TryGetValue("filename", out var fileName) ? fileName?.ToString() ?? "capture.png" : "capture.png",
            ["width"] = asset.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["height"] = asset.Height.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["source"] = asset.SourceDisplayName ?? asset.SourceKind.ToString(),
            ["captureId"] = asset.Id.ToString("N"),
            ["workflow"] = values.TryGetValue("workflow", out var workflow) ? workflow?.ToString() ?? string.Empty : string.Empty,
            ["utc"] = asset.CreatedUtc.ToString("O")
        };
        if (values.TryGetValue("text", out var text) && text is not null) map["text"] = text.ToString() ?? string.Empty;
        return map;
    }

    private static IReadOnlyDictionary<string, string> StringValues(IReadOnlyDictionary<string, object?> values)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            if (!WorkflowVariables.IsValidName(key) || WorkflowVariables.IsReserved(key) || value is null) continue;
            if (value is string text && text.Length <= WorkflowVariables.MaximumValueLength) result[key] = text;
            else if (value is bool boolean) result[key] = boolean ? "true" : "false";
            else if (value is int or long or double or float or decimal) result[key] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }
        return result;
    }

    private static string? Option(IReadOnlyDictionary<string, string>? options, string key) =>
        options is not null && options.TryGetValue(key, out var value) ? value : null;

    private static bool TierAllows(ProductTier actual, ProductTier required) => (int)actual >= (int)required;
}
