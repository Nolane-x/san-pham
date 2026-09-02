using Magic.Capture.Core.Commerce;
using Magic.Capture.Core.Workflows;

namespace Magic.Capture.Core.Tests;

public sealed class WorkflowV2Tests
{
    [Theory]
    [InlineData("exists:text", true)]
    [InlineData("not-exists:missing", true)]
    [InlineData("equals:app:Chrome", true)]
    [InlineData("contains:text:error", true)]
    [InlineData("gt:size:1000", true)]
    [InlineData("lte:size:2048", true)]
    [InlineData("exists:text && contains:text:error", true)]
    [InlineData("equals:app:Edge", false)]
    public void Condition_evaluator_supports_small_deterministic_language(string expression, bool expected)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["text"] = "fatal error happened",
            ["app"] = "Chrome",
            ["size"] = 2048
        };
        Assert.Equal(expected, WorkflowConditionEvaluator.Evaluate(expression, values));
    }

    [Fact]
    public void Validator_accepts_schema_2_and_rejects_unbounded_execution_policy()
    {
        var workflow = new CaptureWorkflow("w", "Workflow", "", ProductTier.Free,
            [new WorkflowStep("s", WorkflowStepKind.CopyImage, MaxAttempts: 99, TimeoutMilliseconds: -1)], SchemaVersion: 2);
        var result = WorkflowValidator.Validate(workflow);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("attempt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, x => x.Contains("timeout", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_rejects_oversized_strings_and_options()
    {
        var options = Enumerable.Range(0, 33).ToDictionary(i => $"k{i}", i => "v");
        var workflow = new CaptureWorkflow("w", new string('n', 121), new string('d', 2001), ProductTier.Free,
            [new WorkflowStep("s", WorkflowStepKind.CopyText, Argument: new string('a', 4097), Options: options)], SchemaVersion: 2);
        var result = WorkflowValidator.Validate(workflow);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, x => x.Contains("option", StringComparison.OrdinalIgnoreCase));
    }
}
