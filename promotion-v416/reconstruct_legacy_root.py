from __future__ import annotations

import base64
import hashlib
import io
import subprocess
import sys
import tarfile
from pathlib import Path, PurePosixPath

PAYLOAD_BRANCH = 'promotion-root-v416'
PAYLOAD_COMMIT = '355e3075784b84b7736959492e418e8ebe35fce6'
PAYLOAD_DIR = 'promotion-root-clean'
PIECES = 153
FULL_PIECE_CHARS = 2000
TAIL_PIECE_CHARS = 956
PAYLOAD_CHARS = 304956
ARCHIVE_BYTES = 228716
ARCHIVE_SHA256 = 'b14840daec3e047d78f5ed738d4c4ab383c2bc51e70fd88781d7efb1c211ac6f'
ROOT_FILE_COUNT = 140
ROOT_MANIFEST_SHA256 = '9feca2d6cd5e55859f77cfc68648bb2bce4bc43a012ce237f9e5d3ec8ddff7a8'


def fail(message: str) -> None:
    raise SystemExit(message)


def git_show(path: str) -> bytes:
    try:
        return subprocess.check_output(['git', 'show', f'{PAYLOAD_COMMIT}:{path}'])
    except subprocess.CalledProcessError as exc:
        fail(f'could not read canonical payload piece {path}: {exc}')


def load_piece(index: int) -> bytes:
    path = f'{PAYLOAD_DIR}/chunk-{index:03d}.b64'
    raw = git_show(path)
    expected_len = TAIL_PIECE_CHARS if index == PIECES - 1 else FULL_PIECE_CHARS
    if len(raw) != expected_len:
        fail(f'{path}: size mismatch: {len(raw)} != {expected_len}')
    try:
        raw.decode('ascii')
    except UnicodeDecodeError as exc:
        fail(f'{path}: payload is not ASCII: {exc}')
    return raw


def safe_member_path(name: str) -> PurePosixPath:
    path = PurePosixPath(name)
    if path.is_absolute() or not path.parts or '..' in path.parts:
        fail(f'unsafe canonical archive path: {name}')
    if path.parts[0] in {'src', 'tests'}:
        fail(f'canonical root archive leaked source/test path: {name}')
    if path.as_posix() in {'Directory.Build.props', 'global.json'}:
        fail(f'canonical root archive must not overwrite bootstrap authority file: {name}')
    return path


def manifest(entries: list[tuple[str, bytes]]) -> str:
    digest = hashlib.sha256()
    for name, data in sorted(entries):
        digest.update(name.encode('utf-8') + b'\0' + hashlib.sha256(data).digest())
    return digest.hexdigest()


def main() -> None:
    output = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('reconstructed')

    subprocess.run(['git', 'fetch', 'origin', PAYLOAD_BRANCH], check=True)
    branch_head = subprocess.check_output(['git', 'rev-parse', f'origin/{PAYLOAD_BRANCH}'], text=True).strip()
    if branch_head != PAYLOAD_COMMIT:
        fail(f'canonical payload branch drift: {branch_head} != {PAYLOAD_COMMIT}')
    subprocess.run(['git', 'cat-file', '-e', f'{PAYLOAD_COMMIT}^{{commit}}'], check=True)

    payload = b''.join(load_piece(index) for index in range(PIECES))
    if len(payload) != PAYLOAD_CHARS:
        fail(f'canonical payload size mismatch: {len(payload)} != {PAYLOAD_CHARS}')

    try:
        archive = base64.b64decode(payload, validate=True)
    except Exception as exc:
        fail(f'canonical payload base64 decode failed: {exc}')

    archive_digest = hashlib.sha256(archive).hexdigest()
    if len(archive) != ARCHIVE_BYTES or archive_digest != ARCHIVE_SHA256:
        fail(
            f'canonical root archive mismatch: bytes={len(archive)} sha256={archive_digest}; '
            f'expected bytes={ARCHIVE_BYTES} sha256={ARCHIVE_SHA256}'
        )

    entries: list[tuple[str, bytes]] = []
    seen: set[str] = set()
    try:
        with tarfile.open(fileobj=io.BytesIO(archive), mode='r:xz') as tf:
            for member in tf.getmembers():
                if not member.isfile() or member.issym() or member.islnk():
                    fail(f'non-file canonical archive member rejected: {member.name}')
                rel = safe_member_path(member.name)
                rel_text = rel.as_posix()
                if rel_text in seen:
                    fail(f'duplicate canonical archive member: {rel_text}')
                seen.add(rel_text)
                source = tf.extractfile(member)
                if source is None:
                    fail(f'could not read canonical archive member: {member.name}')
                entries.append((rel_text, source.read()))
    except tarfile.TarError as exc:
        fail(f'canonical root archive could not be opened: {exc}')

    root_manifest = manifest(entries)
    if len(entries) != ROOT_FILE_COUNT or root_manifest != ROOT_MANIFEST_SHA256:
        fail(
            f'canonical root manifest mismatch: count={len(entries)} manifest={root_manifest}; '
            f'expected count={ROOT_FILE_COUNT} manifest={ROOT_MANIFEST_SHA256}'
        )

    for rel_text, data in entries:
        target = output / Path(*PurePosixPath(rel_text).parts)
        if target.exists():
            fail(f'canonical root would overwrite existing file: {rel_text}')
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)

    print(
        f'GREEN canonical root authority: commit={PAYLOAD_COMMIT} pieces={PIECES} '
        f'payload_chars={len(payload)} archive_bytes={len(archive)} '
        f'archive_sha256={archive_digest} root_files={len(entries)} '
        f'root_manifest={root_manifest}'
    )


if __name__ == '__main__':
    main()
