#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION = json.loads((ROOT / 'release/version.json').read_text(encoding='utf-8'))['semver']
OUTPUT = ROOT.parent / f'Magic-Capture-Desktop-{VERSION}-source.zip'
EXCLUDED_PARTS = {'.git', '.vs', 'bin', 'obj', 'artifacts', '__pycache__'}

for verifier in ('verify-repo.py', 'verify-structure.py', 'verify-csharp-lexical.py', 'verify-workflow-triggers.py', 'verify-workflow-control-flow.py', 'verify-history-intelligence.py', 'verify-settings-personalization.py', 'verify-settings-consistency.py', 'verify-work-recovery.py'):
    verify = subprocess.run([sys.executable, str(ROOT / 'scripts' / verifier)], cwd=ROOT)
    if verify.returncode != 0:
        raise SystemExit(verify.returncode)

files = []
for path in ROOT.rglob('*'):
    if not path.is_file():
        continue
    rel = path.relative_to(ROOT)
    if any(part in EXCLUDED_PARTS for part in rel.parts):
        continue
    if path.suffix.lower() in {'.user', '.suo', '.tmp'}:
        continue
    files.append((path, rel))

with zipfile.ZipFile(OUTPUT, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for path, rel in sorted(files, key=lambda item: item[1].as_posix().lower()):
        info = zipfile.ZipInfo(f'Magic-Capture-Desktop-{VERSION}/{rel.as_posix()}')
        info.date_time = (2026, 8, 27, 0, 0, 0)
        info.compress_type = zipfile.ZIP_DEFLATED
        info.external_attr = 0o644 << 16
        archive.writestr(info, path.read_bytes())

with zipfile.ZipFile(OUTPUT, 'r') as archive:
    bad = archive.testzip()
    if bad:
        raise SystemExit(f'ZIP integrity failure at {bad}')
    count = len(archive.infolist())

digest = hashlib.sha256()
with OUTPUT.open('rb') as stream:
    for chunk in iter(lambda: stream.read(1024 * 1024), b''):
        digest.update(chunk)
sha = digest.hexdigest()
sha_path = OUTPUT.with_suffix(OUTPUT.suffix + '.sha256')
sha_path.write_text(f'{sha}  {OUTPUT.name}\n', encoding='utf-8')
print(f'Created: {OUTPUT}')
print(f'Files:   {count}')
print(f'SHA256:  {sha}')
