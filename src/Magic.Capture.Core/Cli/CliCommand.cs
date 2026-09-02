namespace Magic.Capture.Core.Cli;

public abstract record CliCommand;
public enum CaptureCommandKind { Region, Monitor, Desktop }
public enum OpenPage { History, Settings, Plan, Ai, Workflows, Utilities }
public sealed record CaptureCliCommand(CaptureCommandKind Kind) : CliCommand;
public sealed record WorkflowCliCommand(string Name, IReadOnlyDictionary<string, string> Variables) : CliCommand;
public sealed record TriggerCliCommand(string Id) : CliCommand;
public sealed record OpenCliCommand(OpenPage Page) : CliCommand;
public sealed record CliParseResult(bool IsValid, CliCommand? Command, string? Error = null);
