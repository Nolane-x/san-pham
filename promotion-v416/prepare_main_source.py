from __future__ import annotations
import hashlib, json, sys
from pathlib import Path

ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else 'reconstructed')
WORKFLOW = ROOT / '.github/workflows/windows-ci.yml'
PRE_SHA = 'bf05bbf854278a26f1476e5710902f978f08f53884d8572eadb6d706b9a89ccc'
POST_SHA = '53492ad1957691f3a53dfa1734998f6c051f6741c42d548a3db5ad656571bbd6'


def sha_text(text: str) -> str:
    return hashlib.sha256(text.encode('utf-8')).hexdigest()

text = WORKFLOW.read_text(encoding='utf-8')
if sha_text(text) != PRE_SHA:
    raise SystemExit(f'windows-ci preimage sha mismatch: {sha_text(text)}')
for old, new in [
    ("dotnet-version: '10.0.x'", "dotnet-version: '10.0.400'"),
    ('path: src/Magic.Capture.App/bin/Release/**', 'path: src/Magic.Capture.App/bin/${{ matrix.platform }}/Release/**'),
]:
    if text.count(old) != 1:
        raise SystemExit(f'windows-ci expected one target, found {text.count(old)}: {old}')
    text = text.replace(old, new, 1)
if sha_text(text) != POST_SHA:
    raise SystemExit(f'windows-ci postimage sha mismatch: {sha_text(text)}')
WORKFLOW.write_text(text, encoding='utf-8', newline='')

files = sorted(p for p in ROOT.rglob('*') if p.is_file())
counts = {
    'all': len(files),
    'src': sum(1 for p in files if p.relative_to(ROOT).parts[0] == 'src'),
    'tests': sum(1 for p in files if p.relative_to(ROOT).parts[0] == 'tests'),
    'docs': sum(1 for p in files if p.relative_to(ROOT).parts[0] == 'docs'),
    'scripts': sum(1 for p in files if p.relative_to(ROOT).parts[0] == 'scripts'),
}
expected = {'all': 633, 'src': 372, 'tests': 119, 'docs': 117, 'scripts': 16}
if counts != expected:
    raise SystemExit(f'clean-source file counts mismatch: {counts} != {expected}')
for forbidden in ('bootstrap-appv7', 'bootstrap-tests-v416', 'promotion-supplement-v416', 'reconstructed'):
    if (ROOT / forbidden).exists():
        raise SystemExit(f'bootstrap transport leaked into clean source: {forbidden}')
sdk = json.loads((ROOT / 'global.json').read_text(encoding='utf-8'))['sdk']['version']
if sdk != '10.0.400':
    raise SystemExit(f'unexpected SDK pin: {sdk}')
if '<Using Include="Xunit" />' not in (ROOT / 'tests/Magic.Capture.Core.Tests/Magic.Capture.Core.Tests.csproj').read_text(encoding='utf-8'):
    raise SystemExit('xUnit global using missing from clean test project')
manifest = hashlib.sha256()
for p in files:
    rel = p.relative_to(ROOT).as_posix().encode('utf-8')
    manifest.update(rel + b'\0' + hashlib.sha256(p.read_bytes()).digest())
print(f'GREEN clean source: counts={counts} windows_ci={POST_SHA} manifest={manifest.hexdigest()}')
