#!/usr/bin/env python3
"""Fail fast on mismatched C# (), [] and {} outside string/comment literals.

This catches elementary syntax regressions (including ArenaMode's CS1026) before
starting the multi-project build. It is not a replacement for dotnet build.
"""
from pathlib import Path
import sys

PAIRS = {')': '(', ']': '[', '}': '{'}


def check_text(source):
    stack = []
    i = 0
    line = 1
    n = len(source)
    mode = 'code'
    errors = []
    while i < n:
        ch = source[i]
        nxt = source[i+1] if i + 1 < n else ''
        if ch == '\n':
            line += 1
            if mode == 'line-comment':
                mode = 'code'
            i += 1
            continue
        if mode == 'line-comment':
            i += 1
            continue
        if mode == 'block-comment':
            if ch == '*' and nxt == '/':
                mode = 'code'
                i += 2
            else:
                i += 1
            continue
        if mode in ('string', 'char'):
            if ch == '\\':
                i += 2
                continue
            if (mode == 'string' and ch == '"') or (mode == 'char' and ch == "'"):
                mode = 'code'
            i += 1
            continue
        if mode == 'verbatim':
            if ch == '"' and nxt == '"':
                i += 2
                continue
            if ch == '"':
                mode = 'code'
            i += 1
            continue
        if ch == '/' and nxt == '/':
            mode = 'line-comment'
            i += 2
            continue
        if ch == '/' and nxt == '*':
            mode = 'block-comment'
            i += 2
            continue
        if ch == '@' and nxt == '"':
            mode = 'verbatim'
            i += 2
            continue
        if ch == '$' and nxt == '@' and i + 2 < n and source[i+2] == '"':
            mode = 'verbatim'
            i += 3
            continue
        if ch == '@' and nxt == '$' and i + 2 < n and source[i+2] == '"':
            mode = 'verbatim'
            i += 3
            continue
        if ch == '"':
            # Treat interpolated contents as a string: outer syntax is checked,
            # but braces inside interpolation require the compiler to validate.
            mode = 'string'
            i += 1
            continue
        if ch == "'":
            mode = 'char'
            i += 1
            continue
        if ch in '([{':
            stack.append((ch, line))
        elif ch in ')]}':
            if not stack or stack[-1][0] != PAIRS[ch]:
                errors.append(f'line {line}: unexpected {ch}; expected {stack[-1][0] if stack else "opening delimiter"}')
            else:
                stack.pop()
        i += 1
    if mode in ('block-comment', 'string', 'char', 'verbatim'):
        errors.append(f'line {line}: unterminated {mode}')
    for opening, start in stack:
        errors.append(f'line {start}: unclosed {opening}')
    return errors


def check_sources(root):
    errors = []
    for folder in ('src', 'server-src'):
        for path in sorted((root / folder).rglob('*.cs')):
            try:
                for error in check_text(path.read_text(encoding='utf-8-sig')):
                    errors.append(f'{path.relative_to(root)}: {error}')
            except OSError as ex:
                errors.append(f'{path.relative_to(root)}: {ex}')
    return errors


if __name__ == '__main__':
    root = Path(__file__).resolve().parents[2]
    errors = check_sources(root)
    if errors:
        print('\n'.join('ERROR: ' + message for message in errors))
        sys.exit(1)
    print('OK: C# source delimiter precheck (compiler validation still required)')
