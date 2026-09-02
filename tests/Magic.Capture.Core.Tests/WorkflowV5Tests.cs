using Magic.Capture.Core.Commerce;
using Magic.Capture.Core.Workflows;

namespace Magic.Capture.Core.Tests;

public sealed class WorkflowV5Tests
{
    [Fact]
    public void Validator_requires_schema_v5_for_image_loop()
    {
        var step = new WorkflowStep("loop", WorkflowStepKind.ForEachImage, Argument: "child");
        var v4 = new CaptureWorkflow("v4-loop", "Loop", "", ProductTier.PlusTrial, [step], SchemaVersion: 4);
        var v5 = v4 with { Id = "v5-loop", SchemaVersion = 5 };

        Assert.False(WorkflowValidator.Validate(v4).IsValid);
        Assert.True(WorkflowValidator.Validate(v5).IsValid);
    }

    [Fact]
    public void Loop_policy_is_bounded_and_parses_continue_on_error()
    {
        Assert.Equal(32, WorkflowRuntimePolicy.MaximumLoopImages);
        Assert.False(WorkflowRuntimePolicy.ParseLoopContinueOnError(null));
        Assert.True(WorkflowRuntimePolicy.ParseLoopContinueOnError(new Dictionary<string, string> { ["continueOnError"] = "true" }));
        Assert.Throws<InvalidDataException>(() => WorkflowRuntimePolicy.ParseLoopContinueOnError(new Dictionary<string, string> { ["continueOnError"] = "maybe" }));
    }

    [Fact]
    public void Validator_rejects_retrying_image_loop_steps()
    {
        var workflow = new CaptureWorkflow(
            "retry-loop", "Loop", "", ProductTier.PlusTrial,
            [new WorkflowStep("loop", WorkflowStepKind.ForEachImage, Argument: "child", MaxAttempts: 2)],
            SchemaVersion: 5);

        var result = WorkflowValidator.Validate(workflow);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("cannot retry", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Fingerprint_is_stable_and_changes_with_execution_contract()
    {
        var workflow = new CaptureWorkflow(
            "fingerprint", "Fingerprint", "description does not affect execution", ProductTier.PlusTrial,
            [new WorkflowStep("ocr", WorkflowStepKind.RunOcr, OutputKey: "text")],
            SchemaVersion: 5,
            Variables: new Dictionary<string, string> { ["project"] = "demo" });

        var first = WorkflowFingerprint.Compute(workflow);
        var same = WorkflowFingerprint.Compute(workflow with { Name = "Renamed", Description = "changed prose" });
        var changed = WorkflowFingerprint.Compute(workflow with
        {
            Steps = [new WorkflowStep("ocr", WorkflowStepKind.RunOcr, OutputKey: "different")]
        });

        Assert.Matches("^[0-9a-f]{64}$", first);
        Assert.Equal(first, same);
        Assert.NotEqual(first, changed);
    }

    [Theory]
    [InlineData(WorkflowStepKind.CopyImage, true)]
    [InlineData(WorkflowStepKind.CopyText, true)]
    [InlineData(WorkflowStepKind.SaveImage, true)]
    [InlineData(WorkflowStepKind.PinImage, true)]
    [InlineData(WorkflowStepKind.OpenEditor, true)]
    [InlineData(WorkflowStepKind.CustomHttpDestination, false)]
    [InlineData(WorkflowStepKind.RunLocalAction, false)]
    public void Resume_skip_allowlist_is_narrow(WorkflowStepKind kind, bool expected)
    {
        Assert.Equal(expected, WorkflowRuntimePolicy.IsResumeSkippableSideEffect(kind));
    }

    [Theory]
    [InlineData(WorkflowStepKind.RunMagicAction, true)]
    [InlineData(WorkflowStepKind.CustomHttpDestination, true)]
    [InlineData(WorkflowStepKind.RunLocalAction, true)]
    [InlineData(WorkflowStepKind.RunWorkflow, true)]
    [InlineData(WorkflowStepKind.ForEachImage, true)]
    [InlineData(WorkflowStepKind.RunOcr, false)]
    public void Resume_non_replayable_classification_is_explicit(WorkflowStepKind kind, bool expected)
    {
        Assert.Equal(expected, WorkflowRuntimePolicy.IsResumeNonReplayable(kind));
    }
}
