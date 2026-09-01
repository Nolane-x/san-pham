from __future__ import annotations
import hashlib
import re
import sys
from pathlib import Path

ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else 'reconstructed')
FILES = {
    'src/Magic.Capture.App/MainWindow.xaml.cs': (
        '8fa6813b10885f10218e90d5d9b60557e3b804b2169ef9ced21365ddadd996f4',
        'fac615084e9bcfea03d7e69dfb9466aaa061c3585e85a7fe6a3a678ce6833e19'),
    'src/Magic.Capture.App/Views/CompareWindow.xaml.cs': (
        'e26680ca59651edbb8ae5ff848a1b987e880358ecf664c0014b40158e4f910a5',
        '4c19ba9ce13a30eb0a57a853b8eb0f9eded5ded613cbe5c92db69b9a9ef303aa'),
    'src/Magic.Capture.App/Views/VideoEditorWindow.xaml.cs': (
        'de06abc31e1aeaa5e6ab7b1f5a81bbbc1c59e5ef726366be1524b42d097f5279',
        'bd7e53f7c6c3afd78071981a3d397565e7c3b14d7ee049c7eaebd36b4c508ee9'),
}

def digest(text: str) -> str:
    return hashlib.sha256(text.encode('utf-8')).hexdigest()

def read_verified(rel: str) -> str:
    text = (ROOT / rel).read_text(encoding='utf-8')
    actual = digest(text)
    expected = FILES[rel][0]
    if actual != expected:
        raise SystemExit(f'{rel}: preimage sha256 {actual} != {expected}')
    return text

def write_verified(rel: str, text: str) -> None:
    actual = digest(text)
    expected = FILES[rel][1]
    if actual != expected:
        raise SystemExit(f'{rel}: postimage sha256 {actual} != {expected}')
    (ROOT / rel).write_text(text, encoding='utf-8', newline='')

for rel in [
    'src/Magic.Capture.App/MainWindow.xaml.cs',
    'src/Magic.Capture.App/Views/CompareWindow.xaml.cs',
]:
    text = read_verified(rel)
    old = 'meta charset="utf-8"'
    if text.count(old) != 1:
        raise SystemExit(f'{rel}: expected one HTML charset quote target')
    write_verified(rel, text.replace(old, "meta charset='utf-8'", 1))

rel = 'src/Magic.Capture.App/Views/VideoEditorWindow.xaml.cs'
text = read_verified(rel)
pattern = re.compile(r'(?<!\\)\\\.fff')
matches = pattern.findall(text)
if len(matches) != 18:
    raise SystemExit(f'{rel}: expected 18 invalid TimeSpan escape occurrences, found {len(matches)}')
text = pattern.sub(lambda _: '\\\\.fff', text)
write_verified(rel, text)

print('OK app C# syntax fixes: 3 files patched with verified pre/post SHA-256')
