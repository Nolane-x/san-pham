from __future__ import annotations

import base64
import hashlib
import io
import subprocess
import sys
import tarfile
from pathlib import Path, PurePosixPath

REF = 'origin/bootstrap-4.16-v2'
PIECES = 15
HEADER = 'MAGIC_CAPTURE_CI_BOOTSTRAP_V2'
KNOWN_METADATA_EXCEPTION = {
    7: 'efb206d7cadb21914f370dbeb4bbc5a9c93d99c1bb8ee845e7413ce650248878',
}
KNOWN_PADDING_REPAIR = {14: '='}


def fail(message: str) -> None:
    raise SystemExit(message)


def load_piece(index: int) -> bytes:
    path = f'bootstrap-ci/chunk-{index:03d}.txt'
    try:
        raw_text = subprocess.check_output(
            ['git', 'show', f'{REF}:{path}'], text=True, encoding='utf-8'
        )
    except subprocess.CalledProcessError as exc:
        fail(f'could not read historical bootstrap piece {path}: {exc}')

    lines = raw_text.splitlines()
    if not lines or lines[0] != HEADER:
        fail(f'{path}: unexpected header')

    meta: dict[str, str] = {}
    payload: list[str] = []
    in_payload = False
    for line in lines[1:]:
        if line == 'payload_base64=':
            in_payload = True
            continue
        if in_payload:
            payload.append(line.strip())
        elif '=' in line:
            key, value = line.split('=', 1)
            meta[key] = value

    if meta.get('index') != f'{index:03d}':
        fail(f'{path}: index metadata mismatch')

    encoded = ''.join(payload)
    repair = KNOWN_PADDING_REPAIR.get(index)
    if repair is not None:
        if len(encoded) % 4 != 3:
            fail(f'{path}: expected exactly one missing base64 padding character, length={len(encoded)}')
        encoded += repair
        print(f'KNOWN legacy base64 padding repair applied for {path}')
    elif len(encoded) % 4 != 0:
        fail(f'{path}: unexpected non-aligned base64 length: {len(encoded)}')

    try:
        decoded = base64.b64decode(encoded, validate=True)
    except Exception as exc:
        fail(f'{path}: invalid payload base64 after allowed repairs: {exc}')
    if len(decoded) != int(meta.get('raw_size', '-1')):
        fail(f'{path}: raw size mismatch: {len(decoded)} != {meta.get("raw_size")}')

    digest = hashlib.sha256(decoded).hexdigest()
    declared = meta.get('raw_sha256')
    if digest != declared:
        allowed = KNOWN_METADATA_EXCEPTION.get(index)
        if allowed != digest:
            fail(f'{path}: unexpected raw SHA-256 mismatch: actual={digest} declared={declared}')
        print(
            f'KNOWN legacy metadata mismatch accepted for {path}: '
            f'actual={digest} declared={declared}; final root manifest remains authoritative'
        )
    return decoded


def normalized_member_path(name: str) -> PurePosixPath | None:
    p = PurePosixPath(name)
    if p.is_absolute() or '..' in p.parts:
        fail(f'unsafe historical archive path: {name}')
    parts = list(p.parts)
    if not parts:
        return None
    if parts[0].startswith('Magic-Capture-Desktop-'):
        parts = parts[1:]
    if not parts:
        return None
    return PurePosixPath(*parts)


def main() -> None:
    output = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('reconstructed')
    subprocess.run(['git', 'fetch', 'origin', 'bootstrap-4.16-v2'], check=True)

    archive = b''.join(load_piece(index) for index in range(PIECES))
    archive_digest = hashlib.sha256(archive).hexdigest()
    extracted = 0
    with tarfile.open(fileobj=io.BytesIO(archive), mode='r:*') as tf:
        for member in tf.getmembers():
            rel = normalized_member_path(member.name)
            if rel is None or member.isdir():
                continue
            if member.issym() or member.islnk() or not member.isfile():
                fail(f'non-file historical archive member rejected: {member.name}')
            if rel.parts[0] in {'src', 'tests'}:
                continue
            source = tf.extractfile(member)
            if source is None:
                fail(f'could not read historical archive member: {member.name}')
            target = output / Path(*rel.parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(source.read())
            extracted += 1

    print(
        f'OK legacy root transport: pieces={PIECES} archive_bytes={len(archive)} '
        f'archive_sha256={archive_digest} extracted_root_files={extracted}'
    )


if __name__ == '__main__':
    main()
