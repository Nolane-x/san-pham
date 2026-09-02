using Magic.Capture.Core.Workflows;

namespace Magic.Capture.Core.Ai;

public static class MagicRecipeValidator
{
    public static MagicRecipeValidationResult Validate(MagicRecipe? recipe)
    {
        var errors = new List<string>();
        if (recipe is null) return new(false, ["Recipe is required."]);
        if (string.IsNullOrWhiteSpace(recipe.Id) || recipe.Id.Length > 96) errors.Add("Recipe id is invalid.");
        if (string.IsNullOrWhiteSpace(recipe.Name) || recipe.Name.Length > 120) errors.Add("Recipe name is invalid.");
        if (recipe.SchemaVersion != 1) errors.Add("Unsupported recipe schema version.");
        if (recipe.Steps is null) return new(false, [.. errors, "Recipe steps are required."]);
        if (recipe.Steps.Count is 0 or > 32) errors.Add("Recipe must contain between 1 and 32 steps.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in recipe.Steps)
        {
            if (step is null)
            {
                errors.Add("Recipe cannot contain null steps.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(step.Id) || step.Id.Length > 96) errors.Add("Every recipe step needs a valid id.");
            else if (!ids.Add(step.Id)) errors.Add($"Duplicate recipe step id: {step.Id}.");
            if (!Enum.IsDefined(step.Kind)) errors.Add($"Unknown recipe step kind: {(int)step.Kind}.");
            if (string.IsNullOrWhiteSpace(step.Reference) || step.Reference.Length > 512) errors.Add($"Recipe step {step.Id} has an invalid reference.");
            if (step.Kind == MagicRecipeStepKind.WorkflowStep && !string.IsNullOrWhiteSpace(step.Reference) &&
                !Enum.TryParse<WorkflowStepKind>(step.Reference, ignoreCase: false, out _))
                errors.Add($"Recipe workflow step '{step.Reference}' is not allowed.");

            if (step.Options is not null)
            {
                if (step.Options.Count > 16) errors.Add($"Recipe step {step.Id} cannot contain more than 16 options.");
                foreach (var (key, value) in step.Options.Take(17))
                {
                    if (string.IsNullOrWhiteSpace(key) || key.Length > 128) errors.Add($"Recipe step {step.Id} has an invalid option key.");
                    if ((value ?? string.Empty).Length > 2_048) errors.Add($"Recipe step {step.Id} option '{key}' is too long.");
                }
            }
        }
        return new(errors.Count == 0, errors);
    }
}
