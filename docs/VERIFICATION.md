# Magic Capture Desktop 4.16.0 — Verification Record

## Generation environment

- Host: Linux container.
- Available: Python 3, filesystem tooling, XML/XAML parsing, source/static verification and ZIP integrity checks.
- Unavailable: `dotnet`, Visual Studio/MSBuild, Windows SDK runtime, WinUI runtime and Microsoft Store flight environment.

Therefore this environment **cannot** truthfully prove WinUI compilation, execute xUnit, create/install the final Store MSIX, exercise PasswordVault/StoreContext/StartupTask, or test Windows UI behavior. Those remain mandatory Windows release gates.

## Static repository gates

Run the complete source gate set from the repository root:

```bash
python scripts/verify-repo.py
python scripts/verify-structure.py
python scripts/verify-csharp-lexical.py
python scripts/verify-workflow-triggers.py
python scripts/verify-workflow-control-flow.py
python scripts/verify-history-intelligence.py
python scripts/verify-settings-personalization.py
python scripts/verify-settings-consistency.py
python scripts/verify-work-recovery.py
```

The gates cover repository/release synchronization, XML/XAML structure, C# lexical integrity, workflow trigger/control-flow contracts, history intelligence, settings/personalization consistency and the 4.16 Work Recovery contract. `verify-work-recovery.py` specifically checks the shared recovery policy, typed `.magicdoc`/`.magicclip` stores, App/service wiring, editor lifecycle integration, Home Recover/Discard UI, release packaging integration and the no-original-path recovery privacy contract.

`verify-repo.py` additionally guards the broader product contracts such as branding/version synchronization, dependency policy, Free/Plus/Pro boundaries, provider-secret storage, HTTPS AI endpoints, local-first architecture, custom-destination security, CLI aliasing, XAML event-handler existence and minimum test-suite breadth.

## Current inventory

At the time this 4.16.0 verification record was updated:

```text
C# source files   346
XAML files        16
Core test files   118
```

The xUnit files are source contracts present in the bundle. Their actual execution requires .NET on the Windows release/CI environment.

## 4.16 Work Recovery gates

The source bundle must prove all of the following before #606–#608 remain `Done`:

- recovery snapshots are written before journals are atomically replaced;
- Documentation Builder recovery is backed by `DocumentationProjectStore`;
- Video Editor recovery is backed by `VideoEditProjectStore`;
- recovery journals are bounded by kind, age, count and filename/path policy;
- editor autosave uses revision/generation guards so a stale save cannot delete newer recovery state;
- recovered projects reopen without an authoritative save path and therefore cannot silently overwrite the original project;
- future-schema video projects stay read-only and are never autosaved;
- Home exposes Recover/Discard for both new recovery kinds;
- recording recovery (#609) is not promoted by this wave.

## Mandatory Windows gates

On a Windows build machine with the pinned .NET/Windows App SDK toolchain:

```powershell
.\scripts\test.ps1
.\scripts\build.ps1 -Configuration Release
```

After Partner Center association:

```powershell
.\scripts\store-preflight.ps1
.\scripts\pack.ps1
```

Then complete `docs/WINDOWS_RELEASE_CHECKLIST.md` using the real package identity and Store flight. In particular, execute xUnit, WinUI/XAML compilation, x64/ARM64 Release builds, package install/update, and hands-on recovery crash/restore tests.

## Provider integration gates

A public release also requires real integration smoke tests for the provider families advertised in the Store/listing:

- OpenAI Responses;
- Anthropic Messages;
- Gemini;
- OpenRouter/generic OpenAI-compatible;
- Ollama;
- LM Studio.

Provider behavior can evolve independently of the app. Test the current endpoint/model with a real user-supplied credential/local runtime before public certification.

## Source-release verification

Run:

```bash
python scripts/source-release.py
```

The 4.16.0 release script:

1. runs the full source verifier tuple, including `verify-work-recovery.py`;
2. excludes VCS/build/cache output;
3. builds a deterministic source ZIP rooted at `Magic-Capture-Desktop-4.16.0/`;
4. runs ZIP integrity verification;
5. emits a SHA-256 sidecar.

For a reproducibility gate, run the source release twice from the identical tree and compare SHA-256 values. A deterministic source ZIP is still **not** evidence of Windows compilation; it proves only the source/static/release gates described above.
