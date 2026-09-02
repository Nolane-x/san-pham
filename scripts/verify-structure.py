#!/usr/bin/env python3
from __future__ import annotations

import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EXCLUDED_PARTS = {'.git', '.vs', 'bin', 'obj', 'artifacts', '__pycache__'}
errors: list[str] = []


def included(path: Path) -> bool:
    try:
        rel = path.relative_to(ROOT)
    except ValueError:
        return False
    return not any(part in EXCLUDED_PARTS for part in rel.parts)


# Source archives must not carry editor/merge scratch files. These are easy to miss
# because they are not parsed by normal C#/XAML checks, but they create ambiguity
# about which source is authoritative and can accidentally ship stale code.
TEMP_SUFFIXES = ('.tmp', '.orig', '.rej')
TEMP_NAMES = {'.DS_Store'}
for path in ROOT.rglob('*'):
    if not path.is_file() or not included(path):
        continue
    if path.name in TEMP_NAMES or path.name.endswith(TEMP_SUFFIXES) or path.name.endswith('~'):
        errors.append(f'temporary/source-conflict artifact must not ship: {path.relative_to(ROOT)}')


xml_paths: list[Path] = []
for pattern in ('*.xaml', '*.csproj', '*.props', '*.targets', '*.appxmanifest', '*.xml'):
    xml_paths.extend(path for path in ROOT.rglob(pattern) if included(path))
xml_paths = sorted(set(xml_paths))
for path in xml_paths:
    try:
        ET.parse(path)
    except Exception as exc:
        errors.append(f'XML parse failed: {path.relative_to(ROOT)}: {exc}')

json_paths = sorted(path for path in ROOT.rglob('*.json') if included(path))
for path in json_paths:
    try:
        json.loads(path.read_text(encoding='utf-8'))
    except Exception as exc:
        errors.append(f'JSON parse failed: {path.relative_to(ROOT)}: {exc}')

# XAML handlers are compile-time contracts. Verify event handler names resolve in the paired
# code-behind file before a source archive can be produced.
event_names = {
    'Click', 'ItemClick', 'SelectionChanged', 'TextChanged', 'ValueChanged', 'Loaded', 'Closed',
    'KeyDown', 'KeyUp', 'Tapped', 'DoubleTapped', 'RightTapped', 'SizeChanged', 'Checked', 'Unchecked',
    'PointerPressed', 'PointerReleased', 'PointerMoved', 'PointerEntered', 'PointerExited',
    'DragEnter', 'DragLeave', 'DragOver', 'Drop', 'Invoked', 'Opening', 'Closing',
}
handler_re = re.compile(r'\b(' + '|'.join(sorted(event_names)) + r')="([A-Za-z_][A-Za-z0-9_]*)"')
handler_count = 0
for xaml in sorted(path for path in (ROOT / 'src').rglob('*.xaml') if included(path)):
    text = xaml.read_text(encoding='utf-8', errors='replace')
    handlers = [handler for _, handler in handler_re.findall(text)]
    if not handlers:
        continue
    codebehind = Path(str(xaml) + '.cs')
    if not codebehind.exists():
        errors.append(f'XAML handlers have no code-behind: {xaml.relative_to(ROOT)}')
        continue
    code = codebehind.read_text(encoding='utf-8', errors='replace')
    for handler in handlers:
        handler_count += 1
        if re.search(rf'\b{re.escape(handler)}\s*\(', code) is None:
            errors.append(f'XAML handler missing: {xaml.relative_to(ROOT)} -> {handler}')

# The exact 660-row ledger is an independently parsed release artifact as well as a verifier rule.
audit_path = ROOT / 'release/feature-audit-660.json'
try:
    audit = json.loads(audit_path.read_text(encoding='utf-8'))
    features = audit.get('features') or []
    ids = [item.get('id') for item in features]
    if audit.get('total') != 660 or len(features) != 660 or ids != list(range(1, 661)):
        errors.append('feature audit must contain exactly IDs 1 through 660 in order')
    statuses = ('Done', 'Partial', 'Foundation', 'ReleaseTest', 'Missing')
    computed = {status: sum(item.get('status') == status for item in features) for status in statuses}
    stored = audit.get('counts') or {}
    if computed != {status: stored.get(status, 0) for status in statuses}:
        errors.append(f'feature audit counts mismatch: computed={computed} stored={stored}')
    if sum(computed.values()) != 660:
        errors.append('feature audit counts must total 660')
except Exception as exc:
    errors.append(f'feature audit validation failed: {exc}')

print('Magic Capture Desktop structural verifier')
print(f'  XML/MSBuild files : {len(xml_paths)}')
print(f'  JSON files        : {len(json_paths)}')
print(f'  XAML handlers     : {handler_count}')
print(f'  Errors            : {len(errors)}')
for error in errors:
    print(f'ERROR: {error}')
sys.exit(1 if errors else 0)
