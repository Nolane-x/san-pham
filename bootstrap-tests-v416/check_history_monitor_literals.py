from __future__ import annotations
import hashlib
import runpy
import sys
from pathlib import Path

runpy.run_path(str(Path(__file__).with_name('check_history_monitor_literals_base.py')), run_name='__main__')

ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else 'reconstructed')
REL = 'tests/Magic.Capture.Core.Tests/AppSettingsSchemaTests.cs'
EXPECTED = '0f679b899f87b708454819c3e5452fe231a2586d42b3e97acc85592582e6a17f'
text = (ROOT / REL).read_text(encoding='utf-8')
actual = hashlib.sha256(text.encode('utf-8')).hexdigest()
if actual != EXPECTED:
    raise SystemExit(f'{REL}: sha256 {actual} != {EXPECTED}')
if text.count('[InlineData(2, true)]') != 1 or text.count('[InlineData(3, false)]') != 1:
    raise SystemExit(f'{REL}: schema support matrix is not current/future fail-closed')
print(f'GREEN settings schema test contract: sha256={actual}')
