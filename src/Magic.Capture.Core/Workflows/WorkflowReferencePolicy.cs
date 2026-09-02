using Magic.Capture.Core.Ai;

namespace Magic.Capture.Core.Workflows;

public static class WorkflowReferencePolicy
{
    public static IReadOnlyList<string> FindWorkflowDependents(
        string workflowId,
        IEnumerable<CaptureWorkflow> workflows,
        IEnumerable<WorkflowTrigger> triggers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        var result = new List<string>();
        foreach (var workflow in workflows ?? [])
        {
            if (workflow is null || string.Equals(workflow.Id, workflowId, StringComparison.Ordinal)) continue;
            if (workflow.Steps.Any(step => step.IsEnabled != false &&
                step.Kind is WorkflowStepKind.RunWorkflow or WorkflowStepKind.ForEachImage &&
                string.Equals(step.Argument, workflowId, StringComparison.Ordinal)))
                result.Add($"Workflow ‘{workflow.Name}’");
        }
        foreach (var trigger in triggers ?? [])
            if (trigger is not null && string.Equals(trigger.WorkflowId, workflowId, StringComparison.Ordinal))
                result.Add($"Trigger ‘{trigger.Name}’");
        return result.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<string> FindCaptureProfileDependents(string profileId, IEnumerable<WorkflowTrigger> triggers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        return (triggers ?? [])
            .Where(trigger => trigger is not null && string.Equals(trigger.CaptureProfileId, profileId, StringComparison.Ordinal))
            .Select(trigger => $"Trigger ‘{trigger.Name}’")
            .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<string> FindMagicActionDependents(
        string actionId,
        IEnumerable<CaptureWorkflow> workflows,
        IEnumerable<MagicRecipe> recipes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        var result = new List<string>();
        foreach (var workflow in workflows ?? [])
            if (workflow is not null && workflow.Steps.Any(step => step.IsEnabled != false && step.Kind == WorkflowStepKind.RunMagicAction && string.Equals(step.Argument, actionId, StringComparison.Ordinal)))
                result.Add($"Workflow ‘{workflow.Name}’");
        foreach (var recipe in recipes ?? [])
            if (recipe is not null && recipe.Steps.Any(step => step.Kind == MagicRecipeStepKind.MagicAction && string.Equals(step.Reference, actionId, StringComparison.Ordinal)))
                result.Add($"Magic Recipe ‘{recipe.Name}’");
        return result.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }
    public static IReadOnlyList<string> FindLocalActionDependents(
        string actionId,
        IEnumerable<CaptureWorkflow> workflows,
        IEnumerable<MagicRecipe> recipes) =>
        FindWorkflowArgumentDependents(actionId, WorkflowStepKind.RunLocalAction, workflows, recipes);

    public static IReadOnlyList<string> FindDestinationDependents(
        string destinationId,
        IEnumerable<CaptureWorkflow> workflows,
        IEnumerable<MagicRecipe> recipes) =>
        FindWorkflowArgumentDependents(destinationId, WorkflowStepKind.CustomHttpDestination, workflows, recipes);

    private static IReadOnlyList<string> FindWorkflowArgumentDependents(
        string argument,
        WorkflowStepKind kind,
        IEnumerable<CaptureWorkflow> workflows,
        IEnumerable<MagicRecipe> recipes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(argument);
        var result = new List<string>();
        foreach (var workflow in workflows ?? [])
            if (workflow is not null && workflow.Steps.Any(step => step.IsEnabled != false && step.Kind == kind && string.Equals(step.Argument, argument, StringComparison.Ordinal)))
                result.Add($"Workflow ‘{workflow.Name}’");
        foreach (var recipe in recipes ?? [])
            if (recipe is not null && recipe.Steps.Any(step => RecipeReferences(step, kind, argument)))
                result.Add($"Magic Recipe ‘{recipe.Name}’");
        return result.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static bool RecipeReferences(MagicRecipeStep step, WorkflowStepKind kind, string argument)
    {
        if (step.Kind != MagicRecipeStepKind.WorkflowStep || !string.Equals(step.Reference, kind.ToString(), StringComparison.Ordinal)) return false;
        return step.Options is not null && step.Options.TryGetValue("argument", out var value) && string.Equals(value, argument, StringComparison.Ordinal);
    }

}
