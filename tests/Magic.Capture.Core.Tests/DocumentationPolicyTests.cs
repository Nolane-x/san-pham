using Magic.Capture.Core.Documentation;
using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Tests;

public sealed class DocumentationPolicyTests
{
    [Fact]
    public void PlanCapture_PrefersTargetBoundsWithPaddingAndClampsToMonitor()
    {
        var monitor = new PixelRect(-1920, 0, 1920, 1080);
        var target = new DocumentationTargetEvidence(
            "button-1", "Button", "Save", "saveButton", "app", "Settings", 42,
            new PixelRect(-1800, 100, 120, 40), false);

        var plan = DocumentationPolicy.PlanCapture(monitor, new PixelPoint(-1740, 120), target);

        Assert.Equal(new PixelRect(-1848, 52, 216, 136), plan.Bounds);
        Assert.Equal(new PixelPoint(108, 68), plan.LocalClick);
        Assert.Equal(target, plan.Target);
    }

    [Fact]
    public void PlanCapture_FallsBackToBoundedClickCenteredRegion()
    {
        var monitor = new PixelRect(0, 0, 3840, 2160);

        var plan = DocumentationPolicy.PlanCapture(monitor, new PixelPoint(2000, 1000), null);

        Assert.Equal(960, plan.Bounds.Width);
        Assert.Equal(640, plan.Bounds.Height);
        Assert.True(plan.Bounds.Contains(new PixelPoint(2000, 1000)));
        Assert.Equal(new PixelPoint(480, 320), plan.LocalClick);
    }

    [Fact]
    public void ShouldCoalesce_RejectsNearDuplicateClickBurst()
    {
        var first = new DocumentationClickEvent(new PixelPoint(100, 100), DocumentationMouseButton.Left, DateTimeOffset.UnixEpoch);
        var second = new DocumentationClickEvent(new PixelPoint(106, 103), DocumentationMouseButton.Left, DateTimeOffset.UnixEpoch.AddMilliseconds(120));
        var distinct = second with { Button = DocumentationMouseButton.Right };

        Assert.True(DocumentationPolicy.ShouldCoalesce(first, second));
        Assert.False(DocumentationPolicy.ShouldCoalesce(first, distinct));
    }

    [Theory]
    [InlineData("Button", "Save", "Click “Save”.")]
    [InlineData("CheckBox", "Remember me", "Toggle “Remember me”.")]
    [InlineData("Edit", "Format", "Select or edit “Format”.")]
    [InlineData("ComboBox", "Language", "Select or edit “Language”.")]
    [InlineData("MenuItem", "Export", "Choose “Export”.")]
    [InlineData("Button", null, "Click the button.")]
    public void GenerateDescription_UsesDeterministicUiSemantics(string type, string? name, string expected)
    {
        var target = new DocumentationTargetEvidence("x", type, name, null, null, null, 0, new PixelRect(0, 0, 10, 10), false);

        Assert.Equal(expected, DocumentationPolicy.GenerateDescription(target));
    }

    [Fact]
    public void GenerateDescription_FallsBackWithoutTarget()
    {
        Assert.Equal("Click the highlighted area.", DocumentationPolicy.GenerateDescription(null));
    }

    [Theory]
    [InlineData("Enter", true)]
    [InlineData("Ctrl+S", true)]
    [InlineData("Alt+F4", true)]
    [InlineData("Shift+Tab", true)]
    [InlineData("A", false)]
    [InlineData("7", false)]
    [InlineData("Space", false)]
    [InlineData("password", false)]
    public void IsSafeKeyboardGesture_AllowsCommandsButNotPrintableText(string label, bool expected)
    {
        Assert.Equal(expected, DocumentationPolicy.IsSafeKeyboardGesture(label));
    }

    [Fact]
    public void MoveStep_ReordersWithoutMutatingIdsOrOrdinals()
    {
        var first = Step("one", "1");
        var second = Step("two", "2");
        var project = DocumentationProject.Create("Guide", [first, second]);

        var moved = DocumentationPolicy.MoveStep(project, "two", -1);

        Assert.Equal(["two", "one"], moved.Steps.Select(step => step.Id).ToArray());
        Assert.Equal("1", moved.Steps[1].ImageKey);
    }

