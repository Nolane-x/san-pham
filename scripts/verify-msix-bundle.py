#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import io
import json
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path, PurePosixPath

BUNDLE_NS = "http://schemas.microsoft.com/appx/2013/bundle"
FOUNDATION_NS = "http://schemas.microsoft.com/appx/manifest/foundation/windows10"
UAP5_NS = "http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
RESCAP_NS = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
EXPECTED_ARCHITECTURES = {"x64", "arm64"}
DEV_IDENTITY = "Magic.Capture.Desktop.Dev"
DEV_PUBLISHER = "CN=Magic Capture Desktop Dev"
EXPECTED_ALIAS = "magiccapture.exe"
EXPECTED_STARTUP_TASK = "Magic.Capture.Desktop.Startup"
EXPECTED_MIN_WINDOWS = "10.0.19041.0"


def _normalized_package_path(value: str) -> str:
    return str(PurePosixPath(value.replace("\\", "/")))


def _read_xml_from_zip(archive: zipfile.ZipFile, name: str) -> ET.Element:
    try:
        payload = archive.read(name)
    except KeyError as exc:
        raise ValueError(f"missing {name}") from exc
    return ET.fromstring(payload)


def verify_bundle(bundle_path: Path, root: Path, require_store_identity: bool = False) -> tuple[list[str], dict[str, str]]:
    errors: list[str] = []
    summary: dict[str, str] = {}

    version_path = root / "release" / "version.json"
    if not version_path.is_file():
        return [f"Missing release metadata file: {version_path}"], summary
    release = json.loads(version_path.read_text(encoding="utf-8"))
    expected_version = str(release.get("msixVersion", "")).strip()
    if not expected_version:
        return ["release/version.json msixVersion is empty."], summary
    if not bundle_path.is_file():
        return [f"MSIX bundle does not exist: {bundle_path}"], summary

    digest = hashlib.sha256(bundle_path.read_bytes()).hexdigest()
    summary["sha256"] = digest

    identities: set[tuple[str, str]] = set()
    seen_architectures: set[str] = set()

    with zipfile.ZipFile(bundle_path, "r") as bundle:
        bad = bundle.testzip()
        if bad:
            errors.append(f"MSIX bundle ZIP integrity failed at {bad}.")
            return errors, summary
        try:
            bundle_manifest = _read_xml_from_zip(bundle, "AppxMetadata/AppxBundleManifest.xml")
        except (ValueError, ET.ParseError) as exc:
            errors.append(f"MSIX bundle manifest error: {exc}.")
            return errors, summary

        packages = bundle_manifest.findall(f".//{{{BUNDLE_NS}}}Package")
        application_packages = [p for p in packages if (p.get("Type") or "").lower() == "application"]
        if len(application_packages) != 2:
            errors.append(f"Expected exactly two application packages, found {len(application_packages)}.")

        for descriptor in application_packages:
            arch = (descriptor.get("Architecture") or "").strip().lower()
            filename = (descriptor.get("FileName") or "").strip()
            outer_version = (descriptor.get("Version") or "").strip()
            label = filename or f"<{arch or 'unknown'}>"

            if arch not in EXPECTED_ARCHITECTURES:
                errors.append(f"{label}: unsupported architecture {arch!r}.")
            elif arch in seen_architectures:
                errors.append(f"{label}: duplicate architecture {arch!r}.")
            else:
                seen_architectures.add(arch)

            if outer_version != expected_version:
                errors.append(f"{label}: bundle descriptor version {outer_version!r} does not match release {expected_version!r}.")
            if not filename:
                errors.append(f"{label}: bundle descriptor FileName is empty.")
                continue
            if filename not in bundle.namelist():
                errors.append(f"{label}: referenced package is missing from bundle.")
                continue

            try:
                package_bytes = bundle.read(filename)
                with zipfile.ZipFile(io.BytesIO(package_bytes), "r") as package:
                    bad_inner = package.testzip()
                    if bad_inner:
                        errors.append(f"{label}: package ZIP integrity failed at {bad_inner}.")
                        continue
                    manifest = _read_xml_from_zip(package, "AppxManifest.xml")
                    _verify_package_manifest(
                        manifest,
                        package,
                        label,
                        arch,
                        expected_version,
                        require_store_identity,
                        identities,
                        errors,
                    )
            except (zipfile.BadZipFile, ET.ParseError, ValueError) as exc:
                errors.append(f"{label}: invalid MSIX package: {exc}.")

    if seen_architectures != EXPECTED_ARCHITECTURES:
        errors.append(
            "MSIX bundle architectures are "
            f"{','.join(sorted(seen_architectures)) or '<none>'}; expected arm64,x64."
        )
    if len(identities) > 1:
        errors.append("Packaged Identity Name/Publisher differs between architectures.")

    summary["architectures"] = ",".join(sorted(seen_architectures))
    summary["version"] = expected_version
    if identities:
        name, publisher = next(iter(identities))
        summary["identity"] = name
        summary["publisher"] = publisher
    return errors, summary


