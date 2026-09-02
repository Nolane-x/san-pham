namespace Magic.Capture.Core.Workflows;

public sealed record WorkflowParameterResolution(
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<WorkflowParameterDefinition> Missing,
    IReadOnlyList<string> Errors);

public static class WorkflowParameterResolver
{
    public static WorkflowParameterResolution ResolveKnown(
        IReadOnlyList<WorkflowParameterDefinition>? parameters,
        IReadOnlyDictionary<string, string>? workflowVariables,
        IReadOnlyDictionary<string, string>? runtimeValues)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<WorkflowParameterDefinition>();
        var errors = new List<string>();
        if (parameters is null || parameters.Count == 0)
            return new WorkflowParameterResolution(values, missing, errors);

        foreach (var parameter in parameters)
        {
            string? value = null;
            if (runtimeValues is not null && runtimeValues.TryGetValue(parameter.Name, out var runtime))
                value = runtime;
            else if (workflowVariables is not null && workflowVariables.TryGetValue(parameter.Name, out var variable))
                value = variable;
            else if (parameter.DefaultValue is not null)
                value = parameter.DefaultValue;

            if (value is null)
            {
                if (parameter.Required) missing.Add(parameter);
                continue;
            }

            var error = ValidateResolvedValue(parameter, value);
            if (error is not null)
                errors.Add(error);
            else
                values[parameter.Name] = value;
        }

        return new WorkflowParameterResolution(values, missing, errors);
    }

    public static string? ValidateResolvedValue(WorkflowParameterDefinition parameter, string value)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        value ??= string.Empty;
        if (value.Length > WorkflowRuntimePolicy.MaximumParameterValueLength)
            return $"Workflow parameter '{parameter.Name}' exceeds {WorkflowRuntimePolicy.MaximumParameterValueLength:N0} characters.";

        return parameter.Kind switch
        {
            WorkflowParameterKind.Text => null,
            WorkflowParameterKind.Boolean when bool.TryParse(value, out _) => null,
            WorkflowParameterKind.Boolean => $"Workflow parameter '{parameter.Name}' must be true or false.",
            WorkflowParameterKind.Choice when parameter.Choices is not null && parameter.Choices.Contains(value, StringComparer.Ordinal) => null,
            WorkflowParameterKind.Choice => $"Workflow parameter '{parameter.Name}' must match one of its declared choices.",
            _ => $"Workflow parameter '{parameter.Name}' has an unsupported kind."
        };
    }
}