    [Fact]
    public void DuplicateStep_CreatesNewIdentityAndPreservesEditableContent()
    {
        var project = DocumentationProject.Create("Guide", [Step("one", "1") with { Title = "Save", Description = "Click save" }]);

        var duplicated = DocumentationPolicy.DuplicateStep(project, "one", "copy");

        Assert.Equal(2, duplicated.Steps.Count);
        Assert.Equal("copy", duplicated.Steps[1].Id);
        Assert.Equal("Save", duplicated.Steps[1].Title);
        Assert.Equal("Click save", duplicated.Steps[1].Description);
    }

    [Fact]
    public void MergeSteps_UsesLaterImageAndCombinesDescriptions()
    {
        var a = Step("one", "1") with { Title = "Open settings", Description = "Open the menu." };
        var b = Step("two", "2") with { Description = "Click Settings." };
        var project = DocumentationProject.Create("Guide", [a, b]);

        var merged = DocumentationPolicy.MergeSteps(project, "one", "two");

        var step = Assert.Single(merged.Steps);
        Assert.Equal("one", step.Id);
        Assert.Equal("2", step.ImageKey);
        Assert.Equal("Open settings", step.Title);
        Assert.Equal("Open the menu.\n\nClick Settings.", step.Description);
    }

    [Fact]
    public void Normalize_BoundsStepCountAndStripsPasswordEvidenceValueSurfaces()
    {
        var steps = Enumerable.Range(0, DocumentationPolicy.MaximumSteps + 10)
            .Select(i => Step($"s-{i}", $"{i}.png") with
            {
                Target = new DocumentationTargetEvidence($"k-{i}", "Edit", "Password", "pwd", "app", "Login", 1,
                    new PixelRect(1, 1, 10, 10), true),
                SafeKeyGesture = "Ctrl+V"
            }).ToArray();
        var project = DocumentationProject.Create(new string('T', 600), steps);

        var normalized = DocumentationPolicy.Normalize(project);

        Assert.Equal(DocumentationPolicy.MaximumSteps, normalized.Steps.Count);
        Assert.Equal(DocumentationPolicy.MaximumProjectTitleLength, normalized.Title.Length);
        Assert.All(normalized.Steps, step => Assert.Null(step.SafeKeyGesture));
    }

    [Theory]
    [InlineData("clean", "clean")]
    [InlineData("compact", "compact")]
    [InlineData("presentation", "presentation")]
    [InlineData("print", "print")]
    [InlineData("unknown-template", "clean")]
    [InlineData("<script>", "clean")]
    [InlineData(null, "clean")]
    public void Normalize_CanonicalizesDocumentationTemplate(string? input, string expected)
    {
        var project = DocumentationProject.Create("Guide") with { Template = input };

        var normalized = DocumentationPolicy.Normalize(project);

        Assert.Equal(expected, normalized.Template);
        Assert.Equal(expected, DocumentationTemplateCatalog.Get(input).Id);
    }

    [Theory]
    [InlineData("logo.png", "logo.png")]
    [InlineData("steps/logo.png", null)]
    [InlineData("../logo.png", null)]
    [InlineData(null, null)]
    public void Normalize_CanonicalizesLogoImageKey(string? input, string? expected)
    {
        var project = DocumentationProject.Create("Guide") with { LogoImageKey = input };

        var normalized = DocumentationPolicy.Normalize(project);

        Assert.Equal(expected, normalized.LogoImageKey);
    }

    [Fact]
    public void TemplateCatalog_ExposesFourStablePublishingProfiles()
    {
        Assert.Equal(["clean", "compact", "presentation", "print"], DocumentationTemplateCatalog.All.Select(profile => profile.Id).ToArray());
        Assert.All(DocumentationTemplateCatalog.All, profile =>
        {
            Assert.InRange(profile.CardWidth, 900, 1800);
            Assert.InRange(profile.PageMarginTwips, 360, 1440);
        });
    }

    private static DocumentationStep Step(string id, string imageKey) => new(
        id,
        DateTimeOffset.UnixEpoch,
        imageKey,
        640,
        480,
        null,
        new PixelPoint(100, 100),
        DocumentationMouseButton.Left,
        null,
        null,
        null,
        null);
}
