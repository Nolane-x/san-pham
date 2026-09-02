# ShareX clean-room reference policy

Magic Capture Desktop may study publicly documented product behavior of mature capture tools such as ShareX to understand user needs and workflow categories. Production source in this repository must remain an independent implementation.

## Rules

1. Do not copy ShareX source files, classes, methods or code fragments into Magic Capture Desktop.
2. Do not introduce a ShareX binary/package/source dependency.
3. Do not port implementation details line-by-line from ShareX.
4. Feature names that are generic industry concepts (capture region, OCR, after-capture workflow, custom destination, image editor, QR decoding, etc.) may be implemented independently.
5. Design work should describe observable behavior and user problems, then implement against Magic Capture Desktop's own architecture/contracts.
6. The repository verifier rejects the `ShareX` name in production C#/XAML/project files to discourage accidental source coupling.

## Why

The product goal is feature competition and innovation, not code reuse. Magic Capture Desktop's commercial licensing and architecture should remain independently controllable.

## Independent differentiators

Magic Capture Desktop 2.0 intentionally differs at the architecture level through:

- ScreenGraph deterministic context;
- small-model-first prompt planning;
- capability-based multi-provider routing;
- evidence anchoring to original pixels;
- Context Stack;
- deterministic + AI Magic Recipes;
- cloud payload confirmation and AI Guard;
- AI result cache;
- change-aware Capture Watch;
- tray-first `Win + Shift + X` Freeze Capture Hub.
