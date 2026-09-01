from __future__ import annotations
import hashlib
import sys
from pathlib import Path

ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else 'reconstructed')
FILES = {
    'src/Magic.Capture.App/MainWindow.xaml': (
        '485be2a67751c30a5492b9014e5cf83bd75033e91b495fa160e24cf8f030bc38',
        'cd8308867d692f68bf5c27effd55dc89946f2c1468de66ae47732f19e6ba0a1e'),
    'src/Magic.Capture.App/Views/DesignToolsWindow.xaml': (
        '25cb78fd416ef6e5184fab2062559fa96093155e480082fc1343232c2cc28099',
        'f34b60ab05632632824566c266a4d08392c21e94eb43db58c33b79923fa124f7'),
    'src/Magic.Capture.App/Views/TableWorkspaceWindow.xaml': (
        '689942d981ed47214a25674320a1d24eb5a06ffad5434fed18134c6989488590',
        'fb526cbb83b877f46d74d938700cbb3e6f8670be6ebe47bebae70a1559dec23d'),
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

rel = 'src/Magic.Capture.App/MainWindow.xaml'
text = read_verified(rel)
if text.count('<WrapPanel Orientation="Horizontal">') != 1 or text.count('</WrapPanel>') != 1:
    raise SystemExit(f'{rel}: expected one WrapPanel pair')
text = text.replace('<WrapPanel Orientation="Horizontal">', '<VariableSizedWrapGrid Orientation="Horizontal">')
text = text.replace('</WrapPanel>', '</VariableSizedWrapGrid>')
write_verified(rel, text)

rel = 'src/Magic.Capture.App/Views/DesignToolsWindow.xaml'
text = read_verified(rel)
if text.count('<WrapPanel Orientation="Horizontal">') != 2 or text.count('</WrapPanel>') != 2:
    raise SystemExit(f'{rel}: expected two WrapPanel pairs')
text = text.replace('<WrapPanel Orientation="Horizontal">', '<VariableSizedWrapGrid Orientation="Horizontal">')
text = text.replace('</WrapPanel>', '</VariableSizedWrapGrid>')
write_verified(rel, text)

rel = 'src/Magic.Capture.App/Views/TableWorkspaceWindow.xaml'
text = read_verified(rel)
old = 'TextWrapping="Wrap" VerticalScrollBarVisibility="Auto" PlaceholderText="Selected cell text"'
new = 'TextWrapping="Wrap" ScrollViewer.VerticalScrollBarVisibility="Auto" PlaceholderText="Selected cell text"'
if text.count(old) != 1:
    raise SystemExit(f'{rel}: expected one TextBox scrollbar target')
write_verified(rel, text.replace(old, new, 1))

print('OK WinUI XAML fixes: 3 files patched with verified pre/post SHA-256')
