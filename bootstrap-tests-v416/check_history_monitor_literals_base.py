from __future__ import annotations
import hashlib
import sys
from pathlib import Path

ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else 'reconstructed')
EXPECTED = {
    'tests/Magic.Capture.Core.Tests/HistoryQueryTests.cs': 'b3a65dfc8c8332db22d48bb1757fa4ddc1c77116cdddc8ef06b63d7337a05fac',
    'tests/Magic.Capture.Core.Tests/Magic.Capture.Core.Tests.csproj': '71c50c7e3d7581a5933dffe6ca62b2dbff1b17f31176004dc1a8e3254d15ee71',
    'tests/Magic.Capture.Core.Tests/SettingsReferencePolicyTests.cs': '4135fc98a9647808e7311e4ce64187b3859ee5d4c33bee3ac4f5899d33bd50c1',
    'tests/Magic.Capture.Core.Tests/MagicActionTests.cs': '6daa3cdf7996dfac555f4b2f9ae28939ce40dc3d38275be74c804014a4b3abcf',
}

texts: dict[str, str] = {}
for rel, expected in EXPECTED.items():
    text = (ROOT / rel).read_text(encoding='utf-8')
    actual = hashlib.sha256(text.encode('utf-8')).hexdigest()
    if actual != expected:
        raise SystemExit(f'{rel}: sha256 {actual} != {expected}')
    texts[rel] = text

history = texts['tests/Magic.Capture.Core.Tests/HistoryQueryTests.cs']
for display in ('DISPLAY1', 'DISPLAY2'):
    expected = f'monitor: @"\\\\.\\{display}"'
    if history.count(expected) != 1:
        raise SystemExit(f'history: expected one verbatim monitor literal for {display}')
    invalid = f'monitor: "\\\\.\\{display}"'
    if invalid in history:
        raise SystemExit(f'history: invalid escaped monitor literal still present for {display}')

project = texts['tests/Magic.Capture.Core.Tests/Magic.Capture.Core.Tests.csproj']
if project.count('<Using Include="Xunit" />') != 1:
    raise SystemExit('test project: expected exactly one xUnit global using')

settings = texts['tests/Magic.Capture.Core.Tests/SettingsReferencePolicyTests.cs']
if settings.count('CaptureProfileSource.Region') != 2 or 'CaptureSourceKind.Region' in settings:
    raise SystemExit('settings tests: stale capture source enum contract')

magic_action = texts['tests/Magic.Capture.Core.Tests/MagicActionTests.cs']
if magic_action.count('SchemaVersion: 99') != 1 or 'schemaVersion: 99' in magic_action:
    raise SystemExit('magic action tests: stale SchemaVersion named argument')

print('GREEN test compile contracts: ' + ' '.join(f'{Path(k).name}={v}' for k, v in EXPECTED.items()))
