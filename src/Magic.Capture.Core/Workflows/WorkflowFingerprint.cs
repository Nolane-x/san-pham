using System.Security.Cryptography;
using System.Text;

namespace Magic.Capture.Core.Workflows;

public static class WorkflowFingerprint
{
    public static string Compute(CaptureWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var builder = new StringBuilder(4096);
        Append(builder, workflow.Id);
        Append(builder, workflow.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(builder, workflow.RequiredTier.ToString());
        AppendMap(builder, workflow.Variables);

        foreach (var parameter in workflow.Parameters ?? [])
        {
            Append(builder, parameter.Name);
            Append(builder, parameter.Prompt);
            Append(builder, parameter.Kind.ToString());
            Append(builder, parameter.Required ? "1" : "0");
            Append(builder, parameter.DefaultValue);
            foreach (var choice in parameter.Choices ?? []) Append(builder, choice);
            builder.Append('|');
        }

        foreach (var step in workflow.Steps)
        {
            Append(builder, step.Id);
            Append(builder, step.Kind.ToString());
            Append(builder, step.Required ? "1" : "0");
            Append(builder, step.Argument);
            Append(builder, step.OutputKey);
            Append(builder, step.Condition);
            Append(builder, step.MaxAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(builder, step.RetryDelayMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(builder, step.TimeoutMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(builder, step.IsEnabled?.ToString());
            AppendMap(builder, step.Options);
            builder.Append('#');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void AppendMap(StringBuilder builder, IReadOnlyDictionary<string, string>? values)
    {
        if (values is null) { builder.Append("{}|"); return; }
        foreach (var pair in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Append(builder, pair.Key);
            Append(builder, pair.Value);
        }
        builder.Append("{}|");
    }

    private static void Append(StringBuilder builder, string? value)
    {
        value ??= string.Empty;
        builder.Append(value.Length).Append(':').Append(value).Append('|');
    }
}
