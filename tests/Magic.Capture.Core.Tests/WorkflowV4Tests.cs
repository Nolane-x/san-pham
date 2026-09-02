using Magic.Capture.Core.Commerce;
using Magic.Capture.Core.Workflows;

namespace Magic.Capture.Core.Tests;

public sealed class WorkflowV4Tests
{
    [Fact]
    public void Validator_accepts_bounded_typed_parameters()
    {
        var workflow = new CaptureWorkflow(
            "v4", "V4", "", ProductTier.PlusTrial,
            [new WorkflowStep("copy", WorkflowStepKind.CopyImage)],
            SchemaVersion: 4,
            Parameters:
            [
                new WorkflowParameterDefinition("project", "Project name", WorkflowParameterKind.Text, Required: true),
                new WorkflowParameterDefinition("quality", "Quality", WorkflowParameterKind.Choice, DefaultValue: "high", Choices: ["low", "high"]),
                new WorkflowParameterDefinition("confirmed", "Proceed?", WorkflowParameterKind.Boolean, DefaultValue: "false")
            ]);

        Assert.True(WorkflowValidator.Validate(workflow).IsValid);
    }

    [Fact]
    public void Resolver_uses_runtime_then_variables_then_default()
    {
        var parameters = new[]
        {
            new WorkflowParameterDefinition("runtime", "Runtime", WorkflowParameterKind.Text, Required: true),
            new WorkflowParameterDefinition("variable", "Variable", WorkflowParameterKind.Text, Required: true),
            new WorkflowParameterDefinition("fallback", "Fallback", WorkflowParameterKind.Text, DefaultValue: "default")
        };
        var runtime = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["runtime"] = "r" };
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["variable"] = "v" };

        var resolved = WorkflowParameterResolver.ResolveKnown(parameters, variables, runtime);

        Assert.Equal("r", resolved.Values["runtime"]);
        Assert.Equal("v", resolved.Values["variable"]);
        Assert.Equal("default", resolved.Values["fallback"]);
        Assert.Empty(resolved.Missing);
    }

    [Fact]
    public void Runtime_policy_bounds_delay_and_subworkflow_depth()
    {
        Assert.Equal(60_000, WorkflowRuntimePolicy.ParseDelayMilliseconds("60000"));
        Assert.Throws<InvalidDataException>(() => WorkflowRuntimePolicy.ParseDelayMilliseconds("60001"));
        Assert.True(WorkflowRuntimePolicy.CanEnterSubworkflow(["a", "b", "c"], "d"));
        Assert.False(WorkflowRuntimePolicy.CanEnterSubworkflow(["a", "b", "c", "d"], "e"));
        Assert.False(WorkflowRuntimePolicy.CanEnterSubworkflow(["a"], "a"));
    }

    [Theory]
    [InlineData(WorkflowStepKind.CopyImage, true)]
    [InlineData(WorkflowStepKind.SaveImage, true)]
    [InlineData(WorkflowStepKind.CustomHttpDestination, true)]
    [InlineData(WorkflowStepKind.RunMagicAction, true)]
    [InlineData(WorkflowStepKind.RunLocalAction, true)]
    [InlineData(WorkflowStepKind.RunOcr, false)]
    [InlineData(WorkflowStepKind.BeautifyImage, false)]
    [InlineData(WorkflowStepKind.Delay, false)]
    public void Runtime_policy_classifies_side_effects_for_dry_run(WorkflowStepKind kind, bool expected)
    {
        Assert.Equal(expected, WorkflowRuntimePolicy.IsSideEffecting(kind));
    }
    [Fact]
    public void Validator_keeps_legacy_schema_readable_but_rejects_v4_contracts_labeled_as_v3()
    {
        var legacy = new CaptureWorkflow(
            "legacy", "Legacy", "", ProductTier.Free,
            [new WorkflowStep("copy", WorkflowStepKind.CopyImage)],
            SchemaVersion: 1);
        Assert.True(WorkflowValidator.Validate(legacy).IsValid);

        var mislabeledParameter = new CaptureWorkflow(
            "bad-param", "Bad", "", ProductTier.PlusTrial,
            [new WorkflowStep("copy", WorkflowStepKind.CopyImage)],
            SchemaVersion: 3,
            Parameters: [new WorkflowParameterDefinition("project", "Project", WorkflowParameterKind.Text)]);
        Assert.False(WorkflowValidator.Validate(mislabeledParameter).IsValid);

        var mislabeledStep = new CaptureWorkflow(
            "bad-step", "Bad", "", ProductTier.PlusTrial,
            [new WorkflowStep("delay", WorkflowStepKind.Delay, Argument: "10")],
            SchemaVersion: 3);
        Assert.False(WorkflowValidator.Validate(mislabeledStep).IsValid);
    }

}
