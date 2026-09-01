from __future__ import annotations

import base64
import hashlib
import io
import shutil
import sys
import tarfile
from pathlib import Path, PurePosixPath

EXPECTED_B64_LEN = 66744
EXPECTED_SIZE = 50056
EXPECTED_SHA256 = "03e0cdc6182af3ad8563da8143ac73c95f01c9ea693629f5bf2c1efb278bd98e"
EXPECTED_FILES = 119
PREFIX = PurePosixPath("tests/Magic.Capture.Core.Tests")
CHUNKS = [f"chunk-{i:02d}.txt" for i in range(5)]


def fail(message: str) -> None:
    raise SystemExit(message)


def main() -> None:
    source = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("bootstrap-tests-v416")
    output = Path(sys.argv[2]) if len(sys.argv) > 2 else Path("reconstructed")

    parts: list[str] = []
    for name in CHUNKS:
        path = source / name
        if not path.is_file():
            fail(f"missing test transport chunk: {path}")
        text = "".join(path.read_text(encoding="utf-8").split())
        parts.append(text)

    encoded = "".join(parts)
    if len(encoded) != EXPECTED_B64_LEN:
        fail(f"unexpected test transport base64 length: {len(encoded)}")

    try:
        archive = base64.b64decode(encoded, validate=True)
    except Exception as exc:
        fail(f"invalid test transport base64: {exc}")

    if len(archive) != EXPECTED_SIZE:
        fail(f"unexpected test transport archive size: {len(archive)}")
    digest = hashlib.sha256(archive).hexdigest()
    if digest != EXPECTED_SHA256:
        fail(f"test transport SHA-256 mismatch: {digest}")

    with tarfile.open(fileobj=io.BytesIO(archive), mode="r:xz") as tf:
        members = tf.getmembers()
        if len(members) != EXPECTED_FILES:
            fail(f"unexpected test transport member count: {len(members)}")

        seen: set[str] = set()
        for member in members:
            p = PurePosixPath(member.name)
            if p.is_absolute() or ".." in p.parts:
                fail(f"unsafe test transport path: {member.name}")
            if not member.isfile():
                fail(f"non-file member rejected: {member.name}")
            if len(p.parts) < len(PREFIX.parts) or p.parts[: len(PREFIX.parts)] != PREFIX.parts:
                fail(f"member outside test prefix: {member.name}")
            if member.name in seen:
                fail(f"duplicate test member: {member.name}")
            seen.add(member.name)

        test_root = output / "tests" / "Magic.Capture.Core.Tests"
        if test_root.exists():
            shutil.rmtree(test_root)
        for member in members:
            target = output / Path(*PurePosixPath(member.name).parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            extracted = tf.extractfile(member)
            if extracted is None:
                fail(f"could not read test member: {member.name}")
            target.write_bytes(extracted.read())

    actual_files = [p for p in (output / "tests" / "Magic.Capture.Core.Tests").rglob("*") if p.is_file()]
    if len(actual_files) != EXPECTED_FILES:
        fail(f"reconstructed test file count mismatch: {len(actual_files)}")

    project = output / "tests" / "Magic.Capture.Core.Tests" / "Magic.Capture.Core.Tests.csproj"
    if not project.is_file():
        fail(f"missing reconstructed test project: {project}")

    print(
        f"OK tests-v416: b64={len(encoded)} bytes={len(archive)} "
        f"sha256={digest} files={len(actual_files)}"
    )


if __name__ == "__main__":
    main()
