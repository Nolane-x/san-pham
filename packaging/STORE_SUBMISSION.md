# Magic Capture Desktop 2.0 — Microsoft Store Submission Guide

This document is the Store runbook for the 2.0 architecture. It assumes the final Windows build has passed unit/build/runtime/provider checks first.

## 1. Partner Center products

Reserve/associate the app as:

```text
Magic Capture Desktop
```

Create exactly one paid add-on:

```text
Display name: Magic Capture Desktop Pro Lifetime
In-app offer token: magiccapture.desktop.pro
Type: Durable
Lifetime: Forever
Subscription: No
```

Do **not** create Plus as an add-on. Plus is a local 168-hour trial tier and never becomes a charge.

Before production packaging, replace the development identity (`Magic.Capture.Desktop.Dev`, `CN=Magic Capture Desktop Dev`) through the normal Partner Center/Visual Studio Store association flow. Do not publish the development identity.

## 2. US pricing

Commercial launch baseline:

```text
App                             Free forever
Plus                            Trial only; not sold
Pro Lifetime regular US MSRP    $14.99
Pro Lifetime US launch          $9.99
Launch duration                 90 consecutive days from public Pro availability
Subscription                    None
```

Use Store sale pricing for the launch period while retaining the normal Pro base price. Let the application display Store-localized pricing rather than embedding USD price text.

## 3. AI commercial disclosure

Pro includes **AI integration/runtime**, not AI credits.

Store copy must not imply that a Pro purchase includes unlimited OpenAI, Anthropic, Gemini, OpenRouter or other provider usage.

Recommended disclosure:

> Pro AI features use a provider or local model configured by you. Cloud API usage is billed/limited by your chosen provider. Magic Capture Desktop does not resell AI tokens or route model traffic through a Magic Capture inference service.

## 4. Build machine

Recommended release machine:

- Windows 11;
- Visual Studio 2026 with Windows application development / WinUI tooling;
- .NET 10 SDK matching `global.json`;
- Windows SDK 10.0.26100 or newer.

Run:

```powershell
.\scripts\test.ps1
.\scripts\build.ps1 -Configuration Release
```

Both must exit `0`.

## 5. Store identity preflight

After Store association:

```powershell
.\scripts\store-preflight.ps1
```

The gate deliberately fails if the development package Identity/Publisher remains. Do not bypass it for a public Store package.

## 6. Package

```powershell
.\scripts\pack.ps1
```

The intended Store bundle covers x64 + ARM64.

If Store association/signing policy requires Visual Studio's package wizard, use the Microsoft Store packaging path with Release/x64/ARM64 after the same test/preflight gates.

## 7. Suggested Store positioning

### Short description

> Freeze any part of Windows with Win + Shift + X, then copy, OCR, edit, pin, extract, automate or turn it into a useful result. Fast local tools first; Pro adds your own local/cloud AI, evidence-backed Magic Actions and capture workflows.

### Feature bullets

- Tray-first `Win + Shift + X` Freeze Capture Hub.
- Region, window, monitor and virtual-desktop capture.
- Local Windows OCR, table extraction and QR/barcode tools.
- Pins, annotation, blur/pixelate, transforms, stitching and image comparison.
- Local searchable History and deterministic screen-signal extraction.
- Capture Workflows, Utilities and change-aware Capture Watch.
- Pro user-owned Custom Destinations.
- **Pro ScreenGraph intelligence:** deterministic OCR/structure/evidence before the model is called.
- **Pro BYOK/BYOM AI:** OpenAI, Anthropic, Gemini, OpenRouter, compatible endpoints, Ollama and LM Studio integrations.
- Pro Magic Actions, Context Stack, Evidence Anchoring, Semantic Compare and Magic Recipes.
- No Magic Capture account and no subscription.

Do not claim that every third-party model supports every capability. Provider/model support is capability/profile dependent.

## 8. Privacy/data-use truth

### Local/default behavior

Ordinary capture, OCR, table reconstruction, QR/barcode scanning, editor, History, utilities, stitching, pixel comparison and deterministic signals operate locally.

Magic Capture Desktop does not require a Magic Capture account and does not intentionally upload ordinary captures to a developer-operated backend.

### User-configured cloud AI

When a Pro user configures a cloud AI provider and explicitly runs a cloud Magic Action, selected prompt/context/image data is sent **directly from the PC to that configured provider**.

The final Store privacy declaration and privacy policy must disclose this accurately. Do not say “no data leaves your device” for 2.0, because that would be false when the user chooses cloud AI or a custom destination.

The UI provides:

- local-only mode;
- never-send-images-to-cloud mode;
- payload confirmation;
- AI Guard warnings;
- HTTPS requirement for remote AI endpoints.

