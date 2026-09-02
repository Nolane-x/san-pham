#!/usr/bin/env python3
from __future__ import annotations

import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

SEMVER_RE = re.compile(r"^\d+\.\d+\.\d+$")
MSIX_VERSION_RE = re.compile(r"^\d+\.\d+\.\d+\.\d+$")
TOKEN_RE = re.compile(r'public\s+const\s+string\s+ProOfferToken\s*=\s*"([^"]+)"\s*;')


def _property(project: ET.Element, name: str) -> str:
    node = project.find(f".//{name}")
    return (node.text or "").strip() if node is not None else ""


def verify(root: Path) -> list[str]:
    errors: list[str] = []

    version_path = root / "release" / "version.json"
    csproj_path = root / "src" / "Magic.Capture.App" / "Magic.Capture.App.csproj"
    manifest_path = root / "src" / "Magic.Capture.App" / "Package.appxmanifest"
    store_service_path = root / "src" / "Magic.Capture.App" / "Commerce" / "StorePurchaseService.cs"
    store_guide_path = root / "packaging" / "STORE_SUBMISSION.md"

    required = [version_path, csproj_path, manifest_path, store_service_path, store_guide_path]
    for path in required:
        if not path.is_file():
            errors.append(f"Missing release metadata file: {path.relative_to(root)}")
    if errors:
        return errors

    release = json.loads(version_path.read_text(encoding="utf-8"))
    product = str(release.get("product", "")).strip()
    semver = str(release.get("semver", "")).strip()
    msix_version = str(release.get("msixVersion", "")).strip()
    offer_token = str(release.get("proOfferToken", "")).strip()

    if not product:
        errors.append("release/version.json product is empty.")
    if not SEMVER_RE.fullmatch(semver):
        errors.append(f"release/version.json semver is invalid: {semver!r}.")
    if not MSIX_VERSION_RE.fullmatch(msix_version):
        errors.append(f"release/version.json msixVersion is invalid: {msix_version!r}.")
    if not offer_token:
        errors.append("release/version.json proOfferToken is empty.")

    project = ET.parse(csproj_path).getroot()
    assembly_name = _property(project, "AssemblyName")
    project_version = _property(project, "Version")
    assembly_version = _property(project, "AssemblyVersion")
    file_version = _property(project, "FileVersion")

    if not assembly_name:
        errors.append("Magic.Capture.App.csproj AssemblyName is empty.")
    if project_version != semver:
        errors.append(f"Magic.Capture.App.csproj Version {project_version!r} does not match release semver {semver!r}.")
    if assembly_version != msix_version:
        errors.append(f"Magic.Capture.App.csproj AssemblyVersion {assembly_version!r} does not match MSIX version {msix_version!r}.")
    if file_version != msix_version:
        errors.append(f"Magic.Capture.App.csproj FileVersion {file_version!r} does not match MSIX version {msix_version!r}.")

    manifest = ET.parse(manifest_path).getroot()
    foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10"
    uap5 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
    identity = manifest.find(f"{{{foundation}}}Identity")
    if identity is None:
        errors.append("Package.appxmanifest has no Identity element.")
    else:
        manifest_version = (identity.get("Version") or "").strip()
        if manifest_version != msix_version:
            errors.append(f"Package.appxmanifest Identity Version {manifest_version!r} does not match MSIX version {msix_version!r}.")

    application = manifest.find(f".//{{{foundation}}}Application")
    if application is None:
        errors.append("Package.appxmanifest has no Application element.")
    else:
        expected_executable = f"{assembly_name}.exe" if assembly_name else ""
        extensions = application.findall(f".//{{{uap5}}}Extension")
        by_category = {(ext.get("Category") or "").strip(): ext for ext in extensions}
        for category in ("windows.startupTask", "windows.appExecutionAlias"):
            ext = by_category.get(category)
            if ext is None:
                errors.append(f"Package.appxmanifest is missing {category} extension.")
                continue
            executable = (ext.get("Executable") or "").strip()
            if executable != expected_executable:
                errors.append(
                    f"Package.appxmanifest {category} extension executable {executable!r} "
                    f"does not match assembly executable {expected_executable!r}."
                )
            entry_point = (ext.get("EntryPoint") or "").strip()
            if entry_point != "Windows.FullTrustApplication":
                errors.append(
                    f"Package.appxmanifest {category} EntryPoint {entry_point!r} must be 'Windows.FullTrustApplication'."
                )

    store_service = store_service_path.read_text(encoding="utf-8")
    token_match = TOKEN_RE.search(store_service)
    if token_match is None:
        errors.append("StorePurchaseService.cs ProOfferToken constant was not found.")
    elif token_match.group(1) != offer_token:
        errors.append(
            f"StorePurchaseService.cs ProOfferToken {token_match.group(1)!r} "
            f"does not match release token {offer_token!r}."
        )

    store_guide = store_guide_path.read_text(encoding="utf-8")
    if offer_token and offer_token not in store_guide:
        errors.append("STORE_SUBMISSION.md does not contain release/version.json proOfferToken.")

    return errors


def main(argv: list[str]) -> int:
    root = Path(argv[1]).resolve() if len(argv) > 1 else Path(__file__).resolve().parents[1]
    try:
        errors = verify(root)
    except (OSError, json.JSONDecodeError, ET.ParseError) as exc:
        print(f"Release metadata verifier failed to read metadata: {exc}")
        return 2

    print("Magic Capture Desktop release metadata verifier")
    if errors:
        for error in errors:
            print(f"  ERROR: {error}")
        print(f"  Errors: {len(errors)}")
        return 1

    print("  Version authority : consistent")
    print("  Store offer token : consistent")
    print("  MSIX extensions   : consistent")
    print("  Errors            : 0")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
