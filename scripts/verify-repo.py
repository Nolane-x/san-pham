#!/usr/bin/env python3
from __future__ import annotations

import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ERRORS: list[str] = []
WARNINGS: list[str] = []

REQUIRED = [
    'Magic-Capture-Desktop.sln',
    'global.json',
    'src/Magic.Capture.Core/Magic.Capture.Core.csproj',
    'src/Magic.Capture.App/Magic.Capture.App.csproj',
    'src/Magic.Capture.App/Package.appxmanifest',
    'src/Magic.Capture.App/app.manifest',
    'src/Magic.Capture.App/App.xaml',
    'src/Magic.Capture.App/App.xaml.cs',
    'src/Magic.Capture.App/MainWindow.xaml',
    'src/Magic.Capture.App/MainWindow.xaml.cs',
    'src/Magic.Capture.App/Views/CaptureOverlayWindow.xaml',
    'src/Magic.Capture.App/Views/CaptureResultWindow.xaml',
    'src/Magic.Capture.App/Views/PinWindow.xaml',
    'src/Magic.Capture.App/Views/AnnotationWindow.xaml',
    'src/Magic.Capture.App/Views/CompareWindow.xaml',
    'src/Magic.Capture.App/Views/CompareWindow.xaml.cs',
    'src/Magic.Capture.App/Commerce/EntitlementService.cs',
    'src/Magic.Capture.App/Commerce/StorePurchaseService.cs',
    'src/Magic.Capture.App/Platform/StartupService.cs',
    'src/Magic.Capture.App/Platform/SingleInstanceService.cs',
    'src/Magic.Capture.App/Analysis/WindowsOcrService.cs',
    'src/Magic.Capture.Core/ScreenGraph/ScreenGraphBuilder.cs',
    'src/Magic.Capture.Core/Ai/BuiltInMagicActions.cs',
    'src/Magic.Capture.App/Ai/MagicActionService.cs',
    'src/Magic.Capture.App/Ai/Provider/WindowsPasswordVaultSecretStore.cs',
    'src/Magic.Capture.App/Views/MagicActionWindow.xaml',
    'src/Magic.Capture.App/Analysis/BarcodeService.cs',
    'src/Magic.Capture.Core/LocalActions/LocalActionModels.cs',
    'src/Magic.Capture.Core/LocalActions/LocalActionTemplate.cs',
    'src/Magic.Capture.App/LocalActions/LocalActionProfileStore.cs',
    'src/Magic.Capture.App/LocalActions/LocalActionApprovalStore.cs',
    'src/Magic.Capture.App/LocalActions/LocalActionRunner.cs',
    'src/Magic.Capture.App/Imaging/VerticalImageStitcher.cs',
    'tests/Magic.Capture.Core.Tests/Magic.Capture.Core.Tests.csproj',
    'docs/superpowers/specs/2026-08-23-magic-capture-desktop-v2-ai-intelligence-design.md',
    'docs/superpowers/plans/2026-08-23-magic-capture-desktop-v2-ai-intelligence-implementation.md',
    'docs/superpowers/specs/2026-08-23-magic-capture-desktop-v2-workflow-intelligence-design.md',
    'docs/superpowers/plans/2026-08-23-magic-capture-desktop-v2-workflow-intelligence-implementation.md',
    'docs/FEATURE_MATRIX.md',
    'docs/RELEASE_NOTES_4.11.0.md',
    'docs/RELEASE_NOTES_4.12.0.md',
    'docs/superpowers/specs/2026-08-26-magic-capture-desktop-4.12-automation-triggers-design.md',
    'docs/superpowers/plans/2026-08-26-magic-capture-desktop-4.12-automation-triggers-plan.md',
    'src/Magic.Capture.Core/Workflows/WorkflowTriggerModels.cs',
    'src/Magic.Capture.Core/Workflows/WorkflowTriggerPolicy.cs',
    'src/Magic.Capture.App/Workflows/WorkflowTriggerStore.cs',
    'src/Magic.Capture.App/Workflows/WorkflowTriggerHistoryStore.cs',
    'src/Magic.Capture.App/Workflows/WorkflowTriggerRunner.cs',
    'src/Magic.Capture.App/Workflows/ResidentWorkflowTriggerEngine.cs',
    'src/Magic.Capture.App/Workflows/WorkflowTriggerHotkeyService.cs',
    'src/Magic.Capture.App/Workflows/WindowsTaskSchedulerService.cs',
    'src/Magic.Capture.App/Views/WorkflowTriggerManagerWindow.xaml',
    'src/Magic.Capture.App/Views/WorkflowTriggerManagerWindow.xaml.cs',
    'scripts/verify-workflow-triggers.py',
    'docs/COMMERCIAL_MODEL.md',
    'docs/RELEASE_NOTES_2.0.0.md',
    'docs/AI_PROVIDER_GUIDE.md',
    'docs/SHAREX_CLEAN_ROOM.md',
    'docs/SHAREX_CLEAN_ROOM.md',
    'docs/SHAREX_CLEAN_ROOM.md',
    'docs/SHAREX_CLEAN_ROOM.md',
    'docs/SHAREX_CLEAN_ROOM.md',
    'docs/WINDOWS_RELEASE_CHECKLIST.md',
    'docs/FEATURE_AUDIT_660.md',
    'docs/RELEASE_NOTES_3.7.0.md',
    'docs/RELEASE_NOTES_3.8.0.md',
    'docs/RELEASE_NOTES_3.9.0.md',
    'docs/superpowers/specs/2026-08-24-magic-capture-3.9-capture-backends-design.md',
    'docs/superpowers/plans/2026-08-24-magic-capture-3.9-capture-backends.md',
    'docs/RELEASE_NOTES_4.0.0.md',
    'docs/superpowers/specs/2026-08-24-magic-capture-4.0-visual-recording-design.md',
    'docs/superpowers/plans/2026-08-24-magic-capture-4.0-visual-recording.md',
    'docs/RELEASE_NOTES_4.1.0.md',
    'docs/superpowers/specs/2026-08-24-magic-capture-desktop-4.1-recording-audio-design.md',
    'docs/superpowers/plans/2026-08-24-magic-capture-desktop-4.1-recording-audio.md',
    'docs/RELEASE_NOTES_4.2.0.md',
    'docs/superpowers/specs/2026-08-24-magic-capture-desktop-4.2-webcam-pip-design.md',
    'docs/superpowers/plans/2026-08-24-magic-capture-desktop-4.2-webcam-pip.md',
    'src/Magic.Capture.Core/Recording/RecordingWebcamPolicy.cs',
    'src/Magic.Capture.App/Recording/CameraDeviceCatalog.cs',
    'src/Magic.Capture.App/Recording/RecordingWebcamSource.cs',
    'src/Magic.Capture.App/Recording/RecordingWebcamCompositor.cs',
    'tests/Magic.Capture.Core.Tests/RecordingWebcamPolicyTests.cs',
    'src/Magic.Capture.Core/Recording/RecordingAudioPolicy.cs',
    'src/Magic.Capture.App/Recording/AudioDeviceCatalog.cs',
    'src/Magic.Capture.App/Recording/BoundedPcmBuffer.cs',
    'src/Magic.Capture.App/Recording/WasapiRecordingAudioSource.cs',
    'src/Magic.Capture.App/Recording/RecordingAudioPipeline.cs',
    'tests/Magic.Capture.Core.Tests/RecordingAudioPolicyTests.cs',
    'src/Magic.Capture.Core/Recording/RecordingPolicy.cs',
    'src/Magic.Capture.App/Recording/RecordingRecoveryStore.cs',
    'src/Magic.Capture.App/Recording/RecordingTarget.cs',
    'src/Magic.Capture.App/Recording/RecordingFrameProvider.cs',
    'src/Magic.Capture.App/Recording/RecordingFrameDecoder.cs',
    'src/Magic.Capture.App/Recording/Mp4RecordingEncoder.cs',
    'src/Magic.Capture.App/Recording/RecordingSessionService.cs',
    'src/Magic.Capture.App/Recording/RecordingControlCaptureExclusion.cs',
    'tests/Magic.Capture.Core.Tests/RecordingPolicyTests.cs',
    'tests/Magic.Capture.Core.Tests/RecordingManifestPolicyTests.cs',
    'src/Magic.Capture.Core/Capture/CaptureBackendPolicy.cs',
    'src/Magic.Capture.App/Capture/ICaptureBackend.cs',
    'src/Magic.Capture.App/Capture/WindowsGraphicsCaptureBackend.cs',
    'src/Magic.Capture.App/Capture/DesktopDuplicationCaptureBackend.cs',
    'src/Magic.Capture.App/Capture/CaptureBackendRouter.cs',
    'src/Magic.Capture.App/Capture/GdiCaptureBackend.cs',
    'docs/superpowers/specs/2026-08-24-magic-capture-3.8-capture-robustness-design.md',
    'docs/superpowers/plans/2026-08-24-magic-capture-3.8-capture-robustness.md',
    'src/Magic.Capture.Core/Capture/ScrollCapturePlan.cs',
    'src/Magic.Capture.Core/Capture/DesktopPixelTopology.cs',
    'src/Magic.Capture.Core/Capture/CaptureRetryPolicy.cs',
    'src/Magic.Capture.Core/Imaging/HorizontalOverlapMatcher.cs',
    'src/Magic.Capture.App/Imaging/HorizontalImageStitcher.cs',
    'src/Magic.Capture.App/Imaging/GridImageStitcher.cs',
    'src/Magic.Capture.App/Capture/TwoDimensionalScrollCaptureService.cs',
    'src/Magic.Capture.App/Views/ScrollingCaptureModeDialog.cs',
    'docs/superpowers/specs/2026-08-24-magic-capture-desktop-3.7-data-resilience-design.md',
    'docs/superpowers/plans/2026-08-24-magic-capture-desktop-3.7-data-resilience-implementation.md',
    'docs/feature-audit/feature-backlog-660.json',
    'docs/superpowers/specs/2026-08-24-magic-capture-desktop-2.2-power-ux-design.md',
    'docs/superpowers/plans/2026-08-24-magic-capture-desktop-2.2-power-ux-implementation.md',
    'release/feature-audit-660.json',
    'release/version.json',
    'scripts/store-preflight.ps1',
    'scripts/source-release.py',
    'packaging/STORE_SUBMISSION.md',
    'README.md',
]

for rel in REQUIRED:
    if not (ROOT / rel).exists():
        ERRORS.append(f'missing required file: {rel}')

# Release metadata is the canonical human-readable version source for source bundles.
release_version_path = ROOT / 'release/version.json'
release_meta = {}
if release_version_path.exists():
    try:
        release_meta = json.loads(release_version_path.read_text(encoding='utf-8'))
    except Exception as exc:
        ERRORS.append(f'invalid release/version.json: {exc}')
    for key in ('product', 'semver', 'msixVersion', 'proOfferToken'):
        if not release_meta.get(key):
            ERRORS.append(f'release/version.json missing {key}')
    if release_meta.get('product') != 'Magic Capture Desktop':
        ERRORS.append('release/version.json product must be Magic Capture Desktop')
    if release_meta.get('proOfferToken') != 'magiccapture.desktop.pro':
        ERRORS.append('release/version.json Pro offer token mismatch')

for path in list((ROOT / 'src').rglob('*.xaml')) + list((ROOT / 'src').rglob('*.csproj')) + [ROOT / 'src/Magic.Capture.App/Package.appxmanifest']:
    if not path.exists():
        continue
    try:
        ET.parse(path)
    except Exception as exc:
        ERRORS.append(f'XML parse failed: {path.relative_to(ROOT)}: {exc}')

app_project = ROOT / 'src/Magic.Capture.App/Magic.Capture.App.csproj'
if app_project.exists():
    text = app_project.read_text(encoding='utf-8')
    expectations = {
        '<TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>': '.NET 10 Windows TFM',
        '<TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>': 'Windows 10 2004 minimum',
        'Microsoft.WindowsAppSDK.WinUI" Version="2.3.6"': 'modular WinUI component',
        'Microsoft.WindowsAppSDK.Runtime" Version="2.4.0"': 'Windows App SDK 2.4 runtime',
        'Microsoft.Windows.SDK.BuildTools.MSIX" Version="1.7.260610101"': 'MSIX build tools',
        '<WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>': 'framework-dependent Windows App SDK deployment',
        'ZXing.Net.Bindings.Windows.Compatibility" Version="0.16.14"': 'ZXing binding',
        'Vortice.Direct3D11" Version="3.8.3"': 'Vortice Direct3D11 binding',
        'Vortice.DXGI" Version="3.8.3"': 'Vortice DXGI binding',
        '<AllowUnsafeBlocks>true</AllowUnsafeBlocks>': 'unsafe interop support',
        '<UseWinUI>true</UseWinUI>': 'WinUI 3',
        '<EnableMsixTooling>true</EnableMsixTooling>': 'MSIX tooling',
        '<WindowsPackageType>MSIX</WindowsPackageType>': 'explicit packaged app mode',
        '<ImplicitUsings>enable</ImplicitUsings>': 'implicit framework usings',
        '<Nullable>enable</Nullable>': 'nullable analysis',
        '<ApplicationManifest>app.manifest</ApplicationManifest>': 'Win32 process manifest',
        '<Content Include="Assets\\SplashScreen.png" />': 'MSIX splash content',
        '<Content Include="Assets\\StoreLogo.png" />': 'MSIX Store logo content',
        '<AssemblyName>Magic.Capture.Desktop</AssemblyName>': 'Magic Capture Desktop assembly identity',
        f'<Version>{release_meta.get("semver", "2.0.0")}</Version>': 'semantic app version',
        f'<AssemblyVersion>{release_meta.get("msixVersion", "2.0.0.0")}</AssemblyVersion>': 'assembly version',
        f'<FileVersion>{release_meta.get("msixVersion", "2.0.0.0")}</FileVersion>': 'file version',
    }
    for needle, name in expectations.items():
        if needle not in text:
            ERRORS.append(f'app project does not pin expected {name}')

    forbidden_project_refs = {
        '<PackageReference Include="Microsoft.WindowsAppSDK"': 'full Windows App SDK metapackage',
        'Microsoft.WindowsAppSDK.AI': 'Windows App SDK AI component',
        'Microsoft.WindowsAppSDK.ML': 'Windows App SDK ML component',
        'Microsoft.WindowsAppSDK.Search': 'Windows App SDK Search component',
    }
    for needle, name in forbidden_project_refs.items():
        if needle in text:
            ERRORS.append(f'app project references forbidden {name}')

core_project = ROOT / 'src/Magic.Capture.Core/Magic.Capture.Core.csproj'
if core_project.exists():
    text = core_project.read_text(encoding='utf-8')
    for needle, name in {
        '<TargetFramework>net10.0</TargetFramework>': '.NET 10 core target',
        '<ImplicitUsings>enable</ImplicitUsings>': 'core implicit framework usings',
        '<Nullable>enable</Nullable>': 'core nullable analysis',
    }.items():
        if needle not in text:
            ERRORS.append(f'core project missing expected {name}')

process_manifest = ROOT / 'src/Magic.Capture.App/app.manifest'
if process_manifest.exists():
    try:
        ET.parse(process_manifest)
    except Exception as exc:
        ERRORS.append(f'XML parse failed: {process_manifest.relative_to(ROOT)}: {exc}')
    text = process_manifest.read_text(encoding='utf-8')
    if '>PerMonitorV2</dpiAwareness>' not in text:
        ERRORS.append('process manifest must declare PerMonitorV2 DPI awareness')
    if '>true</longPathAware>' not in text:
        ERRORS.append('process manifest must enable long paths')

# Branding contract: the product name is exactly "Magic Capture Desktop" in every user-facing identity.
branding_expectations = {
    ROOT / 'src/Magic.Capture.App/Package.appxmanifest': [
        '<DisplayName>Magic Capture Desktop</DisplayName>',
        '<PublisherDisplayName>Magic Capture Desktop</PublisherDisplayName>',
        'DisplayName="Magic Capture Desktop"',
    ],
    ROOT / 'src/Magic.Capture.App/Platform/TrayIconService.cs': [
        'Tip = "Magic Capture Desktop"',
        '"Open Magic Capture Desktop"',
    ],
    ROOT / 'src/Magic.Capture.App/Persistence/AppPaths.cs': ['"Magic Capture Desktop"'],
    ROOT / 'src/Magic.Capture.Core/Settings/AppSettings.cs': ['"Magic Capture Desktop_{yyyy}-{MM}-{dd}_{HH}-{mm}-{ss}"'],
    ROOT / 'src/Magic.Capture.App/Export/ExportService.cs': ['SuggestedFileName = "Magic Capture Desktop"'],
}
for path, needles in branding_expectations.items():
    if not path.exists():
        continue
    text = path.read_text(encoding='utf-8', errors='replace')
    for expected in needles:
        if expected not in text:
            ERRORS.append(f'official Magic Capture Desktop branding missing in {path.relative_to(ROOT)}: {expected}')

manifest = ROOT / 'src/Magic.Capture.App/Package.appxmanifest'
if manifest.exists():
    text = manifest.read_text(encoding='utf-8')
    if 'MinVersion="10.0.19041.0"' not in text:
        ERRORS.append('manifest does not declare Windows 10 build 19041 minimum')
    if '<rescap:Capability Name="runFullTrust"' not in text:
        ERRORS.append('manifest must declare runFullTrust for tray/hotkey/GDI desktop integration')
    if release_meta:
        expected_version = release_meta.get('msixVersion')
        if expected_version and f'Version="{expected_version}"' not in text:
            ERRORS.append(f'manifest version does not match release metadata: {expected_version}')


# Official product branding must always be written with a space in user-facing/runtime text.
# Technical C# namespaces and project identifiers intentionally use Magic.Capture.* because identifiers cannot contain spaces.
for path in list((ROOT / 'src').rglob('*')) + [ROOT / 'README.md', ROOT / 'docs/FEATURE_MATRIX.md', ROOT / 'packaging/STORE_SUBMISSION.md']:
    if not path.exists() or not path.is_file() or '.git' in path.parts:
        continue
    if path.suffix.lower() not in {'.cs', '.xaml', '.csproj', '.md', '.appxmanifest', '.manifest'}:
        continue
    text = path.read_text(encoding='utf-8', errors='replace')
    forbidden_brand = 'Magic' + 'Capture'
    if forbidden_brand in text:
        ERRORS.append(f'concatenated product name found: {path.relative_to(ROOT)}')

