from __future__ import annotations
import hashlib
import runpy
import sys
from pathlib import Path

runpy.run_path(str(Path(__file__).with_name('apply_test_compile_fixes_base.py')), run_name='__main__')

ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else 'reconstructed')
REL = 'tests/Magic.Capture.Core.Tests/AppSettingsSchemaTests.cs'
PRE = '613b43f2d24c5d436a63b5ed5e7c8b16bbaa3a25668acbe42777d2ec415f04e5'
POST = '0f679b899f87b708454819c3e5452fe231a2586d42b3e97acc85592582e6a17f'

path = ROOT / REL
text = path.read_text(encoding='utf-8')
actual = hashlib.sha256(text.encode('utf-8')).hexdigest()
if actual != PRE:
    raise SystemExit(f'{REL}: schema-test preimage sha256 {actual} != {PRE}')
old = '''    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]'''
new = '''    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]'''
if text.count(old) != 1:
    raise SystemExit(f'{REL}: expected one stale schema support matrix')
text = text.replace(old, new, 1)
actual = hashlib.sha256(text.encode('utf-8')).hexdigest()
if actual != POST:
    raise SystemExit(f'{REL}: schema-test postimage sha256 {actual} != {POST}')
path.write_text(text, encoding='utf-8', newline='')
print(f'OK settings schema test contract: current=2 supported, future=3 fail-closed, sha256={actual}')