Provider retention/processing policies are controlled by the chosen provider/account. The OpenAI native adapter requests `store=false`, but the app must not promise provider-wide zero retention.

### User-configured destinations

Pro Custom Destinations can send a capture/result to an endpoint configured by the user. This is also direct user-configured network transmission, not a Magic Capture hosting service.

### Credentials

Provider/destination secrets are stored with Windows PasswordVault and should not appear in ordinary JSON profiles/logs/history.

## 9. Free / Plus / Pro certification matrix

### Free

1. Fresh install launches and remains usable without purchase.
2. First successful run starts Plus locally without requesting a payment method.
3. Verify basic capture/OCR/editor/pin/history/workflow/utility functionality.
4. Verify Pro AI controls remain gated.

### Plus

1. Verify exact 168-hour boundary using test seams/controlled trial state.
2. Verify table, barcode, stitch, advanced editor/workflows, utility pack, CLI and change-aware Watch according to `FeatureCatalog`.
3. Verify all AI/ScreenGraph model actions remain Pro-only.
4. Verify trial expiry returns to Free and does not delete local data.
5. Verify Plus cannot be purchased because no Plus Store product exists.

### Pro Lifetime

1. Start Store checkout from the Plan/Upgrade UI.
2. Cancel purchase and confirm entitlement remains unchanged.
3. Complete purchase and confirm Pro Lifetime unlocks immediately.
4. Restart online/offline and verify previously confirmed Pro remains usable during temporary Store unavailability.
5. Verify localized Store price display.
6. Verify repeat region, fixed-aspect capture, Compare, click-through pins, custom destinations and all AI/Magic features.

## 10. AI provider certification matrix

Before advertising a provider family publicly, run a real test against its current API/local runtime.

For every provider advertised:

1. Configure profile without plaintext secret in profile JSON.
2. Verify remote HTTP is rejected; HTTPS accepted; localhost HTTP accepted where appropriate.
3. Test connection.
4. Discover/select a real current model.
5. Run text-only Magic Action.
6. If configured vision-capable, run image Magic Action.
7. If multi-image, run Context Stack / Semantic Compare.
8. Verify structured output parsing for a structured action.
9. Verify invalid key, 4xx, 429, 5xx and timeout fail safely.
10. Verify oversized response is rejected.
11. Verify local-only / never-cloud-image privacy settings.
12. Verify cloud payload confirmation names the **actual routed provider/model**.
13. Verify AI Guard warning path.
14. Verify result cache avoids an unnecessary repeated provider call.

See `docs/AI_PROVIDER_GUIDE.md`.

## 11. Functional Windows smoke matrix

Test at minimum on clean x64 and ARM64 Windows environments:

1. Install Store flight/MSIX candidate.
2. Start-menu launch opens Control Center.
3. Closing Control Center leaves tray/hotkey resident.
4. Start with Windows creates resident state without showing Control Center.
5. Re-launch while resident does not create duplicate tray/hotkey ownership.
6. `Win + Shift + X` freeze/select/action works immediately.
7. Overlay Copy/Save/Pin/Text/Edit/Color work.
8. Plus table/QR/stitch/advanced actions work under Plus/Pro entitlement.
9. Pro repeat/fixed-aspect/Compare/click-through works.
10. History search works over stored metadata/OCR previews.
11. Workflows run from overlay and History.
12. Capture Watch runs and stops cleanly; change threshold works.
13. CLI alias forwards commands to resident instance.
14. Utilities and custom destinations pass local/HTTPS endpoint tests.
15. Magic Action window, Context Stack and evidence highlight work.
16. Mixed-DPI multi-monitor capture is pixel-aligned.
17. Offline ordinary capture/edit/history remains usable.
18. Tray **Exit Magic Capture Desktop** terminates resident process.
19. Uninstall removes the MSIX package cleanly.

## 12. Release pricing checklist

- [ ] Free app remains $0.
- [ ] Plus has no Store SKU/add-on.
- [ ] Pro add-on is Durable / Forever.
- [ ] US Pro base price configured at $14.99-equivalent tier.
- [ ] US launch sale configured at $9.99 for 90 consecutive days.
- [ ] Sale ends back at the regular base price.
- [ ] Non-US localized pricing reviewed.
- [ ] Plan page reads Store current price instead of hard-coded USD.

## 13. Store listing truth checklist

- [ ] Listing says AI is Pro-only and user-supplied/local.
- [ ] Listing does not promise bundled AI credits.
- [ ] Listing distinguishes local deterministic tools from optional cloud AI.
- [ ] Privacy statement discloses user-configured cloud AI/custom destinations.
- [ ] No unsupported “all AI models/providers work” claim.
- [ ] No claim that AI output is guaranteed correct.
- [ ] No claim of complete DLP/security from AI Guard.
- [ ] Clean-room product description does not imply ShareX code reuse.
