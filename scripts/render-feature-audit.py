#!/usr/bin/env python3
from __future__ import annotations
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
audit = json.loads((ROOT / 'release/feature-audit-660.json').read_text(encoding='utf-8'))
counts = audit['counts']
lines = [
    '# Magic Capture Desktop — exact 660-feature audit',
    '',
    '> Source-truth ledger. `Done` means an end-to-end implementation exists in the source tree; it does **not** mean Windows runtime validation has been executed in this Linux environment. `Foundation` is deliberately not counted as complete.',
    '',
    '## Status totals',
    '',
    '| Status | Count | Meaning |',
    '|---|---:|---|',
    f"| Done | {counts['Done']} | End-to-end source implementation |",
    f"| Partial | {counts['Partial']} | Usable subset, but requirement is not complete |",
    f"| Foundation | {counts['Foundation']} | Core/model/primitive exists; end-user feature incomplete |",
    f"| ReleaseTest | {counts['ReleaseTest']} | Compatibility/maturity item requiring real Windows/hardware verification |",
    f"| Missing | {counts['Missing']} | No sufficient implementation yet |",
    '| **Total** | **660** | Must always equal 660 |',
    '',
    '## Rules',
    '',
    '- A feature is never promoted from `Foundation`/`Partial` to `Done` because a neighboring capability exists.',
    '- Runtime-only compatibility items stay `ReleaseTest` until exercised on the required Windows/hardware matrix.',
    '- Every future wave must update this ledger and keep IDs exactly 1 through 660.',
    '',
    '## Ledger',
    '',
    '| # | Feature | Status | Target wave | Evidence |',
    '|---:|---|---|---|---|',
]
for item in audit['features']:
    name = str(item['name']).replace('|', '\\|').replace('\n', ' ')
    status = item['status']
    wave = str(item.get('wave') or '').replace('|', '\\|')
    evidence = str(item.get('evidence') or '').replace('|', '\\|').replace('\n', ' ')
    lines.append(f"| {item['id']} | {name} | **{status}** | `{wave}` | {evidence} |")
(ROOT / 'docs/FEATURE_AUDIT_660.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
print(f"Rendered {len(audit['features'])} feature rows; Done={counts['Done']}")
