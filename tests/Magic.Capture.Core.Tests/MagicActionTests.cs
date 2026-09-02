using Magic.Capture.Core.Ai;

namespace Magic.Capture.Core.Tests;

public sealed class MagicActionTests
{
    [Fact]
    public void Built_in_action_ids_are_unique_and_catalog_is_broad()
    {
        var actions = BuiltInMagicActions.All;
        Assert.True(actions.Count >= 20);
        Assert.Equal(actions.Count, actions.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(actions, a => a.Id == "developer.bug-report");
        Assert.Contains(actions, a => a.Id == "ui.accessibility-review");
        Assert.Contains(actions, a => a.Id == "compare.semantic");
    }

    [Fact]
    public void Custom_action_validator_rejects_unknown_or_unsafe_schema()
    {
        var invalid = new MagicActionDefinition("custom.bad", "Bad", "Custom", "x", "x", AiCapability.None, AiCapability.None,
            MagicActionVisionMode.Optional, MagicActionOutputKind.Markdown, true, false, false, SchemaVersion: 99);
        Assert.False(MagicActionValidator.Validate(invalid).IsValid);
    }

    [Fact]
    public void Custom_action_validator_rejects_invalid_enums_and_oversized_category()
    {
        var invalid = new MagicActionDefinition("custom.bad", "Bad", new string('c', 121), "x", "x", AiCapability.None, AiCapability.None,
            (MagicActionVisionMode)99, (MagicActionOutputKind)99, true, false, false);
        var result = MagicActionValidator.Validate(invalid);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("category", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, e => e.Contains("vision", StringComparison.OrdinalIgnoreCase));
    }
}
