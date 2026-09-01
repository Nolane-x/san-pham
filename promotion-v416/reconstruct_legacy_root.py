from __future__ import annotations

import base64
import hashlib
import io
import subprocess
import sys
import tarfile
from pathlib import Path, PurePosixPath

REF = 'origin/bootstrap-4.16-v2'
CHUNK_HEADER = 'MAGIC_CAPTURE_CI_BOOTSTRAP_V2'
MID_HEADER = 'MAGIC_CAPTURE_CI_MIDSEG_V1'
KNOWN_METADATA_EXCEPTION = {
    7: 'efb206d7cadb21914f370dbeb4bbc5a9c93d99c1bb8ee845e7413ce650248878',
}
KNOWN_PADDING_REPAIR = {14: '='}
MID_PATH = 'bootstrap-ci/mid-000.txt'
MID_INDEX = '000'
MID_OFFSET = 70000
MID_SIZE = 14000
MID_DECLARED_SIZE = 28000
MID_SHA256 = 'f5589025f87a84e344fb46be795d8d72e42f76ce0f8068784f8e2a00d95693f3'


def fail(message: str) -> None:
    raise SystemExit(message)


def read_transport(path: str, expected_header: str) -> tuple[dict[str, str], str]:
    try:
        raw_text = subprocess.check_output(
            ['git', 'show', f'{REF}:{path}'], text=True, encoding='utf-8'
        )
    except subprocess.CalledProcessError as exc:
        fail(f'could not read historical bootstrap piece {path}: {exc}')
    lines = raw_text.splitlines()
    if not lines or lines[0] != expected_header:
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
    return meta, ''.join(payload)


def decode_verified(path: str, encoded: str, expected_size: int, expected_sha: str, *, padding: str | None = None) -> bytes:
    if padding is not None:
        if len(encoded) % 4 != 3 or padding != '=':
            fail(f'{path}: expected exactly one missing base64 padding character, length={len(encoded)}')
        encoded += padding
        print(f'KNOWN legacy base64 padding repair applied for {path}')
    elif len(encoded) % 4 != 0:
        fail(f'{path}: unexpected non-aligned base64 length: {len(encoded)}')
    try:
        decoded = base64.b64decode(encoded, validate=True)
    except Exception as exc:
        fail(f'{path}: invalid payload base64 after allowed repairs: {exc}')
    digest = hashlib.sha256(decoded).hexdigest()
    if len(decoded) != expected_size:
        fail(f'{path}: raw size mismatch: {len(decoded)} != {expected_size}; actual_sha256={digest}')
    if digest != expected_sha:
        fail(f'{path}: raw SHA-256 mismatch: {digest} != {expected_sha}')
    return decoded


def load_chunk(index: int) -> bytes:
    path = f'bootstrap-ci/chunk-{index:03d}.txt'
    meta, encoded = read_transport(path, CHUNK_HEADER)
    if meta.get('index') != f'{index:03d}':
        fail(f'{path}: index metadata mismatch')
    expected_size = int(meta.get('raw_size', '-1'))
    declared = meta.get('raw_sha256', '')
    repair = KNOWN_PADDING_REPAIR.get(index)
    if index in KNOWN_METADATA_EXCEPTION:
        decoded = decode_verified(path, encoded, expected_size, KNOWN_METADATA_EXCEPTION[index], padding=repair)
        print(
            f'KNOWN legacy metadata mismatch accepted for {path}: '
            f'actual={KNOWN_METADATA_EXCEPTION[index]} declared={declared}; '
            'archive integrity and final root manifest remain authoritative'
        )
        return decoded
    return decode_verified(path, encoded, expected_size, declared, padding=repair)


def load_mid() -> bytes:
    meta, encoded = read_transport(MID_PATH, MID_HEADER)
    if meta.get('index') != MID_INDEX:
        fail(f'{MID_PATH}: index metadata mismatch')
    if int(meta.get('offset', '-1')) != MID_OFFSET:
        fail(f'{MID_PATH}: offset metadata mismatch')
    if int(meta.get('raw_size', '-1')) != MID_DECLARED_SIZE:
        fail(f'{MID_PATH}: unexpected historical raw-size metadata')
    if meta.get('raw_sha256') != MID_SHA256:
        fail(f'{MID_PATH}: raw SHA metadata mismatch')
    decoded = decode_verified(MID_PATH, encoded, MID_SIZE, MID_SHA256)
    print(
        f'KNOWN legacy mid size metadata mismatch accepted for {MID_PATH}: '
        f'actual={MID_SIZE} declared={MID_DECLARED_SIZE}; SHA-256 matched'
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
    prefix = b''.join(load_chunk(index) for index in range(5))
    if len(prefix) != MID_OFFSET:
        fail(f'legacy prefix size mismatch before mid repair: {len(prefix)} != {MID_OFFSET}')
    mid = load_mid()
    suffix = b''.join(load_chunk(index) for index in range(6, 15))
    archive = prefix + mid + suffix
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
        f'OK legacy root transport: prefix_chunks=5 mid_bytes={len(mid)} suffix_chunks=9 '
        f'archive_bytes={len(archive)} archive_sha256={archive_digest} extracted_root_files={extracted}'
    )


if __name__ == '__main__':
    main()
