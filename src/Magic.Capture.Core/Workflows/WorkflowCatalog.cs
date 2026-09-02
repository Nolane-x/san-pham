using Magic.Capture.Core.Commerce;

namespace Magic.Capture.Core.Workflows;

public static class WorkflowCatalog
{
    public static IReadOnlyList<CaptureWorkflow> BuiltIns { get; } =
    [
        new(
            "quick-copy", "Quick Copy", "Capture and copy the image immediately.", ProductTier.Free,
            [new("copy", WorkflowStepKind.CopyImage)], IsBuiltIn: true),
        new(
            "ocr-copy", "OCR → Copy text", "Recognize text locally and copy the result.", ProductTier.Free,
            [new("ocr", WorkflowStepKind.RunOcr), new("copy", WorkflowStepKind.CopyText)], IsBuiltIn: true),
        new(
            "documentation", "Documentation", "Beautify a capture and open it in the editor for final review/export.", ProductTier.PlusTrial,
            [
                new("beautify", WorkflowStepKind.BeautifyImage),
                new("edit", WorkflowStepKind.OpenEditor)
            ], IsBuiltIn: true),
        new(
            "data-capture", "Data Capture", "Extract a table and copy structured text.", ProductTier.PlusTrial,
            [new("table", WorkflowStepKind.ExtractTable), new("copy", WorkflowStepKind.CopyText, Argument: "csv")], IsBuiltIn: true),
        new(
            "bug-report", "Bug Report · PRO", "Extract deterministic context and generate an evidence-backed bug report.", ProductTier.ProLifetime,
            [
                new("ocr", WorkflowStepKind.RunOcr),
                new("signals", WorkflowStepKind.ExtractSignals),
                new("magic", WorkflowStepKind.RunMagicAction, Argument: "developer.bug-report"),
                new("copy", WorkflowStepKind.CopyText, Argument: "markdown")
            ], IsBuiltIn: true)
    ];
}
