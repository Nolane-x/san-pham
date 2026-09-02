from __future__ import annotations

import io
import json
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
COMMERCIAL = ROOT / "scripts" / "verify-commercial-metadata.py"
MSIX = ROOT / "scripts" / "verify-msix-bundle.py"


def run_script(script: Path, *args: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [sys.executable, str(script), *args],
        cwd=ROOT,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        check=False,
    )


def write_release_fixture(root: Path, *, readme_price: str = "$14.99", store_price: str = "$14.99") -> None:
    (root / "release").mkdir(parents=True)
    (root / "packaging").mkdir(parents=True)
    (root / "release" / "commercial.json").write_text(
        json.dumps(
            {
                "market": "US",
                "currency": "USD",
                "appPrice": "0.00",
                "plusTrialHours": 168,
                "plusSold": False,
                "proSkuType": "Durable",
                "proLifetime": True,
                "proMsrp": "14.99",
                "proLaunchPrice": "9.99",
                "launchDurationDays": 90,
                "subscription": False,
                "developerAiCreditsIncluded": False,
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    (root / "README.md").write_text(
        f"Pro Lifetime MSRP (US)       {readme_price}\n"
        "Launch price (US)            $9.99\n"
        "Launch-price duration        90 consecutive days\n",
        encoding="utf-8",
    )
    (root / "packaging" / "STORE_SUBMISSION.md").write_text(
        f"Pro Lifetime regular US MSRP    {store_price}\n"
        "Pro Lifetime US launch          $9.99\n"
        "Launch duration                 90 consecutive days from public Pro availability\n",
        encoding="utf-8",
    )


def inner_msix(architecture: str, *, version: str = "4.16.0.0", extension_exe: str = "Magic.Capture.Desktop.exe", identity_name: str = "Magic.Capture.Desktop.Dev", publisher: str = "CN=Magic Capture Desktop Dev") -> bytes:
    manifest = f'''<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
 xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
 xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities">
 <Identity Name="{identity_name}" Publisher="{publisher}" Version="{version}" ProcessorArchitecture="{architecture}" />
 <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" /></Dependencies>
 <Applications><Application Id="App" Executable="Magic.Capture.Desktop.exe" EntryPoint="Windows.FullTrustApplication"><Extensions>
  <uap5:Extension Category="windows.startupTask" Executable="{extension_exe}" EntryPoint="Windows.FullTrustApplication"><uap5:StartupTask TaskId="Magic.Capture.Desktop.Startup" Enabled="true" DisplayName="Magic Capture Desktop" /></uap5:Extension>
  <uap5:Extension Category="windows.appExecutionAlias" Executable="{extension_exe}" EntryPoint="Windows.FullTrustApplication"><uap5:AppExecutionAlias><uap5:ExecutionAlias Alias="magiccapture.exe" /></uap5:AppExecutionAlias></uap5:Extension>
 </Extensions></Application></Applications>
 <Capabilities><rescap:Capability Name="runFullTrust" /><DeviceCapability Name="microphone" /><DeviceCapability Name="webcam" /></Capabilities>
</Package>'''
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("AppxManifest.xml", manifest)
        z.writestr("Magic.Capture.Desktop.exe", b"MZ-test")
    return buf.getvalue()


def write_bundle(path: Path, *, extension_exe: str = "Magic.Capture.Desktop.exe", identity_name: str = "Magic.Capture.Desktop.Dev", publisher: str = "CN=Magic Capture Desktop Dev") -> None:
    packages = []
    payloads: dict[str, bytes] = {}
    for arch in ("x64", "arm64"):
        name = f"MagicCapture_{arch}.msix"
        payloads[name] = inner_msix(arch, extension_exe=extension_exe, identity_name=identity_name, publisher=publisher)
        packages.append(f'<Package Type="application" Version="4.16.0.0" Architecture="{arch}" FileName="{name}" />')
    manifest = '<?xml version="1.0" encoding="utf-8"?><Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle"><Identity Name="bundle" Publisher="CN=test" Version="4.16.0.0"/><Packages>' + "".join(packages) + '</Packages></Bundle>'
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("AppxMetadata/AppxBundleManifest.xml", manifest)
        for name, payload in payloads.items():
            z.writestr(name, payload)


class CommercialMetadataTests(unittest.TestCase):
    def test_accepts_canonical_commercial_contract(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            write_release_fixture(root)
            result = run_script(COMMERCIAL, str(root))
            self.assertEqual(0, result.returncode, result.stdout)

    def test_rejects_stale_document_price(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            write_release_fixture(root, readme_price="$29.99")
            result = run_script(COMMERCIAL, str(root))
            self.assertEqual(1, result.returncode, result.stdout)
            self.assertIn("README.md", result.stdout)


class MsixBundleVerifierTests(unittest.TestCase):
    def test_accepts_valid_x64_arm64_bundle(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            (root / "release").mkdir()
            (root / "release" / "version.json").write_text(json.dumps({"msixVersion": "4.16.0.0"}), encoding="utf-8")
            bundle = root / "app.msixbundle"
            write_bundle(bundle)
            result = run_script(MSIX, str(bundle), "--root", str(root))
            self.assertEqual(0, result.returncode, result.stdout)
            self.assertIn("arm64,x64", result.stdout.lower())

    def test_rejects_packaged_extension_executable_drift(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            (root / "release").mkdir()
            (root / "release" / "version.json").write_text(json.dumps({"msixVersion": "4.16.0.0"}), encoding="utf-8")
            bundle = root / "app.msixbundle"
            write_bundle(bundle, extension_exe="$targetnametoken$.exe")
            result = run_script(MSIX, str(bundle), "--root", str(root))
            self.assertEqual(1, result.returncode, result.stdout)
            self.assertIn("startupTask", result.stdout)

    def test_store_identity_mode_rejects_development_identity(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            (root / "release").mkdir()
            (root / "release" / "version.json").write_text(json.dumps({"msixVersion": "4.16.0.0"}), encoding="utf-8")
            bundle = root / "app.msixbundle"
            write_bundle(bundle)
            result = run_script(MSIX, str(bundle), "--root", str(root), "--require-store-identity")
            self.assertEqual(1, result.returncode, result.stdout)
            self.assertIn("development identity", result.stdout.lower())


if __name__ == "__main__":
    unittest.main()
