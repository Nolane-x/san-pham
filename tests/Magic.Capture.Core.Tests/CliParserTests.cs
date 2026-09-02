using Magic.Capture.Core.Cli;

namespace Magic.Capture.Core.Tests;

public sealed class CliParserTests
{
    [Theory]
    [InlineData("region", CaptureCommandKind.Region)]
    [InlineData("monitor", CaptureCommandKind.Monitor)]
    [InlineData("desktop", CaptureCommandKind.Desktop)]
    public void Parses_capture_commands(string mode, CaptureCommandKind expected)
    {
        var result = CliParser.Parse(["--capture", mode]);
        Assert.True(result.IsValid);
        var command = Assert.IsType<CaptureCliCommand>(result.Command);
        Assert.Equal(expected, command.Kind);
    }

    [Fact]
    public void Parses_workflow_and_open_commands()
    {
        var workflow = CliParser.Parse(["--workflow", "Documentation"]);
        Assert.True(workflow.IsValid);
        var workflowCommand = Assert.IsType<WorkflowCliCommand>(workflow.Command);
        Assert.Equal("Documentation", workflowCommand.Name);
        Assert.Empty(workflowCommand.Variables);

        var open = CliParser.Parse(["--open", "history"]);
        Assert.True(open.IsValid);
        Assert.Equal(OpenPage.History, Assert.IsType<OpenCliCommand>(open.Command).Page);
    }


    [Fact]
    public void Parses_workflow_variables_and_preserves_value_text()
    {
        var result = CliParser.Parse(["--workflow", "Local pipeline", "--var", "ticket=MC-42", "--var", "label=value with spaces & symbols"]);
        Assert.True(result.IsValid, result.Error);
        var command = Assert.IsType<WorkflowCliCommand>(result.Command);
        Assert.Equal("MC-42", command.Variables["ticket"]);
        Assert.Equal("value with spaces & symbols", command.Variables["label"]);
    }

    [Theory]
    [InlineData("width=123")]
    [InlineData("missingEquals")]
    [InlineData("1bad=value")]
    public void Rejects_unsafe_or_malformed_workflow_variables(string assignment)
    {
        var result = CliParser.Parse(["--workflow", "Documentation", "--var", assignment]);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Rejects_unknown_or_incomplete_commands()
    {
        Assert.False(CliParser.Parse(["--shell", "cmd.exe"]).IsValid);
        Assert.False(CliParser.Parse(["--workflow"]).IsValid);
    }
    [Fact]
    public void Parses_safe_trigger_id_and_rejects_unsafe_trigger_id()
    {
        var valid = CliParser.Parse(["--trigger", "morning_capture-1"]);
        Assert.True(valid.IsValid, valid.Error);
        Assert.Equal("morning_capture-1", Assert.IsType<TriggerCliCommand>(valid.Command).Id);

        Assert.False(CliParser.Parse(["--trigger", "bad trigger"]).IsValid);
        Assert.False(CliParser.Parse(["--trigger", @"bad\path"]).IsValid);
    }

}
