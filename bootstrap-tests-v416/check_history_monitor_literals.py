from __future__ import annotations
import hashlib
import sys
from pathlib import Path

ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else 'reconstructed')
REL = 'tests/Magic.Capture.Core.Tests/HistoryQueryTests.cs'
EXPECTED = 'b3a65dfc8c8332db22d48bb1757fa4ddc1c77116cdddc8ef06b63d7337a05fac'

path = ROOT / REL
text = path.read_text(encoding='utf-8')
digest = hashlib.sha256(text.encode('utf-8')).hexdigest()
if digest != EXPECTED:
    raise SystemExit(f'{REL}: sha256 {digest} != {EXPECTED}')
for display in ('DISPLAY1', 'DISPLAY2'):
    expected = f'monitor: @"\\\\.\\{display}"'
    if text.count(expected) != 1:
        raise SystemExit(f'{REL}: expected one verbatim monitor literal for {display}')
    invalid = f'monitor: "\\\\.\\{display}"'
    if invalid in text:
        raise SystemExit(f'{REL}: invalid escaped monitor literal still present for {display}')
print(f'GREEN history monitor literals: sha256={digest}')
