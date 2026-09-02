namespace Magic.Capture.Core.Workflows;

public static class WorkflowValidator
{
    public static WorkflowValidationResult Validate(CaptureWorkflow? workflow)
    {
        var errors = new List<string>();
        if (workflow is null)
            return new WorkflowValidationResult(false, ["Workflow is required."]);

        if (string.IsNullOrWhiteSpace(workflow.Id) || workflow.Id.Length > 96) errors.Add("Workflow id is invalid.");
        if (string.IsNullOrWhiteSpace(workflow.Name) || workflow.Name.Length > 120) errors.Add("Workflow name is invalid.");
        if ((workflow.Description ?? string.Empty).Length > 2_000) errors.Add("Workflow description is too long.");
        if (workflow.SchemaVersion is < 1 or > 5) errors.Add("Unsupported workflow schema version.");
        if (workflow.SchemaVersion < 4 && workflow.Parameters is { Count: > 0 })
            errors.Add("Workflow parameters require schema version 4.");
        ValidateParameters(workflow.Parameters, errors);
        errors.AddRange(WorkflowVariables.Validate(workflow.Variables, "Workflow"));
        if (workflow.Steps is null)
            return new WorkflowValidationResult(false, [.. errors, "Workflow steps are required."]);
        if (workflow.Steps.Count == 0) errors.Add("Workflow must contain at least one step.");
        if (workflow.Steps.Count > 64) errors.Add("Workflow cannot contain more than 64 steps.");
        if (workflow.SchemaVersion < 4 && workflow.Steps.Any(step => step is not null && WorkflowRuntimePolicy.RequiresSchemaV4(step.Kind)))
            errors.Add("Interactive, delay, and subworkflow steps require schema version 4.");
        if (workflow.SchemaVersion < 5 && workflow.Steps.Any(step => step is not null && WorkflowRuntimePolicy.RequiresSchemaV5(step.Kind)))
            errors.Add("Image-loop steps require schema version 5.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in workflow.Steps)
        {
            if (step is null)
            {
                errors.Add("Workflow cannot contain null steps.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(step.Id) || step.Id.Length > 96)
            {
                errors.Add("Every workflow step needs a valid id.");
                continue;
            }

            if (!ids.Add(step.Id)) errors.Add($"Duplicate workflow step id: {step.Id}.");
            if (!Enum.IsDefined(step.Kind)) errors.Add($"Unknown workflow step kind: {step.Kind}.");

            if (step.Kind is WorkflowStepKind.RunMagicAction or WorkflowStepKind.CustomHttpDestination or WorkflowStepKind.RunLocalAction && string.IsNullOrWhiteSpace(step.Argument))
                errors.Add($"Step {step.Id} requires an argument.");
            if (step.Argument is { Length: > 4_096 }) errors.Add($"Step {step.Id} argument is too long.");
            if (step.OutputKey is { Length: > 128 }) errors.Add($"Step {step.Id} output key is too long.");
            if (step.MaxAttempts is < 1 or > 5)
                errors.Add($"Step {step.Id} max attempts must be between 1 and 5.");
            if (step.RetryDelayMilliseconds is < 0 or > 60_000)
                errors.Add($"Step {step.Id} retry delay must be between 0 and 60000 ms.");
            if (step.TimeoutMilliseconds is < 0 or > 600_000)
                errors.Add($"Step {step.Id} timeout must be between 0 and 600000 ms.");
            if (!string.IsNullOrWhiteSpace(step.Condition) && step.Condition.Length > 512)
                errors.Add($"Step {step.Id} condition cannot exceed 512 characters.");

            ValidateRuntimeStep(step, errors);

            if (step.Options is not null)
            {
                if (step.Options.Count > 32) errors.Add($"Step {step.Id} cannot contain more than 32 options.");
                foreach (var (key, value) in step.Options.Take(33))
                {
                    if (string.IsNullOrWhiteSpace(key) || key.Length > 128) errors.Add($"Step {step.Id} has an invalid option key.");
                    if ((value ?? string.Empty).Length > 4_096) errors.Add($"Step {step.Id} option '{key}' is too long.");
                }
            }
        }

        return new WorkflowValidationResult(errors.Count == 0, errors);
    }

    private static void ValidateParameters(IReadOnlyList<WorkflowParameterDefinition>? parameters, List<string> errors)
    {
        if (parameters is null) return;
        if (parameters.Count > WorkflowRuntimePolicy.MaximumParameters)
            errors.Add($"Workflow cannot contain more than {WorkflowRuntimePolicy.MaximumParameters} parameters.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in parameters.Take(WorkflowRuntimePolicy.MaximumParameters + 1))
        {
            if (parameter is null)
            {
                errors.Add("Workflow cannot contain null parameters.");
                continue;
            }
            if (!WorkflowVariables.IsValidName(parameter.Name) || WorkflowVariables.IsReserved(parameter.Name))
                errors.Add("Workflow contains an invalid or reserved parameter name.");
            else if (!names.Add(parameter.Name))
                errors.Add($"Duplicate workflow parameter: {parameter.Name}.");
            if (string.IsNullOrWhiteSpace(parameter.Prompt) || parameter.Prompt.Length > WorkflowRuntimePolicy.MaximumParameterPromptLength)
                errors.Add($"Workflow parameter '{parameter.Name}' needs a prompt up to {WorkflowRuntimePolicy.MaximumParameterPromptLength} characters.");
            if (!Enum.IsDefined(parameter.Kind))
                errors.Add($"Workflow parameter '{parameter.Name}' has an unknown kind.");

            var choices = parameter.Choices;
            if (parameter.Kind == WorkflowParameterKind.Choice)
            {
                if (choices is null || choices.Count is < 2 or > WorkflowRuntimePolicy.MaximumParameterChoices)
                    errors.Add($"Choice parameter '{parameter.Name}' must contain 2–{WorkflowRuntimePolicy.MaximumParameterChoices} choices.");
                else
                {
                    var unique = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var choice in choices)
                    {
                        if (string.IsNullOrWhiteSpace(choice) || choice.Length > WorkflowRuntimePolicy.MaximumChoiceLength)
                            errors.Add($"Choice parameter '{parameter.Name}' contains an invalid choice.");
                        else if (!unique.Add(choice))
                            errors.Add($"Choice parameter '{parameter.Name}' contains duplicate choices.");
                    }
                }
            }
            else if (choices is { Count: > 0 })
            {
                errors.Add($"Workflow parameter '{parameter.Name}' can declare choices only when its kind is Choice.");
            }

            if (parameter.DefaultValue is not null)
            {
                var error = WorkflowParameterResolver.ValidateResolvedValue(parameter, parameter.DefaultValue);
                if (error is not null) errors.Add(error);
            }
        }
    }

    private static void ValidateRuntimeStep(WorkflowStep step, List<string> errors)
    {
        switch (step.Kind)
        {
            case WorkflowStepKind.PromptText:
                if (string.IsNullOrWhiteSpace(step.OutputKey)) errors.Add($"Step {step.Id} PromptText requires an output key.");
                if (string.IsNullOrWhiteSpace(step.Argument) || step.Argument.Length > WorkflowRuntimePolicy.MaximumParameterPromptLength)
                    errors.Add($"Step {step.Id} PromptText requires a bounded prompt.");
                break;
            case WorkflowStepKind.PromptChoice:
                if (string.IsNullOrWhiteSpace(step.OutputKey)) errors.Add($"Step {step.Id} PromptChoice requires an output key.");
                if (string.IsNullOrWhiteSpace(step.Argument) || step.Argument.Length > WorkflowRuntimePolicy.MaximumParameterPromptLength)
                    errors.Add($"Step {step.Id} PromptChoice requires a bounded prompt.");
                var choices = WorkflowRuntimePolicy.ParseChoices(step.Options);
                if (choices.Count is < 2 or > WorkflowRuntimePolicy.MaximumParameterChoices || choices.Any(choice => choice.Length > WorkflowRuntimePolicy.MaximumChoiceLength))
                    errors.Add($"Step {step.Id} PromptChoice requires 2–{WorkflowRuntimePolicy.MaximumParameterChoices} bounded choices in option 'choices'.");
                break;
            case WorkflowStepKind.Confirm:
                if (string.IsNullOrWhiteSpace(step.Argument) || step.Argument.Length > WorkflowRuntimePolicy.MaximumParameterPromptLength)
                    errors.Add($"Step {step.Id} Confirm requires a bounded prompt.");
                break;
            case WorkflowStepKind.Delay:
                try { _ = WorkflowRuntimePolicy.ParseDelayMilliseconds(step.Argument); }
                catch (InvalidDataException ex) { errors.Add($"Step {step.Id}: {ex.Message}"); }
                break;
            case WorkflowStepKind.RunWorkflow:
                if (string.IsNullOrWhiteSpace(step.Argument) || step.Argument.Length > 96)
                    errors.Add($"Step {step.Id} RunWorkflow requires a valid workflow id.");
                break;
            case WorkflowStepKind.ForEachImage:
                if (string.IsNullOrWhiteSpace(step.Argument) || step.Argument.Length > 96)
                    errors.Add($"Step {step.Id} ForEachImage requires a child workflow id.");
                if (step.MaxAttempts != 1) errors.Add($"Step {step.Id} ForEachImage cannot retry because earlier child images may already have side effects.");
                try { _ = WorkflowRuntimePolicy.ParseLoopContinueOnError(step.Options); }
                catch (InvalidDataException ex) { errors.Add($"Step {step.Id}: {ex.Message}"); }
                break;
        }
    }
}

