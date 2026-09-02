using Magic.Capture.Core.Commerce;
using Magic.Capture.Core.LocalActions;
using Magic.Capture.Core.Workflows;

namespace Magic.Capture.Core.Tests;

public sealed class LocalActionTests
{
    [Fact]
    public void Validator_accepts_bounded_direct_executable_profile()
    {
        var profile = new LocalActionProfile(
            "resize", "Resize locally", @"C:\\Tools\\resize.exe",
            ["--input", "$input", "--output", "$output", "--width", "$width"],
            LocalActionOutputMode.OutputFileImage, ".png");

        var result = LocalActionProfileValidator.Validate(profile);
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Theory]
    [InlineData(@"C:\\Tools\\script.bat")]
    [InlineData("resize.exe")]
    [InlineData(@"C:\\Tools\\script.ps1")]
    public void Validator_rejects_shell_or_relative_launch_targets(string executable)
    {
        var profile = new LocalActionProfile("unsafe", "Unsafe", executable, []);
        var result = LocalActionProfileValidator.Validate(profile);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Template_expands_local_action_and_custom_variables_without_shell_parsing()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["input"] = @"C:\\Temp\\input.png",
            ["output"] = @"C:\\Temp\\output.txt",
            ["width"] = "1920",
            ["label"] = "A value with spaces & symbols"
        };

        var expanded = LocalActionTemplate.Expand("$input|${output}|$width|$label|$unknown|$$literal", values);
        Assert.Equal(@"C:\\Temp\\input.png|C:\\Temp\\output.txt|1920|A value with spaces & symbols|$unknown|$literal", expanded);
    }

    [Fact]
    public void Workflow_variables_reject_reserved_runtime_names()
    {
        var errors = WorkflowVariables.Validate(new Dictionary<string, string>
        {
            ["ticket"] = "MC-42",
            ["width"] = "override"
        }, "Workflow");

        Assert.Single(errors);
        Assert.Contains("reserved", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Workflow_validator_accepts_local_action_step_and_defaults()
    {
        var workflow = new CaptureWorkflow(
            "local", "Local pipeline", "", Magic.Capture.Core.Commerce.ProductTier.PlusTrial,
            [new WorkflowStep("tool", WorkflowStepKind.RunLocalAction, Argument: "resize")],
            SchemaVersion: 3,
            Variables: new Dictionary<string, string> { ["ticket"] = "MC-42" });

        var result = WorkflowValidator.Validate(workflow);
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }
}

public sealed class WorkflowStudioCompatibilityTests
{
    [Fact]
    public void LegacyWorkflowJsonKeepsStepEnabledByDefault()
    {
        const string json = """
        {
          "Id":"legacy",
          "Name":"Legacy",
          "Description":"",
          "RequiredTier":0,
          "Steps":[{"Id":"copy","Kind":0,"Required":true}],
          "SchemaVersion":1,
          "IsBuiltIn":false
        }
        """;

        var workflow = System.Text.Json.JsonSerializer.Deserialize<CaptureWorkflow>(json);

        Assert.NotNull(workflow);
        Assert.Single(workflow!.Steps);
        Assert.Null(workflow.Steps[0].IsEnabled);
        Assert.True(WorkflowValidator.Validate(workflow).IsValid);
    }

    [Fact]
    public void DisabledStepIsExplicitAndStillValid()
    {
        var workflow = new CaptureWorkflow(
            "custom-test",
            "Custom test",
            "",
            ProductTier.PlusTrial,
            [new WorkflowStep("copy", WorkflowStepKind.CopyImage, IsEnabled: false)],
            SchemaVersion: 3);

        var validation = WorkflowValidator.Validate(workflow);

        Assert.True(validation.IsValid, string.Join(" ", validation.Errors));
        Assert.Equal(false, workflow.Steps[0].IsEnabled);
    }
}