def _verify_package_manifest(
    manifest: ET.Element,
    package: zipfile.ZipFile,
    label: str,
    architecture: str,
    expected_version: str,
    require_store_identity: bool,
    identities: set[tuple[str, str]],
    errors: list[str],
) -> None:
    identity = manifest.find(f"{{{FOUNDATION_NS}}}Identity")
    if identity is None:
        errors.append(f"{label}: AppxManifest.xml has no Identity element.")
        return

    name = (identity.get("Name") or "").strip()
    publisher = (identity.get("Publisher") or "").strip()
    version = (identity.get("Version") or "").strip()
    processor_arch = (identity.get("ProcessorArchitecture") or architecture).strip().lower()
    identities.add((name, publisher))

    if version != expected_version:
        errors.append(f"{label}: manifest version {version!r} does not match release {expected_version!r}.")
    if processor_arch != architecture:
        errors.append(f"{label}: manifest architecture {processor_arch!r} does not match bundle descriptor {architecture!r}.")
    if require_store_identity and (name == DEV_IDENTITY or publisher == DEV_PUBLISHER):
        errors.append(f"{label}: development identity/publisher is not allowed for a Store package.")

    target = manifest.find(f".//{{{FOUNDATION_NS}}}TargetDeviceFamily")
    if target is None or (target.get("Name") or "").strip() != "Windows.Desktop":
        errors.append(f"{label}: Windows.Desktop TargetDeviceFamily is missing.")
    elif (target.get("MinVersion") or "").strip() != EXPECTED_MIN_WINDOWS:
        errors.append(f"{label}: TargetDeviceFamily MinVersion must be {EXPECTED_MIN_WINDOWS}.")

    application = manifest.find(f".//{{{FOUNDATION_NS}}}Application")
    if application is None:
        errors.append(f"{label}: AppxManifest.xml has no Application element.")
        return

    executable = (application.get("Executable") or "").strip()
    entry_point = (application.get("EntryPoint") or "").strip()
    if not executable or "$target" in executable.lower():
        errors.append(f"{label}: packaged Application Executable is unresolved or empty: {executable!r}.")
    if entry_point != "Windows.FullTrustApplication":
        errors.append(f"{label}: Application EntryPoint must be 'Windows.FullTrustApplication', got {entry_point!r}.")
    normalized_executable = _normalized_package_path(executable) if executable else ""
    if normalized_executable and normalized_executable not in package.namelist():
        errors.append(f"{label}: packaged executable {executable!r} is missing from the MSIX payload.")

    extensions = application.findall(f".//{{{UAP5_NS}}}Extension")
    by_category = {(ext.get("Category") or "").strip(): ext for ext in extensions}
    for category in ("windows.startupTask", "windows.appExecutionAlias"):
        ext = by_category.get(category)
        if ext is None:
            errors.append(f"{label}: missing {category} extension.")
            continue
        ext_executable = (ext.get("Executable") or "").strip()
        if ext_executable != executable:
            errors.append(f"{label}: {category} executable {ext_executable!r} does not match packaged Application executable {executable!r}.")
        if "$target" in ext_executable.lower():
            errors.append(f"{label}: {category} executable still contains an unresolved target token.")
        if (ext.get("EntryPoint") or "").strip() != "Windows.FullTrustApplication":
            errors.append(f"{label}: {category} EntryPoint must be 'Windows.FullTrustApplication'.")

    startup_ext = by_category.get("windows.startupTask")
    if startup_ext is not None:
        startup = startup_ext.find(f"{{{UAP5_NS}}}StartupTask")
        if startup is None:
            errors.append(f"{label}: windows.startupTask has no StartupTask element.")
        else:
            if (startup.get("TaskId") or "").strip() != EXPECTED_STARTUP_TASK:
                errors.append(f"{label}: startup TaskId must be {EXPECTED_STARTUP_TASK!r}.")
            if (startup.get("Enabled") or "").strip().lower() != "true":
                errors.append(f"{label}: startup task must be enabled by default.")

    alias_ext = by_category.get("windows.appExecutionAlias")
    if alias_ext is not None:
        alias = alias_ext.find(f".//{{{UAP5_NS}}}ExecutionAlias")
        if alias is None or (alias.get("Alias") or "").strip().lower() != EXPECTED_ALIAS:
            actual = "" if alias is None else (alias.get("Alias") or "").strip()
            errors.append(f"{label}: execution alias {actual!r} must be {EXPECTED_ALIAS!r}.")

    run_full_trust = manifest.find(f".//{{{RESCAP_NS}}}Capability[@Name='runFullTrust']")
    if run_full_trust is None:
        errors.append(f"{label}: runFullTrust restricted capability is missing.")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Verify a built Magic Capture Desktop MSIX bundle.")
    parser.add_argument("bundle", type=Path)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--require-store-identity", action="store_true")
    args = parser.parse_args(argv[1:])

    try:
        errors, summary = verify_bundle(args.bundle.resolve(), args.root.resolve(), args.require_store_identity)
    except (OSError, json.JSONDecodeError, zipfile.BadZipFile, ET.ParseError, ValueError) as exc:
        print(f"MSIX bundle verifier failed: {exc}")
        return 2

    print("Magic Capture Desktop MSIX bundle verifier")
    if errors:
        for error in errors:
            print(f"  ERROR: {error}")
        print(f"  Errors: {len(errors)}")
        return 1

    print(f"  Architectures : {summary.get('architectures', '')}")
    print(f"  Version       : {summary.get('version', '')}")
    print(f"  Identity      : {summary.get('identity', '')}")
    print(f"  SHA256        : {summary.get('sha256', '')}")
    print("  Errors        : 0")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
