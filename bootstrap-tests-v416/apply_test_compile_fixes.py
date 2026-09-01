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

for rel, (_, after) in FILES.items():
    text = texts[rel]
    actual = digest(text)
    if actual != after:
        raise SystemExit(f'{rel}: postimage sha256 {actual} != {after}')
    (ROOT / rel).write_text(text, encoding='utf-8', newline='')

print('OK test compile fixes: history monitor literals + xUnit global using, verified pre/post SHA-256')
