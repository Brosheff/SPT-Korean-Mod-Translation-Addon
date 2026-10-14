#!/usr/bin/env python3
"""Safely retire known pre-docs/note documents left by in-place ZIP updates.

This is an update/working-tree migration, not part of the installable addon.
Legacy files are *moved* into a local, unshipped archive; no contents are lost.
Only the four paths explicitly superseded by docs/note reorganization are touched.
"""
import argparse
from pathlib import Path
import shutil
import sys

PROJECT_ROOT = Path(__file__).resolve().parents[1]
MOVED = (
    ('README.md', 'note/README.md'),
    ('docs/번역지원모드목록.md', 'note/번역지원모드목록.md'),
    ('docs/RELEASE_1.0.1_KO.md', 'note/update.md'),
    ('docs/RELEASE_1.0.2_KO.md', 'note/update.md'),
)


def migrate(root):
    """Return moved (legacy, archive) pairs. Fail without removing legacy files if
    a replacement document is missing. Retain potentially edited old contents.
    """
    root = Path(root)
    pending = []
    for old, new in MOVED:
        source = root / old
        if source.is_file():
            if not (root / new).is_file():
                raise ValueError('cannot retire ' + old + ': missing replacement ' + new)
            pending.append((old, source))
    relocated = []
    for old, source in pending:
        target = root / '_dist' / '_legacy_source_documents' / old
        target.parent.mkdir(parents=True, exist_ok=True)
        if target.exists():
            num = 1
            while True:
                candidate = target.with_name(target.stem + f'.{num}' + target.suffix)
                if not candidate.exists():
                    target = candidate
                    break
                num += 1
        # Destination is on the same volume, normally atomic on Windows.
        # shutil.move also supports unusual separate-volume setups.
        shutil.move(str(source), str(target))
        relocated.append((old, target.relative_to(root).as_posix()))
    return relocated


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=PROJECT_ROOT)
    args = parser.parse_args()
    try:
        relocated = migrate(args.root)
    except (OSError, ValueError) as exc:
        print('FAIL: legacy document migration: ' + str(exc), file=sys.stderr)
        return 1
    for old, saved in relocated:
        print('MIGRATED: ' + old + ' -> ' + saved + ' (preserved; not packaged)')
    if not relocated:
        print('OK: no obsolete source documents to migrate')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
