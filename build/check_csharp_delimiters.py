#!/usr/bin/env python3
"""Fail fast on mismatched C# (), [] and {} outside string/comment literals.

This catches elementary syntax regressions (including ArenaMode's CS1026) before
starting the multi-project build. It is not a replacement for dotnet build.
"""
from pathlib import Path
import re
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



def code_without_literals(source):
    """Mask strings/comments for the missing-reference preflight, keeping line numbers.

    This is a conservative early warning, not a complete C# parser or compiler.
    """
    out = list(source)
    i = 0
    mode = 'code'
    while i < len(source):
        ch = source[i]
        nxt = source[i + 1] if i + 1 < len(source) else ''
        if mode == 'code':
            if ch == '/' and nxt == '/':
                out[i] = out[i + 1] = ' '
                mode = 'line-comment'; i += 2; continue
            if ch == '/' and nxt == '*':
                out[i] = out[i + 1] = ' '
                mode = 'block-comment'; i += 2; continue
            if ch == '@' and nxt == '"':
                out[i] = out[i + 1] = ' '
                mode = 'verbatim'; i += 2; continue
            if ch == '$' and nxt == '@' and i + 2 < len(source) and source[i + 2] == '"':
                out[i] = out[i + 1] = out[i + 2] = ' '
                mode = 'verbatim'; i += 3; continue
            if ch == '@' and nxt == '$' and i + 2 < len(source) and source[i + 2] == '"':
                out[i] = out[i + 1] = out[i + 2] = ' '
                mode = 'verbatim'; i += 3; continue
            if ch == '"': mode = 'string'; out[i] = ' '; i += 1; continue
            if ch == "'": mode = 'char'; out[i] = ' '; i += 1; continue
            i += 1; continue
        if ch == '\n' and mode == 'line-comment':
            mode = 'code'; i += 1; continue
        if ch != '\n': out[i] = ' '
        if mode == 'line-comment': i += 1; continue
        if mode == 'block-comment':
            if ch == '*' and nxt == '/':
                out[i + 1] = ' '; mode = 'code'; i += 2
            else: i += 1
            continue
        if mode == 'verbatim':
            if ch == '"' and nxt == '"':
                out[i + 1] = ' '; i += 2
            elif ch == '"': mode = 'code'; i += 1
            else: i += 1
            continue
        if ch == '\\' and i + 1 < len(source):
            if source[i + 1] != '\n': out[i + 1] = ' '
            i += 2; continue
        if (mode == 'string' and ch == '"') or (mode == 'char' and ch == "'"):
            mode = 'code'
        i += 1
    return ''.join(out)


def unreferenced_game_type_errors(source):
    """The client/server project does not reference EFT game assemblies.

    Source-mod types must be obtained via reflection. A direct `EFT.UI.*`
    declaration otherwise causes CS0246 before any plugin can start.
    """
    masked = code_without_literals(source)
    return [f'line {masked.count(chr(10), 0, hit.start()) + 1}: direct EFT type requires an absent game assembly; use scoped runtime reflection'
            for hit in re.finditer(r'\b(?:global::)?EFT\s*\.', masked)]


def check_sources(root):
    errors = []
    for folder in ('src', 'server-src'):
        for path in sorted((root / folder).rglob('*.cs')):
            try:
                source = path.read_text(encoding='utf-8-sig')
                for error in check_text(source) + unreferenced_game_type_errors(source):
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
