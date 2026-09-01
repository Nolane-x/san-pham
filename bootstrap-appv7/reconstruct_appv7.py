from __future__ import annotations
import base64, hashlib, os, sys, tarfile
from pathlib import Path, PurePosixPath

EXPECTED_B64_LEN = 479_528
EXPECTED_SIZE = 359_644
EXPECTED_SHA256 = '0ffd10934a6c8f9088e66d2cd926c9677dc9652f4abb80bfa07442458e1763ea'

ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else 'bootstrap-appv7')
OUT = Path(sys.argv[2] if len(sys.argv) > 2 else 'reconstructed')

mapping: dict[int, list[str]] = {
    0:['seg-000.b64'],1:['seg-001.b64'],2:['seg-002-part-00.b64','seg-002-part-01.b64'],
    3:['seg-003.b64'],4:['seg-004.b64'],5:['seg-005.b64'],
    6:[f'seg-006-piece-{i:02d}.b64' for i in range(4)],7:['seg-007.b64'],
    8:['seg-008-piece-00-sub-00.b64','seg-008-piece-00-sub-01.b64','seg-008-piece-01.b64','seg-008-piece-02.b64','seg-008-piece-03.b64'],
    9:[f'seg-009-piece-{i:02d}.b64' for i in range(4)],
    10:[f'seg-010-piece-{i:02d}.b64' for i in range(4)],
    11:['seg-011-piece-00.b64','seg-011-piece-01.b64','seg-011-piece-02.b64','seg-011-piece-03a.b64','seg-011-piece-03b.b64'],
    12:['seg-012-piece-00.b64','seg-012-piece-01.b64','seg-012-piece-02.b64','seg-012-piece-03-sub-00.b64','seg-012-piece-03-sub-01.b64'],
    13:[f'seg-013-piece-{i:02d}.b64' for i in range(4)],
    14:[f'seg-014-piece-{i:02d}.b64' for i in range(4)],
}
for i in range(15,19): mapping[i] = [f'seg-{i:03d}-half-0.b64',f'seg-{i:03d}-half-1.b64']
mapping[19] = ['seg-019-half-0.b64','seg-019-half-1a.b64','seg-019-half-1b.b64']
for i in range(20,26): mapping[i] = [f'seg-{i:03d}-piece-{j:02d}.b64' for j in range(4)]
for i in range(26,39): mapping[i] = [f'seg-{i:03d}-half-0.b64',f'seg-{i:03d}-half-1.b64']
mapping[39] = ['seg-039-half-0.b64','seg-039-half-1a.b64','seg-039-half-1b.b64']

if set(mapping) != set(range(40)):
    raise SystemExit('segment mapping is incomplete')

chunks=[]
for i in range(40):
    parts=[]
    for name in mapping[i]:
        p=ROOT/name
        if not p.is_file(): raise SystemExit(f'missing payload file: {p}')
        t=''.join(p.read_text(encoding='utf-8').split())
        if not t: raise SystemExit(f'empty payload file: {p}')
        parts.append(t)
    seg=''.join(parts)
    expected_len = 12_000 if i < 39 else 11_528
    if len(seg) != expected_len:
        raise SystemExit(f'segment {i:03d} length {len(seg)} != {expected_len}')
    chunks.append(seg)

payload=''.join(chunks)
if len(payload) != EXPECTED_B64_LEN:
    raise SystemExit(f'base64 length {len(payload)} != {EXPECTED_B64_LEN}')
try:
    archive=base64.b64decode(payload, validate=True)
except Exception as e:
    raise SystemExit(f'base64 decode failed: {e}')
if len(archive) != EXPECTED_SIZE:
    raise SystemExit(f'archive size {len(archive)} != {EXPECTED_SIZE}')
sha=hashlib.sha256(archive).hexdigest()
if sha != EXPECTED_SHA256:
    raise SystemExit(f'archive sha256 {sha} != {EXPECTED_SHA256}')

OUT.mkdir(parents=True, exist_ok=True)
archive_path=OUT.parent/'app-v7.tar.xz'
archive_path.write_bytes(archive)
with tarfile.open(archive_path,'r:xz') as tf:
    members=tf.getmembers()
    for m in members:
        q=PurePosixPath(m.name)
        if q.is_absolute() or '..' in q.parts or m.issym() or m.islnk():
            raise SystemExit(f'unsafe archive member: {m.name}')
    tf.extractall(OUT, members=members, filter='data')
print(f'OK app-v7: b64={len(payload)} bytes={len(archive)} sha256={sha} files={len(members)}')
