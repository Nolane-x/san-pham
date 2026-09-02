using Magic.Capture.App.Workflows;
using Magic.Capture.Core.Ai;
using Magic.Capture.Core.Commerce;
using Magic.Capture.Core.Workflows;

namespace Magic.Capture.App.Ai;

internal sealed class MagicRecipeService
{
    private readonly WorkflowExecutor _workflows;
    private readonly Func<ProductTier> _tier;

    public MagicRecipeService(WorkflowExecutor workflows, Func<ProductTier> tier)
    {
        _workflows = workflows;
        _tier = tier;
    }

    public async Task<WorkflowExecutionResult> ExecuteAsync(MagicRecipe recipe, WorkflowExecutionContext context, CancellationToken cancellationToken = default)
    {
        var validation = MagicRecipeValidator.Validate(recipe);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
        if (_tier() != ProductTier.ProLifetime) throw new InvalidOperationException("Magic Recipes require Pro Lifetime.");

        var steps = recipe.Steps.Select(ToWorkflowStep).ToArray();
        var workflow = new CaptureWorkflow(
            $"recipe:{recipe.Id}",
            recipe.Name,
            "Hybrid deterministic + AI Magic Recipe",
            ProductTier.ProLifetime,
            steps,
            IsBuiltIn: false);
        return await _workflows.ExecuteAsync(workflow, context, cancellationToken);
    }

    private static WorkflowStep ToWorkflowStep(MagicRecipeStep step)
    {
        if (step.Kind == MagicRecipeStepKind.MagicAction)
            return new WorkflowStep(step.Id, WorkflowStepKind.RunMagicAction, Argument: step.Reference, Options: step.Options);

        if (!Enum.TryParse<WorkflowStepKind>(step.Reference, ignoreCase: false, out var kind))
            throw new InvalidDataException($"Unknown workflow step in recipe: {step.Reference}");
        var argument = step.Options is not null && step.Options.TryGetValue("argument", out var value) ? value : null;
        var outputKey = step.Options is not null && step.Options.TryGetValue("outputKey", out var key) ? key : null;
        return new WorkflowStep(step.Id, kind, Argument: argument, OutputKey: outputKey, Options: step.Options);
    }
}
