using Magic.Capture.Core.Commerce;
using Magic.Capture.Core.Workflows;

namespace Magic.Capture.Core.Tests;

public sealed class WorkflowTests
{
    [Fact]
    public void Built_in_workflows_have_unique_ids_and_expected_profiles()
    {
        var workflows = WorkflowCatalog.BuiltIns;
        Assert.True(workflows.Count >= 5);
        Assert.Equal(workflows.Count, workflows.Select(w => w.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(workflows, w => w.Id == "quick-copy");
        Assert.Contains(workflows, w => w.Id == "ocr-copy");
        Assert.Contains(workflows, w => w.Id == "documentation");
        Assert.Contains(workflows, w => w.Id == "data-capture");
        Assert.Contains(workflows, w => w.Id == "bug-report");
    }

    [Fact]
    public void Validator_rejects_empty_or_duplicate_step_ids()
    {
        var workflow = new CaptureWorkflow(
            "bad", "Bad", "", ProductTier.Free,
            [
                new WorkflowStep("x", WorkflowStepKind.CopyImage, true),
                new WorkflowStep("x", WorkflowStepKind.SaveImage, true)
            ]);

        var result = WorkflowValidator.Validate(workflow);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Bug_report_requires_pro_and_magic_action_step()
    {
        var workflow = Assert.Single(WorkflowCatalog.BuiltIns, w => w.Id == "bug-report");
        Assert.Equal(ProductTier.ProLifetime, workflow.RequiredTier);
        Assert.Contains(workflow.Steps, s => s.Kind == WorkflowStepKind.RunMagicAction && s.Argument == "developer.bug-report");
    }
}
