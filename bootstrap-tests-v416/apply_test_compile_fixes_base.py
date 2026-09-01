from __future__ import annotations
import hashlib
import sys
from pathlib import Path

ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else 'reconstructed')

FILES = {
    'tests/Magic.Capture.Core.Tests/HistoryQueryTests.cs': (
        'a1abdcd4dacfa6cb583e66d3f994208b7216e16778018ebc4587bfb13bd3f9c7',
        'b3a65dfc8c8332db22d48bb1757fa4ddc1c77116cdddc8ef06b63d7337a05fac'),
    'tests/Magic.Capture.Core.Tests/Magic.Capture.Core.Tests.csproj': (
        'e6595ac6c7fe0745f3a0a2cf8c1033cc99ebea3cf162acff08d7b3ca607090cd',
        '71c50c7e3d7581a5933dffe6ca62b2dbff1b17f31176004dc1a8e3254d15ee71'),
    'tests/Magic.Capture.Core.Tests/SettingsReferencePolicyTests.cs': (
        '27f7a144749d88d6dc3165bc420a707fdc6c14688ef4db7218bf714e63910286',
        '4135fc98a9647808e7311e4ce64187b3859ee5d4c33bee3ac4f5899d33bd50c1'),
    'tests/Magic.Capture.Core.Tests/MagicActionTests.cs': (
        'bdf2890540139072434efbe1c4cb381a3095d59fae0bb6906ce6fa5020700123',
        '6daa3cdf7996dfac555f4b2f9ae28939ce40dc3d38275be74c804014a4b3abcf'),
}


def digest(text: str) -> str:
    return hashlib.sha256(text.encode('utf-8')).hexdigest()


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected one replacement target, found {count}')
    return text.replace(old, new, 1)


texts: dict[str, str] = {}
for rel, (before, _) in FILES.items():
    text = (ROOT / rel).read_text(encoding='utf-8')
    actual = digest(text)
    if actual != before:
        raise SystemExit(f'{rel}: preimage sha256 {actual} != {before}')
    texts[rel] = text

history = 'tests/Magic.Capture.Core.Tests/HistoryQueryTests.cs'
for display in ('DISPLAY1', 'DISPLAY2'):
    old = f'monitor: "\\\\.\\{display}"'
    new = f'monitor: @"\\\\.\\{display}"'
    texts[history] = replace_once(texts[history], old, new, f'{history}:{display}')

project = 'tests/Magic.Capture.Core.Tests/Magic.Capture.Core.Tests.csproj'
texts[project] = replace_once(
    texts[project],
    '  <ItemGroup>\n    <ProjectReference Include="..\\..\\src\\Magic.Capture.Core\\Magic.Capture.Core.csproj" />\n  </ItemGroup>',
    '  <ItemGroup>\n    <Using Include="Xunit" />\n  </ItemGroup>\n  <ItemGroup>\n    <ProjectReference Include="..\\..\\src\\Magic.Capture.Core\\Magic.Capture.Core.csproj" />\n  </ItemGroup>',
    project)

settings = 'tests/Magic.Capture.Core.Tests/SettingsReferencePolicyTests.cs'
if texts[settings].count('CaptureSourceKind.Region') != 2:
    raise SystemExit(f'{settings}: expected two stale CaptureSourceKind.Region references')
texts[settings] = texts[settings].replace('CaptureSourceKind.Region', 'CaptureProfileSource.Region')

magic_action = 'tests/Magic.Capture.Core.Tests/MagicActionTests.cs'
texts[magic_action] = replace_once(
    texts[magic_action], 'schemaVersion: 99', 'SchemaVersion: 99', magic_action)

for rel, (_, after) in FILES.items():
    text = texts[rel]
    actual = digest(text)
    if actual != after:
        raise SystemExit(f'{rel}: postimage sha256 {actual} != {after}')
    (ROOT / rel).write_text(text, encoding='utf-8', newline='')

print('OK test compile fixes: monitor literals + xUnit global using + capture profile enum + MagicAction schema named arg, verified pre/post SHA-256')
