namespace Magic.Capture.Core.Ai;

public static class BuiltInMagicActions
{
    private const AiCapability Text = AiCapability.TextInput;
    private const AiCapability Json = AiCapability.StructuredJson;

    public static IReadOnlyList<MagicActionDefinition> All { get; } =
    [
        A("general.explain", "Explain capture", "General", "Explain only what is supported by the supplied screen context.", "Explain the selected capture clearly.", Text, Text | Json, MagicActionVisionMode.Optional, MagicActionOutputKind.Markdown, true),
        A("general.summarize", "Summarize", "General", "Summarize faithfully and preserve important numbers.", "Summarize this capture.", Text, Text | Json, MagicActionVisionMode.Optional, MagicActionOutputKind.Markdown, true),
        A("general.translate", "Translate", "General", "Translate faithfully. Preserve names, numbers and formatting where useful.", "Translate the content into the user's requested language.", Text, Text, MagicActionVisionMode.Optional, MagicActionOutputKind.Markdown, true),
        A("general.key-facts", "Extract key facts", "General", "Extract facts only from supplied evidence.", "Extract the key facts.", Text, Text | Json, MagicActionVisionMode.Optional, MagicActionOutputKind.StructuredJson, true),
        A("general.notes", "Clean notes", "General", "Turn visible content into concise reusable notes.", "Create clean notes.", Text, Text, MagicActionVisionMode.Optional, MagicActionOutputKind.Markdown, true),
        A("general.ask", "Ask about capture", "General", "Answer the user's question using only the supplied capture context.", "{{question}}", Text, Text, MagicActionVisionMode.Optional, MagicActionOutputKind.Markdown, true),

        A("developer.explain-error", "Explain error", "Developer", "Explain errors precisely; distinguish evidence from hypotheses.", "Explain this error, likely causes and next debugging steps.", Text, Text | Json, MagicActionVisionMode.Optional, MagicActionOutputKind.Markdown, true),
        A("developer.bug-report", "Create bug report", "Developer", "Create a reproducible bug report without inventing missing reproduction steps.", "Create a Markdown bug report with observed behavior, error evidence, environment clues and unknowns.", Text, Text | Json, MagicActionVisionMode.Optional, MagicActionOutputKind.Markdown, true),
        A("developer.stack-trace", "Extract stack trace", "Developer", "Return only stack/error structure supported by the capture.", "Extract exception, message, frames, files and line numbers.", Text, Json, MagicActionVisionMode.None, MagicActionOutputKind.StructuredJson, true),
        A("developer.causes", "Suggest likely causes", "Developer", "Separate likely causes from confirmed facts.", "List likely causes ranked by plausibility and cite evidence.", Text, Text, MagicActionVisionMode.Optional, MagicActionOutputKind.Markdown, true),
        A("developer.debug-checklist", "Debugging checklist", "Developer", "Produce a practical checklist based on visible evidence.", "Create a debugging checklist.", Text, Text, MagicActionVisionMode.Optional, MagicActionOutputKind.Markdown, true),
        A("developer.explain-code", "Explain code", "Developer", "Explain visible code; do not assume unseen files.", "Explain what this code does and important edge cases.", Text, Text, MagicActionVisionMode.Optional, MagicActionOutputKind.Markdown, true),
        A("developer.find-bug", "Find likely bug", "Developer", "Identify likely bugs only from visible code and label uncertainty.", "Find likely bugs and explain why.", Text, Text, MagicActionVisionMode.Optional, MagicActionOutputKind.Markdown, true),
        A("developer.test-ideas", "Generate test ideas", "Developer", "Generate tests grounded in the visible behavior/code.", "Generate high-value test cases.", Text, Text, MagicActionVisionMode.Optional, MagicActionOutputKind.Markdown, false),

        A("data.explain-table", "Explain table", "Data", "Interpret table values faithfully.", "Explain this table and its main relationships.", Text, Text | Json, MagicActionVisionMode.Optional, MagicActionOutputKind.Markdown, true),
        A("data.anomalies", "Find anomalies", "Data", "Flag possible anomalies and distinguish exact values from interpretation.", "Find anomalies, outliers or inconsistencies.", Text, Text | Json, MagicActionVisionMode.Optional, MagicActionOutputKind.Markdown, true),
        A("data.trends", "Describe trends", "Data", "Describe only trends supported by values/visual evidence.", "Describe the major trends and comparisons.", Text, Text | Json, MagicActionVisionMode.Optional, MagicActionOutputKind.Markdown, true),
        A("data.records", "Extract records", "Data", "Return structured records from supplied context.", "Extract structured records as JSON.", Text, Json, MagicActionVisionMode.Optional, MagicActionOutputKind.StructuredJson, true),

        A("ui.describe", "Describe UI", "UI", "Describe visible hierarchy and controls.", "Describe this interface and its hierarchy.", Text, Text, MagicActionVisionMode.Optional, MagicActionOutputKind.Markdown, true),
        A("ui.ux-review", "UX review", "UI", "Review visible usability without pretending to know analytics or user research.", "Review usability issues and improvements.", Text, Text, MagicActionVisionMode.Required, MagicActionOutputKind.Markdown, true),
        A("ui.accessibility-review", "Accessibility review", "UI", "Review visible accessibility risks; do not claim automated compliance.", "Identify visible accessibility risks and practical fixes.", Text, Text, MagicActionVisionMode.Required, MagicActionOutputKind.Markdown, true),
        A("ui.documentation", "Generate UI documentation", "UI", "Write user-facing steps grounded in visible controls.", "Create concise documentation for this interface.", Text, Text, MagicActionVisionMode.Required, MagicActionOutputKind.Markdown, true),
        A("ui.acceptance-criteria", "Acceptance criteria", "UI", "Derive testable acceptance criteria only from visible UI.", "Create acceptance criteria from this interface.", Text, Text, MagicActionVisionMode.Required, MagicActionOutputKind.Markdown, true),

        A("document.actions", "Extract action items", "Document", "Extract action items and owners only when present.", "Extract action items, owners and dates.", Text, Json, MagicActionVisionMode.Optional, MagicActionOutputKind.StructuredJson, true),
        A("document.entities", "Extract entities", "Document", "Extract named entities and fields from visible content.", "Extract important entities and fields.", Text, Json, MagicActionVisionMode.Optional, MagicActionOutputKind.StructuredJson, true),
        A("compare.semantic", "Semantic compare", "Compare", "Compare two captures semantically and cite evidence from each where possible.", "Describe meaningful differences between the primary capture and context capture.", Text, Text | Json, MagicActionVisionMode.Optional, MagicActionOutputKind.Markdown, true, true)
    ];

    public static MagicActionDefinition ById(string id) => All.First(a => a.Id == id);

    private static MagicActionDefinition A(string id, string name, string category, string system, string user,
        AiCapability min, AiCapability preferred, MagicActionVisionMode vision, MagicActionOutputKind output,
        bool evidence, bool context = false) =>
        new(id, name, category, system, user, min, preferred, vision, output, evidence, context, true);
}
