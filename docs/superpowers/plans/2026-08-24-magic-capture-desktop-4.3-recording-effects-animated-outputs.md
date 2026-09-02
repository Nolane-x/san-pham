# Magic Capture Desktop 4.3 Recording Effects & Animated Outputs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add local recording effects plus GIF/APNG visual recording while preserving the 4.2 MP4/audio/webcam fast path.

**Architecture:** Core owns effect/output policy and deterministic geometry. App owns session-scoped low-level input hooks, frame composition, and stream encoders. `RecordingSessionService` orchestrates these through small interfaces and keeps recovery/finalization format-aware.

**Tech Stack:** .NET 10, WinUI 3, Win32 low-level hooks, BGRA8 buffers, Windows MediaTranscoder for MP4, managed GIF89a/APNG writers.

**Spec:** `docs/superpowers/specs/2026-08-24-magic-capture-desktop-4.3-recording-effects-animated-outputs-design.md`

## Global Constraints

- Windows minimum remains 10.0.19041.0.
- No cloud dependency or external encoder executable.
- Low-level hooks must never swallow input.
- No raw unmodified character logging in safe-key overlay.
- Animated GIF/APNG are visual-only in 4.3.
- Existing MP4/audio/webcam path must remain available unchanged when effects are disabled.
- Recovery stays same-directory and partial outputs are never promoted after failure.

---

### Task 1: Core effect and output policies

**Files:**
- Create: `src/Magic.Capture.Core/Recording/RecordingEffectsPolicy.cs`
- Create: `tests/Magic.Capture.Core.Tests/RecordingEffectsPolicyTests.cs`
- Modify: `src/Magic.Capture.Core/Recording/RecordingPolicy.cs`

**Interfaces:**
- Produces `RecordingOutputFormat`, effect options, safe-key formatter, zoom/ripple/stroke geometry policies.

- [ ] Write tests for normalization, safe-key privacy filtering, ripple lifetime, coordinate mapping, zoom crop and stroke bounds.
- [ ] Verify source-contract RED because production types do not exist.
- [ ] Implement policies and option fields.
- [ ] Run lexical/structural gates.

### Task 2: Session-scoped input tracker

**Files:**
- Create: `src/Magic.Capture.App/Recording/RecordingInputTracker.cs`
- Modify: `src/Magic.Capture.App/Platform/Native/NativeMethods.cs`
- Modify: `src/Magic.Capture.App/Platform/Native/NativeStructs.cs`
- Modify: `src/Magic.Capture.App/Platform/Native/NativeConstants.cs`

**Interfaces:**
- Produces immutable `RecordingInputSnapshot` for the current active timestamp.

- [ ] Add source-contract tests for bounded state and hook lifecycle.
- [ ] Add WH_MOUSE_LL/WH_KEYBOARD_LL interop.
- [ ] Implement tracker that always calls `CallNextHookEx` and stores only bounded current/ripple/stroke/key state.
- [ ] Run static gates.

### Task 3: Effects compositor

**Files:**
- Create: `src/Magic.Capture.App/Recording/RecordingEffectsCompositor.cs`
- Modify: `src/Magic.Capture.App/Recording/RecordingSessionService.cs`

**Interfaces:**
- Consumes BGRA frame, target bounds, input snapshot, `RecordingOptions`, active elapsed.
- Produces composited BGRA frame.

- [ ] Add source-contract RED for effect pipeline invocation.
- [ ] Implement zoom, highlight, click rings/ripples, strokes and safe-key badge rendering.
- [ ] Wire compositor only when effects are enabled.
- [ ] Run static gates.

### Task 4: GIF/APNG encoders

**Files:**
- Create: `src/Magic.Capture.Core/Recording/GifEncodingPolicy.cs`
- Create: `src/Magic.Capture.Core/Recording/ApngEncodingPolicy.cs`
- Create: `src/Magic.Capture.App/Recording/GifRecordingEncoder.cs`
- Create: `src/Magic.Capture.App/Recording/ApngRecordingEncoder.cs`
- Create: `tests/Magic.Capture.Core.Tests/AnimatedRecordingEncodingPolicyTests.cs`

**Interfaces:**
- GIF/APNG consume deterministic BGRA frames and frame cadence; no audio.

- [ ] Write tests for palette mapping/LZW boundaries and PNG CRC/chunk rules.
- [ ] Verify source-contract RED.
- [ ] Implement managed encoders with checked bounds and cancellation.
- [ ] Run static gates.

### Task 5: Format-aware session/recovery/UI

**Files:**
- Modify: `src/Magic.Capture.App/Recording/RecordingSessionService.cs`
- Modify: `src/Magic.Capture.App/Recording/RecordingRecoveryStore.cs`
- Modify: `src/Magic.Capture.Core/Recording/RecordingPolicy.cs`
- Modify: `src/Magic.Capture.App/MainWindow.xaml`
- Modify: `src/Magic.Capture.App/MainWindow.xaml.cs`

**Interfaces:**
- Start selects MP4/GIF/APNG encoder, temp suffix and file picker extension from normalized options.

- [ ] Add UI/source contracts first.
- [ ] Add format/effect controls and handlers.
- [ ] Reject audio for GIF/APNG and update `.partial.*` recovery validation.
- [ ] Bump recording journal schema to v4.
- [ ] Run repository/structural/lexical gates.

### Task 6: Audit, release contracts and deterministic package

**Files:**
- Modify: `docs/FEATURE_AUDIT_660.md`
- Modify: `docs/WINDOWS_RELEASE_CHECKLIST.md`
- Create: `docs/RELEASE_NOTES_4.3.0.md`
- Modify: `release/version.json`
- Modify: `src/Magic.Capture.App/Magic.Capture.App.csproj`
- Modify: `src/Magic.Capture.App/Package.appxmanifest`
- Modify: `scripts/verify-repo.py`

**Interfaces:**
- Source version `4.3.0`, MSIX `4.3.0.0`.

- [ ] Update source-truth audit only for fully wired capabilities.
- [ ] Add 4.3 repository contracts.
- [ ] Run full tree gates.
- [ ] Build deterministic source ZIP and SHA-256 sidecar.
- [ ] Extract ZIP into a clean tree and rerun all gates/invariants from the packaged source.
