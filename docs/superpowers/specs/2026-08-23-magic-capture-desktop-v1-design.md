# Magic Capture Desktop v1 — Product, UX, Commerce, and Architecture Specification

## 1. Product identity

**Official product name:** Magic Capture Desktop

**Positioning:** A fast, local-first, resident Windows capture utility. The main interaction is not “open the app and choose a tool”. The main interaction is **Win + Shift + X → freeze → select → act**.

**Non-goals:**
- no generative AI, LLM, VLM, embeddings, or model downloads;
- no Magic Capture Desktop account;
- no required developer-operated backend;
- no subscription;
- no forced registration;
- no automatic billing at the end of a trial;
- no decorative custom UI framework that fights Windows conventions.

## 2. Product tiers

### 2.1 Magic Capture Desktop Free
Free is permanent and useful on its own. It must never feel like a disabled demo.

Free includes:
- region freeze capture through Win + Shift + X;
- foreground window, active monitor, virtual desktop capture;
- copy/save PNG/JPEG;
- basic OCR plain text;
- basic pinning;
- basic history;
- basic annotation/editing;
- crop, resize, rotate;
- color sampling;
- tray resident lifecycle;
- capture cursor option;
- delay capture;
- privacy-safe local storage.

### 2.2 Magic Capture Desktop Plus
**Plus is not sold.** It exists only as the automatic 7-day trial tier.

Plus begins on first successful app launch and lasts exactly 168 hours. It unlocks:
- full table extraction and structured formats;
- QR/barcode recognition;
- vertical stitch / scrolling-composition tools;
- advanced editor tools (blur, pixelate, highlighter, richer transforms);
- advanced image export formats;
- unlimited pins during trial;
- direct OCR/Table/QR actions from the freeze overlay.

The trial:
- requires no payment method;
- requires no account;
- does not auto-renew;
- does not convert to a paid product;
- never charges the user;
- automatically falls back to Free at expiry;
- shows a one-time “Your 7-day Plus trial has ended” message;
- continues to advertise Pro Lifetime.

### 2.3 Magic Capture Desktop Pro
Pro is the only paid tier and is purchased once through Microsoft Store as a **Durable add-on with lifetime Forever**.

Pro contains all Plus capabilities plus power-user capabilities that remain visibly Pro even during the Plus trial:
- repeat last captured region;
- Pro fixed-aspect capture modes (1:1, 16:9, 4:3) in freeze overlay;
- compare workspace for two captures/files with side-by-side and overlay comparison;
- click-through pin mode;
- unlimited history retention options;
- Pro capture command hotkey for repeat-region;
- future capture profiles/batch/session features plug into the same Pro feature gate without changing engines.

Pro ownership always outranks Plus trial state.

### 2.4 Pricing policy

The initial public commercial policy is:

- Magic Capture Desktop app: free forever;
- Plus: trial-only, never sold;
- Pro Lifetime regular US price: **$29.99**;
- Pro Lifetime US launch price: **$19.99** for **90 consecutive days** from the first public Pro availability;
- no subscription and no recurring billing.

The US launch offer is configured in Partner Center as sale pricing against the regular durable-add-on base price. Exact start/end timestamps are release metadata, not source-code constants. The UI must not hard-code currency amounts; it reads Microsoft Store `FormattedPrice` and, when applicable, `FormattedBasePrice` so price presentation follows the customer's Store market/currency.

## 3. Entitlement rules

Product tier ordering:

`ProLifetime > PlusTrial > Free`

Feature access is centralized. Engines never contain pricing logic. UI/command entry points ask `EntitlementService.CanUse(feature)` before invoking a gated feature.

The Plus and Pro feature catalogs are deterministic and testable. No scattered `if (isPro)` branches are allowed across engine code.

## 4. Trial persistence

Trial state is stored locally in a small JSON record containing:
- schema version;
- first-start UTC;
- last-seen UTC;
- whether the expiry notice was already shown.

Effective trial time uses `max(systemUtcNow, lastSeenUtc)` to prevent trivial rollback by moving the system clock backwards during an existing installation.

Absolute anti-reinstall DRM is explicitly out of scope because the product has no server/account. The app must not introduce invasive DRM to protect a seven-day local trial.

Trial-created captures/settings are never deleted when Plus expires.

## 5. Microsoft Store Pro ownership

Partner Center add-on Product ID: `magiccapture.desktop.pro`.

The runtime uses `Windows.Services.Store.StoreContext` and identifies the add-on by its Partner Center in-app offer token rather than hard-coding a final Store ID where possible.

Checkout flow:
1. resolve associated Durable product for `magiccapture.desktop.pro`;
2. initialize StoreContext with the main HWND for desktop modal ownership;
3. call Store product purchase on the UI thread;
4. refresh entitlement;
5. immediately unlock Pro if ownership is confirmed.

