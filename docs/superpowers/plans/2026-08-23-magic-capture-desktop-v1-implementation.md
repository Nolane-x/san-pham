# Magic Capture Desktop v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade the existing Magic Capture Desktop source into Magic Capture Desktop with resident tray-first UX, Win+Shift+X freeze hub, Free/7-day Plus/Pro Lifetime monetization, Microsoft Store durable Pro unlock, and additional Pro capture/compare capabilities.

**Architecture:** Keep deterministic capture/recognition engines tier-agnostic. Add a pure Core entitlement/trial model, then an App-level entitlement service combining local trial state and Microsoft Store ownership. Gate commands at UI/command boundaries, while resident lifecycle keeps the main HWND alive and hides the Control Center instead of exiting.

**Tech Stack:** C# / .NET 10, WinUI 3, Windows App SDK modular packages, Win32 tray/RegisterHotKey/GDI capture, Windows.Media.Ocr, Windows.Services.Store, MSIX.

**Spec:** `docs/superpowers/specs/2026-08-23-magic-capture-desktop-v1-design.md`

## Global Constraints

- Official visible product name: `Magic Capture Desktop`.
- Default region hotkey: `Win + Shift + X`.
- Plus is trial-only and lasts exactly 168 hours.
- Pro is lifetime and sold only through a Microsoft Store Durable add-on.
- Initial US Pro MSRP: `$29.99`; launch sale: `$19.99` for 90 consecutive days, configured in Partner Center rather than hard-coded in the app.
- No subscription, app account, cloud inference, LLM/VLM, embeddings, or model download.
- Main window close hides to tray; only tray Exit terminates resident process.
- Capture fast path performs no OCR/barcode/table analysis until requested.
- MSIX/package identity remains required.
- Windows 10 build 19041 minimum; x64 + ARM64.

---

### Task 1: Core entitlement and trial model

**Files:**
- Create `src/Magic.Capture.Core/Commerce/ProductTier.cs`
- Create `src/Magic.Capture.Core/Commerce/ProductFeature.cs`
- Create `src/Magic.Capture.Core/Commerce/FeatureCatalog.cs`
- Create `src/Magic.Capture.Core/Commerce/TrialState.cs`
- Create `src/Magic.Capture.Core/Commerce/TrialClock.cs`
- Create `src/Magic.Capture.Core/Commerce/EntitlementSnapshot.cs`
- Create `tests/Magic.Capture.Core.Tests/CommerceTests.cs`

**Interfaces:**
- `ProductTier { Free, PlusTrial, ProLifetime }`
- `FeatureCatalog.CanUse(ProductTier tier, ProductFeature feature) -> bool`
- `TrialClock.Evaluate(TrialState state, DateTimeOffset now) -> TrialEvaluation`

- [ ] Write tests proving Free/Plus/Pro matrix, exact 168-hour expiry, and backward-clock guard.
- [ ] Verify tests are RED because Commerce types do not exist.
- [ ] Implement the minimal pure Core model.
- [ ] Run tests on Windows/.NET toolchain; in this environment run static verifier and lexical checks.

### Task 2: Trial persistence and Store entitlement

**Files:**
- Create `src/Magic.Capture.App/Commerce/TrialStateStore.cs`
- Create `src/Magic.Capture.App/Commerce/StorePurchaseService.cs`
- Create `src/Magic.Capture.App/Commerce/EntitlementService.cs`
- Modify `src/Magic.Capture.App/Persistence/AppPaths.cs`
- Modify `src/Magic.Capture.App/ApplicationServices.cs`

**Interfaces:**
- `EntitlementService.InitializeAsync(IntPtr ownerHwnd)`
- `EntitlementService.Current`
- `EntitlementService.CanUse(ProductFeature feature)`
- `EntitlementService.PurchaseProAsync()`
- `EntitlementService.MarkTrialExpiryNoticeShownAsync()`

- [ ] Persist trial atomically.
- [ ] Resolve Pro Durable add-on by Partner Center offer token `magiccapture.desktop.pro`.
- [ ] Cache confirmed Pro ownership to tolerate transient Store outages.
- [ ] Never make capture depend on Store availability.

### Task 3: Resident lifecycle and startup task

**Files:**
- Create `src/Magic.Capture.App/Platform/StartupService.cs`
- Create `src/Magic.Capture.App/Platform/SingleInstanceService.cs`
- Modify `src/Magic.Capture.App/App.xaml.cs`
- Modify `src/Magic.Capture.App/MainWindow.xaml.cs`
- Modify `src/Magic.Capture.App/Package.appxmanifest`

- [ ] Detect StartupTask activation with AppInstance early.
- [ ] Add MSIX `windows.startupTask` extension.
- [ ] Hide Control Center for startup activation.
- [ ] Cancel main AppWindow closing and hide to tray.
- [ ] Keep tray Exit as the only actual application exit path.
- [ ] Add Settings support for Start with Windows state.
- [ ] Enforce a single resident instance and signal the existing instance on repeated launch.

### Task 4: Hotkeys and repeat-last-region Pro command

**Files:**
- Create `src/Magic.Capture.Core/Capture/LastRegionState.cs`
- Modify `src/Magic.Capture.App/Platform/HotkeyService.cs`
- Modify `src/Magic.Capture.App/Capture/CaptureCoordinator.cs`
- Modify `src/Magic.Capture.App/App.xaml.cs`
- Modify `src/Magic.Capture.App/Platform/TrayIconService.cs`
- Add `tests/Magic.Capture.Core.Tests/LastRegionStateTests.cs`

