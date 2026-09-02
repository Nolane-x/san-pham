using Magic.Capture.Core.Ai;
using Magic.Capture.Core.Commerce;
using Magic.Capture.Core.Workflows;
using Magic.Capture.Core.Settings;
using Xunit;

namespace Magic.Capture.Core.Tests;

public sealed class WorkflowReferencePolicyTests
{
    [Fact]
    public void FindWorkflowDependents_FindsChildWorkflowAndTrigger()
    {
        var target = new CaptureWorkflow("target", "Target", "", ProductTier.Free, []);
        var parent = new CaptureWorkflow("parent", "Parent", "", ProductTier.Free,
            [new WorkflowStep("s", WorkflowStepKind.RunWorkflow, Argument: "target")]);
        var trigger = new WorkflowTrigger("t", "T", WorkflowTriggerKind.Hotkey, "profile", "target", Hotkey: HotkeyGesture.DefaultRegion);

        var result = WorkflowReferencePolicy.FindWorkflowDependents("target", [target, parent], [trigger]);

        Assert.Contains(result, item => item.Contains("Parent", StringComparison.Ordinal));
        Assert.Contains(result, item => item.Contains("T", StringComparison.Ordinal));
    }

    [Fact]
    public void FindMagicActionDependents_FindsWorkflowAndRecipe()
    {
        var workflow = new CaptureWorkflow("w", "W", "", ProductTier.Free,
            [new WorkflowStep("s", WorkflowStepKind.RunMagicAction, Argument: "action")]);
        var recipe = new MagicRecipe("r", "R", 1, [new MagicRecipeStep("s", MagicRecipeStepKind.MagicAction, "action", null)]);

        var result = WorkflowReferencePolicy.FindMagicActionDependents("action", [workflow], [recipe]);

        Assert.Equal(2, result.Count);
    }
    [Fact]
    public void FindLocalActionDependents_FindsWorkflow()
    {
        var workflow = new CaptureWorkflow("w", "W", "", ProductTier.Free,
            [new WorkflowStep("s", WorkflowStepKind.RunLocalAction, Argument: "local")]);
        Assert.Single(WorkflowReferencePolicy.FindLocalActionDependents("local", [workflow], []));
    }

    [Fact]
    public void FindDestinationDependents_FindsRecipeWorkflowStep()
    {
        var recipe = new MagicRecipe("r", "R", 1,
            [new MagicRecipeStep("s", MagicRecipeStepKind.WorkflowStep, WorkflowStepKind.CustomHttpDestination.ToString(), new Dictionary<string, string> { ["argument"] = "dest" })]);
        Assert.Single(WorkflowReferencePolicy.FindDestinationDependents("dest", [], [recipe]));
    }

}
