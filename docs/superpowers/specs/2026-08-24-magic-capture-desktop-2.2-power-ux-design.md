# Magic Capture Desktop 2.2 — Power UX Design

## Goal

Increase the exact 660-feature completion count without compromising the resident desktop application's low idle cost, local-first privacy, stability, or maintainability.

## Scope

2.2 closes high-value deterministic capabilities that build on the 2.1 foundations:

1. **Editor Objects 2.0** — multi-select, group/ungroup, align/distribute/equal-size, internal copy/paste, editable bounds and styling after creation.
2. **Compare 2.0** — threshold, alpha/transparent handling, per-channel metrics, heatmap/mask, blink, triptych, and optional bounded translation auto-alignment.
3. **Privacy Pipeline** — opt-in redact-before-copy/save/pin/workflow, user regex + sensitive words, local OCR/ScreenGraph redaction with no cloud dependency.
4. **Capture Precision UX** — richer X/Y/W/H HUD, selection reset/reselect, post-drag handle resizing primitives and light/dark overlay preference without adding background work.
5. **660 feature audit** — every original numbered request has an explicit status: Done, Partial, Foundation, Missing, or Test/Release Gate.

## Constraints

- No server, mandatory account, telemetry, hosted storage, or developer-hosted AI.
- Screenshot-only startup must not load recording codecs, ML models, FFmpeg, or new background workers.
- Core behavior remains deterministic and unit-testable.
- Windows adapters remain thin; settings are normalized before runtime use.
- User regex execution is bounded by timeout and length.
- Redaction is opt-in and local. History continues to preserve the original capture unless the user explicitly exports/copies/pins/runs a workflow through redaction policy.
- Comparison work runs only while Compare is open.
- All new collections and loops have explicit limits.

## Data flow

### Editor

`AnnotationWindow selection -> AnnotationDocumentEditor batch operation -> immutable AnnotationDocument -> AnnotationRenderer`

Group membership is stored as `GroupId` on each annotation. Styling remains serialized inside `.magiccapture` through the existing annotation records.

### Compare

`A/B PNG -> normalized BGRA -> optional bounded translation align -> DifferenceAnalyzer -> Difference render mode -> CompareWindow`

Metrics and difference classification are computed in one pass where practical. Render modes are generated only when requested or during the Compare session.

### Privacy

`CaptureAsset -> OCR -> ScreenGraph -> SensitiveDataDetector(custom patterns + words) -> RedactionPlanner -> AnnotationRenderer -> redacted CaptureAsset`

The host applies this asset only to selected outbound actions: copy/save/pin/workflow.

### Capture precision

The overlay continues to freeze one monitor once. Selection manipulation modifies only geometry; no new capture loop, timer, hook, or service is introduced.

## Error and recovery policy

- Invalid multi-select operations are no-ops or produce a bounded user-facing error, never partial document mutation.
- Invalid custom regexes are ignored by detector and surfaced by validation status where applicable.
- Privacy OCR failures do not silently export an unredacted image when a redact-before-* policy is enabled; the outbound action fails and explains why.
- Compare rejects empty/oversized malformed image buffers through existing decode limits and bounded alignment search.
- Settings normalization caps patterns, words, lengths, and compare defaults.

## Verification

Linux/source environment:

- repository verifier;
- C#/XAML/XML/JSON structural parsing where available;
- regression signature scans;
- Python reference checks for compare/alignment math;
- deterministic source archive integrity.

Windows release gate remains mandatory for .NET/xUnit/XAML compilation and real input/render behavior.