- [ ] Keep Win+Shift+X for freeze region.
- [ ] Add Pro Win+Shift+R repeat-region hotkey.
- [ ] Store last successful region in global physical coordinates.
- [ ] Validate repeat bounds against current virtual desktop.
- [ ] Gate repeat at command boundary with Pro entitlement.

### Task 5: Freeze overlay command hub

**Files:**
- Modify `src/Magic.Capture.App/Views/CaptureOverlayWindow.xaml`
- Modify `src/Magic.Capture.App/Views/CaptureOverlayWindow.xaml.cs`
- Modify `src/Magic.Capture.App/Capture/CaptureCoordinator.cs`
- Modify `src/Magic.Capture.App/App.xaml.cs`

- [ ] Replace minimal action bar with native-style compact CommandBar.
- [ ] Add Copy, Save, Pin, Text, Table, QR, Edit, Color, More actions.
- [ ] Add keyboard shortcuts C/S/P/T/E.
- [ ] Return requested action without running recognition in overlay.
- [ ] Direct gated actions through EntitlementService after crop.

### Task 6: Fixed-aspect selection foundation

**Files:**
- Create `src/Magic.Capture.Core/Geometry/AspectLockedSelection.cs`
- Create `tests/Magic.Capture.Core.Tests/AspectLockedSelectionTests.cs`
- Modify `src/Magic.Capture.App/Views/CaptureOverlayWindow.*`

- [ ] Test freeform and fixed ratio geometry.
- [ ] Add 1:1, 16:9, 4:3 selection options labeled Pro.
- [ ] Preserve selection within monitor physical bounds.

### Task 7: Pro compare workspace

**Files:**
- Create `src/Magic.Capture.App/Views/CompareWindow.xaml`
- Create `src/Magic.Capture.App/Views/CompareWindow.xaml.cs`
- Modify `src/Magic.Capture.App/MainWindow.xaml`
- Modify `src/Magic.Capture.App/MainWindow.xaml.cs`

- [ ] Pro gate compare entry.
- [ ] Pick two local images.
- [ ] Implement side-by-side mode.
- [ ] Implement overlay mode with opacity slider.
- [ ] Implement deterministic pixel-difference image and metrics.
- [ ] Keep all processing local.

### Task 8: Tier-aware editor and pins

**Files:**
- Modify `src/Magic.Capture.App/Views/AnnotationWindow.*`
- Modify `src/Magic.Capture.App/Views/PinWindow.*`
- Modify `src/Magic.Capture.App/App.xaml.cs`
- Modify `src/Magic.Capture.App/Platform/WindowHelpers.cs`

- [ ] Free editor retains core tools.
- [ ] Gate blur/pixelate/highlighter as Plus.
- [ ] Enforce two concurrent pins in Free; Plus/Pro unlimited.
- [ ] Add Pro click-through pin toggle.

### Task 9: Control Center and plan UX

**Files:**
- Modify `src/Magic.Capture.App/MainWindow.xaml`
- Modify `src/Magic.Capture.App/MainWindow.xaml.cs`

- [ ] Rename visible UI to Magic Capture Desktop.
- [ ] Keep Windows-style NavigationView.
- [ ] Add plan status on Home.
- [ ] Add Upgrade/Plan page with Free/Plus Trial/Pro comparison.
- [ ] Add Upgrade to Pro checkout button.
- [ ] Add one-time trial-ended ContentDialog.
- [ ] Add About page.
- [ ] Keep History/Stitch functional.

### Task 10: Packaging, branding, docs, verifier

**Files:**
- Rename solution to `Magic-Capture-Desktop.sln`
- Modify `src/Magic.Capture.App/Magic.Capture.App.csproj`
- Modify `src/Magic.Capture.App/Package.appxmanifest`
- Modify `README.md`
- Modify `docs/FEATURE_MATRIX.md`
- Modify `docs/VERIFICATION.md`
- Modify `packaging/STORE_SUBMISSION.md`
- Modify `scripts/*.ps1`
- Modify `scripts/verify-repo.py`

- [ ] Visible branding contains Magic Capture Desktop.
- [ ] Package identity/display text updated.
- [ ] Store guide documents durable Pro add-on Product ID and Forever lifetime.
- [ ] Verifier checks startup task, tier types, trial duration, Store token, resident close-to-tray, overlay actions, and no AI/network-client dependencies.
- [ ] Run full static verifier.
- [ ] Validate ZIP integrity and generate SHA-256.

## Execution status — source release candidate

The source implementation for Tasks 1–10 is present in the current branch, including commerce/trial, Store purchase/price discovery, resident lifecycle, startup, single instance, overlay hub, repeat region, fixed-aspect selection, Compare Workspace, tier-aware result/editor/pin behavior, Control Center, MSIX packaging metadata and documentation.

The checkbox steps above remain intentionally unmodified where they require **Windows execution evidence**. The Linux generation environment cannot truthfully mark Windows xUnit, WinUI XAML compilation, MSIX packaging, Store checkout or physical hotkey/OCR/DPI smoke tests as passed. Those are the final release gates documented in `docs/VERIFICATION.md` and `packaging/STORE_SUBMISSION.md`.