# Commercial architecture contract: Free forever, 168-hour Plus trial, Pro lifetime Store add-on.
commerce_checks = {
    ROOT / 'src/Magic.Capture.Core/Commerce/TrialClock.cs': ['TimeSpan.FromHours(168)'],
    ROOT / 'src/Magic.Capture.Core/Commerce/ProductTier.cs': ['Free', 'PlusTrial', 'ProLifetime'],
    ROOT / 'src/Magic.Capture.App/Commerce/StorePurchaseService.cs': ['magiccapture.desktop.pro', 'RequestPurchaseAsync', 'QueryProPriceAsync', 'price.FormattedPrice', 'price.FormattedBasePrice', 'price.IsOnSale'],
    ROOT / 'src/Magic.Capture.App/Platform/StartupService.cs': ['Magic.Capture.Desktop.Startup'],
    ROOT / 'src/Magic.Capture.App/Platform/SingleInstanceService.cs': ['Magic.Capture.Desktop.Singleton', 'Magic.Capture.Desktop.Show', 'new Mutex(true, MutexName, out _ownsMutex)'],
    ROOT / 'src/Magic.Capture.App/Package.appxmanifest': ['Category="windows.startupTask"', 'TaskId="Magic.Capture.Desktop.Startup"'],
    ROOT / 'src/Magic.Capture.Core/Settings/HotkeyGesture.cs': ['DefaultRegion', 'DefaultRepeat'],
    ROOT / 'src/Magic.Capture.App/Views/CaptureOverlayWindow.xaml': ['Text', 'Table', 'QR', 'Edit', 'Color'],
    ROOT / 'src/Magic.Capture.App/Views/CompareWindow.xaml': ['Side by side', 'Overlay', 'Difference'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': ['ProPriceText', 'Price shown by Microsoft Store'],
    ROOT / 'packaging/STORE_SUBMISSION.md': ['$29.99', '$19.99', '90 consecutive days'],
}
for path, needles in commerce_checks.items():
    if not path.exists():
        continue
    text = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in text:
            ERRORS.append(f'commercial/runtime contract missing in {path.relative_to(ROOT)}: {needle}')


# Only the current Magic Capture Desktop spec/plan may ship in the source release.
for obsolete in (
    ROOT / 'docs/superpowers/specs/2026-08-23-magic-capture-v1-design.md',
    ROOT / 'docs/superpowers/plans/2026-08-23-magic-capture-v1-implementation.md',
):
    if obsolete.exists():
        ERRORS.append(f'obsolete pre-Desktop design artifact still present: {obsolete.relative_to(ROOT)}')

# Free-result performance contract: Free runs OCR only; extended table/barcode analysis is tier-gated.
result_code = ROOT / 'src/Magic.Capture.App/Views/CaptureResultWindow.xaml.cs'
if result_code.exists():
    text = result_code.read_text(encoding='utf-8', errors='replace')
    for needle, name in {
        '_extendedRecognition = _services.Entitlements.CanUse(ProductFeature.TableExtraction);': 'tier-aware extended recognition gate',
        '_services.Ocr.RecognizeAsync': 'OCR-only Free path',
        '_services.Analysis.AnalyzeAsync': 'Plus/Pro extended analysis path',
        'TableTab.Visibility = _extendedRecognition ? Visibility.Visible : Visibility.Collapsed;': 'Free table-tab suppression',
        'BarcodeTab.Visibility = _extendedRecognition ? Visibility.Visible : Visibility.Collapsed;': 'Free barcode-tab suppression',
    }.items():
        if needle not in text:
            ERRORS.append(f'CaptureResultWindow missing {name}')

# Control Center History search must stay lightweight and use existing local metadata/OCR previews.
history_search = ROOT / 'src/Magic.Capture.Core/History/HistorySearch.cs'
main_window_xaml = ROOT / 'src/Magic.Capture.App/MainWindow.xaml'
main_window_code = ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs'
for path, needles in {
    history_search: ['public static class HistorySearch', 'StringComparison.OrdinalIgnoreCase', 'item.OcrPreview', 'item.BarcodePreview', 'GetSearchableText'],
    ROOT / 'src/Magic.Capture.Core/History/HistoryTextIndex.cs': ['public sealed class HistoryTextIndex', 'HistorySearch.GetSearchableText', 'pair.Key.Contains'],
    ROOT / 'src/Magic.Capture.App/Persistence/HistoryStore.Resilience.cs': ['SearchAsync', 'HistoryTextIndex.Build', 'HistorySearch.Matches', 'HistoryQuery.Apply'],
    main_window_xaml: ['HistorySearchBox', 'TextChanged="HistorySearchBox_TextChanged"', 'HistorySearchCountText'],
    main_window_code: ['HistoryStore.SearchAsync', 'ApplyHistoryFilterAsync', '_historyDisplayItems'],
}.items():
    if not path.exists():
        ERRORS.append(f'history search implementation missing: {path.relative_to(ROOT)}')
        continue
    text = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in text:
            ERRORS.append(f'history search contract missing in {path.relative_to(ROOT)}: {needle}')

# Release scripts must fail fast before Store packaging.
for path, needles in {
    ROOT / 'scripts/pack.ps1': ['store-preflight.ps1'],
    ROOT / 'scripts/store-preflight.ps1': ['Magic.Capture.Desktop.Dev', 'CN=Magic Capture Desktop Dev', 'release/version.json'],
    ROOT / 'scripts/source-release.py': ['verify-repo.py', 'ZIP integrity failure', 'sha256'],
}.items():
    if path.exists():
        text = path.read_text(encoding='utf-8', errors='replace')
        for needle in needles:
            if needle not in text:
                ERRORS.append(f'release hardening contract missing in {path.relative_to(ROOT)}: {needle}')

# 2.0 AI dependency and secret-boundary contract.
# Provider integrations intentionally use HttpClient, but only inside the dedicated provider layer.
app_project_text = app_project.read_text(encoding='utf-8', errors='replace') if app_project.exists() else ''
for banned_pkg in ('OpenAI', 'Anthropic', 'Google.GenerativeAI', 'Google.GenAI', 'Azure.AI.OpenAI', 'Microsoft.SemanticKernel', 'OnnxRuntime', 'Microsoft.ML'):
    if re.search(rf'<PackageReference[^>]+Include="[^"]*{re.escape(banned_pkg)}', app_project_text, re.IGNORECASE):
        ERRORS.append(f'AI provider SDK/runtime package must not be bundled in 2.0: {banned_pkg}')

for path in (ROOT / 'src').rglob('*.cs'):
    text = path.read_text(encoding='utf-8', errors='replace')
    rel = path.relative_to(ROOT).as_posix()
    if re.search(r'\bHttpClient\b', text):
        allowed_network_layers = (
            'src/Magic.Capture.App/Ai/Provider/',
            'src/Magic.Capture.App/Destinations/',
        )
        if not rel.startswith(allowed_network_layers):
            ERRORS.append(f'HttpClient must stay inside approved provider/destination layers: {rel}')
    for pattern, name in {r'NotImplementedException': 'NotImplementedException', r'\bTODO\b': 'TODO placeholder'}.items():
        if re.search(pattern, text): ERRORS.append(f'{name} found in production source: {rel}')

profile_file = ROOT / 'src/Magic.Capture.App/Ai/Provider/AiProviderProfile.cs'
if profile_file.exists():
    profile_text = profile_file.read_text(encoding='utf-8', errors='replace')
    for forbidden_secret_property in ('ApiKey', 'ApiToken', 'SecretValue', 'Password'):
        if re.search(rf'\b{forbidden_secret_property}\b\s*[,;){{]', profile_text):
            ERRORS.append(f'plaintext secret property found in AI provider profile: {forbidden_secret_property}')

vault_file = ROOT / 'src/Magic.Capture.App/Ai/Provider/WindowsPasswordVaultSecretStore.cs'
if vault_file.exists() and 'PasswordVault' not in vault_file.read_text(encoding='utf-8', errors='replace'):
    ERRORS.append('AI secret store must use Windows PasswordVault')

# AI is Pro Lifetime only: no AI feature may appear in the Plus feature set.
feature_catalog = ROOT / 'src/Magic.Capture.Core/Commerce/FeatureCatalog.cs'
if feature_catalog.exists():
    ft = feature_catalog.read_text(encoding='utf-8', errors='replace')
    plus_match = re.search(r'PlusFeatures\s*=\s*\[(.*?)\];', ft, re.S)
    plus_text = plus_match.group(1) if plus_match else ''
    for ai_feature in ('AiProviders', 'MagicActions', 'ContextStack', 'EvidenceAnchoring', 'SemanticCompare', 'CustomMagicActions', 'AiGuard', 'AiResultCache', 'MagicRecipes'):
        if ai_feature in plus_text:
            ERRORS.append(f'AI feature illegally unlocked by Plus trial: {ai_feature}')

# AI source/UX gates must be present but must not execute on the capture fast path.
ai_contract = {
    ROOT / 'src/Magic.Capture.App/Views/CaptureOverlayWindow.xaml': ['Magic · PRO', 'MagicButton_Click'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': ['AI &amp; Magic · PRO', 'AiConfigurationPanel', 'AiPayloadConfirmCheck'],
    ROOT / 'src/Magic.Capture.App/Views/MagicActionWindow.xaml': ['Run Magic Action', 'EvidenceList', 'ContextList'],
    ROOT / 'src/Magic.Capture.App/Ai/MagicActionService.cs': ['ProductFeature.MagicActions', 'AiContextPlanner.Plan', 'MagicPromptCompiler.Compile'],
    ROOT / 'src/Magic.Capture.Core/Ai/AiEndpointPolicy.cs': ['UriSchemeHttps', 'IsLoopback', 'localhost'],
    ROOT / 'src/Magic.Capture.App/Ai/Provider/AiProviderClientBase.cs': ['AiEndpointPolicy.TryValidate'],
    ROOT / 'src/Magic.Capture.App/Ai/Provider/AiProviderProfile.cs': ['uri.IsLoopback', 'uri.Host.Equals("localhost"'],
    ROOT / 'src/Magic.Capture.App/Ai/Provider/OpenAiResponsesClient.cs': ['store = false'],
    ROOT / 'src/Magic.Capture.App/Ai/MagicActionService.cs': ['promptHash', 'imagePayloadHash'],
    ROOT / 'src/Magic.Capture.Core/ScreenGraph/ScreenGraphBuilder.cs': ['TextSignalExtractor.Extract', 'ScreenGraphDocument'],
}
for path, needles in ai_contract.items():
    if not path.exists():
        ERRORS.append(f'AI 2.0 contract file missing: {path.relative_to(ROOT)}')
        continue
    text = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in text: ERRORS.append(f'AI 2.0 contract missing in {path.relative_to(ROOT)}: {needle}')

# 2.0 workflow/utility/destination architecture contract.
workflow_contract = {
    ROOT / 'src/Magic.Capture.Core/Workflows/WorkflowCatalog.cs': ['quick-copy', 'ocr-copy', 'documentation', 'data-capture', 'bug-report'],
    ROOT / 'src/Magic.Capture.App/Workflows/WorkflowExecutor.cs': ['RunMagicAction', 'CustomHttpDestination', 'ProductFeature.CustomDestinations', 'RunLocalAction', 'ApplyInitialVariables'],
    ROOT / 'src/Magic.Capture.App/Utilities/ImageUtilityService.cs': ['Beautify', 'Combine', 'Split', 'StripMetadata'],
    ROOT / 'src/Magic.Capture.App/Destinations/CustomHttpDestinationClient.cs': ['DestinationValidator.Validate', 'MaxResponseBytes', 'ReadBoundedAsync'],
    ROOT / 'src/Magic.Capture.Core/Destinations/DestinationModels.cs': ['EndpointPolicy.IsAllowed', 'AllowPrivateLanHttp'],
    ROOT / 'src/Magic.Capture.Core/Ai/AiGuard.cs': ['AiGuardFindingKind.PrivateKey', 'AiGuardFindingKind.BearerToken', 'AiGuardFindingKind.Jwt', 'RedactedPreview'],
    ROOT / 'src/Magic.Capture.App/Ai/AiResultCache.cs': ['TryGetAsync', 'PutAsync'],
    ROOT / 'src/Magic.Capture.Core/Cli/CliParser.cs': ['--capture', '--workflow', '--open', '--var', 'WorkflowVariables.IsReserved'],
    ROOT / 'src/Magic.Capture.Core/LocalActions/LocalActionModels.cs': ['LocalActionProfile', 'IsAllowedExecutableExtension', 'MaximumCapturedStreamBytes', 'LocalActionApproval'],
    ROOT / 'src/Magic.Capture.Core/LocalActions/LocalActionTemplate.cs': ['GeneratedRegex', 'References', 'Replace("$$", "$"'],
    ROOT / 'src/Magic.Capture.App/LocalActions/LocalActionRunner.cs': ['UseShellExecute = false', 'info.ArgumentList.Add', 'ComputeSha256Async', 'timeout.CancelAfter', 'ReadBoundedFileAsync', 'TryKill(process)'],
    ROOT / 'src/Magic.Capture.App/LocalActions/LocalActionApprovalStore.cs': ['IsApprovedAsync', 'ApproveAsync', 'RevokeAsync', 'Sha256'],
    ROOT / 'src/Magic.Capture.App/Package.appxmanifest': ['windows.appExecutionAlias', 'Alias="magiccapture.exe"'],
}
for path, needles in workflow_contract.items():
    if not path.exists():
        ERRORS.append(f'2.0 workflow contract file missing: {path.relative_to(ROOT)}')
        continue
    text = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in text:
            ERRORS.append(f'2.0 workflow contract missing in {path.relative_to(ROOT)}: {needle}')

# Clean-room guard: ShareX is a design reference, never a copied code dependency/namespace.
for path in (ROOT / 'src').rglob('*'):
    if not path.is_file() or path.suffix.lower() not in {'.cs', '.xaml', '.csproj'}:
        continue
    text = path.read_text(encoding='utf-8', errors='replace')
    if re.search(r'\bShareX\b', text, re.IGNORECASE):
        ERRORS.append(f'clean-room violation marker in production source: {path.relative_to(ROOT)}')

# Every XAML named event handler should exist in its code-behind.
for xaml in (ROOT / 'src/Magic.Capture.App').rglob('*.xaml'):
    codebehind = Path(str(xaml) + '.cs')
    if not codebehind.exists():
        continue
    xaml_text = xaml.read_text(encoding='utf-8')
    cs_text = codebehind.read_text(encoding='utf-8')
    handlers = set(re.findall(r'\b(?:Click|SelectionChanged|ItemClick|PointerPressed|PointerMoved|PointerReleased|PointerEntered|PointerExited|Invoked|Toggled|ValueChanged|TextChanged)="([A-Za-z_][A-Za-z0-9_]*)"', xaml_text))
    for handler in handlers:
        if not re.search(rf'\b{re.escape(handler)}\s*\(', cs_text):
            ERRORS.append(f'XAML handler {handler} missing in {codebehind.relative_to(ROOT)}')

# Pin window must meet the v1 utility-grade behavior contract.
pin_xaml = ROOT / 'src/Magic.Capture.App/Views/PinWindow.xaml'
pin_code = ROOT / 'src/Magic.Capture.App/Views/PinWindow.xaml.cs'
if pin_xaml.exists() and pin_code.exists():
    px = pin_xaml.read_text(encoding='utf-8')
    pc = pin_code.read_text(encoding='utf-8')
    for token, name in {
        'PinControls': 'hover toolbar',
        'PointerEntered="Root_PointerEntered"': 'hover reveal entry',
        'PointerExited="Root_PointerExited"': 'hover hide exit',
    }.items():
        if token not in px:
            ERRORS.append(f'PinWindow missing {name}')
    for token, name in {
        'SetBorderAndTitleBar(false, false)': 'borderless presenter',
        'NativeConstants.WmSizing': 'aspect-ratio resize hook',
        'AspectRatioResize.Constrain': 'aspect-ratio constraint',
        'IsAlwaysOnTop = true': 'always-on-top behavior',
    }.items():
        if token not in pc:
            ERRORS.append(f'PinWindow missing {name}')

# Core test suite breadth.
test_files = list((ROOT / 'tests/Magic.Capture.Core.Tests').glob('*Tests.cs'))
if len(test_files) < 31:
    ERRORS.append(f'expected at least 31 core test files, found {len(test_files)}')

# Cheap lexical guard for braces using a small C# lexer so braces inside strings/comments do not count.
def brace_balance_cs(text: str) -> tuple[int, int]:
    opens = closes = 0
    i = 0
    state = 'code'
    while i < len(text):
        ch = text[i]
        nxt = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if ch == '/' and nxt == '/': state = 'line'; i += 2; continue
            if ch == '/' and nxt == '*': state = 'block'; i += 2; continue
            if ch == '@' and nxt == '"': state = 'verbatim'; i += 2; continue
            if ch == '$' and nxt == '"': state = 'string'; i += 2; continue
            if ch == '$' and nxt == '@' and i + 2 < len(text) and text[i + 2] == '"': state = 'verbatim'; i += 3; continue
            if ch == '@' and nxt == '$' and i + 2 < len(text) and text[i + 2] == '"': state = 'verbatim'; i += 3; continue
            if ch == '"': state = 'string'; i += 1; continue
            if ch == "'": state = 'char'; i += 1; continue
            if ch == '{': opens += 1
            elif ch == '}': closes += 1
            i += 1; continue
        if state == 'line':
            if ch == '\n': state = 'code'
            i += 1; continue
        if state == 'block':
            if ch == '*' and nxt == '/': state = 'code'; i += 2
            else: i += 1
            continue
        if state == 'string':
            if ch == '\\': i += 2; continue
            if ch == '"': state = 'code'
            i += 1; continue
        if state == 'verbatim':
            if ch == '"' and nxt == '"': i += 2; continue
            if ch == '"': state = 'code'
            i += 1; continue
        if state == 'char':
            if ch == '\\': i += 2; continue
            if ch == "'": state = 'code'
            i += 1; continue
    return opens, closes

for path in (ROOT / 'src').rglob('*.cs'):
    opens, closes = brace_balance_cs(path.read_text(encoding='utf-8', errors='replace'))
    if opens != closes:
        ERRORS.append(f'unbalanced braces: {path.relative_to(ROOT)} ({opens} != {closes})')

# Documentation may legitimately mention words like TODO/TBD while defining the no-placeholder rule.
# Production source is scanned above; docs are instead required to exist and be non-trivial.
for path in (ROOT / 'docs/superpowers/specs/2026-08-23-magic-capture-desktop-v1-design.md', ROOT / 'docs/superpowers/plans/2026-08-23-magic-capture-desktop-v1-implementation.md'):
    if path.exists() and len(path.read_text(encoding='utf-8').splitlines()) < 100:
        ERRORS.append(f'design/plan unexpectedly short: {path.relative_to(ROOT)}')

# Resident-app reliability contracts added by the 2.1 foundation upgrade.
screen_graph_service = ROOT / 'src/Magic.Capture.App/Ai/ScreenGraphService.cs'
if screen_graph_service.exists():
    text = screen_graph_service.read_text(encoding='utf-8', errors='replace')
    if 'ConcurrentDictionary<Guid, Task<ScreenGraphDocument>>' in text:
        ERRORS.append('ScreenGraphService must not retain an unbounded task cache keyed only by capture id')
    for needle, name in {
        'MaxCachedGraphs': 'bounded ScreenGraph cache',
        'HashUtility.ComputeSha256(asset.PngBytes)': 'image-content-aware ScreenGraph cache key',
        'settings.PreferredOcrLanguage': 'OCR-language-aware ScreenGraph cache key',
    }.items():
        if needle not in text:
            ERRORS.append(f'ScreenGraphService missing {name}')

app_code = ROOT / 'src/Magic.Capture.App/App.xaml.cs'
if app_code.exists():
    text = app_code.read_text(encoding='utf-8', errors='replace')
    profile_match = re.search(r'internal async Task RunCaptureProfileAsync\(CaptureProfile profile\)(.*?)(?=\n    private async Task RememberRegionAsync)', text, re.S)
    if profile_match:
        body = profile_match.group(1)
        for forbidden, name in {
            'CaptureRegionFromUiAsync()': 'default region flow',
            'CaptureForegroundWindowAsync()': 'default foreground-window flow',
        }.items():
            if forbidden in body:
                ERRORS.append(f'capture profile runner delegates to {name} and can lose profile-specific options')
        for needle in ('profile.CaptureCursor', 'profile.WorkflowId', 'profile.PostCaptureAction'):
            if needle not in body:
                ERRORS.append(f'capture profile runner does not preserve {needle}')
    else:
        ERRORS.append('capture profile runner could not be inspected')

auto_scroll = ROOT / 'src/Magic.Capture.App/Capture/AutomaticScrollCaptureService.cs'
if auto_scroll.exists():
    text = auto_scroll.read_text(encoding='utf-8', errors='replace')
    for needle, name in {
        'Math.Clamp(options.EndChangedPixelPercent, 0, 100)': 'end-change threshold clamp',
        'Math.Clamp(options.WheelDelta': 'wheel delta clamp',
    }.items():
        if needle not in text:
            ERRORS.append(f'automatic scrolling capture missing {name}')

# JSON persistence must preserve cancellation and retain a recoverable previous generation.
atomic_json = ROOT / 'src/Magic.Capture.App/Persistence/AtomicJsonFile.cs'
if atomic_json.exists():
    text = atomic_json.read_text(encoding='utf-8', errors='replace')
    for required, name in {
        'path + ".bak"': 'persistent JSON backup generation',
        'TryReadBackupAsync': 'JSON backup recovery path',
        'QuarantineCorruptPrimary': 'corrupt-primary quarantine path',
        'DefaultMaximumJsonBytes': 'default JSON size budget',
        'stream.Length > maximumBytes': 'pre-deserialization JSON length guard',
    }.items():
        if required not in text:
            ERRORS.append(f'AtomicJsonFile missing {name}')
for rel in (
    'src/Magic.Capture.App/Workflows/WorkflowStore.cs',
    'src/Magic.Capture.App/Destinations/DestinationProfileStore.cs',
):
    path = ROOT / rel
    if path.exists():
        config_text = path.read_text(encoding='utf-8', errors='replace')
        if 'catch {' in config_text or 'catch\n        {' in config_text:
            ERRORS.append(f'{rel} must not silently convert configuration read failures into an empty collection')

capture_watch = ROOT / 'src/Magic.Capture.App/Capture/CaptureWatchService.cs'
if capture_watch.exists():
    text = capture_watch.read_text(encoding='utf-8', errors='replace')
    if '_compare.Compare(' in text:
        ERRORS.append('Capture Watch must not run the full image-comparison/difference pipeline on every timer tick')
    for required in ('FrameDifference.SampledChangedPercent', 'previousPixels'):
        if required not in text:
            ERRORS.append(f'Capture Watch missing lightweight change detection primitive: {required}')

settings_rules = ROOT / 'src/Magic.Capture.Core/Settings/AppSettingsRules.cs'
if not settings_rules.exists():
    ERRORS.append('runtime settings normalization rules are missing')
settings_store = ROOT / 'src/Magic.Capture.App/Persistence/SettingsStore.cs'
if settings_store.exists() and 'AppSettingsRules.NormalizeForRuntime' not in settings_store.read_text(encoding='utf-8', errors='replace'):
    ERRORS.append('SettingsStore must normalize persisted settings before exposing them to runtime services')
if settings_store.exists():
    settings_text = settings_store.read_text(encoding='utf-8', errors='replace')
    for required in ('SettingsLoadResult', 'UsedFallback', 'Warning'):
        if required not in settings_text:
            ERRORS.append(f'2.7.1 settings recovery contract missing: {required}')
    if 'catch\n        {' in settings_text or 'catch {' in settings_text:
        ERRORS.append('SettingsStore must not silently swallow arbitrary settings-load failures')
if atomic_json.exists():
    atomic_text = atomic_json.read_text(encoding='utf-8', errors='replace')
    if 'Primary JSON is missing and its backup is not readable' not in atomic_text:
        ERRORS.append('AtomicJsonFile must distinguish a corrupt backup-only state from a genuinely new file')

app_source = ROOT / 'src/Magic.Capture.App/App.xaml.cs'
if app_source.exists():
    app_text = app_source.read_text(encoding='utf-8', errors='replace')
    if 'MutateSettingsAsync' not in app_text or 'AppSettingsRules.NormalizeForRuntime(mutation(previous))' not in app_text:
        ERRORS.append('settings mutation authority must normalize the latest snapshot before persistence/runtime exposure')
    if 'Services.CaptureWatch.Dispose();' not in app_text:
        ERRORS.append('application shutdown must dispose Capture Watch')
    for required in ('Licensing initialization failed', 'History retention initialization failed', 'FatalExceptionPolicy.IsFatal'):
        if required not in app_text:
            ERRORS.append(f'2.7.1 startup/fatal-exception hardening contract missing: {required}')

ai_cache = ROOT / 'src/Magic.Capture.App/Ai/AiResultCache.cs'
if ai_cache.exists():
    cache_text = ai_cache.read_text(encoding='utf-8', errors='replace')
    if 'catch (OperationCanceledException) { throw; }' not in cache_text:
        ERRORS.append('AI result cache must not swallow cancellation')
    if 'await _gate.WaitAsync(cancellationToken);' not in cache_text or 'public async Task ClearAsync(CancellationToken cancellationToken = default)' not in cache_text:
        ERRORS.append('AI result cache clear must be serialized with reads/writes')

watch_service = ROOT / 'src/Magic.Capture.App/Capture/CaptureWatchService.cs'
if watch_service.exists() and 'Interlocked.Exchange(ref _cts, null)' not in watch_service.read_text(encoding='utf-8', errors='replace'):
    ERRORS.append('Capture Watch must release its CancellationTokenSource after completion')

history_store = ROOT / 'src/Magic.Capture.App/Persistence/HistoryStore.cs'
if history_store.exists():
    history_text = history_store.read_text(encoding='utf-8', errors='replace')
    if 'LocalPathGuard.ResolveWithinRoot' not in history_text or 'IsSafeHistoryItem' not in history_text:
        ERRORS.append('History store must constrain persisted relative paths to the History root')

single_instance = ROOT / 'src/Magic.Capture.App/Platform/SingleInstanceService.cs'
if single_instance.exists():
    ipc_text = single_instance.read_text(encoding='utf-8', errors='replace')
    if 'PipeOptions.CurrentUserOnly' not in ipc_text:
        ERRORS.append('single-instance command pipe must be restricted to the current user')
    if 'MaximumCommandPayloadChars' not in ipc_text or 'ReadBoundedPayload' not in ipc_text:
        ERRORS.append('single-instance command pipe must bound command payload size')

project_core = ROOT / 'src/Magic.Capture.Core/Projects/EditableProject.cs'
if project_core.exists():
    project_text = project_core.read_text(encoding='utf-8', errors='replace')
    for required in ('MaxAnnotationLayers', 'MaxPointsPerLayer', 'float.IsFinite(layer.Opacity)', 'ValidateMetadata'):
        if required not in project_text:
            ERRORS.append(f'editable project validation missing safety rule: {required}')

settings_store_hardening = ROOT / 'src/Magic.Capture.App/Persistence/SettingsStore.cs'
if settings_store_hardening.exists():
    settings_store_text = settings_store_hardening.read_text(encoding='utf-8', errors='replace')
    for required in ('IsPersistenceHealthy', 'TrySaveAsync', 'ResetAsync', 'PreserveForRecovery'):
        if required not in settings_store_text:
            ERRORS.append(f'2.7.1 settings recovery hardening missing: {required}')

services_source = ROOT / 'src/Magic.Capture.App/ApplicationServices.cs'
if services_source.exists():
    services_text = services_source.read_text(encoding='utf-8', errors='replace')
    if 'CommitSettingsSnapshot' not in services_text or 'AppSettingsRules.NormalizeForRuntime(settings)' not in services_text:
        ERRORS.append('resident ApplicationServices settings commit must normalize every controlled snapshot')
    if 'set => _settings' in services_text:
        ERRORS.append('resident ApplicationServices.Settings must not expose a direct setter')

app_text = app_source.read_text(encoding='utf-8', errors='replace') if app_source.exists() else ''
if 'saveFormat: profile.FileFormat' not in app_text or 'profile.FileFormat);' not in app_text:
    ERRORS.append('capture profiles must propagate their configured save format to the capture result path')

# Exact 660-feature ledger is a release contract. It prevents broad feature claims from drifting
# away from the user's numbered backlog as the codebase grows.
audit_path = ROOT / 'release/feature-audit-660.json'
if audit_path.exists():
    try:
        audit = json.loads(audit_path.read_text(encoding='utf-8'))
        audit_features = audit.get('features') or []
        audit_ids = [item.get('id') for item in audit_features]
        if audit.get('total') != 660 or len(audit_features) != 660 or audit_ids != list(range(1, 661)):
            ERRORS.append('feature audit must contain exactly IDs 1 through 660 in order')
        valid_statuses = {'Done', 'Partial', 'Foundation', 'ReleaseTest', 'Missing'}
        if any(item.get('status') not in valid_statuses for item in audit_features):
            ERRORS.append('feature audit contains an unknown status')
        computed_counts = {status: sum(1 for item in audit_features if item.get('status') == status) for status in valid_statuses}
        stored_counts = audit.get('counts') or {}
        if any(stored_counts.get(status, 0) != count for status, count in computed_counts.items()):
            ERRORS.append('feature audit status counts do not match feature rows')
        if sum(computed_counts.values()) != 660:
            ERRORS.append('feature audit status counts must total 660')
    except Exception as exc:
        ERRORS.append(f'invalid release/feature-audit-660.json: {exc}')

# 2.2 power/UX source contracts. These are deliberately static contracts; Windows runtime
# validation remains in WINDOWS_RELEASE_CHECKLIST.md.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/Annotation/AnnotationDocumentEditor.cs': ['DuplicateMany', 'Group(', 'Ungroup(', 'MatchSize(', 'Distribute(', 'SetStyle('],
    ROOT / 'src/Magic.Capture.App/Views/AnnotationWindow.xaml': ['SelectionMode="Multiple"', 'LayerCopy_Click', 'LayerGroup_Click', 'ApplyLayerStyle_Click'],
    ROOT / 'src/Magic.Capture.Core/Imaging/ImageDifference.cs': ['IgnoreFullyTransparent', 'MeanRedDifference', 'ChangedPixelPercent'],
    ROOT / 'src/Magic.Capture.App/Views/CompareWindow.xaml': ['Heatmap', 'Mask', 'Blink', 'Triptych', 'ThresholdSlider'],
    ROOT / 'src/Magic.Capture.Core/Privacy/SensitiveDataDetector.cs': ['IsValidIpv6', 'SensitiveWords', 'CustomPatterns'],
    ROOT / 'src/Magic.Capture.App/Privacy/CaptureRedactionService.cs': ['RedactionPlanner.Create', 'SensitiveDataDetector.Scan'],
    ROOT / 'src/Magic.Capture.App/Views/CaptureOverlayWindow.xaml': ['HandleNorthWest', 'HandleSouthEast', 'Reselect_Click', 'OverlayMode_Click'],
    ROOT / 'src/Magic.Capture.App/Views/CaptureOverlayWindow.xaml.cs': ['SelectionHandleMath.Resize', 'desktopX', 'SetHandlesVisible'],
}.items():
    if not path.exists():
        ERRORS.append(f'2.2 source contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'2.2 source contract missing in {path.relative_to(ROOT)}: {needle}')

# 2.3 Library + Pin source contracts.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/History/HistoryQuery.cs': ['HistoryQueryOptions', 'FileSizeDescending', 'MaximumResults'],
    ROOT / 'src/Magic.Capture.App/Persistence/HistoryStore.cs': ['DeleteManyAsync', 'AddTagsAsync', '_sessionId', 'TryDeletePrimaryFile'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': ['HistoryFilters_Click', 'HistoryBatchExport_Click', 'HistoryImport_Click'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs': ['ApplyHistoryFilterAsync', 'HistoryBatchDelete_Click', 'HistoryBatchTag_Click', 'FolderPicker', 'CaptureSizePresets.BuiltIn'],
    ROOT / 'src/Magic.Capture.App/Views/PinWindow.xaml': ['ZoomOut_Click', 'ActualSize_Click', 'Copy_Click', 'Save_Click', 'Edit_Click'],
    ROOT / 'src/Magic.Capture.App/Views/PinWindow.xaml.cs': ['SetFitView', 'SetZoom', 'SetOpacityAndPersistAsync', 'SetClickThrough(!_clickThrough)'],
    ROOT / 'src/Magic.Capture.Core/Capture/CaptureSizePresets.cs': ['720p', '1080p', '1440p', '4k', 'social-portrait'],
}.items():
    if not path.exists():
        ERRORS.append(f'2.3 source contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'2.3 source contract missing in {path.relative_to(ROOT)}: {needle}')

# 2.4 capture-engine source contracts: on-demand desktop targeting, precision overlay and
# resilient scrolling capture. These contracts intentionally avoid requiring background watchers.
for path, needles in {
    ROOT / 'src/Magic.Capture.App/Capture/WindowCaptureService.cs': ['ListCapturableWindows', 'CaptureWindow(WindowCaptureTarget', 'MaximumCatalogWindows'],
    ROOT / 'src/Magic.Capture.App/Capture/MonitorService.cs': ['ListMonitors()', 'EnumDisplayMonitors'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs': ['CaptureWindowMenu_Click', 'CaptureMonitorMenu_Click', 'SelectionMode = ListViewSelectionMode.Multiple'],
    ROOT / 'src/Magic.Capture.App/App.xaml.cs': ['CaptureWindowTargetsAsync', 'CaptureMonitorTargetAsync', 'HideMainWindow();'],
    ROOT / 'src/Magic.Capture.App/Views/CaptureOverlayWindow.xaml': ['LoupeViewport', 'SnapRectangle', 'SnapMode_Click'],
    ROOT / 'src/Magic.Capture.App/Views/CaptureOverlayWindow.xaml.cs': ['CaptureSnapRules.SelectSmallestContaining', 'UpdateLoupe', '_monitor.Bounds.X + px'],
    ROOT / 'src/Magic.Capture.Core/Imaging/StableEdgeBandDetector.cs': ['MinimumGlobalChangedPercent', 'TopRows', 'BottomRows'],
    ROOT / 'src/Magic.Capture.Core/Imaging/VerticalOverlapMatcher.cs': ['FindTrimmed(', 'upperTopRows', 'lowerBottomRows'],
    ROOT / 'src/Magic.Capture.App/Imaging/VerticalImageStitcher.cs': ['StitchFrameTrim', 'FindPairOverlap', 'normalizedTrims'],
    ROOT / 'src/Magic.Capture.App/Capture/AutomaticScrollCaptureService.cs': ['DynamicProbeMilliseconds', 'Alignment retry', 'StableEdgeBandDetector.Detect', 'FindPairOverlap', 'StickyTopRowsRemoved'],
}.items():
    if not path.exists():
        ERRORS.append(f'2.4 source contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'2.4 source contract missing in {path.relative_to(ROOT)}: {needle}')


# 2.5 output/optimization/local-utility source contracts. All of these paths are on-demand and
# must not add resident polling or codec/model background services.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/Export/PdfImageDocumentWriter.cs': ['MaximumPages', 'MaximumJpegPayloadBytes', 'Write(IReadOnlyList<PdfJpegPage> pages)'],
    ROOT / 'src/Magic.Capture.App/Export/PdfExportService.cs': ['CreateContactSheet', 'PdfImageDocumentSession'],
    ROOT / 'src/Magic.Capture.App/Utilities/ImageOptimizationService.cs': ['CompressJpegToTarget', 'OptimizePngLossless', 'OptimizePngLossy', 'SearchQuality'],
    ROOT / 'src/Magic.Capture.Core/Export/ImageOptimizationPolicy.cs': ['TargetBytes', 'ResizeScale', 'Normalize()'],
    ROOT / 'src/Magic.Capture.App/Utilities/BarcodeGeneratorService.cs': ['GenerateQr', 'GenerateCode128'],
    ROOT / 'src/Magic.Capture.Core/Imaging/PixelStatistics.cs': ['ComputeBgra', 'OpaquePixelPercent'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': ['UtilityPdf_Click', 'UtilityMultiPdf_Click', 'UtilityContactSheetPdf_Click', 'UtilityOptimizeJpeg_Click', 'UtilityBatchOptimize_Click', 'UtilityQrGenerator_Click', 'UtilityPixelStatistics_Click', 'UtilityExternalEditor_Click'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs': ['Base64ClipboardPolicy.ValidateSourceLength', 'data:image/png;base64,', 'CopyFileAsync', 'BuildDirectoryIndex', 'HashUtility.ComputeFileSha256Async', 'ProcessStartInfo', 'start.ArgumentList.Add(imagePath)'],
    ROOT / 'src/Magic.Capture.App/Views/MonitorTestWindow.xaml.cs': ['Gradient_Click', 'ColorBars_Click', 'Grid_Click'],
}.items():
    if not path.exists():
        ERRORS.append(f'2.5 source contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'2.5 source contract missing in {path.relative_to(ROOT)}: {needle}')


# 2.6 image-effect pipeline source contracts. Effects decode/encode once per image and run only
# when explicitly invoked from Utilities; no resident rendering worker is introduced.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/Imaging/ImageEffectPipeline.cs': ['ImageEffectKind', 'ImageEffectPresets', 'Take(32)', 'Posterize', 'Threshold'],
    ROOT / 'src/Magic.Capture.App/Utilities/ImageEffectPipelineService.cs': ['BitmapPixelBuffer.ReadBgra', 'foreach (var step in pipeline.Steps)', 'ImageEffectKind.Contrast', 'ImageEffectKind.Gamma', 'ImageEffectKind.Sepia'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': ['UtilityEffectPipeline_Click'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs': ['ImageEffectPresets.BuiltIn', 'new ImageEffectPipeline', 'UtilityEffectPipelineBatch'],
}.items():
    if not path.exists():
        ERRORS.append(f'2.6 source contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'2.6 source contract missing in {path.relative_to(ROOT)}: {needle}')


# 2.7 annotation-tool source contracts. New tools remain non-destructive annotation layers and
# therefore inherit grouping/z-order/project persistence instead of flattening at creation time.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/Annotation/AnnotationModels.cs': ['SpeechBalloon', 'StepNumber', 'CursorStamp', 'Magnify', 'CurvedArrow', 'Bracket'],
    ROOT / 'src/Magic.Capture.Core/Annotation/AnnotationStepLabels.cs': ['Alpha(', 'Roman(', 'Number('],
    ROOT / 'src/Magic.Capture.App/Views/AnnotationWindow.xaml': ['Tag="SpeechBalloon"', 'Tag="StepAlpha"', 'Tag="Magnify"', 'Tag="CurvedArrow"'],
    ROOT / 'src/Magic.Capture.App/Views/AnnotationWindow.xaml.cs': ['AnnotationStepLabels.Alpha', 'AnnotationStepLabels.Roman', 'AnnotationKind.SpeechBalloon', 'AnnotationKind.Emoji'],
    ROOT / 'src/Magic.Capture.App/Imaging/AnnotationRenderer.cs': ['DrawSpeechBalloon', 'DrawCallout', 'DrawCursorStamp', 'RenderMagnify', 'RenderSpotlight', 'DrawBezier'],
}.items():
    if not path.exists():
        ERRORS.append(f'2.7 source contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'2.7 source contract missing in {path.relative_to(ROOT)}: {needle}')


# 2.7.1 hardening contracts. This wave intentionally does not increase the 660-feature Done
# count; it locks correctness, cancellation, bounded allocation and crash-consistency invariants.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/Capture/CaptureWatchTriggerPolicy.cs': ['if (!hasBaseline) return new CaptureWatchDecision(false, true);', 'double.IsFinite'],
    ROOT / 'src/Magic.Capture.Core/Utilities/GeneratedCodeInputPolicy.cs': ['MaximumQrUtf8Bytes = 2_048', 'MaximumCode128Characters = 512'],
    ROOT / 'src/Magic.Capture.Core/Utilities/Base64ClipboardPolicy.cs': ['MaximumOutputCharacters', 'ComputeBase64CharacterCount', 'ValidateSourceLength'],
    ROOT / 'src/Magic.Capture.Core/Storage/LocalConfigurationLimits.cs': ['MaximumCustomWorkflows', 'MaximumDestinations', 'MaximumMagicActions', 'MaximumMagicRecipes', 'MaximumAiProviderProfiles', 'ValidateCount'],
    ROOT / 'src/Magic.Capture.Core/Workflows/WorkflowValidator.cs': ['workflow.Name.Length > 120', '(workflow.Description ?? string.Empty).Length > 2_000', 'step.Argument is { Length: > 4_096 }', 'step.Options.Count > 32'],
    ROOT / 'src/Magic.Capture.Core/Ai/MagicRecipeValidator.cs': ['recipe.Name.Length > 120', 'step.Reference.Length > 512', 'step.Options.Count > 16'],
    ROOT / 'src/Magic.Capture.Core/Ai/MagicActionValidator.cs': ['action.Category.Length > 120', '!Enum.IsDefined(action.VisionMode)', '!Enum.IsDefined(action.OutputKind)'],
    ROOT / 'src/Magic.Capture.Core/Destinations/DestinationModels.cs': ['destination.Headers.Count > 64', 'destination.Query.Count > 64', 'destination.BodyTemplate is { Length: > 65_536 }', 'destination.Endpoint.OriginalString.Length > 2_048'],
    ROOT / 'src/Magic.Capture.Core/Platform/CredentialVaultErrorPolicy.cs': ['ElementNotFoundHResult', '0x80070490', 'IsElementNotFound'],
    ROOT / 'src/Magic.Capture.Core/Imaging/ImageWorkloadLimits.cs': ['MaximumPixelProcessingPixelCount', 'MaximumComparePixelCount', 'MaximumResidentSelectionEncodedBytes'],
    ROOT / 'src/Magic.Capture.Core/Imaging/BitmapStridePolicy.cs': ['checked(row * stride)'],
    ROOT / 'src/Magic.Capture.Core/Imaging/TranslationAlignment.cs': ['EvaluatedOffsetCount', 'overlapPixels * 100 < pixelCount * 70', 'phaseCount'],
    ROOT / 'src/Magic.Capture.Core/Imaging/BgraTranslation.cs': ['TranslateInPlace', 'Array.Copy', 'Array.Clear'],
    ROOT / 'src/Magic.Capture.Core/History/HistoryStoragePathPolicy.cs': ['IsExpectedPrimary', 'IsExpectedThumbnail'],
    ROOT / 'src/Magic.Capture.Core/History/HistoryThumbnailPolicy.cs': ['MaximumPreGeneratedSourcePixels', 'ShouldPreGenerate'],
    ROOT / 'src/Magic.Capture.Core/Commerce/TrialStatePolicy.cs': ['IsValidPersisted', 'CurrentSchemaVersion', 'LastSeenUtc < state.StartedUtc'],
    ROOT / 'src/Magic.Capture.Core/Projects/EditableProjectArchivePolicy.cs': ['ValidateArchiveLength', 'entries.Count != 2', 'MaximumManifestBytes', 'MaximumBaseImageBytes', 'ValidateBaseImageLength'],
    ROOT / 'src/Magic.Capture.Core/Export/PdfImageDocumentWriter.cs': ['PdfImageDocumentSession', 'MaximumJpegPayloadBytes = 96L * 1024 * 1024', 'AddPage', 'Complete()'],
    ROOT / 'src/Magic.Capture.Core/Projects/EditableProject.cs': ['MaxValidationErrors', 'MaxOcrDocumentTextLength', 'MaxTableRows', 'MaxScreenGraphNodes', 'ValidateOcr', 'ValidateTable', 'ValidateScreenGraph'],
    ROOT / 'src/Magic.Capture.App/Imaging/BitmapPixelBuffer.cs': ['BitmapStridePolicy.RowOffset', 'ValidatePixelProcessingDimensions', 'ReadBgraCanvas'],
    ROOT / 'src/Magic.Capture.App/Imaging/ImageFileReader.cs': ['ValidateEncodedLength(stream.Length)', 'BoundedStreamReader.ReadExactAsync'],
    ROOT / 'src/Magic.Capture.App/Imaging/BoundedStreamReader.cs': ['GC.AllocateUninitializedArray', 'expectedLength > maximumLength', 'EndOfStreamException'],
    ROOT / 'src/Magic.Capture.App/Persistence/AtomicJsonFile.cs': ['DefaultMaximumJsonBytes', 'stream.Length > maximumBytes', 'CreateFallbackBackupOrThrow', 'JSON root must not be null'],
    ROOT / 'src/Magic.Capture.App/Commerce/TrialStateStore.cs': ['stateFilesExist', 'TrialStatePolicy.IsValidPersisted', 'throw new InvalidDataException'],
    ROOT / 'src/Magic.Capture.App/Ai/AiResultCache.cs': ['PriorityQueue<FileInfo, long>', 'MaximumFilesScannedPerPrune', 'MaximumEntryJsonBytes', 'TryPutAsync', 'TryDeleteCachePath', 'path + ".bak"', 'LocalLog'],
    ROOT / 'src/Magic.Capture.App/Imaging/BitmapCodec.cs': ['DecodeForPixelProcessing', 'DecodeForCompare', 'ValidatePixelProcessingDimensions(bitmap.Width, bitmap.Height)'],
    ROOT / 'src/Magic.Capture.App/Capture/CaptureWatchService.cs': ['CaptureWatchTriggerPolicy.Decide', 'Interlocked.Exchange(ref _cts, null)', '_disposed'],
    ROOT / 'src/Magic.Capture.App/Imaging/ImageCompareService.cs': ['DecodeForCompare', 'cancellationToken', 'var mapBuffer = new byte[pixelsA.Length]', 'ReadBgraCanvas', 'BgraTranslation.TranslateInPlace'],
    ROOT / 'src/Magic.Capture.App/Views/CompareWindow.xaml.cs': ['_recomputeCts', 'previous?.Cancel()', 'Interlocked.CompareExchange(ref _recomputeCts, null, cts);'],
    ROOT / 'src/Magic.Capture.App/Persistence/EditableProjectService.cs': ['EditableProjectArchivePolicy.ValidateArchiveLength', 'EditableProjectArchivePolicy.ValidateEntries', 'ValidateBaseImageLength', 'BoundedStreamReader.ReadExactAsync'],
    ROOT / 'src/Magic.Capture.App/Persistence/HistoryStore.cs': ['HistoryPendingAddFile', 'RecoverPendingAddUnsafeAsync', 'NormalizeLoadedHistoryItem', 'HistoryStoragePathPolicy.IsExpectedPrimary', 'catch (InvalidDataException)', 'QuarantineCorruptIndex', 'MaximumHistoryIndexJsonBytes'],
    ROOT / 'src/Magic.Capture.App/Imaging/PixelBufferEffects.cs': ['var horizontal = new byte[source.Length]', 'return source;'],
    ROOT / 'src/Magic.Capture.App/Utilities/ImageUtilityService.cs': ['PngDimensions.TryRead', 'images.Count > 128', 'ImageWorkloadLimits'],
    ROOT / 'src/Magic.Capture.App/Export/PdfExportService.cs': ['PdfImageDocumentSession', 'writer.AddPage', 'writer.Complete', 'CreateFromFilesAsync', 'ImageFileReader.ReadAsync'],
    ROOT / 'src/Magic.Capture.App/Ai/Provider/WindowsPasswordVaultSecretStore.cs': ['CredentialVaultErrorPolicy.IsElementNotFound', 'catch (Exception ex) when'],
    ROOT / 'src/Magic.Capture.App/Workflows/WorkflowStore.cs': ['MaximumCustomWorkflows', 'MaximumWorkflowJsonBytes', '_writeEnabled', 'Duplicate workflow id'],
    ROOT / 'src/Magic.Capture.App/Destinations/DestinationProfileStore.cs': ['MaximumDestinations', 'MaximumDestinationJsonBytes', '_writeEnabled', 'Duplicate destination id'],
    ROOT / 'src/Magic.Capture.App/Ai/MagicActionStore.cs': ['MaximumMagicActions', 'MaximumMagicActionJsonBytes', '_writeEnabled', 'Duplicate Magic Action id'],
    ROOT / 'src/Magic.Capture.App/Ai/MagicRecipeStore.cs': ['MaximumMagicRecipes', 'MaximumMagicRecipeJsonBytes', '_writeEnabled', 'Duplicate Magic Recipe id'],
    ROOT / 'src/Magic.Capture.App/Ai/Provider/AiProviderProfileStore.cs': ['MaximumAiProviderProfiles', 'MaximumAiProviderJsonBytes', '_writeEnabled', 'ValidateState'],
    ROOT / 'src/Magic.Capture.App/Destinations/WindowsDestinationSecretStore.cs': ['CredentialVaultErrorPolicy.IsElementNotFound', 'catch (Exception ex) when'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs': ['_destinationsLoadHealthy', 'Destination storage is not safely loaded', '_historyRefreshCts', 'previous?.Cancel()', 'History could not be refreshed. Keeping the previous list.'],
    ROOT / 'src/Magic.Capture.App/Persistence/LocalLog.cs': ['MaximumLogBytes = 8L * 1024 * 1024', 'RotateIfNeeded', 'IsExpectedLogFailure'],
    ROOT / 'src/Magic.Capture.App/Platform/SingleInstanceService.cs': ['FatalExceptionPolicy.IsFatal', 'catch (JsonException)', 'catch (InvalidDataException)', 'catch (TimeoutException)'],
    ROOT / 'src/Magic.Capture.Core/Platform/ClipboardPreviewPolicy.cs': ['MaximumTextPreviewCharacters = 16_000', 'BoundedCharacterCount'],
    ROOT / 'src/Magic.Capture.Core/Ai/AiModelListPolicy.cs': ['MaximumModels = 512', 'MaximumModelIdCharacters = 256', 'Accept'],
    ROOT / 'src/Magic.Capture.Core/Platform/FatalExceptionPolicy.cs': ['IsFatal', 'OutOfMemoryException', 'AccessViolationException', 'SEHException'],
    ROOT / 'src/Magic.Capture.Core/Capture/UiAutomationSnapshot.cs': ['MaximumNodes = 384', 'FindSnapTarget', 'ProjectSnapTargets', 'ProjectForCapture'],
    ROOT / 'src/Magic.Capture.Core/Capture/CaptureSelectionGeometry.cs': ['CaptureSelectionKind', 'MultiRegionOutputMode', 'MaximumPathPoints = 2_048', 'MaximumRegions = 16', 'TryCreateBox', 'TryCreatePath', 'TryCreateMultiRegion', 'HasNonZeroArea'],
    ROOT / 'src/Magic.Capture.App/Ai/Provider/AiProviderClientBase.cs': ['CollectModelNames', 'AiModelListPolicy.MaximumModels'],
    ROOT / 'src/Magic.Capture.App/Platform/ClipboardTextPreviewReader.cs': ['OpenClipboard', 'GetClipboardData', 'GlobalSize', 'GlobalLock', 'CloseClipboard'],
    ROOT / 'src/Magic.Capture.Core/Capture/CaptureSelectionOutputPolicy.cs': ['MaximumSeparateRegionPixels', 'ValidateSeparateRegions', 'MaximumRegions'],
    ROOT / 'src/Magic.Capture.App/Imaging/CaptureSelectionImageRenderer.cs': ['CaptureSelectionKind.Ellipse', 'CaptureSelectionKind.Polygon', 'CaptureSelectionKind.Freehand', 'CaptureSelectionKind.MultiRegion', 'RenderSeparateRegions', 'CaptureSelectionOutputPolicy.ValidateSeparateRegions', 'ImageWorkloadLimits.ValidateResidentSelectionBytes', 'graphics.SetClip'],
    ROOT / 'src/Magic.Capture.App/Capture/CaptureCoordinator.cs': ['IReadOnlyList<CaptureAsset> Assets', 'SelectionBounds', 'CaptureSelectionImageRenderer.Render(frozen.PngBytes, selection.Geometry)', 'CaptureSelectionImageRenderer.RenderSeparateRegions', 'selection.MultiRegionOutput', 'ToDesktopBounds', 'bool rectangularOnly = false', 'snapBounds, rectangularOnly'],
    ROOT / 'src/Magic.Capture.App/Views/CaptureOverlayWindow.xaml.cs': ['_rectangularOnly', 'kind != CaptureSelectionKind.Rectangle', 'EllipseShapeButton.Visibility = _rectangularOnly', 'MultiRegionOutputMode.SeparateImages', 'MultiRegionOutput_Click', 'supports Open, Save, or Workflow'],
    ROOT / 'src/Magic.Capture.App/Export/ExportService.cs': ['PickImageOutputFolderAsync', 'SaveImageToFolderAsync', 'CreationCollisionOption.GenerateUniqueName'],
    ROOT / 'src/Magic.Capture.App/App.xaml.cs': ['HandleCaptureRequestAsync', 'SaveSeparateCaptureAssetsAsync', 'Captured {result.Assets.Count} separate regions into History.', 'Auto-copy is skipped for separate multi-region output'],
}.items():
    if not path.exists():
        ERRORS.append(f'2.7.1 hardening contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'2.7.1 hardening contract missing in {path.relative_to(ROOT)}: {needle}')

app_cs_text = '\n'.join(path.read_text(encoding='utf-8', errors='replace') for path in (ROOT / 'src/Magic.Capture.App').rglob('*.cs'))
for forbidden in ('File.ReadAllBytes(', 'File.ReadAllBytesAsync('):
    if forbidden in app_cs_text:
        ERRORS.append(f'2.7.1 hardening forbids unbounded synchronous/pre-allocation image reads in App source: {forbidden}')

source_text_all = '\n'.join(path.read_text(encoding='utf-8', errors='replace') for path in (ROOT / 'src').rglob('*.cs'))
if 'catch {' in source_text_all:
    ERRORS.append('2.7.1 hardening forbids bare catch blocks in production source')

main_window_text = (ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs').read_text(encoding='utf-8', errors='replace')
if 'Directory.EnumerateFileSystemEntries(folder).OrderBy' in main_window_text or 'Directory.EnumerateFileSystemEntries(folder).ToArray' in main_window_text:
    ERRORS.append('2.7.1 hardening forbids materializing a whole directory before the Directory Index entry limit is enforced')

# 2.9 UI Automation Capture Intelligence contracts. UIA is acquired before overlay activation,
# remains bounded/on-demand, strips password values at the Core boundary, and merges spatial OCR
# evidence without introducing resident polling.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/Capture/UiAutomationSnapshot.cs': [
        'MaximumNodes = 384', 'MaximumDepth = 10', 'MaximumTopLevelWindows = 12',
        'candidate.IsPassword == true ? null', 'FindSnapTarget', 'ProjectForCapture'],
    ROOT / 'src/Magic.Capture.Core/ScreenGraph/UiAutomationOcrCorrelation.cs': [
        'MaximumIndexedWords = 4_096', 'MaximumEvidenceWordsPerNode = 16',
        'MaximumEvidenceTextLength = 512', 'ContainerTypes', 'claimedWords'],
    ROOT / 'src/Magic.Capture.Core/ScreenGraph/ScreenGraphBuilder.cs': [
        'UiAutomationOcrCorrelation.Correlate', 'ocrText', 'ocrWordIds', 'ocrWordCount'],
    ROOT / 'src/Magic.Capture.Core/Capture/CaptureSnapRules.cs': [
        'SnapEdges(', 'NearestEdge', 'threshold = 8'],
    ROOT / 'src/Magic.Capture.App/Capture/UiAutomationSnapshotService.cs': [
        'MaximumForegroundWaitMilliseconds = 260', 'MaximumTraversalMilliseconds = 900',
        'RunMtaAsync', 'GetControlViewCondition', 'SetTreeFilter', 'IsPasswordProperty'],
    ROOT / 'src/Magic.Capture.App/Platform/Native/UiAutomationInterop.cs': [
        'IUiAutomationNative', 'CreateCacheRequest', 'GetControlViewCondition', 'IUiAutomationElementNative'],
    ROOT / 'src/Magic.Capture.App/Capture/CaptureCoordinator.cs': [
        '_uiAutomation.CaptureForMonitorAsync', 'UiAutomationSnapshotRules.ProjectSnapTargets',
        'UiAutomationSnapshotRules.ProjectForCapture', 'var smartSnapTargets = windowTargets', '.Concat(uiAutomationTargets)'],
    ROOT / 'src/Magic.Capture.App/Views/CaptureOverlayWindow.xaml.cs': [
        '_controlSnapTargets', '_edgeSnapBounds', 'CaptureSnapRules.SnapEdges',
        'UiAutomationSnapshotRules.FindSnapTarget(_controlSnapTargets'],
    ROOT / 'src/Magic.Capture.App/Capture/CaptureAsset.cs': [
        'IReadOnlyList<ScreenUiAutomationNode>? UiAutomationNodes',
        'width == Width && height == Height ? UiAutomationNodes : null'],
}.items():
    if not path.exists():
        ERRORS.append(f'2.9 UIA contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'2.9 UIA contract missing in {path.relative_to(ROOT)}: {needle}')

# 3.0 OCR + Table Intelligence contracts. OCR interaction is bounded and on-demand;
# table schema/type inference remains deterministic/local and spreadsheet export has an explicit
# formula-safe path instead of silently treating OCR text as trusted Excel formulas.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/Ocr/OcrSpatialIndex.cs': [
        'MaximumWords = 8_192', 'MaximumLines = 2_048', 'MaximumSearchMatches = 256',
        'MaximumBlocks = 512', 'FindWord(', 'FindLine(', 'FindBlock(', 'SearchDetailed(', 'IsTruncated'],
    ROOT / 'src/Magic.Capture.Core/Ocr/OcrTextReconstruction.cs': [
        'OcrTextReconstructionMode', 'BuildLayout', 'BuildCode', 'MaximumOutputCharacters = 1_000_000'],
    ROOT / 'src/Magic.Capture.Core/Tables/TableCellInference.cs': [
        'MaximumInspectedCells = 20_000', 'MaximumAnomalies = 256', 'DetectHeader',
        'TableCellKind.Integer', 'TableCellKind.Date', 'TableCellKind.Currency', 'TableCellKind.Percent'],
    ROOT / 'src/Magic.Capture.Core/Tables/TableExtractor.cs': [
        'MaximumInputWords = OcrSpatialIndex.MaximumWords', 'MaximumOutputRows = OcrSpatialIndex.MaximumLines',
        'MaximumOutputColumns = 512', 'MaximumCellCharacters = 4_096', 'Take(MaximumInputWords)'],
    ROOT / 'src/Magic.Capture.Core/Tables/TableSerializers.cs': [
        'TableDelimitedOptions', 'TableNumberLocaleMode', 'ExcelSafeText', 'ToExcelFriendlyTsv',
        'MaximumInputCharacters = 500_000', 'MaximumOutputCharacters = 2_000_000',
        'ValidateInput(table)', 'IsFormulaLikeText', 'EscapeDelimited'],
    ROOT / 'src/Magic.Capture.App/Views/CaptureResultWindow.xaml': [
        'Search OCR on screenshot', 'OcrHitModeCombo', 'Content="Block"', 'OcrResultLanguageCombo',
        'Windows language packs…', 'Excel-safe TSV', 'TableLocaleCombo', 'TableDiagnosticsText'],
    ROOT / 'src/Magic.Capture.App/Views/CaptureResultWindow.xaml.cs': [
        'OcrSpatialIndex.Create', 'FindWord(point)', 'FindLine(point)', 'FindBlock(point)',
        'UpdateOcrSearchHighlights', 'SearchDetailed(OcrSearchBox.Text)', 'OcrTextReconstruction.Build', 'CreateLinkedTokenSource',
        'ms-settings:regionlanguage', 'TableCellInference.Infer', 'TableDelimitedOptions',
        'CopyTableButton.IsEnabled = false', 'SaveTableButton.IsEnabled = false', 'ExpectedKind}→{anomaly.ActualKind',
        'FatalExceptionPolicy.IsFatal'],
}.items():
    if not path.exists():
        ERRORS.append(f'3.0 OCR/table contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'3.0 OCR/table contract missing in {path.relative_to(ROOT)}: {needle}')


# 3.1 Table Workspace contracts. Editing remains bounded/paged; XLSX is local inline-string
# output and compare input is file-size checked before text allocation.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/Tables/EditableTableDocument.cs': [
        'MaximumRows = 2_048', 'MaximumColumns = 128', 'MaximumCells = 100_000',
        'MaximumMerges = 2_048', 'WithCellValue', 'TableDocumentOperations',
        'InsertRow(', 'DeleteRow(', 'InsertColumn(', 'DeleteColumn(', 'CopySelectionTsv',
        'MaximumCopiedCharacters = 2_500_000'],
    ROOT / 'src/Magic.Capture.Core/Tables/DelimitedTableParser.cs': [
        'MaximumInputCharacters = 2_000_000', 'MaximumChanges = 1_000',
        'DelimitedTableParser', 'TableDiffEngine', 'IsTruncated'],
    ROOT / 'src/Magic.Capture.Core/Tables/TableXlsxWriter.cs': [
        'MaximumOutputBytes = 64L * 1024 * 1024', 'inlineStr', 'mergeCells',
        'FixedTimestamp', 'ZipArchiveMode.Create'],
    ROOT / 'src/Magic.Capture.App/Views/TableWorkspaceWindow.xaml': [
        'Extend selection', 'Export XLSX', 'Compare CSV/TSV…',
        'TableRowsRepeater', 'CellEditorTextBox', 'DiffOutputTextBox', 'TableCell_Loaded'],
    ROOT / 'src/Magic.Capture.App/Views/TableWorkspaceWindow.xaml.cs': [
        'PageRows = 64', 'PageColumns = 16', 'MaximumCompareFileBytes = 2UL * 1024 * 1024',
        'GetBasicPropertiesAsync', 'TableDocumentOperations.Merge', 'TableXlsxWriter.Write',
        'DelimitedTableParser.Parse', 'TableDiffEngine.Compare', 'MaximumUndoStates = 20',
        '_visibleCellButtons', 'UpdateVisibleSelectionStyles'],
    ROOT / 'src/Magic.Capture.App/Views/CaptureResultWindow.xaml': [
        'EditTableButton', 'OpenTableWorkspace_Click'],
    ROOT / 'src/Magic.Capture.App/Views/CaptureResultWindow.xaml.cs': [
        'new TableWorkspaceWindow(table, _services)', 'TrackChildWindow(window)'],
}.items():
    if not path.exists():
        ERRORS.append(f'3.1 table-workspace contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'3.1 table-workspace contract missing in {path.relative_to(ROOT)}: {needle}')


# 3.2 Image Effects 2.0 contracts. Effects stay bounded/on-demand, packs are data-only,
# and geometry/compositing operations validate output workloads before allocation.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/Imaging/ImageEffectPipeline.cs': [
        'ImageEffectKind.Hue', 'ImageEffectKind.Vibrance', 'ImageEffectKind.ColorBalance',
        'ImageEffectKind.Sharpen', 'ImageEffectKind.NoiseReduction', 'ImageEffectKind.EdgeDetection',
        'ImageEffectKind.Mosaic', 'SecondaryAmount', 'TertiaryAmount'],
    ROOT / 'src/Magic.Capture.Core/Imaging/ImageEffectPackSerializer.cs': [
        'MaximumJsonBytes = 64 * 1024', 'SchemaVersion = 1', 'Serialize(', 'Deserialize(', 'InvalidDataException'],
    ROOT / 'src/Magic.Capture.App/Utilities/ImageEffectPipelineService.cs': [
        'ApplyNeighborhoodEffect', 'ApplyHue', 'ApplyVibrance', 'ApplyColorBalance', 'ImageEffectKind.Mosaic'],
    ROOT / 'src/Magic.Capture.App/Utilities/ImageCanvasOperationsService.cs': [
        'AddBorder', 'TornEdges', 'FadeEdges', 'AddReflection', 'AddTextWatermark', 'AddImageWatermark',
        'AutoCropPlainBorders', 'ExpandCanvas', 'MakeColorTransparent', 'RotateArbitrary', 'ImageWorkloadLimits'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': [
        'Advanced canvas effects…', 'Import effect pack…', 'Export effect pack…'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs': [
        'UtilityCanvasEffects_Click', 'ImportEffectPackAsync', 'ExportEffectPackAsync'],
}.items():
    if not path.exists():
        ERRORS.append(f'3.2 image-effects contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'3.2 image-effects contract missing in {path.relative_to(ROOT)}: {needle}')


# 3.3 Compare 3.0 contracts. Perceptual/semantic algorithms are bounded, content
# registration is on-demand, and batch/history/report paths do not add resident work.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/Imaging/PerceptualHash.cs': [
        'ComputeDHashBgra', 'HammingDistance', 'stackalloc byte[72]'],
    ROOT / 'src/Magic.Capture.Core/Imaging/BgraContentBounds.cs': [
        'public static PixelRect Find', 'tolerance = Math.Clamp', 'cancellationToken.ThrowIfCancellationRequested'],
    ROOT / 'src/Magic.Capture.Core/Ocr/OcrSemanticDiff.cs': [
        'MaximumWordsPerSide = 1_024', 'MaximumChanges = 512', 'OcrWordChangeKind', 'ushort[a.Length + 1, b.Length + 1]'],
    ROOT / 'src/Magic.Capture.Core/Ocr/OcrLayoutDiff.cs': [
        'MaximumLines = 512', 'MaximumChanges = 256', 'OcrLayoutChange', 'var used = new bool[rightLines.Length]'],
    ROOT / 'src/Magic.Capture.App/Imaging/BitmapContentBounds.cs': [
        'LockBits', 'BitmapStridePolicy.RowOffset', 'cancellationToken.ThrowIfCancellationRequested'],
    ROOT / 'src/Magic.Capture.App/Imaging/ImageCompareService.cs': [
        'autoRegisterContent', 'BitmapContentBounds.Find', 'HighQualityBicubic', 'PerceptualHashDistance', 'ComputeDHashBgra'],
    ROOT / 'src/Magic.Capture.App/Imaging/CompareSemanticAnalysisService.cs': [
        'OcrSemanticDiff.Compare', 'OcrLayoutDiff.Compare', 'TableDiffEngine.Compare', 'Task.WhenAll'],
    ROOT / 'src/Magic.Capture.App/Views/CompareWindow.xaml': [
        'Register content', 'Semantic diff', 'Export report', 'SemanticHighlightCanvas', 'SemanticDetailsBox'],
    ROOT / 'src/Magic.Capture.App/Views/CompareWindow.xaml.cs': [
        'SemanticDiff_Click', 'RenderSemanticHighlights', 'BuildSemanticDetails', 'ExportReport_Click', 'dHash distance'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': [
        'Latest History pair', 'Batch compare…'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs': [
        'CompareLatestHistory_Click', 'CompareBatch_Click', 'Take(32)', 'GetAbsolutePath(previous)'],
}.items():
    if not path.exists():
        ERRORS.append(f'3.3 compare contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'3.3 compare contract missing in {path.relative_to(ROOT)}: {needle}')


# 3.4 Pin Power UX contracts. Pin annotations/import/layout are session/on-demand and
# persistence uses the existing settings health gate instead of a new background service.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/Settings/AppSettings.cs': [
        'PinLastX', 'PinLastY', 'PinLastWidth', 'PinLastHeight'],
    ROOT / 'src/Magic.Capture.Core/Settings/AppSettingsRules.cs': [
        'PinLastX = NormalizeNullableInt', 'PinLastWidth = NormalizeNullableInt'],
    ROOT / 'src/Magic.Capture.App/Platform/ClipboardImageReader.cs': [
        'MaximumClipboardImageBytes', 'StandardDataFormats.Bitmap', 'DecodeForPixelProcessing'],
    ROOT / 'src/Magic.Capture.App/Views/PinWindow.xaml': [
        'ImageHost_PointerPressed', 'Step', 'Note', 'Clear marks', 'Min', 'Edge', 'Lock', 'Grid pins', 'Snap pins'],
    ROOT / 'src/Magic.Capture.App/Views/PinWindow.xaml.cs': [
        'PinAnnotationItem', 'ImageHost_PointerPressed', 'Step_Click', 'Note_Click', 'Minimize_Click',
        'HideEdge_Click', 'LockPosition_Click', 'PinLastX', 'GetWindowBounds', 'MovePin'],
    ROOT / 'src/Magic.Capture.App/App.xaml.cs': [
        'ArrangePinsGrid', 'SnapPins', 'DisplayArea.GetFromWindowId'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': [
        'Pin clipboard image', 'Pin image file…'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs': [
        'UtilityPinClipboard_Click', 'UtilityPinImageFile_Click', 'OpenPin(asset)'],
}.items():
    if not path.exists():
        ERRORS.append(f'3.4 pin contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'3.4 pin contract missing in {path.relative_to(ROOT)}: {needle}')


# 3.5 Design Tools contracts. Color/measurement utilities stay on-demand; the live
# picker is bounded and persisted history/swatches use the existing settings health gate.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/Color/ColorValue.cs': [
        'public string Hsv', 'public string Cmyk', 'public string Css', 'public string CSharp', 'public string Cpp'],
    ROOT / 'src/Magic.Capture.Core/Color/ColorContrast.cs': [
        'public static double Ratio', 'WcagLabel', 'RelativeLuminance'],
    ROOT / 'src/Magic.Capture.Core/Color/ColorPaletteExtractor.cs': [
        'MaximumSampledPixels = 250_000', 'MaximumPaletteColors = 16', 'ExtractBgra'],
    ROOT / 'src/Magic.Capture.Core/Geometry/ScreenMeasurement.cs': [
        'CalibrateDpi', 'Math.Clamp(dpi, 10, 2_000)', 'AngleDegrees', 'Centimeters'],
    ROOT / 'src/Magic.Capture.Core/Settings/AppSettingsRules.cs': [
        'MaximumColorHistory = 32', 'MaximumColorSwatches = 24', 'ColorHistory = NormalizeColors', 'ColorSwatches = NormalizeColors'],
    ROOT / 'src/Magic.Capture.App/Views/DesignToolsWindow.xaml': [
        'Live screen picker', 'HSV', 'CMYK', 'Check WCAG', 'Color history', 'Saved swatches',
        'Calibrate DPI…', 'Pixel ruler / protractor', 'Screen Focus', 'Whiteboard'],
    ROOT / 'src/Magic.Capture.App/Views/DesignToolsWindow.xaml.cs': [
        'TimeSpan.FromMilliseconds(100)', 'Design region sample', 'ColorPaletteExtractor.ExtractBgra', 'ColorContrast.Ratio',
        'ScreenMeasurement.CalibrateDpi', 'MeasurementOverlayMode.Ruler', 'MeasurementOverlayMode.Focus',
        'MeasurementOverlayMode.Whiteboard', 'TryMutateSettingsAsync'],
    ROOT / 'src/Magic.Capture.App/Views/MeasurementOverlayWindow.xaml.cs': [
        'MeasurementOverlayMode', 'ScreenMeasurement.Measure', 'ToPhysical', 'CrosshairH', 'CrosshairV',
        'HorizontalLine', 'VerticalLine', 'ApplyFocusMasks', 'Polyline', '8_192', 'Root_SizeChanged', 'OnClosed'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': ['Design tools…'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs': ['UtilityDesignTools_Click', 'new DesignToolsWindow(Services)'],
}.items():
    if not path.exists():
        ERRORS.append(f'3.5 design-tools contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'3.5 design-tools contract missing in {path.relative_to(ROOT)}: {needle}')

# 3.7 Data resilience + portability source contracts.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/History/HistoryDuplicateIndex.cs': ['FindExact', 'FindNear', 'MaximumNearDuplicateHammingDistance', 'BitOperations.PopCount'],
    ROOT / 'src/Magic.Capture.Core/History/HistorySessions.cs': ['HistorySessionSummary', 'Summarize'],
    ROOT / 'src/Magic.Capture.Core/History/HistoryMaintenance.cs': ['HistoryMaintenancePlan', 'OrphanPrimaryPaths', 'MissingFingerprintItemIds'],
    ROOT / 'src/Magic.Capture.App/Imaging/ImageFingerprintService.cs': ['SHA256.HashData', 'ComputeDifferenceHash64'],
    ROOT / 'src/Magic.Capture.App/Persistence/HistoryStore.Resilience.cs': ['ScanHealthAsync', 'RepairAsync', 'ImportPortableAsync', 'RebuildSearchIndexAsync'],
    ROOT / 'src/Magic.Capture.Core/Portability/PortableArchivePolicy.cs': ['CurrentSchemaVersion = 1', 'ConfigurationAllowlist', 'IsCanonicalEntryName', 'IsHistoryImageEntry'],
    ROOT / 'src/Magic.Capture.App/Persistence/ConfigurationArchiveService.cs': ['CommitTransactionAsync', 'RequireUniqueCanonicalEntries', '"settings.json"', '"local-actions.json"', '"magic-recipes.json"'],
    ROOT / 'src/Magic.Capture.App/Persistence/HistoryArchiveService.cs': ['PortableArchiveKind.History', 'ValidateHash', 'RequireUniqueCanonicalEntries', 'PreflightHistoryPayloadsAsync', 'ImportPortableAsync'],
    ROOT / 'src/Magic.Capture.Core/Settings/AppSettingsRules.cs': ['CurrentPersistenceSchemaVersion =', 'IsPersistenceSchemaSupported'],
    ROOT / 'src/Magic.Capture.App/Persistence/SettingsStore.cs': ['ProbeFutureSchemaAsync', 'read-only recovery mode'],
    ROOT / 'src/Magic.Capture.Core/Ai/AiCacheMaintenancePolicy.cs': ['AiCacheMaintenanceDecision', 'DeleteFutureTimestamp'],
    ROOT / 'src/Magic.Capture.App/Ai/AiResultCache.cs': ['RepairAsync', 'AiCacheRepairReport'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': ['HistoryDoctor_Click', 'HistoryArchiveExport_Click', 'ConfigurationArchiveImport_Click', 'RepairAiCache_Click'],
}.items():
    if not path.exists():
        ERRORS.append(f'3.7 source contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'3.7 source contract missing in {path.relative_to(ROOT)}: {needle}')

config_archive = ROOT / 'src/Magic.Capture.App/Persistence/ConfigurationArchiveService.cs'
if config_archive.exists():
    config_source = config_archive.read_text(encoding='utf-8', errors='replace')
    for forbidden in ('ai-providers.json', 'local-action-approvals.json', 'entitlement-cache.json', 'trial.json'):
        if forbidden in config_source:
            ERRORS.append(f'3.7 configuration archive must not name sensitive payload: {forbidden}')


# 3.8 Capture robustness + 2D scrolling source contracts.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/Capture/ScrollCapturePlan.cs': [
        'public enum ScrollAxis', 'MaximumRows = 8', 'MaximumColumns = 8', 'MaximumTiles = 64',
        'horizontalReset', 'checked(rows * columns)'],
    ROOT / 'src/Magic.Capture.Core/Imaging/HorizontalOverlapMatcher.cs': [
        'HorizontalOverlapMatch', 'OverlapColumns', 'MaximumMeanAbsoluteDifference', 'leftStart'],
    ROOT / 'src/Magic.Capture.App/Imaging/HorizontalImageStitcher.cs': [
        'HorizontalOverlapMatcher', 'ImageWorkloadLimits.ValidateDimensions', 'horizontal overlap'],
    ROOT / 'src/Magic.Capture.App/Imaging/GridImageStitcher.cs': [
        'ScrollCaptureGridPlan.MaximumTiles', 'horizontalSeams', 'verticalSeams', 'Median(overlaps)', 'DrawImageUnscaled'],
    ROOT / 'src/Magic.Capture.App/Capture/TwoDimensionalScrollCaptureService.cs': [
        'ScrollCaptureGridPlan.Create', 'MinimumChangedPixelPercent', 'netHorizontal', 'netVertical',
        'checked(-netHorizontal)', 'GridImageStitcher'],
    ROOT / 'src/Magic.Capture.App/Platform/InputSynthesisService.cs': [
        'ScrollHorizontal', 'MouseEventHWheel', 'Scroll(ScrollVector vector)'],
    ROOT / 'src/Magic.Capture.App/Platform/Native/NativeConstants.cs': ['MouseEventHWheel = 0x01000', 'MdtEffectiveDpi = 0'],
    ROOT / 'src/Magic.Capture.Core/Capture/DesktopPixelTopology.cs': [
        'DesktopPixelMonitor', 'double.IsFinite', 'ToDesktopBounds', 'ToLocalBounds', 'ClipToDesktop'],
    ROOT / 'src/Magic.Capture.App/Capture/MonitorService.cs': [
        'GetDpiForMonitor', 'GetDesktopPixelTopology', 'ToDesktopBounds', 'return (96, 96)'],
    ROOT / 'src/Magic.Capture.App/Capture/CaptureCoordinator.cs': ['_monitors.ToDesktopBounds'],
    ROOT / 'src/Magic.Capture.Core/Capture/CaptureRetryPolicy.cs': ['MaximumAttempts = 3', 'RetryDelayMilliseconds = 40'],
    ROOT / 'src/Magic.Capture.App/Capture/GdiCaptureBackend.cs': [
        'CaptureRetryPolicy.MaximumAttempts', 'CaptureRetryPolicy.ShouldRetry',
        'PngDimensions.TryRead', 'physical-pixel region'],
    ROOT / 'src/Magic.Capture.App/Capture/ScreenCaptureService.cs': [
        'CaptureWithDiagnostics', 'CaptureBackendRouter', 'PngDimensions.TryRead'],
    ROOT / 'src/Magic.Capture.App/Views/ScrollingCaptureModeDialog.cs': [
        'ScrollingCaptureMode.Vertical', 'ScrollingCaptureMode.Horizontal', 'ScrollingCaptureMode.Grid2D',
        'Maximum = 8', '2D mode is bounded'],
    ROOT / 'src/Magic.Capture.App/App.xaml.cs': [
        'new HorizontalImageStitcher()', 'new GridImageStitcher', 'new TwoDimensionalScrollCaptureService',
        'ScrollingCaptureModeDialog.ShowAsync', 'new AutomaticScrollCaptureOptions(Axis: axis)'],
    ROOT / 'tests/Magic.Capture.Core.Tests/ScrollCapturePlanTests.cs': ['GridPlan_IsRowMajorAndResetsHorizontalPositionBetweenRows', '8, 8'],
    ROOT / 'tests/Magic.Capture.Core.Tests/HorizontalOverlapMatcherTests.cs': ['FindsExactRightLeftOverlap', 'RejectsUnrelatedFrames'],
    ROOT / 'tests/Magic.Capture.Core.Tests/DesktopPixelTopologyTests.cs': ['NegativeCoordinateMonitor_RoundTripsLocalAndDesktopPixels', 'PortraitMonitor_IsValidAndClipsInPhysicalPixels'],
    ROOT / 'tests/Magic.Capture.Core.Tests/CaptureRetryPolicyTests.cs': ['RetriesOnlyWithinThreeAttemptBudget'],
}.items():
    if not path.exists():
        ERRORS.append(f'3.8 capture-robustness contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'3.8 capture-robustness contract missing in {path.relative_to(ROOT)}: {needle}')

# 3.9 Multi-backend capture architecture contracts.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/Capture/CaptureBackendPolicy.cs': [
        'WindowsGraphicsCapture', 'DesktopDuplication', 'CaptureBackendAvailability',
        'BuildCandidates', 'DesktopDuplicationRebuildBudget = 1', 'ShouldFallback',
        'DesktopDuplicationCursorPolicy', 'CanGuaranteeCursorExcluded'],
    ROOT / 'src/Magic.Capture.App/Capture/ICaptureBackend.cs': [
        'CaptureBackendRequest', 'SourceBounds', 'CaptureBackendProbe', 'ICaptureBackend'],
    ROOT / 'src/Magic.Capture.App/Capture/WindowsGraphicsCaptureBackend.cs': [
        'CreateFreeThreaded', 'FirstFrameTimeoutMilliseconds = 1500', 'GraphicsCaptureItemInterop',
        'SoftwareBitmap.CreateCopyFromSurfaceAsync', 'IsCursorCaptureEnabled'],
    ROOT / 'src/Magic.Capture.App/Capture/GraphicsCaptureItemInterop.cs': [
        'RoGetActivationFactory', '3628E81B-3CAC-4C60-B7F4-23CE0E0C3356', 'CreateForWindow', 'CreateForMonitor', 'GraphicsCaptureItem.FromAbi'],
    ROOT / 'src/Magic.Capture.App/Capture/Direct3D11DeviceHost.cs': [
        'DeviceCreationFlags.BgraSupport', 'CreateDirect3D11DeviceFromDXGIDevice', 'Invalidate'],
    ROOT / 'src/Magic.Capture.App/Capture/DesktopDuplicationCaptureBackend.cs': [
        'DuplicateOutput', 'AcquireNextFrame', 'OutduplFrameInfo frameInfo', 'ResourceUsage.Staging',
        'CanGuaranteeCursorExcluded', 'DesktopDuplicationRebuildBudget', 'ReleaseFrame',
        'throw new CaptureBackendException(Kind, ex.FailureKind, ex.Message, ex, rebuild)'],
    ROOT / 'src/Magic.Capture.App/Capture/CaptureBackendFailureClassifier.cs': [
        'ResultCode.AccessLost', 'ResultCode.DeviceRemoved', 'ResultCode.DeviceReset', 'FromException'],
    ROOT / 'src/Magic.Capture.App/Capture/GdiCaptureBackend.cs': [
        'CopyFromScreen', 'CaptureRetryPolicy.MaximumAttempts', 'DrawCursorIfVisible'],
    ROOT / 'src/Magic.Capture.App/Capture/CaptureBackendRouter.cs': [
        'CaptureBackendPolicy.BuildCandidates', 'ValidateAndCrop', 'CaptureBackendAttempt', 'ShouldFallback'],
    ROOT / 'src/Magic.Capture.App/App.xaml.cs': [
        'new WindowsGraphicsCaptureBackend', 'new DesktopDuplicationCaptureBackend',
        'new GdiCaptureBackend', 'new CaptureBackendRouter'],
    ROOT / 'tests/Magic.Capture.Core.Tests/CaptureBackendPolicyTests.cs': [
        'Window_UsesWgcThenGdi', 'Monitor_UsesWgcThenDesktopDuplicationThenGdi',
        'SingleMonitorRegion_WithCursor_SkipsDesktopDuplication', 'DesktopDuplicationRecovery_IsBounded',
        'DesktopDuplicationCursorExclusion_RequiresVisibleSeparatePointerMetadata'],
}.items():
    if not path.exists():
        ERRORS.append(f'3.9 capture-backend contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'3.9 capture-backend contract missing in {path.relative_to(ROOT)}: {needle}')

# 4.0 Visual recording source contracts.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/Recording/RecordingPolicy.cs': [
        'RecordingTargetKind', 'MaximumFramesPerSecond = 60', 'MaximumBitrateMbps = 50',
        'ScaleDimension', 'TimestampForFrame', 'ShouldStop', 'RecordingStateMachine', 'RecordingManifestPolicy'],
    ROOT / 'src/Magic.Capture.App/Persistence/AppPaths.cs': ['recording-session.json', 'RecordingJournalFile'],
    ROOT / 'src/Magic.Capture.App/Recording/RecordingRecoveryStore.cs': [
        'RecordingJournalFile', 'RecordingSessionManifest', 'LoadUnfinishedAsync', 'newer recording journal schema',
        'AtomicJsonFile.WriteAsync', 'RecordingOutputPolicy.PartialSuffix'],
    ROOT / 'src/Magic.Capture.App/Recording/RecordingFrameProvider.cs': [
        'RecordingTargetKind.Region', 'RecordingTargetKind.Window', 'RecordingTargetKind.Monitor',
        'RecordingTargetKind.VirtualDesktop', 'RegionCrossMonitor', 'changed size', 'changed resolution'],
    ROOT / 'src/Magic.Capture.App/Recording/RecordingFrameDecoder.cs': [
        'BitmapPixelFormat.Bgra8', 'BitmapAlphaMode.Premultiplied', 'BitmapTransform',
        'ValidatePixelProcessingDimensions', 'CopyToBuffer'],
    ROOT / 'src/Magic.Capture.App/Recording/Mp4RecordingEncoder.cs': [
        'MediaStreamSource', 'SampleRequested', 'GetDeferral', 'CreateFromBuffer',
        'MediaEncodingProfile.CreateMp4', 'MediaEncodingSubtypes.H264', 'profile.Audio = audioFactory is null',
        'HardwareAccelerationEnabled = true', 'PrepareMediaStreamSourceTranscodeAsync'],
    ROOT / 'src/Magic.Capture.App/Recording/RecordingSessionService.cs': [
        'PauseAsync', 'ResumeAsync', 'Stop()', 'CountdownSeconds', 'StopAfterMinutes',
        'RecordingOutputPolicy.PartialSuffix', 'File.Move(tempPath, finalPath, overwrite: true)', 'RecordingRecoveryStore',
        'RecordingStopPolicy.ShouldStop', 'LastRegion'],
    ROOT / 'src/Magic.Capture.App/Recording/RecordingControlCaptureExclusion.cs': [
        'WdaExcludeFromCapture', 'SetWindowDisplayAffinity', 'WdaNone'],
    ROOT / 'src/Magic.Capture.App/ApplicationServices.cs': ['RecordingSessionService Recording'],
    ROOT / 'src/Magic.Capture.App/App.xaml.cs': [
        'new RecordingRecoveryStore(paths)', 'new RecordingFrameProvider(screen, monitors)',
        'new RecordingSessionService(recordingFrames, recordingRecovery, log)', 'Recording = recording'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': [
        'Screen recording', 'RecordingTargetCombo', 'RecordingFpsBox', 'RecordingBitrateBox',
        'RecordingScaleBox', 'RecordingCountdownBox', 'RecordingStopAfterBox',
        'RecordingStart_Click', 'RecordingPause_Click', 'RecordingResume_Click', 'RecordingStop_Click'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs': [
        'ResolveRecordingTargetAsync', 'StartRecordingTargetAsync', 'RecordingControlCaptureExclusion.Exclude',
        'Recording_ProgressChanged', 'RecordingDiscardRecovery_Click', 'VideosLibrary', 'RecordingOutputPolicy.DisplayName'],
    ROOT / 'src/Magic.Capture.App/Platform/Native/NativeConstants.cs': ['WdaExcludeFromCapture = 0x00000011'],
    ROOT / 'src/Magic.Capture.App/Platform/Native/NativeMethods.cs': ['SetWindowDisplayAffinity'],
    ROOT / 'tests/Magic.Capture.Core.Tests/RecordingPolicyTests.cs': [
        'Normalize_ClampsAllBoundedOptions', 'ScaleDimension_IsEvenAndBounded', 'Cadence_ProducesMonotonicFrameTimestamps',
        'StopPolicy_UsesActiveElapsedMinutes', 'StateMachine_AllowsOnlyLifecycleTransitions'],
    ROOT / 'tests/Magic.Capture.Core.Tests/RecordingManifestPolicyTests.cs': [
        'Unfinished_OnlyIncludesRecoverableLifecycleStates', 'FutureSchema_IsReadOnly'],
}.items():
    if not path.exists():
        ERRORS.append(f'4.0 recording contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'4.0 recording contract missing in {path.relative_to(ROOT)}: {needle}')

# 4.1 Native recording-audio source contracts.
for path, needles in {
    ROOT / 'src/Magic.Capture.App/Magic.Capture.App.csproj': [
        'PackageReference Include="NAudio.Wasapi" Version="3.0.1"'],
    ROOT / 'src/Magic.Capture.App/Package.appxmanifest': ['DeviceCapability Name="microphone"'],
    ROOT / 'src/Magic.Capture.Core/Recording/RecordingPolicy.cs': [
        'IncludeSystemAudio', 'IncludeMicrophone', 'SystemAudioDeviceId', 'MicrophoneDeviceId',
        'AudioBitrateKbps', 'SystemAudioGainPercent', 'MicrophoneGainPercent', 'RecordingManifestPolicy'],
    ROOT / 'src/Magic.Capture.Core/Recording/RecordingAudioPolicy.cs': [
        'SampleRate = 48_000', 'Channels = 2', 'BitsPerSample = 16', 'BlockMilliseconds = 20',
        'MaximumBufferedSeconds = 2', 'MixPcm16', 'RecordingAudioLevels'],
    ROOT / 'src/Magic.Capture.App/Recording/AudioDeviceCatalog.cs': [
        'DataFlow.Render', 'DataFlow.Capture', 'EnumerateAudioEndPoints', 'GetDefaultAudioEndpoint'],
    ROOT / 'src/Magic.Capture.App/Recording/BoundedPcmBuffer.cs': [
        'DroppedBytes', 'ReadAndFillSilence', 'capacityBytes'],
    ROOT / 'src/Magic.Capture.App/Recording/WasapiRecordingAudioSource.cs': [
        'WasapiRecorderBuilder', 'WithSharedMode', 'WithLoopbackCapture', 'WithFormat',
        'WithMmcssThreadPriority', 'DataAvailable', 'RecordingStopped', 'AudioClientBufferFlags.Silent'],
    ROOT / 'src/Magic.Capture.App/Recording/RecordingAudioPipeline.cs': [
        'IncludeSystemAudio', 'IncludeMicrophone', 'ReadMixedBlockAsync', 'MixPcm16',
        'CryptographicBuffer.CreateFromByteArray', 'DroppedBytes'],
    ROOT / 'src/Magic.Capture.App/Recording/Mp4RecordingEncoder.cs': [
        'AudioStreamDescriptor', 'AudioEncodingProperties.CreatePcm', 'AudioEncodingProperties.CreateAac',
        'RecordingAudioPolicy.TimestampForBlock', 'audioBlockIndex', 'audioFactory'],
    ROOT / 'src/Magic.Capture.App/Recording/RecordingSessionService.cs': [
        '_activeAudioPipeline', 'StartAndWarmUpAsync', 'PaceAudioBlockAsync', 'ReadMixedBlockAsync',
        'SetPaused(true)', 'SetPaused(false)', 'RecordingAudioStatus'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': [
        'RecordingSystemAudioCheck', 'RecordingMicrophoneCheck', 'RecordingSystemAudioDeviceCombo',
        'RecordingMicrophoneDeviceCombo', 'RecordingAudioBitrateBox', 'RecordingSystemAudioGainBox',
        'RecordingMicrophoneGainBox', 'RecordingAudioStatusText', 'RecordingRefreshAudioDevices_Click'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs': [
        'RefreshRecordingAudioDevices', 'IncludeSystemAudio:', 'IncludeMicrophone:', 'AudioBitrateKbps:',
        'SystemAudioGainPercent:', 'MicrophoneGainPercent:', 'progress.AudioStatus'],
    ROOT / 'tests/Magic.Capture.Core.Tests/RecordingAudioPolicyTests.cs': [
        'Normalize_ClampsAudioOptionsAndTrimsDeviceIds', 'Cadence_IsTwentyMillisecondsAtCanonicalFormat',
        'Mixer_SaturatesAndAppliesIndependentGains', 'Mixer_UsesSilenceForMissingSource', 'LevelMeter_ComputesPeakAndRmsWithoutNaN'],
    ROOT / 'tests/Magic.Capture.Core.Tests/RecordingManifestPolicyTests.cs': [
        'FutureSchema_IsReadOnly'],
    ROOT / 'docs/WINDOWS_RELEASE_CHECKLIST.md': [
        '4.1 native recording-audio gate', 'system audio + microphone', '2 hours', 'schema 3+'],
}.items():
    if not path.exists():
        ERRORS.append(f'4.1 recording-audio contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'4.1 recording-audio contract missing in {path.relative_to(ROOT)}: {needle}')

# 4.2 Webcam / Picture-in-Picture source contracts.
for path, needles in {
    ROOT / 'src/Magic.Capture.App/Package.appxmanifest': ['DeviceCapability Name="webcam"'],
    ROOT / 'src/Magic.Capture.Core/Recording/RecordingPolicy.cs': [
        'IncludeWebcam', 'WebcamDeviceId', 'WebcamXPercent', 'WebcamYPercent', 'WebcamWidthPercent',
        'WebcamShape', 'MirrorWebcam', 'WebcamOpacityPercent', 'WebcamBorderPixels', 'RecordingManifestPolicy', 'CanWriteSchema'],
    ROOT / 'src/Magic.Capture.Core/Recording/RecordingWebcamPolicy.cs': [
        'WebcamOverlayShape', 'ComputeOverlayRect', 'WarmUpTimeout', 'BgraWebcamCompositor',
        'CompositeInPlace', 'SampleBilinear', 'InsideMask'],
    ROOT / 'src/Magic.Capture.App/Recording/CameraDeviceCatalog.cs': [
        'DeviceClass.VideoCapture', 'CameraDeviceInfo', 'FindAllAsync'],
    ROOT / 'src/Magic.Capture.App/Recording/RecordingWebcamSource.cs': [
        'MediaCaptureInitializationSettings', 'StreamingCaptureMode.Video', 'MediaCaptureMemoryPreference.Cpu',
        'MediaCaptureSharingMode.SharedReadOnly', 'MediaFrameReader', 'TryAcquireLatestFrame',
        'SoftwareBitmap.Copy', 'SoftwareBitmap.Convert', 'WarmUpTimeout', 'MaximumFrameAge', '_latest = frame'],
    ROOT / 'src/Magic.Capture.App/Recording/RecordingFrameDecoder.cs': [
        'RecordingFramePixels', 'DecodeBgra8PixelsAsync', 'DataReader.FromBuffer', 'DetachBuffer'],
    ROOT / 'src/Magic.Capture.App/Recording/RecordingWebcamCompositor.cs': [
        'RecordingWebcamPolicy.ComputeOverlayRect', 'BgraWebcamCompositor.CompositeInPlace'],
    ROOT / 'src/Magic.Capture.App/Recording/RecordingSessionService.cs': [
        'RecordingWebcamSource? webcam', 'webcam.StartAsync', 'webcam.ThrowIfFailed',
        'RecordingWebcamCompositor.Composite', '_activeWebcamSource', 'RecordingWebcamDispose'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': [
        'RecordingWebcamCheck', 'RecordingWebcamDeviceCombo', 'RecordingWebcamPositionCombo',
        'RecordingWebcamWidthBox', 'RecordingWebcamShapeCombo', 'RecordingWebcamOpacityBox',
        'RecordingWebcamMirrorCheck', 'RecordingWebcamBorderBox', 'RecordingRefreshCameras_Click'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs': [
        'RefreshRecordingCamerasAsync', 'IncludeWebcam:', 'WebcamDeviceId:', 'WebcamXPercent:',
        'WebcamYPercent:', 'WebcamWidthPercent:', 'WebcamShape:', 'MirrorWebcam:',
        'WebcamOpacityPercent:', 'WebcamBorderPixels:', 'progress.WebcamStatus'],
    ROOT / 'tests/Magic.Capture.Core.Tests/RecordingWebcamPolicyTests.cs': [
        'Normalize_ClampsWebcamOptions', 'ComputeOverlayRect_AlwaysStaysInsideOutput',
        'Compositor_CircleMaskLeavesCornerUntouched', 'Compositor_MirrorFlipsSourceHorizontally',
        'Compositor_OpacityBlendsWithCanvas'],
    ROOT / 'tests/Magic.Capture.Core.Tests/RecordingManifestPolicyTests.cs': [
        'FutureSchema_IsReadOnly'],
    ROOT / 'docs/WINDOWS_RELEASE_CHECKLIST.md': [
        '4.2 webcam / Picture-in-Picture gate', 'camera permission', 'USB camera', 'schema 4+'],
}.items():
    if not path.exists():
        ERRORS.append(f'4.2 webcam/PiP contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'4.2 webcam/PiP contract missing in {path.relative_to(ROOT)}: {needle}')

# 4.3 Recording effects + animated-output source contracts.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/Recording/RecordingPolicy.cs': [
        'RecordingOutputFormat OutputFormat', 'CursorHighlight', 'ClickVisualization', 'SafeKeyOverlay',
        'DrawWhileRecording', 'LiveZoom', 'ZoomPercent', 'RecordingManifestPolicy', 'CanWriteSchema'],
    ROOT / 'src/Magic.Capture.Core/Recording/RecordingEffectsPolicy.cs': [
        'RecordingOutputFormat', 'MaximumStrokePoints = 2048', 'RippleLifetime', 'KeyOverlayLifetime',
        'ComputeZoomSourceRect', 'RecordingSafeKeyFormatter', 'PartialSuffix', 'ValidateCompatibility'],
    ROOT / 'src/Magic.Capture.Core/Recording/GifEncodingPolicy.cs': [
        'PaletteSize = 256', 'ToPaletteIndex', 'BuildRgb332Palette', 'FrameDelayHundredths', 'EncodeLzw',
        'MaximumLzwCode = 4095'],
    ROOT / 'src/Magic.Capture.Core/Recording/ApngEncodingPolicy.cs': [
        'ApngFrameDelay', 'FrameDelay', 'Crc32', '0xEDB88320'],
    ROOT / 'src/Magic.Capture.App/Platform/Native/NativeConstants.cs': [
        'WhKeyboardLl = 13', 'WhMouseLl = 14', 'WmLButtonDown', 'WmRButtonDown',
        'VkLControl', 'VkRControl', 'VkLMenu', 'VkRMenu'],
    ROOT / 'src/Magic.Capture.App/Platform/Native/NativeMethods.cs': [
        'SetWindowsHookExW', 'UnhookWindowsHookEx', 'CallNextHookEx', 'LowLevelHookProc'],
    ROOT / 'src/Magic.Capture.App/Recording/RecordingInputTracker.cs': [
        'MaximumClicks = 16', 'MaximumStrokes = 128', 'SetWindowsHookExW', 'CallNextHookEx',
        'RecordingSafeKeyFormatter.Format', 'data.VirtualKey == 0x5A', 'UnhookWindowsHookEx', 'IDisposable'],
    ROOT / 'src/Magic.Capture.App/Recording/RecordingEffectsCompositor.cs': [
        'ApplyZoomInPlace', 'ApplyOverlaysInPlace', 'DrawRing', 'DrawLine', 'DrawKeyBadge'],
    ROOT / 'src/Magic.Capture.App/Recording/GifRecordingEncoder.cs': [
        'GIF89a', 'BuildRgb332Palette', 'EncodeLzw', 'FrameDelayHundredths', '0x3B'],
    ROOT / 'src/Magic.Capture.App/Recording/ApngRecordingEncoder.cs': [
        'acTL', 'fcTL', 'fdAT', 'IDAT', 'PatchAnimationControlAsync', 'Crc32'],
    ROOT / 'src/Magic.Capture.App/Recording/RecordingSessionService.cs': [
        'RecordingOutputPolicy.ValidateCompatibility', 'RecordingOutputPolicy.PartialSuffix',
        'RecordingInputTracker', 'RecordingEffectsCompositor.ApplyZoomInPlace',
        'RecordingEffectsCompositor.ApplyOverlaysInPlace', 'RecordingOutputFormat.Gif',
        'GifRecordingEncoder', 'RecordingOutputFormat.Apng', 'ApngRecordingEncoder'],
    ROOT / 'src/Magic.Capture.App/Recording/RecordingRecoveryStore.cs': [
        'RecordingOutputPolicy.ValidateCompatibility', 'RecordingOutputPolicy.Extension',
        'RecordingOutputPolicy.PartialSuffix', 'RecordingManifestPolicy.CurrentSchemaVersion'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': [
        'RecordingOutputFormatCombo', 'RecordingCursorHighlightCheck', 'RecordingClickVisualizationCheck',
        'RecordingSafeKeyOverlayCheck', 'RecordingDrawCheck', 'RecordingLiveZoomCheck',
        'RecordingZoomBox', 'RecordingOutputFormat_SelectionChanged'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs': [
        'SelectedRecordingOutputFormat', 'RecordingOutputPolicy.ValidateCompatibility',
        'RecordingOutputPolicy.DisplayName', 'RecordingOutputPolicy.Extension',
        'CursorHighlight:', 'ClickVisualization:', 'SafeKeyOverlay:', 'DrawWhileRecording:',
        'LiveZoom:', 'ZoomPercent:'],
    ROOT / 'tests/Magic.Capture.Core.Tests/RecordingEffectsPolicyTests.cs': [
        'Normalize_ClampsEffectOptionsAndPreservesOutputFormat', 'SafeKeyFormatter_DoesNotRetainPlainTyping',
        'RipplePolicy_ExpiresAtBoundedLifetime', 'ComputeZoomSourceRect_StaysInsideFrame',
        'StrokePolicy_BoundsPointCount'],
    ROOT / 'tests/Magic.Capture.Core.Tests/AnimatedRecordingEncodingPolicyTests.cs': [
        'GifPalette_MapsRgbToDeterministic332', 'GifLzw_ProducesBoundedNonEmptyPayload',
        'PngCrc32_MatchesStandardVector', 'ApngDelay_UsesMillisecondRational'],
    ROOT / 'tests/Magic.Capture.Core.Tests/RecordingManifestPolicyTests.cs': [
        'FutureSchema_IsReadOnly'],
    ROOT / 'docs/WINDOWS_RELEASE_CHECKLIST.md': [
        '4.3 recording effects + animated outputs gate', 'plain unmodified A-Z/0-9 typing', 'Start/stop 100 sessions',
        'GIF', 'APNG', 'schema 5+'],
}.items():
    if not path.exists():
        ERRORS.append(f'4.3 recording-effects contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'4.3 recording-effects contract missing in {path.relative_to(ROOT)}: {needle}')

# 4.4 Recording post-processing / Clip Editor source contracts.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/VideoEditing/VideoEditModels.cs': [
        'VideoEditProjectSchema', 'VideoEditSource', 'VideoEditSegment',
        'VideoEditProject', 'VideoContactSheetPlan', 'MaximumFrames = 64', 'MaximumBgraBytes = 256L * 1024 * 1024'],
    ROOT / 'src/Magic.Capture.Core/VideoEditing/VideoEditPolicy.cs': [
        'MaximumSources = 64', 'MaximumSegments = 256', 'MaximumVolume = 2.0',
        'NormalizeVolume', 'NormalizeCrop', 'NormalizeOutputDimension', 'CutOut', 'TimelineDuration', 'ValidateProject'],
    ROOT / 'tests/Magic.Capture.Core.Tests/VideoEditPolicyTests.cs': [
        'Trim_KeepsRequestedSourceRange', 'CutOut_MiddleIntervalReturnsTwoSegments',
        'NormalizeVolume_ClampsToTwoHundredPercent', 'NormalizeCrop_StaysInsideUnitCanvas',
        'NormalizeOutputDimension_IsEvenAndBounded', 'ProjectSchema_FutureVersionIsReadOnly',
        'ContactSheetPlan_BoundsFrameCountAndPixelBudget', 'TimelineDuration_SumsSegmentDurations'],
    ROOT / 'src/Magic.Capture.App/VideoEditing/VideoEditProjectStore.cs': [
        'MaximumProjectBytes = 4L * 1024 * 1024', 'AtomicJsonFile.ReadAsync<VideoEditProject>',
        'AtomicJsonFile.WriteAsync', 'info.Length == 0', 'future clip-project schema',
        'newer Magic Capture Desktop version and will not be overwritten'],
    ROOT / 'src/Magic.Capture.App/VideoEditing/VideoEditCompositionService.cs': [
        'MediaComposition', 'MediaClip.CreateFromFileAsync', 'TrimTimeFromStart', 'TrimTimeFromEnd',
        'clip.Volume', 'VideoTransformEffectDefinition', 'CropRectangle', 'OutputSize',
        'GeneratePreviewMediaStreamSource', 'MediaTrimmingPreference.Precise', 'RenderToFileAsync',
        '.partial.mp4', 'File.Move(partialPath, finalPath, overwrite: true)'],
    ROOT / 'src/Magic.Capture.App/VideoEditing/VideoEditThumbnailService.cs': [
        'GetThumbnailAsync', 'VideoFramePrecision.NearestFrame', 'VideoContactSheetPlan.Create',
        'GC.AllocateUninitializedArray<byte>', 'BitmapEncoder.PngEncoderId', 'plan.RequiredBgraBytes'],
    ROOT / 'src/Magic.Capture.App/Views/VideoEditorWindow.xaml': [
        'Add clips', 'Open project', 'Save project', 'Undo', 'Redo', 'Export MP4',
        'Capture frame', 'Contact sheet', 'Cut interval', 'Apply selected'],
    ROOT / 'src/Magic.Capture.App/Views/VideoEditorWindow.xaml.cs': [
        'AddClips_Click', 'OpenProject_Click', 'SaveProject_Click', 'Undo_Click', 'Redo_Click',
        'RefreshPreview_Click', 'ExportMp4_Click', 'CaptureFrame_Click', 'ContactSheet_Click',
        'CutInterval_Click', 'ApplySegment_Click', 'CommitProject', 'MaximumUndoStates = 32'],
    ROOT / 'src/Magic.Capture.App/ApplicationServices.cs': [
        'VideoEditProjectStore VideoEditProjects', 'VideoEditCompositionService VideoEditComposition',
        'VideoEditThumbnailService VideoEditThumbnails'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': ['Open Clip Editor', 'OpenVideoEditor_Click'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs': ['OpenVideoEditor_Click', 'new VideoEditorWindow(Services)'],
    ROOT / 'docs/RELEASE_NOTES_4.4.0.md': [
        'Recording Post-Processing / Clip Editor', 'Done: **370**', '#84', '#100', 'Playback speed #89'],
    ROOT / 'docs/WINDOWS_RELEASE_CHECKLIST.md': [
        '4.4 recording post-processing / clip-editor gate', 'MediaTrimmingPreference.Precise',
        '64-frame / 256 MiB BGRA hard caps', '#89 playback speed `Missing`'],
}.items():
    if not path.exists():
        ERRORS.append(f'4.4 clip-editor contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'4.4 clip-editor contract missing in {path.relative_to(ROOT)}: {needle}')

# 4.5 Advanced Clip Editor source contracts.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/VideoEditing/VideoEditModels.cs': [
        'VideoEditProjectSchema', 'VideoEditProjectMigration', 'VideoEditTitleCard', 'VideoEditOverlayKind',
        'VideoEditOverlayKeyframe', 'VideoEditOverlay', 'IReadOnlyList<VideoEditOverlay>? Overlays'],
    ROOT / 'src/Magic.Capture.Core/VideoEditing/VideoEditPolicy.cs': [
        'MaximumOverlays = 128', 'MaximumTrackingKeyframes = 256', 'MaximumOverlayTextLength = 1024',
        'MaximumTitleTextLength = 512', 'MaximumTrackingDuration = TimeSpan.FromMinutes(5)',
        'ValidateTitleCard', 'ValidateOverlays'],
    ROOT / 'src/Magic.Capture.Core/VideoEditing/VideoEditTracking.cs': [
        'VideoEditTemplateTracker', 'TrackNext', 'MeanAbsoluteError', 'IsConfident', 'confidenceErrorThreshold'],
    ROOT / 'src/Magic.Capture.Core/VideoEditing/VideoEditExportPolicy.cs': [
        'VideoEditAudioFormat', 'Wav', 'Mp3', 'M4a', 'VideoEditVideoFormat', 'H264Mp4', 'HevcMp4', 'Wmv',
        'ValidateOutputPath'],
    ROOT / 'tests/Magic.Capture.Core.Tests/VideoEditPolicyTests.cs': [
        'ProjectMigration_UpgradesSchemaOneToCurrent', 'TitleCard_DurationContributesToTimeline',
        'ValidateProject_RejectsOverlayOutsideTimelineAndTooManyKeyframes', 'TemplateTracker_FollowsSyntheticMovingSquare'],
    ROOT / 'tests/Magic.Capture.Core.Tests/VideoEditExportPolicyTests.cs': [
        'AudioFormat_UsesExpectedExtension', 'VideoFormat_UsesExpectedExtension', 'ValidateOutputPath_RejectsWrongExtension'],
    ROOT / 'src/Magic.Capture.App/VideoEditing/VideoEditProjectStore.cs': [
        'VideoEditProjectMigration.UpgradeToCurrent', 'project.SchemaVersion > VideoEditProjectSchema.CurrentVersion',
        'future clip-project schema'],
    ROOT / 'src/Magic.Capture.App/VideoEditing/VideoEditOverlayAssetStore.cs': [
        'MaximumCacheFiles = 256', 'MaximumCacheBytes = 64L * 1024 * 1024', 'SHA256.HashData',
        'VideoEditOverlayKind.Text', 'VideoEditOverlayKind.Rectangle', 'VideoEditOverlayKind.Ellipse', 'VideoEditOverlayKind.Arrow'],
    ROOT / 'src/Magic.Capture.App/VideoEditing/VideoEditCompositionService.cs': [
        'MaximumGeneratedOverlayPieces = VideoEditOverlayAnimationPolicy.MaximumAnimatedOverlayPieces', 'MediaOverlayLayer', 'MediaClip.CreateFromColor',
        'MediaClip.CreateFromImageFileAsync', 'Delay =', 'Position =', 'AudioEnabled = false',
        'includeOverlays = true', 'AddSolidOverlayPieces', 'AddRasterOverlaysAsync'],
    ROOT / 'src/Magic.Capture.App/VideoEditing/VideoEditTrackingService.cs': [
        'MaximumTrackingWidth = 960', 'MaximumTrackingKeyframes', 'includeOverlays: false',
        'VideoEditTemplateTracker.TrackNext', 'could not confidently follow'],
    ROOT / 'src/Magic.Capture.App/VideoEditing/VideoEditTranscodeService.cs': [
        'PrepareMediaStreamSourceTranscodeAsync', 'CreateWav', 'CreateMp3', 'CreateM4a', 'CreateMp4',
        'CreateHevc', 'CreateWmv', 'CanTranscode', '.partial', 'File.Move(partialPath, finalPath, overwrite: true)'],
    ROOT / 'src/Magic.Capture.App/Views/VideoEditorWindow.xaml': [
        'Advanced timeline', 'Title card', 'Timed overlays', 'Auto-track selected redaction',
        'Extract audio', 'Convert video'],
    ROOT / 'src/Magic.Capture.App/Views/VideoEditorWindow.xaml.cs': [
        'AddTitleCard_Click', 'AddOverlay_Click', 'RemoveOverlay_Click', 'TrackRedaction_Click',
        'ExtractAudio_Click', 'ConvertVideo_Click', 'VideoEditTracking.TrackRedactionAsync',
        'VideoEditTranscode.ExtractAudioAsync', 'VideoEditTranscode.ConvertVideoAsync'],
    ROOT / 'src/Magic.Capture.App/ApplicationServices.cs': [
        'VideoEditTrackingService VideoEditTracking', 'VideoEditTranscodeService VideoEditTranscode'],
    ROOT / 'docs/RELEASE_NOTES_4.5.0.md': [
        'Advanced Clip Editor', 'Done: **376**', '#92', '#94', '#95', '#97', '#98', '#99',
        'Playback speed #89', 'post-record zoom #96'],
    ROOT / 'docs/WINDOWS_RELEASE_CHECKLIST.md': [
        '4.5 advanced clip-editor gate', 'schema-v1 `.magicclip`', '256-file / 64 MiB hard cap',
        'CanTranscode=false', '#89 playback speed `Missing`', '#96 post-record zoom `Missing`'],
}.items():
    if not path.exists():
        ERRORS.append(f'4.5 advanced clip-editor contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'4.5 advanced clip-editor contract missing in {path.relative_to(ROOT)}: {needle}')

# 4.6 Advanced editor retiming/effects + audio-only recording source contracts.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/VideoEditing/VideoEditModels.cs': [
        'PlaybackRate = 1.0', 'RenderedDuration', 'OutputFramesPerSecond = 30',
        'IReadOnlyList<VideoEditFrameEffect>? FrameEffects'],
    ROOT / 'src/Magic.Capture.Core/VideoEditing/VideoEditTimelineMap.cs': [
        'VideoEditTimelineMap', 'MapOutputToBaseTimeline', 'BaseTimelinePosition'],
    ROOT / 'src/Magic.Capture.Core/VideoEditing/VideoEditFrameEffects.cs': [
        'VideoEditFrameEffectKind', 'ZoomPan', 'GaussianBlur', 'Pixelate', 'MinimumPlaybackRate = 0.25',
        'MaximumPlaybackRate = 4.0', 'MaximumFrameEffects = 128', 'MaximumKeyframesPerEffect = 256',
        'ApplyGaussianBlurInPlace', 'ApplyPixelateInPlace', 'ApplyZoomPanInPlace'],
    ROOT / 'src/Magic.Capture.Core/VideoEditing/VideoEditPolicy.cs': [
        'public static TimeSpan RenderedDuration', 'NormalizePlaybackRate', 'segment.RenderedDuration.Ticks'],
    ROOT / 'tests/Magic.Capture.Core.Tests/VideoEditPolicyTests.cs': [
        'PlaybackRate_ChangesRenderedDurationWithoutChangingSourceDuration',
        'TimelineMap_MapsOutputTimeBackIntoSourceTimeline', 'Keyframes_InterpolateZoomAndPanLinearly',
        'PixelEffects_BlurPixelateAndZoomStayBounded', 'ProjectMigration_UpgradesSchemaTwoToCurrent'],
    ROOT / 'src/Magic.Capture.App/VideoEditing/VideoEditAdvancedRenderService.cs': [
        'VideoEditAdvancedRenderService', 'NormalizeSegmentsToBaseTimeline', 'PlaybackRate = 1.0',
        'MapOutputToBaseTimeline', 'ApplyFrameEffects', 'StagePcmAudioAsync', 'VideoEditPcmWavReader',
        'Mp4RecordingEncoder', 'RecordingAudioPolicy.SampleRate'],
    ROOT / 'src/Magic.Capture.App/ApplicationServices.cs': ['VideoEditAdvancedRenderService VideoEditAdvancedRender'],
    ROOT / 'src/Magic.Capture.App/App.xaml.cs': ['new VideoEditAdvancedRenderService', 'VideoEditAdvancedRender = videoEditAdvancedRender'],
    ROOT / 'src/Magic.Capture.App/Views/VideoEditorWindow.xaml': [
        'PlaybackRateBox', 'OutputFpsBox', 'FrameEffectKindCombo', 'AddFrameEffect_Click'],
    ROOT / 'src/Magic.Capture.App/Views/VideoEditorWindow.xaml.cs': [
        'AddFrameEffect_Click', 'RemoveFrameEffect_Click', 'PlaybackRateBox.Value', 'OutputFpsBox.Value',
        'VideoEditAdvancedRender.RenderMp4Async'],
    ROOT / 'src/Magic.Capture.Core/Recording/RecordingEffectsPolicy.cs': [
        'RecordingOutputFormat.M4a', 'IsAudioOnly', 'M4A audio-only recording requires system audio'],
    ROOT / 'src/Magic.Capture.Core/Recording/RecordingPolicy.cs': [
        'AudioOnly', 'CurrentSchemaVersion = 5'],
    ROOT / 'src/Magic.Capture.App/Recording/M4aAudioRecordingEncoder.cs': [
        'M4aAudioRecordingEncoder', 'CreateM4a', 'RecordingAudioPolicy.SampleRate', 'AudioEncodingQuality.High'],
    ROOT / 'src/Magic.Capture.App/Recording/RecordingSessionService.cs': [
        'StartAudioOnlyAsync', 'RunAudioOnlyAsync', 'RecordingTargetKind.AudioOnly', 'AudioBlockCount'],
    ROOT / 'src/Magic.Capture.App/Recording/RecordingRecoveryStore.cs': ['AudioBlockCount'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': ['M4A / AAC audio only', 'Tag="M4a"'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs': [
        'StartAudioOnlyRecordingAsync', 'RecordingOutputPolicy.IsAudioOnly',
        'RecordingWebcamCheck.IsChecked = false', 'RecordingLiveZoomCheck.IsChecked = false'],
    ROOT / 'tests/Magic.Capture.Core.Tests/RecordingEffectsPolicyTests.cs': [
        'M4a_IsAudioOnlyAndRequiresAnAudioSource', 'M4a_RejectsVisualOnlyFeatures'],
    ROOT / 'tests/Magic.Capture.Core.Tests/RecordingManifestPolicyTests.cs': [
        'AudioOnlyJournalSchema_IsVersion5AndKeepsLegacyReadable'],
    ROOT / 'docs/RELEASE_NOTES_4.6.0.md': [
        'Editor Retiming, Frame Effects & Audio-Only Recording', 'Done: **379**', '#83', '#89', '#96'],
    ROOT / 'docs/WINDOWS_RELEASE_CHECKLIST.md': [
        '4.6 editor retiming / frame-effects / audio-only gate', '0.25×', '4×', 'M4A audio-only',
        'schema v3', 'recording journal schema v5'],
}.items():
    if not path.exists():
        ERRORS.append(f'4.6 editor/audio-only contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'4.6 editor/audio-only contract missing in {path.relative_to(ROOT)}: {needle}')

# 4.7 General Timeline / Keyframes source contracts.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/VideoEditing/VideoEditModels.cs': [
        'CurrentVersion = 4', 'VideoEditTextStyle? TextStyle', 'VideoEditAudioEnvelope? AudioEnvelope',
        'VideoEditEasingKind Easing = VideoEditEasingKind.Linear'],
    ROOT / 'src/Magic.Capture.Core/VideoEditing/VideoEditAnimation.cs': [
        'VideoEditEasingKind', 'EaseInOut', 'Hold', 'MaximumAnimatedOverlayPieces = 2048',
        'MaximumAnimationSamplesPerSecond = 12', 'VideoEditOverlayAnimationPolicy',
        'VideoEditAudioEnvelope', 'CreateFadeAndDuck', 'MaximumKeyframesPerSegment = 128',
        'VideoEditTextStyle', 'MaximumOutlineWidth = 12.0'],
    ROOT / 'src/Magic.Capture.Core/VideoEditing/VideoEditFrameEffects.cs': [
        'VideoEditEasing.Apply', 'x.AudioEnvelope?.Keyframes.Count'],
    ROOT / 'src/Magic.Capture.Core/VideoEditing/VideoEditTimelineMap.cs': [
        'OutputOffsetInSegment'],
    ROOT / 'src/Magic.Capture.Core/VideoEditing/VideoEditPolicy.cs': [
        'ValidateAudioEnvelope', 'ValidateTextStyle', 'audio-envelope keyframe', 'keyframe opacity'],
    ROOT / 'src/Magic.Capture.App/VideoEditing/VideoEditCompositionService.cs': [
        'VideoEditOverlayAnimationPolicy.BuildPieces', 'AddRasterOverlaysAsync', 'AddSolidOverlayPieces',
        'piece.Value.Opacity'],
    ROOT / 'src/Magic.Capture.App/VideoEditing/VideoEditOverlayAssetStore.cs': [
        'VideoEditTextStyle.Normalize', 'textStyle.FontFamily', 'textStyle.Italic', 'textStyle.Underline',
        'textStyle.ShadowArgb', 'textStyle.OutlineArgb'],
    ROOT / 'src/Magic.Capture.App/VideoEditing/VideoEditAdvancedRenderService.cs': [
        'VideoEditAudioEnvelopePolicy.Evaluate', 'mapped.OutputOffsetInSegment', 'ApplyGain'],
    ROOT / 'src/Magic.Capture.App/Views/VideoEditorWindow.xaml': [
        'OverlayKeyframeList', 'FrameKeyframeList', 'AudioKeyframeList', 'Apply fade / duck',
        'TitleFontFamilyBox', 'OverlayFontFamilyBox', 'Ease in/out'],
    ROOT / 'src/Magic.Capture.App/Views/VideoEditorWindow.xaml.cs': [
        'AddOverlayKeyframe_Click', 'AddFrameKeyframe_Click', 'AddAudioKeyframe_Click',
        'ApplyAudioEnvelopePreset_Click', 'ApplyTitleStyle_Click', 'CommitValidatedProject',
        'SelectedEasing', 'BuildTextStyle'],
    ROOT / 'tests/Magic.Capture.Core.Tests/VideoEditPolicyTests.cs': [
        'Easing_IsDeterministicAndBounded', 'OverlayAnimation_InterpolatesBoundsAndOpacityUsingEasing',
        'AudioEnvelope_FadeAndDuckRemainWithinTwoHundredPercent', 'TextStyle_NormalizesFontAndDecorationFields',
        'FrameEffect_EasingControlsInterpolation', 'ProjectMigration_UpgradesSchemaThreeToFour',
        'TimelineMap_ExposesOutputLocalOffsetForEnvelopeAtTwoTimesSpeed', 'AnimatedOverlayPieces_AreBounded'],
    ROOT / 'docs/RELEASE_NOTES_4.7.0.md': [
        'General Timeline, Keyframes & Audio Envelopes', '379 Done', 'schema v4', 'Fade In/Out', 'Duck'],
    ROOT / 'docs/WINDOWS_RELEASE_CHECKLIST.md': [
        '4.7 general timeline / keyframe gate', 'schema v4', '2,048', '0.25x', '4x'],
}.items():
    if not path.exists():
        ERRORS.append(f'4.7 timeline/keyframe contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'4.7 timeline/keyframe contract missing in {path.relative_to(ROOT)}: {needle}')

# 4.8 Local Step Recorder / Documentation Builder source contracts.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/Documentation/DocumentationModels.cs': [
        'DocumentationProject', 'DocumentationStep', 'DocumentationTargetEvidence', 'DocumentationMouseButton'],
    ROOT / 'src/Magic.Capture.Core/Documentation/DocumentationPolicy.cs': [
        'MaximumSteps = 512', 'PlanCapture', 'GenerateDescription', 'GenerateProjectTitle',
        'MoveStep', 'RemoveStep', 'DuplicateStep', 'MergeSteps', 'IsSafeKeyboardGesture'],
    ROOT / 'tests/Magic.Capture.Core.Tests/DocumentationPolicyTests.cs': [
        'PlanCapture', 'GenerateDescription', 'IsSafeKeyboardGesture', 'MoveStep'],
    ROOT / 'src/Magic.Capture.Core/Documentation/DocumentationArchivePolicy.cs': [
        'MaximumManifestBytes', 'MaximumImageBytes', 'MaximumTotalImageBytes', 'ValidateEntries', 'IsCanonicalEntryName'],
    ROOT / 'src/Magic.Capture.Core/Documentation/DocumentationTextExport.cs': [
        'BuildHtml', 'BuildMarkdown', 'BuildSelfContainedHtml', 'HtmlEncode', 'MarkdownEscape'],
    ROOT / 'src/Magic.Capture.Core/Documentation/DocumentationDocxWriter.cs': [
        'DocumentationDocxWriter', '[Content_Types].xml', 'word/document.xml', 'word/media/'],
    ROOT / 'tests/Magic.Capture.Core.Tests/DocumentationArchivePolicyTests.cs': [
        'ValidateEntries', 'IsCanonicalEntryName', 'MaximumImageBytes'],
    ROOT / 'tests/Magic.Capture.Core.Tests/DocumentationTextExportTests.cs': [
        'BuildHtml', 'BuildMarkdown', 'BuildSelfContainedHtml', 'DocumentationDocxWriter'],
    ROOT / 'src/Magic.Capture.App/Documentation/StepRecorderInputTracker.cs': [
        'SetWindowsHookExW', 'WhMouseLl', 'WhKeyboardLl', 'CallNextHookEx', 'RecordingSafeKeyFormatter.Format',
        'DocumentationPolicy.IsSafeKeyboardGesture', 'UnhookWindowsHookEx', 'ActionCaptured'],
    ROOT / 'src/Magic.Capture.App/Documentation/StepRecorderService.cs': [
        'Channel.CreateBounded', 'CaptureForMonitorAsync', 'UiAutomationSnapshotRules.FindSnapTarget',
        'HasKeyboardFocus == true', 'DocumentationPolicy.PlanCapture', '_screenCapture.Capture', 'StepCaptured', 'StopAsync'],
    ROOT / 'src/Magic.Capture.App/ApplicationServices.cs': [
        'using Magic.Capture.App.Documentation;', 'StepRecorderService StepRecorder',
        'DocumentationProjectStore DocumentationProjects', 'DocumentationCardRenderer DocumentationRenderer',
        'DocumentationExportService DocumentationExport'],
    ROOT / 'src/Magic.Capture.App/App.xaml.cs': [
        'using Magic.Capture.App.Documentation;', 'new StepRecorderService(', 'StepRecorder = stepRecorder',
        'new DocumentationProjectStore()', 'new DocumentationCardRenderer()', 'new DocumentationExportService(',
        'DocumentationProjects = documentationProjects', 'DocumentationRenderer = documentationRenderer',
        'DocumentationExport = documentationExport'],
    ROOT / 'src/Magic.Capture.App/Documentation/DocumentationProjectStore.cs': [
        'DocumentationProjectPackage', 'ZipArchive', 'DocumentationArchivePolicy.ValidateEntries',
        'BoundedStreamReader.ReadExactAsync', 'DocumentationProject.CurrentSchemaVersion', 'File.Move(temp'],
    ROOT / 'src/Magic.Capture.App/Documentation/DocumentationCardRenderer.cs': [
        'MaximumLongImagePixels = 150_000_000', 'RenderStepCard', 'RenderLongImage', 'FillEllipse', 'DrawString'],
    ROOT / 'src/Magic.Capture.App/Documentation/DocumentationExportService.cs': [
        'ExportLongPngAsync', 'ExportPdfAsync', 'ExportDocxAsync', 'ExportHtmlAsync',
        'ExportMarkdownAsync', 'ExportOfflineHtmlAsync', 'DocumentationDocxWriter.Write',
        'DocumentationTextExport.BuildHtml', 'DocumentationTextExport.BuildMarkdown',
        'DocumentationTextExport.BuildSelfContainedHtml'],
    ROOT / 'src/Magic.Capture.App/Views/DocumentationWindow.xaml': [
        'StartStopRecording_Click', 'AddImage_Click', 'ApplyStep_Click', 'MoveStepUp_Click',
        'MoveStepDown_Click', 'DuplicateStep_Click', 'MergeNext_Click', 'RemoveStep_Click',
        'OpenProject_Click', 'SaveProject_Click', 'ExportLongPng_Click', 'ExportPdf_Click',
        'ExportDocx_Click', 'ExportHtml_Click', 'ExportMarkdown_Click', 'ExportOfflineHtml_Click'],
    ROOT / 'src/Magic.Capture.App/Views/DocumentationWindow.xaml.cs': [
        'StepCaptured +=', 'DispatcherQueue.TryEnqueue', 'DocumentationPolicy.MoveStep',
        'DocumentationPolicy.DuplicateStep', 'DocumentationPolicy.MergeSteps', 'DocumentationProjects.SaveAsync',
        'DocumentationExport.ExportLongPngAsync', 'DocumentationExport.ExportPdfAsync',
        'DocumentationExport.ExportDocxAsync', 'DocumentationExport.ExportHtmlAsync',
        'DocumentationExport.ExportMarkdownAsync', 'DocumentationExport.ExportOfflineHtmlAsync'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': ['OpenDocumentationBuilder_Click', 'Step Recorder &amp; Documentation Builder'],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs': [
        'OpenDocumentationBuilder_Click', 'EnsurePlus(ProductFeature.AdvancedWorkflows)', 'new DocumentationWindow(Services)'],
}.items():
    if not path.exists():
        ERRORS.append(f'4.8 documentation contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'4.8 documentation contract missing in {path.relative_to(ROOT)}: {needle}')

# 4.9 Documentation Publishing source contracts.
for path, needles in {
    ROOT / 'src/Magic.Capture.Core/Documentation/DocumentationTemplateCatalog.cs': [
        'DocumentationTemplateCatalog', 'clean', 'compact', 'presentation', 'print', 'NormalizeId'],
    ROOT / 'src/Magic.Capture.Core/Documentation/DocumentationPolicy.cs': [
        'DocumentationTemplateCatalog.NormalizeId'],
    ROOT / 'src/Magic.Capture.Core/Documentation/DocumentationTextExport.cs': [
        'DocumentationContentsEntry', 'BuildContents', 'template-', 'AppendHtmlContents', 'logoHref'],
    ROOT / 'tests/Magic.Capture.Core.Tests/DocumentationPolicyTests.cs': [
        'Normalize_CanonicalizesDocumentationTemplate', 'TemplateCatalog_ExposesFourStablePublishingProfiles'],
    ROOT / 'tests/Magic.Capture.Core.Tests/DocumentationTextExportTests.cs': [
        'BuildContents_EmitsSectionsOnceAndKeepsStepOrder', 'BuildHtml_RendersTemplateHeaderContentsFooterAndLogo',
        'DocumentationDocxWriter_WritesContentsAndRealHeaderFooterParts'],
    ROOT / 'src/Magic.Capture.App/Views/DocumentationWindow.xaml': [
        'CanDragItems="True"', 'CanReorderItems="True"', 'DragItemsCompleted="StepList_DragItemsCompleted"',
        'ProjectHeaderBox', 'TemplateComboBox', 'ChooseLogo_Click', 'ClearLogo_Click'],
    ROOT / 'src/Magic.Capture.App/Views/DocumentationWindow.xaml.cs': [
        'StepList_DragItemsCompleted', 'DocumentationTemplateCatalog.All', 'LogoImageKey = "logo.png"',
        'ChooseLogo_Click', 'ClearLogo_Click', '_logoPng'],
    ROOT / 'src/Magic.Capture.App/Documentation/DocumentationCardRenderer.cs': [
        'RenderOverviewCard', 'DocumentationTemplateCatalog.Get', 'project.Header', 'project.Footer', 'logoPng'],
    ROOT / 'src/Magic.Capture.Core/Documentation/DocumentationDocxWriter.cs': [
        'word/header1.xml', 'word/footer1.xml', 'w:headerReference', 'w:footerReference', 'BuildContents'],
    ROOT / 'src/Magic.Capture.App/Documentation/DocumentationExportService.cs': [
        'byte[]? logoPng', 'logo.png', 'BuildSelfContainedHtml(project, images, logoPng)'],
    ROOT / 'src/Magic.Capture.App/Documentation/DocumentationProjectStore.cs': [
        'Documentation manifest references a logo that is not present.',
        'Documentation package contains a logo that is not referenced by the manifest.',
        'ValidatePng(logoPng, "Documentation logo")'],
}.items():
    if not path.exists():
        ERRORS.append(f'4.9 documentation publishing contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'4.9 documentation publishing contract missing in {path.relative_to(ROOT)}: {needle}')

# 4.8 release truth: docs/checklist must describe the shipped source and its runtime boundary.
for path, needles in {
    ROOT / 'docs/RELEASE_NOTES_4.8.0.md': [
        'Local Step Recorder & Documentation Builder', '410 Done', '.magicdoc',
        'long PNG', 'DOCX', 'offline HTML', 'session-scoped'],
    ROOT / 'docs/WINDOWS_RELEASE_CHECKLIST.md': [
        '4.8.0', '4.8 step recorder / documentation gate', '.magicdoc',
        'password', 'session-scoped hooks', 'DOCX', 'offline HTML'],
    ROOT / 'docs/FEATURE_MATRIX.md': [
        'Step Recorder & Documentation Builder', '.magicdoc', 'AdvancedWorkflows'],
}.items():
    if not path.exists():
        ERRORS.append(f'4.8 documentation release contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'4.8 documentation release contract missing in {path.relative_to(ROOT)}: {needle}')

# 4.9 release truth: documentation publishing must be represented consistently in release artifacts.
for path, needles in {
    ROOT / 'docs/RELEASE_NOTES_4.9.0.md': [
        'Documentation Publishing', '415 Done', 'drag reorder', 'page templates',
        'header/footer', 'logo', 'table of contents'],
    ROOT / 'docs/WINDOWS_RELEASE_CHECKLIST.md': [
        '4.9 documentation publishing gate', 'drag reorder', 'presentation',
        'logo', 'table of contents', 'header/footer'],
    ROOT / 'docs/FEATURE_MATRIX.md': [
        'Documentation Publishing', 'TemplateComboBox', 'generated table of contents'],
}.items():
    if not path.exists():
        ERRORS.append(f'4.9 documentation release contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'4.9 documentation release contract missing in {path.relative_to(ROOT)}: {needle}')


# 4.10 release truth: editable-project recovery must be represented consistently in release artifacts.
for path, needles in {
    ROOT / 'docs/RELEASE_NOTES_4.10.0.md': [
        'Editable Project Recovery', '416 Done', '1.5-second', '8', '64 KiB', '14 days',
        'Recover', 'Discard', 'never overwrites'],
    ROOT / 'docs/WINDOWS_RELEASE_CHECKLIST.md': [
        'kill the process', 'newest eight', 'journal that points at another session',
        'without modifying or overwriting the original'],
    ROOT / 'docs/FEATURE_MATRIX.md': [
        'crash-safe local autosave recovery', 'EditableProjectRecoveryStore.cs'],
}.items():
    if not path.exists():
        ERRORS.append(f'4.10 recovery release contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'4.10 recovery release contract missing in {path.relative_to(ROOT)}: {needle}')

# 4.11 release truth: Workflow Runtime v4 must be represented consistently in release artifacts.
for path, needles in {
    ROOT / 'docs/RELEASE_NOTES_4.11.0.md': [
        'Workflow Runtime v4', '426 Done', 'typed parameters', 'Prompt Text', 'Prompt Choice',
        'subworkflow', 'dry-run', '500', '100', 'privacy-safe'],
    ROOT / 'docs/WINDOWS_RELEASE_CHECKLIST.md': [
        '4.11.0', 'Workflow Runtime v4', 'Prompt Text', 'subworkflow cycle',
        'dry-run', '500', 'trace privacy'],
    ROOT / 'docs/FEATURE_MATRIX.md': [
        '4.11.0', 'Workflow Runtime v4', 'WorkflowBatchRunner.cs', 'WorkflowTraceStore.cs',
        'PromptText', 'RunWorkflow'],
    ROOT / 'README.md': [
        'Workflow Runtime v4', 'schema-v4 custom workflows', 'WorkflowTraceStore'],
}.items():
    if not path.exists():
        ERRORS.append(f'4.11 workflow release contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'4.11 workflow release contract missing in {path.relative_to(ROOT)}: {needle}')

# 4.12 release truth: local automation triggers must be represented consistently.
for path, needles in {
    ROOT / 'docs/RELEASE_NOTES_4.12.0.md': [
        'Automation Triggers', '433 Done', '#438', '#444', 'metadata-only',
        '20 accepted attempts', 'AdvancedWorkflows'],
    ROOT / 'docs/WINDOWS_RELEASE_CHECKLIST.md': [
        '4.12 Automation Triggers', 'Task Scheduler', 'Clipboard trigger', 'process-start trigger',
        'trigger_kind_mismatch', 'entitlement', 'newest-200'],
    ROOT / 'docs/FEATURE_MATRIX.md': [
        'Automation Triggers — 4.12.0', 'ResidentWorkflowTriggerEngine.cs',
        'WindowsTaskSchedulerService.cs', 'WorkflowTriggerHotkeyService.cs'],
    ROOT / 'README.md': [
        '4.12 Automation Triggers', '64 triggers', 'metadata records'],
}.items():
    if not path.exists():
        ERRORS.append(f'4.12 automation release contract file missing: {path.relative_to(ROOT)}')
        continue
    source = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in source:
            ERRORS.append(f'4.12 automation release contract missing in {path.relative_to(ROOT)}: {needle}')


# Source-truth audit invariants. Historical 3.8 constraints apply only to a 3.8 source tree.
audit_path_capture = ROOT / 'release/feature-audit-660.json'
if audit_path_capture.exists():
    try:
        audit_capture = json.loads(audit_path_capture.read_text(encoding='utf-8'))
        statuses_capture = {int(item['id']): item['status'] for item in audit_capture.get('features', [])}
        source_version_capture = audit_capture.get('sourceVersion')
        if source_version_capture == '3.8.0':
            for feature_id in (2, 3, 40, 41, 42):
                if statuses_capture.get(feature_id) != 'Done':
                    ERRORS.append(f'3.8 audit feature #{feature_id} must be Done')
            for feature_id in (31, 32):
                if statuses_capture.get(feature_id) != 'Foundation':
                    ERRORS.append(f'3.8 audit feature #{feature_id} must remain Foundation until real GPU backend implementation exists')
            if statuses_capture.get(33) != 'Partial':
                ERRORS.append('3.8 audit feature #33 must remain Partial until a real WGC/DXGI→GDI fallback chain exists')
        elif source_version_capture == '3.9.0':
            for feature_id in (31, 32, 33, 34):
                if statuses_capture.get(feature_id) != 'Done':
                    ERRORS.append(f'3.9 audit feature #{feature_id} must be Done for the multi-backend capture architecture')
        elif source_version_capture == '4.0.0':
            for feature_id in (46, 47, 48, 49, 57, 58, 59, 60, 61, 68, 69, 70, 71, 79, 81):
                if statuses_capture.get(feature_id) != 'Done':
                    ERRORS.append(f'4.0 audit feature #{feature_id} must be Done for visual recording')
            for feature_id in (77, 82, 609):
                if statuses_capture.get(feature_id) != 'Partial':
                    ERRORS.append(f'4.0 audit feature #{feature_id} must remain Partial until Windows runtime/recovery completion')
            for feature_id in (50, 51, 52, 53, 54, 55, 56, 72, 73, 74, 75, 76, 78, 80, 83):
                if statuses_capture.get(feature_id) != 'Missing':
                    ERRORS.append(f'4.0 audit feature #{feature_id} must remain Missing; 4.0 is visual-only MP4/H.264')
        elif source_version_capture == '4.1.0':
            for feature_id in (51, 52, 53):
                if statuses_capture.get(feature_id) != 'Done':
                    ERRORS.append(f'4.1 audit feature #{feature_id} must be Done for native recording audio')
            for feature_id in (77, 82, 609):
                if statuses_capture.get(feature_id) != 'Partial':
                    ERRORS.append(f'4.1 audit feature #{feature_id} must remain Partial until Windows runtime/recovery completion')
            for feature_id in (50, 54, 55, 56, 72, 73, 74, 75, 76, 78, 80, 83):
                if statuses_capture.get(feature_id) != 'Missing':
                    ERRORS.append(f'4.1 audit feature #{feature_id} must remain Missing; it is outside native A/V recording scope')
        elif source_version_capture == '4.2.0':
            for feature_id in (51, 52, 53, 54, 55, 56):
                if statuses_capture.get(feature_id) != 'Done':
                    ERRORS.append(f'4.2 audit feature #{feature_id} must be Done for native A/V + webcam/PiP recording')
            for feature_id in (77, 82, 609):
                if statuses_capture.get(feature_id) != 'Partial':
                    ERRORS.append(f'4.2 audit feature #{feature_id} must remain Partial until Windows runtime/recovery completion')
            for feature_id in (50, 72, 73, 74, 75, 76, 78, 80, 83):
                if statuses_capture.get(feature_id) != 'Missing':
                    ERRORS.append(f'4.2 audit feature #{feature_id} must remain Missing; it is outside webcam/PiP scope')
        elif source_version_capture == '4.3.0':
            for feature_id in (51, 52, 53, 54, 55, 56, 62, 63, 64, 66, 67, 75):
                if statuses_capture.get(feature_id) != 'Done':
                    ERRORS.append(f'4.3 audit feature #{feature_id} must be Done for A/V + webcam + recording effects/animated output')
            for feature_id in (76, 77, 82, 609):
                if statuses_capture.get(feature_id) != 'Partial':
                    ERRORS.append(f'4.3 audit feature #{feature_id} must remain Partial until WebP/runtime/recovery completion')
            for feature_id in (50, 65, 72, 73, 74, 78, 80, 83):
                if statuses_capture.get(feature_id) != 'Missing':
                    ERRORS.append(f'4.3 audit feature #{feature_id} must remain Missing; it is outside this recording-effects scope')
        elif source_version_capture == '4.4.0':
            for feature_id in (51, 52, 53, 54, 55, 56, 62, 63, 64, 66, 67, 75, 84, 85, 86, 87, 88, 90, 91, 93, 100):
                if statuses_capture.get(feature_id) != 'Done':
                    ERRORS.append(f'4.4 audit feature #{feature_id} must be Done for the existing recorder plus native clip-editor implementation')
            for feature_id in (76, 77, 82, 609):
                if statuses_capture.get(feature_id) != 'Partial':
                    ERRORS.append(f'4.4 audit feature #{feature_id} must remain Partial until WebP/runtime/recovery completion')
            for feature_id in (50, 65, 72, 73, 74, 78, 80, 83, 89, 92, 94, 95, 96, 97, 98, 99):
                if statuses_capture.get(feature_id) != 'Missing':
                    ERRORS.append(f'4.4 audit feature #{feature_id} must remain Missing; no end-to-end source implementation exists in this wave')
        elif source_version_capture == '4.5.0':
            for feature_id in (51, 52, 53, 54, 55, 56, 62, 63, 64, 66, 67, 75, 84, 85, 86, 87, 88, 90, 91, 92, 93, 94, 95, 97, 98, 99, 100):
                if statuses_capture.get(feature_id) != 'Done':
                    ERRORS.append(f'4.5 audit feature #{feature_id} must be Done for the recorder plus advanced clip-editor implementation')
            for feature_id in (76, 77, 82, 609):
                if statuses_capture.get(feature_id) != 'Partial':
                    ERRORS.append(f'4.5 audit feature #{feature_id} must remain Partial until WebP/runtime/recovery completion')
            for feature_id in (50, 65, 72, 73, 74, 78, 80, 83, 89, 96):
                if statuses_capture.get(feature_id) != 'Missing':
                    ERRORS.append(f'4.5 audit feature #{feature_id} must remain Missing; it is outside advanced clip-editor scope')
        elif source_version_capture == '4.6.0':
            for feature_id in (51, 52, 53, 54, 55, 56, 62, 63, 64, 66, 67, 75, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100):
                if statuses_capture.get(feature_id) != 'Done':
                    ERRORS.append(f'4.6 audit feature #{feature_id} must be Done for the recorder plus retiming/frame-effects/audio-only implementation')
            for feature_id in (76, 77, 82, 609):
                if statuses_capture.get(feature_id) != 'Partial':
                    ERRORS.append(f'4.6 audit feature #{feature_id} must remain Partial until WebP/runtime/recovery completion')
            for feature_id in (50, 65, 72, 73, 74, 78, 80):
                if statuses_capture.get(feature_id) != 'Missing':
                    ERRORS.append(f'4.6 audit feature #{feature_id} must remain Missing; it is outside this wave')
        elif source_version_capture == '4.7.0':
            for feature_id in (51, 52, 53, 54, 55, 56, 62, 63, 64, 66, 67, 75, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100):
                if statuses_capture.get(feature_id) != 'Done':
                    ERRORS.append(f'4.7 audit feature #{feature_id} must remain Done while the general timeline deepens existing editor capabilities')
            for feature_id in (76, 77, 82, 609):
                if statuses_capture.get(feature_id) != 'Partial':
                    ERRORS.append(f'4.7 audit feature #{feature_id} must remain Partial until WebP/runtime/recovery completion')
            for feature_id in (50, 65, 72, 73, 74, 78, 80):
                if statuses_capture.get(feature_id) != 'Missing':
                    ERRORS.append(f'4.7 audit feature #{feature_id} must remain Missing; it is outside this wave')
        elif source_version_capture == '4.8.0':
            for feature_id in range(214, 233):
                if statuses_capture.get(feature_id) != 'Done':
                    ERRORS.append(f'4.8 audit feature #{feature_id} must be Done for the Step Recorder / documentation implementation')
            for feature_id in (233, 235, 236, 237, 238, 242, 243, 244, 245, 246, 248, 249):
                if statuses_capture.get(feature_id) != 'Done':
                    ERRORS.append(f'4.8 audit feature #{feature_id} must be Done for the source-wired documentation builder')
            for feature_id in (234, 239, 240, 241, 247):
                if statuses_capture.get(feature_id) != 'Foundation':
                    ERRORS.append(f'4.8 audit feature #{feature_id} must remain Foundation until its dedicated end-user UX is complete')
            expected_counts = {'Done': 410, 'Partial': 64, 'Foundation': 127, 'Missing': 37, 'ReleaseTest': 22}
            if audit_capture.get('counts') != expected_counts:
                ERRORS.append(f'4.8 audit counts must equal {expected_counts}')
        elif source_version_capture == '4.9.0':
            for feature_id in range(214, 250):
                if statuses_capture.get(feature_id) != 'Done':
                    ERRORS.append(f'4.9 audit feature #{feature_id} must be Done for the complete Documentation Publishing workflow')
            expected_counts = {'Done': 415, 'Partial': 64, 'Foundation': 122, 'Missing': 37, 'ReleaseTest': 22}
            if audit_capture.get('counts') != expected_counts:
                ERRORS.append(f'4.9 audit counts must equal {expected_counts}')
        elif source_version_capture == '4.10.0':
            for feature_id in range(214, 250):
                if statuses_capture.get(feature_id) != 'Done':
                    ERRORS.append(f'4.10 audit feature #{feature_id} must remain Done from Documentation Publishing')
            if statuses_capture.get(254) != 'Done':
                ERRORS.append('4.10 audit feature #254 must be Done for editable-project autosave recovery')
            if statuses_capture.get(606) != 'Foundation':
                ERRORS.append('4.10 audit feature #606 must remain Foundation; universal editor autosave is not claimed')
            expected_counts = {'Done': 416, 'Partial': 64, 'Foundation': 122, 'Missing': 36, 'ReleaseTest': 22}
            if audit_capture.get('counts') != expected_counts:
                ERRORS.append(f'4.10 audit counts must equal {expected_counts}')
        elif source_version_capture == '4.11.0':
            for feature_id in (420, 421, 422, 423, 425, 426, 427, 430, 431, 433):
                if statuses_capture.get(feature_id) != 'Done':
                    ERRORS.append(f'4.11 audit feature #{feature_id} must be Done for Workflow Runtime v4')
            if statuses_capture.get(424) != 'Foundation':
                ERRORS.append('4.11 audit feature #424 must remain Foundation; in-workflow image collection loop is not claimed')
            if statuses_capture.get(432) != 'Foundation':
                ERRORS.append('4.11 audit feature #432 must remain Foundation; resume/checkpoint semantics are deferred')
            for feature_id in (438, 439, 440, 441, 442, 443):
                if statuses_capture.get(feature_id) != 'Foundation':
                    ERRORS.append(f'4.11 audit feature #{feature_id} must remain Foundation; background trigger automation is deferred')
            if statuses_capture.get(444) != 'Partial':
                ERRORS.append('4.11 audit feature #444 must remain Partial; hotkey trigger expansion is outside this wave')
            expected_counts = {'Done': 426, 'Partial': 62, 'Foundation': 114, 'Missing': 36, 'ReleaseTest': 22}
            if audit_capture.get('counts') != expected_counts:
                ERRORS.append(f'4.11 audit counts must equal {expected_counts}')
        elif source_version_capture == '4.12.0':
            for feature_id in (420, 421, 422, 423, 425, 426, 427, 430, 431, 433):
                if statuses_capture.get(feature_id) != 'Done':
                    ERRORS.append(f'4.12 audit feature #{feature_id} must retain Workflow Runtime v4 Done status')
            for feature_id in range(438, 445):
                if statuses_capture.get(feature_id) != 'Done':
                    ERRORS.append(f'4.12 audit feature #{feature_id} must be Done for Automation Triggers')
            if statuses_capture.get(424) != 'Foundation':
                ERRORS.append('4.12 audit feature #424 must remain Foundation; in-workflow image loop is not claimed')
            if statuses_capture.get(432) != 'Foundation':
                ERRORS.append('4.12 audit feature #432 must remain Foundation; workflow resume/checkpoint is not claimed')
            expected_counts = {'Done': 433, 'Partial': 61, 'Foundation': 108, 'Missing': 36, 'ReleaseTest': 22}
            if audit_capture.get('counts') != expected_counts:
                ERRORS.append(f'4.12 audit counts must equal {expected_counts}')
        elif source_version_capture and (source_version_capture.startswith('3.') or source_version_capture.startswith('4.')):
            # Future source trees carry their own wave contract; do not force historical 3.8/3.9 states.
            pass
    except Exception as exc:
        ERRORS.append(f'capture audit contract could not be evaluated: {exc}')


# 4.10 editable-project autosave recovery source contract.
recovery_contracts = {
    ROOT / 'src/Magic.Capture.Core/Projects/EditableProjectRecoveryPolicy.cs': [
        'public const int CurrentJournalSchemaVersion = 1;',
        'public const int MaximumActiveSessions = 8;',
        'TimeSpan.FromDays(14)',
        'public sealed record EditableProjectRecoveryJournal',
        'public sealed record EditableProjectRecoveryCandidate',
        'BuildSnapshotFileName',
        'dirtyRevision:D20',
        'Path.GetFileName(snapshotFileName)',
        "snapshotFileName.Contains('/') || snapshotFileName.Contains('\\\\')",
        '.EndsWith(".magiccapture", StringComparison.OrdinalIgnoreCase)',
        'OrderByDescending(candidate => candidate.Journal.UpdatedUtc)',
        '.Take(MaximumActiveSessions)',
    ],
    ROOT / 'src/Magic.Capture.App/Persistence/AppPaths.cs': [
        'EditableProjectRecoveryRoot = Path.Combine(Root, "recovery", "editable-projects")',
        'Directory.CreateDirectory(EditableProjectRecoveryRoot);',
        'public string EditableProjectRecoveryRoot { get; }',
    ],
    ROOT / 'src/Magic.Capture.App/Persistence/EditableProjectRecoveryStore.cs': [
        'internal sealed class EditableProjectRecoveryStore',
        'EditableProjectArchivePolicy.MaximumArchiveBytes',
        'BoundedStreamReader.ReadExactAsync',
        '_editableProjects.SaveAsync',
        '_editableProjects.LoadAsync',
        'File.Move(tempJournal, journalPath, overwrite: true)',
        'EditableProjectRecoveryPolicy.SelectCandidates',
        'public async Task DeleteAsync',
        'public async Task PruneAsync',
        'private readonly SemaphoreSlim _gate = new(1, 1);',
        'PruneCoreAsync',
        'PruneStaleTempFiles',
        'currentJournal != item.Journal',
        'EditableProjectRecoveryPolicy.BuildSnapshotFileName(sessionId, dirtyRevision)',
        'previousSnapshotFileName',
        'CanDeleteJournalSnapshot',
        'EditableProjectRecoveryPolicy.BuildSnapshotFileName(journal.SessionId, journal.DirtyRevision)',
    ],
    ROOT / 'src/Magic.Capture.App/Views/AnnotationWindow.xaml.cs': [
        'TimeSpan.FromMilliseconds(1500)',
        'DispatcherQueue.CreateTimer()',
        'ScheduleRecoveryAutosave();',
        'await FlushRecoveryAutosaveAsync()',
        'await _services.EditableProjectRecovery.DeleteAsync',
        '_appWindow.Closing += AnnotationAppWindow_Closing;',
        'private long _recoveryGeneration;',
        'private bool _closeCleanupComplete;',
        'var generation = _recoveryGeneration;',
        'generation != _recoveryGeneration',
        'HandleExplicitSaveSucceededAsync',
        'InvalidateAndDeleteRecoveryAsync',
        'args.Cancel = true;',
        '(Application.Current as App)?.IsExitRequested == true',
        'if ((Application.Current as App)?.IsExitRequested != true) Close();',
    ],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': [
        'x:Name="EditableProjectRecoveryCard"',
        'Click="RecoverEditableProject_Click"',
        'Click="DiscardEditableProjectRecovery_Click"',
    ],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs': [
        'RefreshEditableProjectRecoveryAsync',
        'RecoverEditableProject_Click',
        'DiscardEditableProjectRecovery_Click',
        'EditableProjectRecoveryCard.Visibility',
        'catch (Exception ex) when (IsInvalidRecoveryCandidate(ex))',
        'private static bool IsInvalidRecoveryCandidate(Exception ex)',
        'ex is InvalidDataException or JsonException',
    ],
    ROOT / 'src/Magic.Capture.App/ApplicationServices.cs': [
        'public required EditableProjectRecoveryStore EditableProjectRecovery { get; init; }',
    ],
    ROOT / 'src/Magic.Capture.App/App.xaml.cs': [
        'var editableProjectRecovery = new EditableProjectRecoveryStore(paths, editableProjects, log);',
        'EditableProjectRecovery = editableProjectRecovery,',
        'OpenRecoveredEditableProject',
    ],
}
for path, needles in recovery_contracts.items():
    if not path.exists():
        ERRORS.append(f'4.10 editable-project recovery contract missing file: {path.relative_to(ROOT)}')
        continue
    recovery_text = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in recovery_text:
            ERRORS.append(f'4.10 editable-project recovery contract missing in {path.relative_to(ROOT)}: {needle}')

annotation_recovery_path = ROOT / 'src/Magic.Capture.App/Views/AnnotationWindow.xaml.cs'
if annotation_recovery_path.exists():
    annotation_recovery_text = annotation_recovery_path.read_text(encoding='utf-8', errors='replace')
    push_start = annotation_recovery_text.find('private void PushUndo()')
    push_end = annotation_recovery_text.find('private async void Undo_Click', push_start)
    if push_start >= 0 and push_end > push_start and 'ScheduleRecoveryAutosave();' in annotation_recovery_text[push_start:push_end]:
        ERRORS.append('4.10 recovery dirty tracking must schedule after mutation commit, not inside PushUndo')
    open_start = annotation_recovery_text.find('private async void OpenProject_Click')
    open_end = annotation_recovery_text.find('private EditableProjectManifest BuildProjectManifest', open_start)
    if open_start >= 0 and open_end > open_start:
        open_section = annotation_recovery_text[open_start:open_end]
        if 'PushUndo();' in open_section or '_undo.Clear();' not in open_section or '_redo.Clear();' not in open_section:
            ERRORS.append('4.10 Open Project must reset undo/redo instead of carrying undo state across project identities')
    for committed_mutation in (
        '_layers.AddRange(plan.Layers);\n            ScheduleRecoveryAutosave();',
        '_layers.AddRange(edited.Layers);\n            _redo.Clear();\n            ScheduleRecoveryAutosave();',
        '_imageHeight = bitmap.Height;\n        ScheduleRecoveryAutosave();\n        await RefreshPreviewAsync();',
    ):
        if committed_mutation not in annotation_recovery_text:
            ERRORS.append(f'4.10 recovery committed-mutation contract missing: {committed_mutation}')


# 4.11 Workflow Runtime v4 source contract. Runtime remains on-demand/local: typed parameters,
# bounded interactive steps, subworkflow safety, batch execution, dry-run, and privacy-safe traces.
workflow_v4_contracts = {
    ROOT / 'src/Magic.Capture.Core/Workflows/WorkflowModels.cs': [
        'public enum WorkflowParameterKind',
        'public sealed record WorkflowParameterDefinition',
        'PromptText,',
        'PromptChoice,',
        'Confirm,',
        'Delay,',
        'RunWorkflow',
        'IReadOnlyList<WorkflowParameterDefinition>? Parameters = null',
    ],
    ROOT / 'src/Magic.Capture.Core/Workflows/WorkflowValidator.cs': [
        'workflow.SchemaVersion < 4',
        'WorkflowRuntimePolicy.RequiresSchemaV4',
    ],
    ROOT / 'src/Magic.Capture.Core/Workflows/WorkflowParameterResolver.cs': [
        'public static class WorkflowParameterResolver',
        'public sealed record WorkflowParameterResolution',
        'ResolveKnown',
        'ValidateResolvedValue',
    ],
    ROOT / 'src/Magic.Capture.Core/Workflows/WorkflowRuntimePolicy.cs': [
        'public const int MaximumSubworkflowDepth = 4;',
        'public const int MaximumBatchAssets = 500;',
        'public const int MaximumTraceEntries = 100;',
        'ParseDelayMilliseconds',
        'CanEnterSubworkflow',
        'IsSideEffecting',
        'RequiresSchemaV4',
    ],
    ROOT / 'src/Magic.Capture.App/Workflows/WorkflowExecutionContext.cs': [
        'PromptTextAsync',
        'PromptChoiceAsync',
        'ConfirmStepAsync',
        'ResolveWorkflowAsync',
        'bool DryRun = false',
        'IReadOnlyList<string>? WorkflowCallStack = null',
        'WorkflowStepStatus',
        'StartedUtc',
        'FinishedUtc',
    ],
    ROOT / 'src/Magic.Capture.App/Workflows/WorkflowExecutor.cs': [
        'WorkflowParameterResolver.ResolveKnown',
        'WorkflowStepKind.PromptText',
        'WorkflowStepKind.PromptChoice',
        'WorkflowStepKind.Confirm',
        'WorkflowStepKind.Delay',
        'WorkflowStepKind.RunWorkflow',
        'WorkflowRuntimePolicy.IsSideEffecting',
        'WorkflowRuntimePolicy.CanEnterSubworkflow',
        'DryRun',
    ],
    ROOT / 'src/Magic.Capture.App/Workflows/WorkflowStore.cs': [
        'WorkflowCatalog.BuiltIns.Select(workflow => workflow.Id)',
    ],
    ROOT / 'src/Magic.Capture.App/Workflows/WorkflowBatchRunner.cs': [
        'internal sealed class WorkflowBatchRunner',
        'WorkflowRuntimePolicy.MaximumBatchAssets',
        'WorkflowBatchExecutionResult',
        'foreach (var loader in assetLoaders)',
    ],
    ROOT / 'src/Magic.Capture.App/Workflows/WorkflowTraceStore.cs': [
        'internal sealed class WorkflowTraceStore',
        'WorkflowRuntimePolicy.MaximumTraceEntries',
        'WorkflowTraceRecord',
        'AtomicJsonFile',
        'MaximumWorkflowTraceJsonBytes',
    ],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml': [
        'x:Name="WorkflowBuilderParameterList"',
        'Click="WorkflowBuilderAddParameter_Click"',
        'Click="WorkflowBuilderApplyParameter_Click"',
        'Click="WorkflowBuilderRemoveParameter_Click"',
        'Click="WorkflowDryRun_Click"',
        'x:Name="WorkflowTraceList"',
        'SelectionChanged="WorkflowTraceList_SelectionChanged"',
    ],
    ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs': [
        'WorkflowBuilderParameterView',
        'WorkflowBuilderAddParameter_Click',
        'WorkflowBuilderApplyParameter_Click',
        'WorkflowBuilderRemoveParameter_Click',
        'WorkflowDryRun_Click',
        'RefreshWorkflowTracesAsync',
        'WorkflowTraceList_SelectionChanged',
        'Services.WorkflowBatchRunner',
        'app.ShouldRedactWorkflowAsync(workflow)',
    ],
    ROOT / 'src/Magic.Capture.App/App.xaml.cs': [
        'PrepareWorkflowAssetAsync',
        'WorkflowGraphContainsStepKindAsync',
        'ShouldRedactWorkflowAsync',
        'ApplyOutboundRedactionAsync(asset, redactWorkflow, "workflow", cancellationToken)',
    ],
    ROOT / 'src/Magic.Capture.App/ApplicationServices.cs': [
        'public required WorkflowBatchRunner WorkflowBatchRunner { get; init; }',
        'public required WorkflowTraceStore WorkflowTraces { get; init; }',
    ],
    ROOT / 'src/Magic.Capture.App/Persistence/AppPaths.cs': [
        'WorkflowTracesFile = Path.Combine(Root, "workflow-traces.json")',
        'public string WorkflowTracesFile { get; }',
    ],
}
for path, needles in workflow_v4_contracts.items():
    if not path.exists():
        ERRORS.append(f'4.11 workflow runtime contract missing file: {path.relative_to(ROOT)}')
        continue
    workflow_v4_text = path.read_text(encoding='utf-8', errors='replace')
    for needle in needles:
        if needle not in workflow_v4_text:
            ERRORS.append(f'4.11 workflow runtime contract missing in {path.relative_to(ROOT)}: {needle}')

workflow_trace_path = ROOT / 'src/Magic.Capture.App/Workflows/WorkflowTraceStore.cs'
if workflow_trace_path.exists():
    workflow_trace_text = workflow_trace_path.read_text(encoding='utf-8', errors='replace')
    for forbidden in ('result.Values', '.PngBytes', '.Stdout', '.Stderr', 'OcrText', 'ResponseBody'):
        if forbidden in workflow_trace_text:
            ERRORS.append(f'4.11 workflow trace privacy contract forbids payload reference: {forbidden}')

workflow_store_path = ROOT / 'src/Magic.Capture.App/Workflows/WorkflowStore.cs'
if workflow_store_path.exists():
    workflow_store_text = workflow_store_path.read_text(encoding='utf-8', errors='replace')
    if workflow_store_text.count('WorkflowCatalog.BuiltIns.Select(workflow => workflow.Id)') < 2:
        ERRORS.append('4.11 workflow store must reserve built-in ids on both load and save')

workflow_batch_path = ROOT / 'src/Magic.Capture.App/Workflows/WorkflowBatchRunner.cs'
if workflow_batch_path.exists():
    workflow_batch_text = workflow_batch_path.read_text(encoding='utf-8', errors='replace')
    if 'IReadOnlyList<CaptureAsset>' in workflow_batch_text or 'List<CaptureAsset>' in workflow_batch_text:
        ERRORS.append('4.11 workflow batch must use lazy loaders instead of retaining a capture collection')

# Regression: privacy traversal must revisit a workflow when a shallower path is discovered.
# A plain visited set can first see a node at the depth cap, then incorrectly skip the same
# node reached directly where its descendants are still executable.
app_v4_path = ROOT / 'src/Magic.Capture.App/App.xaml.cs'
if app_v4_path.exists():
    app_v4_text = app_v4_path.read_text(encoding='utf-8', errors='replace')
    if 'bestDepthByWorkflow' not in app_v4_text:
        ERRORS.append('4.11 workflow redaction graph must track best/minimum depth per workflow')
    if 'var visited = new HashSet<string>(StringComparer.Ordinal);' in app_v4_text:
        ERRORS.append('4.11 workflow redaction graph must not use a plain visited set that can hide a shallower executable path')

# Trace regression: executions that fail before a WorkflowExecutionResult exists must still
# leave privacy-safe metadata. The failure API intentionally accepts no exception/message payload.
if workflow_trace_path.exists():
    workflow_trace_text = workflow_trace_path.read_text(encoding='utf-8', errors='replace')
    if 'public async Task AppendFailureAsync(' not in workflow_trace_text or 'bool dryRun' not in workflow_trace_text:
        ERRORS.append('4.11 workflow trace store must support payload-free preflight failure traces')
app_v4_path = ROOT / 'src/Magic.Capture.App/App.xaml.cs'
if app_v4_path.exists():
    app_v4_text = app_v4_path.read_text(encoding='utf-8', errors='replace')
    if 'StoreWorkflowFailureTraceBestEffortAsync' not in app_v4_text:
        ERRORS.append('4.11 single-run workflow failures must persist a best-effort privacy-safe trace')
main_v4_path = ROOT / 'src/Magic.Capture.App/MainWindow.xaml.cs'
if main_v4_path.exists():
    main_v4_text = main_v4_path.read_text(encoding='utf-8', errors='replace')
    if 'StoreWorkflowFailureTraceBestEffortAsync(workflow, dryRun: true' not in main_v4_text:
        ERRORS.append('4.11 dry-run preflight failures must persist a best-effort privacy-safe trace')
if workflow_batch_path.exists():
    workflow_batch_text = workflow_batch_path.read_text(encoding='utf-8', errors='replace')
    if 'TryAppendFailureTraceAsync(workflow' not in workflow_batch_text:
        ERRORS.append('4.11 batch preflight failures must persist a best-effort privacy-safe trace')


source_release = ROOT / 'scripts/source-release.py'
if source_release.exists():
    release_text = source_release.read_text(encoding='utf-8', errors='replace')
    for verifier in ('verify-repo.py', 'verify-structure.py', 'verify-csharp-lexical.py', 'verify-workflow-triggers.py'):
        if verifier not in release_text:
            ERRORS.append(f'source release must run {verifier}')
else:
    ERRORS.append('source release script is missing')

source_cs = list((ROOT / 'src').rglob('*.cs'))
print(f'Magic Capture Desktop repository verifier')
print(f'  C# source files : {len(source_cs)}')
print(f'  XAML files      : {len(list((ROOT / "src").rglob("*.xaml")))}')
print(f'  Core test files : {len(test_files)}')
print(f'  Errors          : {len(ERRORS)}')
print(f'  Warnings        : {len(WARNINGS)}')
for item in WARNINGS:
    print(f'WARNING: {item}')
for item in ERRORS:
    print(f'ERROR: {item}')

sys.exit(1 if ERRORS else 0)
