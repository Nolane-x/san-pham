namespace Magic.Capture.Core.Ai;

public enum MagicRecipeStepKind { WorkflowStep, MagicAction }

public sealed record MagicRecipeStep(string Id, MagicRecipeStepKind Kind, string Reference, IReadOnlyDictionary<string, string>? Options);

public sealed record MagicRecipe(string Id, string Name, int SchemaVersion, IReadOnlyList<MagicRecipeStep> Steps);

public sealed record MagicRecipeValidationResult(bool IsValid, IReadOnlyList<string> Errors);