Store failure rules:
- confirmed Pro ownership is cached locally;
- temporary Store unavailability never immediately downgrades a previously confirmed Pro installation;
- an explicit Store result proving no entitlement can fall back to Plus/Free;
- local cache is not presented as stronger proof than the Store once Store is reachable.

## 6. Resident Windows lifecycle

Magic Capture Desktop is designed to stay resident.

### Manual launch
- create resident main window/host;
- initialize tray and hotkeys;
- show Control Center.

### Windows startup activation
- detect `ExtendedActivationKind.StartupTask` as early as possible;
- initialize tray and hotkeys;
- keep Control Center hidden;
- consume near-zero CPU while idle.

### Main window close button
- cancel actual window destruction;
- hide Control Center to tray;
- keep tray icon, message HWND, and hotkeys alive.

### Real exit
Only **Exit Magic Capture Desktop** in the tray menu terminates the process.

Magic Capture Desktop is single-instance per signed-in Windows user. If the user launches it again while the resident process already exists, the second process signals the existing instance to show its Control Center and exits. This prevents duplicate tray icons, duplicate history writers, and global-hotkey registration conflicts.

Manifest includes a packaged desktop startup task so the app can start at sign-in. The Settings page exposes a Start with Windows toggle.

## 7. Primary interaction: Win + Shift + X

Win + Shift + X is the canonical freeze capture gesture.

On invocation:
1. identify active monitor;
2. capture/freeze it immediately;
3. display a borderless topmost overlay;
4. let user drag a region;
5. keep the frozen frame stable so hover menus/transient UI remain capturable;
6. show a compact Windows-style CommandBar adjacent to the selection.

### Overlay actions
The first-level overlay exposes nearly everything a user commonly needs:
- Copy;
- Save;
- Pin;
- Text;
- Table;
- QR;
- Edit;
- Color;
- More.

Actions must not require opening Control Center first.

### Access behavior
- Free action: execute immediately.
- Plus action while trial active: execute immediately.
- Plus action after trial: show a compact upgrade explanation, not an error.
- Pro action without Pro: explain that the feature is Pro, while the capture remains available for Free actions.

### Keyboard behavior
- Escape: cancel;
- Enter: default action;
- arrows: nudge region by 1 physical pixel;
- Shift+arrows: nudge 10 physical pixels;
- C: copy;
- S: save;
- P: pin;
- T: OCR text;
- E: edit;
- R: repeat-region is exposed through the Pro command/hotkey outside the current overlay.

## 8. Capture modes

Must ship:
- region;
- foreground window;
- active monitor;
- virtual desktop;
- delay 0/3/5/10 seconds;
- optional cursor;
- repeat last region (Pro);
- freeform selection;
- fixed aspect 1:1 / 16:9 / 4:3 (Pro).

Last region is saved as global physical coordinates plus source monitor metadata. If the topology no longer contains a valid intersection, repeat gracefully refuses and falls back to a new region capture.

## 9. Main Control Center

The main app is secondary. It uses native WinUI controls and familiar Windows information architecture.

Navigation:
- Home;
- History;
- Stitch;
- Compare (Pro);
- Settings;
- Upgrade / Plan;
- About.

Home is operational, not promotional:
- current hotkey;
- capture buttons;
- current tier / trial remaining;
- recent captures;
- short resident-state indicator.

History is the main content workspace.

Settings contains behavior, startup, hotkeys, OCR language, history retention, export quality, theme, and tray behavior.

Upgrade page explains Free / Plus Trial / Pro with a plain comparison table and one `Upgrade to Pro` button.

## 10. UI direction

Use WinUI 3 defaults:
- Segoe UI/system typography;
- NavigationView;
- CommandBar;
- standard Button/ToggleSwitch/ComboBox/NumberBox;
- InfoBar for non-blocking status;
- ContentDialog for trial-ended/upgrade confirmation;
- system theme by default;
- standard spacing and corner radii.

Avoid:
- giant gradients;
- glassmorphism;
- custom ornamental dashboards;
- animation that delays capture;
- large hero marketing sections in the desktop app.

## 11. Overlay performance

The capture fast path must not run OCR, barcode recognition, or table extraction before the user asks.

Copy/Save/Pin remain independent of recognition.

Text/Table/QR trigger local analysis only after region crop is known.

Idle resident process does no periodic visual analysis.

## 12. Recognition tiers

Free OCR uses Windows.Media.Ocr and returns plain text.

Plus adds:
- table reconstruction from OCR geometry;
- structured table serialization;
- barcode/QR decoding;
- direct overlay recognition actions.

No AI component package or network inference service is introduced.

## 13. Editor tiers

Free:
- crop;
- rectangle;
- arrow/line;
- pen;
- text;
- rotate/flip/resize;
- undo/redo;
- color sampling.

