#!/usr/bin/env python3
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EXCLUDED_PARTS = {'.git', '.vs', 'bin', 'obj', 'artifacts', '__pycache__'}
PAIRS = {'(': ')', '[': ']', '{': '}'}
CLOSERS = {value: key for key, value in PAIRS.items()}
errors: list[str] = []
checked = 0


def included(path: Path) -> bool:
    rel = path.relative_to(ROOT)
    return not any(part in EXCLUDED_PARTS for part in rel.parts)


def scan(path: Path, text: str) -> None:
    stack: list[tuple[str, int]] = []
    i = 0
    n = len(text)
    while i < n:
        ch = text[i]
        nxt = text[i + 1] if i + 1 < n else ''

        if ch == '/' and nxt == '/':
            end = text.find('\n', i + 2)
            i = n if end < 0 else end + 1
            continue
        if ch == '/' and nxt == '*':
            end = text.find('*/', i + 2)
            if end < 0:
                errors.append(f'unterminated block comment: {path.relative_to(ROOT)} at offset {i}')
                return
            i = end + 2
            continue

        # Raw C# string literal. This intentionally treats interpolation bodies as literal content;
        # the gate is a delimiter/scratch-corruption smoke check, not a replacement for Roslyn.
        if ch == '"':
            run = 1
            while i + run < n and text[i + run] == '"':
                run += 1
            if run >= 3:
                marker = '"' * run
                end = text.find(marker, i + run)
                if end < 0:
                    errors.append(f'unterminated raw string: {path.relative_to(ROOT)} at offset {i}')
                    return
                i = end + run
                continue

        # Verbatim string, including the @" portion of $@"...".
        if ch == '@' and nxt == '"':
            i += 2
            while i < n:
                if text[i] == '"':
                    if i + 1 < n and text[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                i += 1
            else:
                errors.append(f'unterminated verbatim string: {path.relative_to(ROOT)}')
                return
            continue

        if ch == '"':
            i += 1
            while i < n:
                if text[i] == '\\':
                    i += 2
                    continue
                if text[i] == '"':
                    i += 1
                    break
                i += 1
            else:
                errors.append(f'unterminated string: {path.relative_to(ROOT)}')
                return
            continue

        if ch == "'":
            i += 1
            while i < n:
                if text[i] == '\\':
                    i += 2
                    continue
                if text[i] == "'":
                    i += 1
                    break
                i += 1
            else:
                errors.append(f'unterminated character literal: {path.relative_to(ROOT)}')
                return
            continue

        if ch in PAIRS:
            stack.append((ch, i))
        elif ch in CLOSERS:
            if not stack or stack[-1][0] != CLOSERS[ch]:
                errors.append(f'unmatched {ch!r}: {path.relative_to(ROOT)} at offset {i}')
                return
            stack.pop()
        i += 1

    if stack:
        opener, offset = stack[-1]
        errors.append(f'unclosed {opener!r}: {path.relative_to(ROOT)} at offset {offset}')


for path in sorted((ROOT / 'src').rglob('*.cs')) + sorted((ROOT / 'tests').rglob('*.cs')):
    if not included(path):
        continue
    text = path.read_text(encoding='utf-8', errors='strict')
    checked += 1
    if '\x00' in text:
        errors.append(f'NUL byte in C# source: {path.relative_to(ROOT)}')
        continue
    if any(marker in text for marker in ('<<<<<<<', '=======\n', '>>>>>>>')):
        errors.append(f'merge-conflict marker in C# source: {path.relative_to(ROOT)}')
        continue
    scan(path, text)

print('Magic Capture Desktop C# lexical verifier')
print(f'  C# files : {checked}')
print(f'  Errors    : {len(errors)}')
for error in errors:
    print(f'ERROR: {error}')
sys.exit(1 if errors else 0)
