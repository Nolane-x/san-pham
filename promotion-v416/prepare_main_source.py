from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else 'reconstructed')
ROOT_AUTHORITY_COUNT = 142
ROOT_AUTHORITY_MANIFEST = 'fadd44d9766341d1727d5109f6f2502416113afd7454665ff3bcaba19a3a30c8'
WORKFLOW_PRE_SHA = 'bf05bbf854278a26f1476e5710902f978f08f53884d8572eadb6d706b9a89ccc'
WORKFLOW_POST_SHA = '53492ad1957691f3a53dfa1734998f6c051f6741c42d548a3db5ad656571bbd6'


def file_manifest(files: list[Path]) -> str:
    digest = hashlib.sha256()
    for path in sorted(files):
        rel = path.relative_to(ROOT).as_posix().encode('utf-8')
        digest.update(rel + b'\0' + hashlib.sha256(path.read_bytes()).digest())
    return digest.hexdigest()


def text_sha(text: str) -> str:
    return hashlib.sha256(text.encode('utf-8')).hexdigest()

all_files = [p for p in ROOT.rglob('*') if p.is_file()]
root_files = [p for p in all_files if p.relative_to(ROOT).parts[0] not in {'src', 'tests'}]
root_manifest = file_manifest(root_files)
if len(root_files) != ROOT_AUTHORITY_COUNT or root_manifest != ROOT_AUTHORITY_MANIFEST:
    raise SystemExit(
        f'root authority mismatch: count={len(root_files)} manifest={root_manifest}; '
        f'expected count={ROOT_AUTHORITY_COUNT} manifest={ROOT_AUTHORITY_MANIFEST}'
    )

workflow = ROOT / '.github/workflows/windows-ci.yml'
text = workflow.read_text(encoding='utf-8')
pre = text_sha(text)
if pre != WORKFLOW_PRE_SHA:
    raise SystemExit(f'windows-ci preimage SHA-256 mismatch: {pre}')
for old, new in [
    ("dotnet-version: '10.0.x'", "dotnet-version: '10.0.400'"),
    ('path: src/Magic.Capture.App/bin/Release/**', 'path: src/Magic.Capture.App/bin/${{ matrix.platform }}/Release/**'),
]:
    if text.count(old) != 1:
        raise SystemExit(f'windows-ci expected exactly one target, found {text.count(old)}: {old}')
    text = text.replace(old, new, 1)
post = text_sha(text)
if post != WORKFLOW_POST_SHA:
    raise SystemExit(f'windows-ci postimage SHA-256 mismatch: {post}')
workflow.write_text(text, encoding='utf-8', newline='')

all_files = [p for p in ROOT.rglob('*') if p.is_file()]
counts = {
    'all': len(all_files),
    'src': sum(1 for p in all_files if p.relative_to(ROOT).parts[0] == 'src'),
    'tests': sum(1 for p in all_files if p.relative_to(ROOT).parts[0] == 'tests'),
    'docs': sum(1 for p in all_files if p.relative_to(ROOT).parts[0] == 'docs'),
    'scripts': sum(1 for p in all_files if p.relative_to(ROOT).parts[0] == 'scripts'),
}
expected = {'all': 633, 'src': 372, 'tests': 119, 'docs': 117, 'scripts': 16}
if counts != expected:
    raise SystemExit(f'clean-source file counts mismatch: {counts} != {expected}')

sdk = json.loads((ROOT / 'global.json').read_text(encoding='utf-8'))['sdk']['version']
if sdk != '10.0.400':
    raise SystemExit(f'unexpected SDK pin: {sdk}')
if '<Using Include="Xunit" />' not in (ROOT / 'tests/Magic.Capture.Core.Tests/Magic.Capture.Core.Tests.csproj').read_text(encoding='utf-8'):
    raise SystemExit('xUnit global using missing from clean test project')
for forbidden in ('bootstrap-appv7', 'bootstrap-tests-v416', 'bootstrap-ci', 'promotion-v416', 'reconstructed'):
    if (ROOT / forbidden).exists():
        raise SystemExit(f'bootstrap transport leaked into clean source: {forbidden}')

final_manifest = file_manifest(all_files)
print(
    f'GREEN clean source: counts={counts} root_authority={root_manifest} '
    f'windows_ci={WORKFLOW_POST_SHA} manifest={final_manifest}'
)
