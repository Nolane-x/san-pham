using Magic.Capture.Core.Ai;
using Magic.Capture.Core.Workflows;

namespace Magic.Capture.Core.Tests;

public sealed class MagicRecipeTests
{
    [Fact]
    public void Safe_recipe_accepts_deterministic_and_magic_action_steps()
    {
        var recipe = new MagicRecipe("r1", "Bug report", 1,
        [
            new MagicRecipeStep("ocr", MagicRecipeStepKind.WorkflowStep, WorkflowStepKind.RunOcr.ToString(), null),
            new MagicRecipeStep("ai", MagicRecipeStepKind.MagicAction, "developer.bug-report", null),
            new MagicRecipeStep("copy", MagicRecipeStepKind.WorkflowStep, WorkflowStepKind.CopyText.ToString(), null)
        ]);

        Assert.True(MagicRecipeValidator.Validate(recipe).IsValid);
    }

    [Fact]
    public void Recipe_rejects_unknown_step_kind_and_duplicate_ids()
    {
        var recipe = new MagicRecipe("r2", "Bad", 1,
        [
            new MagicRecipeStep("x", (MagicRecipeStepKind)99, "x", null),
            new MagicRecipeStep("x", MagicRecipeStepKind.MagicAction, "general.explain", null)
        ]);

        var result = MagicRecipeValidator.Validate(recipe);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Recipe_rejects_oversized_reference_and_options()
    {
        var options = Enumerable.Range(0, 17).ToDictionary(i => $"k{i}", i => "v");
        var recipe = new MagicRecipe("r", new string('n', 121), 1,
            [new MagicRecipeStep("s", MagicRecipeStepKind.MagicAction, new string('r', 513), options)]);
        var result = MagicRecipeValidator.Validate(recipe);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("reference", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, e => e.Contains("option", StringComparison.OrdinalIgnoreCase));
    }
}