Plus/Pro:
- blur;
- pixelate;
- highlighter;
- advanced export formats.

All editing stays non-destructive until copy/save flattening.

## 14. Pin behavior

Free:
- always-on-top;
- resize preserving aspect ratio;
- opacity;
- maximum two concurrent pins.

Plus trial:
- unlimited concurrent pins.

Pro:
- unlimited pins;
- click-through mode toggled from the hover toolbar.

## 15. Compare workspace (Pro)

Compare accepts two local images or history captures.

Modes:
- side-by-side;
- overlay with adjustable opacity.

No AI/image semantics are involved. Comparison is pixel-based visual inspection.

## 16. History

History remains local PNG + atomic JSON metadata.

Free/Plus default retention remains conservative.

Pro permits unlimited count/age settings (bounded only by disk space) and keeps the same file format; upgrading or downgrading never migrates/deletes history simply because tier changed.

## 17. Tray UX

Right-click menu:
- Capture region — Win + Shift + X;
- Repeat last region — Win + Shift + R (Pro);
- Capture active monitor;
- Capture foreground window;
- Capture virtual desktop;
- separator;
- Open Magic Capture Desktop;
- History;
- Settings;
- Plan: Free / Plus Trial / Pro;
- separator;
- Exit Magic Capture Desktop.

Left-click opens Control Center.

## 18. MSIX packaging

Magic Capture Desktop remains a packaged WinUI 3 MSIX app with:
- package identity;
- full-trust desktop execution for Win32 tray/hotkey/capture integration;
- `windows.startupTask` extension;
- x64 and ARM64 packages;
- Per-Monitor-V2 DPI awareness;
- no internet capability declaration solely for product functionality;
- Microsoft Store responsible for distribution/update and Pro checkout.

Development identity remains a placeholder until Partner Center association.

## 19. Architecture

```text
                         App lifecycle
                              |
                    Resident host window
                  /           |           \
             Tray         Hotkeys        Control Center
                              |
                       Capture commands
                              |
                +-------------+-------------+
                |                           |
          Freeze Overlay                Direct modes
                |                           |
           selected region                   |
                +-------------+-------------+
                              |
                         CaptureAsset
                              |
      +------------+----------+----------+-----------+
      |            |                     |           |
   Clipboard    Local analysis          Editor       History
                    |                     |           |
               OCR/Table/QR              Pin         Export

        EntitlementService (UI/command boundary only)
                  /          |           \
               Free       PlusTrial   ProLifetime
```

## 20. Core modules added in this revision

- `Commerce/ProductTier.cs`
- `Commerce/ProductFeature.cs`
- `Commerce/FeatureCatalog.cs`
- `Commerce/TrialState.cs`
- `Commerce/TrialClock.cs`
- `Commerce/EntitlementSnapshot.cs`
- `Capture/LastRegionState.cs`
- `Geometry/AspectLockedSelection.cs`

App modules:
- `Commerce/EntitlementService.cs`
- `Commerce/StorePurchaseService.cs`
- `Commerce/TrialStateStore.cs`
- `Platform/StartupService.cs`
- `Platform/SingleInstanceService.cs`
- `Views/CompareWindow.*`

## 21. Failure behavior

Hotkey registration conflict:
- app remains usable via tray;
- settings clearly show the conflict and allow changing the shortcut.

Store unavailable:
- Free/Plus continues;
- cached prior Pro remains Pro;
- purchase button reports Store unavailable without blocking capture.

OCR unavailable:
- capture remains fully usable;
- only recognition action reports failure.

Startup task disabled by user in Windows Settings:
- app respects OS state and does not re-enable itself silently.

## 22. Privacy

No screenshot content is sent to the developer.
No capture telemetry is required.
No AI/cloud SDK is added.
Store APIs are used only for ownership/purchase.
All user captures, history, settings, and trial state remain local.

## 23. Definition of Done for this implementation wave

This wave is complete when source includes:
1. official visible branding `Magic Capture Desktop`;
2. resident tray lifecycle with close-to-tray and startup activation handling;
3. startup task MSIX manifest extension;
4. Free / Plus Trial / Pro Lifetime core feature model with tests;
5. 168-hour Plus trial state and clock rollback guard with tests;
6. Store durable add-on purchase/ownership service for `magiccapture.desktop.pro`;
7. plan/upgrade UI and trial-ended notification flow;
8. freeze overlay expanded with direct Text/Table/QR/Edit/Color actions;
9. feature gating centralized at command boundary;
10. repeat last region Pro capability and Pro repeat hotkey;
11. Pro compare window;
12. Pro fixed-aspect selection infrastructure;
13. tray menu showing repeat/plan/control-center commands;
14. existing capture/OCR/table/barcode/editor/history functionality preserved;
15. repo verifier updated to reject stale branding and missing tier/startup requirements;
16. release ZIP generated without `.git` or build caches.
