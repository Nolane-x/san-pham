using Magic.Capture.Core.Workflows;

namespace Magic.Capture.Core.Cli;

public static class CliParser
{
    public static CliParseResult Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return new(true, null);
        if (args.Count < 2) return new(false, null, "Expected a command value.");

        var command = args[0].Trim().ToLowerInvariant();
        if (command == "--workflow") return ParseWorkflow(args);
        if (command == "--trigger") return ParseTrigger(args);
        if (args.Count != 2) return new(false, null, "This command accepts exactly one value.");

        var value = args[1].Trim();
        if (string.IsNullOrWhiteSpace(value)) return new(false, null, "Command value is required.");

        return command switch
        {
            "--capture" => ParseCapture(value),
            "--open" => ParseOpen(value),
            _ => new(false, null, $"Unknown command: {args[0]}")
        };
    }

    private static CliParseResult ParseWorkflow(IReadOnlyList<string> args)
    {
        var name = args[1].Trim();
        if (string.IsNullOrWhiteSpace(name)) return new(false, null, "Workflow name or id is required.");
        if (name.Length > 120) return new(false, null, "Workflow name or id is too long.");

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 2; index < args.Count; index += 2)
        {
            if (index + 1 >= args.Count || !string.Equals(args[index], "--var", StringComparison.OrdinalIgnoreCase))
                return new(false, null, "Workflow variables must use repeated --var key=value pairs.");

            var assignment = args[index + 1];
            var separator = assignment.IndexOf('=');
            if (separator <= 0)
                return new(false, null, "Workflow variable must use key=value syntax.");
            var key = assignment[..separator].Trim();
            var value = assignment[(separator + 1)..];
            if (!WorkflowVariables.IsValidName(key)) return new(false, null, $"Invalid workflow variable name: {key}");
            if (WorkflowVariables.IsReserved(key)) return new(false, null, $"Workflow variable '{key}' is reserved by the runtime.");
            if (value.Length > WorkflowVariables.MaximumValueLength) return new(false, null, $"Workflow variable '{key}' is too long.");
            if (!variables.TryAdd(key, value)) return new(false, null, $"Workflow variable '{key}' was specified more than once.");
            if (variables.Count > WorkflowVariables.MaximumVariables) return new(false, null, $"Workflow cannot receive more than {WorkflowVariables.MaximumVariables} CLI variables.");
        }
        return new(true, new WorkflowCliCommand(name, variables));
    }

    private static CliParseResult ParseTrigger(IReadOnlyList<string> args)
    {
        if (args.Count != 2) return new(false, null, "Trigger command accepts exactly one id.");
        var id = args[1].Trim();
        if (!WorkflowTriggerPolicy.IsSafeIdentifier(id))
            return new(false, null, "Trigger id is invalid.");
        return new(true, new TriggerCliCommand(id));
    }

    private static CliParseResult ParseCapture(string value) => value.ToLowerInvariant() switch
    {
        "region" => new(true, new CaptureCliCommand(CaptureCommandKind.Region)),
        "monitor" => new(true, new CaptureCliCommand(CaptureCommandKind.Monitor)),
        "desktop" => new(true, new CaptureCliCommand(CaptureCommandKind.Desktop)),
        _ => new(false, null, $"Unknown capture mode: {value}")
    };

    private static CliParseResult ParseOpen(string value)
    {
        if (!Enum.TryParse<OpenPage>(value, ignoreCase: true, out var page))
            return new(false, null, $"Unknown page: {value}");
        return new(true, new OpenCliCommand(page));
    }
}
