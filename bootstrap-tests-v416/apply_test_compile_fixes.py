from __future__ import annotations
import hashlib
import sys
from pathlib import Path

ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else 'reconstructed')
REL = 'tests/Magic.Capture.Core.Tests/HistoryQueryTests.cs'
PRE = 'a1abdcd4dacfa6cb583e66d3f994208b7216e16778018ebc4587bfb13bd3f9c7'
POST = 'b3a65dfc8c8332db22d48bb1757fa4ddc1c77116cdddc8ef06b63d7337a05fac'


def digest(text: str) -> str:
    return hashlib.sha256(text.encode('utf-8')).hexdigest()


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected one replacement target, found {count}')
    return text.replace(old, new, 1)


path = ROOT / REL
text = path.read_text(encoding='utf-8')
actual = digest(text)
if actual != PRE:
    raise SystemExit(f'{REL}: preimage sha256 {actual} != {PRE}')

for display in ('DISPLAY1', 'DISPLAY2'):
    old = f'monitor: "\\\\.\\{display}"'
    new = f'monitor: @"\\\\.\\{display}"'
    text = replace_once(text, old, new, f'{REL}:{display}')

actual = digest(text)
if actual != POST:
    raise SystemExit(f'{REL}: postimage sha256 {actual} != {POST}')

path.write_text(text, encoding='utf-8', newline='')
print(f'OK test compile fix: {REL} pre={PRE} post={POST}')
