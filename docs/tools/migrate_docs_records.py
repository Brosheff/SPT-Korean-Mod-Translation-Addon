#!/usr/bin/env python3
"""One-time safe cleanup for source trees updated by file overwrite.

Overwriting a ZIP does not delete old files. This utility moves old top-level
records/, reference-data/, tools/ and leftover work files in docs/ into the
appropriate docs/ subdirectory without replacing differing archived files.
New root-level docs manuals must be listed in verify_workflow.py.
"""
import argparse
import hashlib
from pathlib import Path
import shutil
import sys
from verify_workflow import ALLOWED_DOC_FILES, OBSOLETE_BUILD_HELPERS


def sha256(path):
    dig = hashlib.sha256()
    with path.open('rb') as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b''):
            dig.update(chunk)
    return dig.hexdigest()


def record_destination(root, relative):
    if relative.parts[0] == 'patches':
        return root / 'docs' / 'records' / 'patches' / Path(*relative.parts[1:])
    if relative.name in ('PARENT_PATTERN_AUDIT_1.7.14.md', 'LOCALE_DISPLAY_1.0.0_KO.md'):
        return root / 'docs' / 'records' / 'audits' / relative.name
    if relative.suffix == '.csv' and '버전' in relative.name:
        return root / 'docs' / 'records' / 'versioning' / relative.name
    if relative.name.startswith('번역_원본모드_버전관리대장_'):
        return root / 'docs' / 'records' / 'versioning' / relative.name
    if relative.suffix == '.csv':
        return root / 'docs' / 'records' / 'audits' / relative.name
    if relative.name.startswith(('1.0.4_', '3개_모드_', '3개모드_')):
        return root / 'docs' / 'records' / 'work-history' / relative.name
    if relative.as_posix() == 'prompts/vanilla_trader_dialogue_reference.json':
        return root / 'docs' / 'reference-data' / relative.name
    return root / 'docs' / 'records' / 'legacy-imports' / relative


def migrate(root):
    root = Path(root).resolve()
    docs = root / 'docs'
    if not docs.is_dir():
        return []
    actions = []
    # Old releases placed AGENTS.md in the repository root. Preserve that
    # superseded document as history; never overwrite docs/AGENTS.md.
    old_agent = root / 'AGENTS.md'
    new_agent = docs / 'AGENTS.md'
    if old_agent.is_file():
        if not new_agent.is_file():
            raise ValueError('missing current docs/AGENTS.md; refusing to migrate root AGENTS.md')
        if sha256(old_agent) == sha256(new_agent):
            old_agent.unlink()
            actions.append(('removed identical old entrypoint', Path('AGENTS.md'), new_agent.relative_to(root)))
        else:
            archive = docs / 'records' / 'legacy-imports' / 'AGENTS.md'
            archive.parent.mkdir(parents=True, exist_ok=True)
            idx = 1
            while archive.exists():
                archive = archive.with_name(f'{archive.stem}.legacy-{idx}{archive.suffix}')
                idx += 1
            shutil.move(str(old_agent), str(archive))
            actions.append(('archived superseded root entrypoint', Path('AGENTS.md'), archive.relative_to(root)))
    # Older source ZIPs placed GPT working directories at project root.
    # Move their contents into docs/ before scanning the older docs layout.
    for category in ('records', 'reference-data', 'tools'):
        old_dir = root / category
        new_dir = docs / category
        if not old_dir.is_dir():
            continue
        new_dir.mkdir(parents=True, exist_ok=True)
        for old_file in sorted(old_dir.rglob('*')):
            if not old_file.is_file():
                continue
            relpath = old_file.relative_to(old_dir)
            new_file = new_dir / relpath
            new_file.parent.mkdir(parents=True, exist_ok=True)
            if new_file.exists():
                if sha256(new_file) == sha256(old_file):
                    old_file.unlink()
                    actions.append(('removed identical old location', Path(category) / relpath, new_file.relative_to(root)))
                    continue
                archive = docs / 'records' / 'legacy-imports' / category / relpath
                archive.parent.mkdir(parents=True, exist_ok=True)
                index=1
                while archive.exists():
                    archive=archive.with_name(f'{archive.stem}.legacy-{index}{archive.suffix}')
                    index+=1
                new_file=archive
            shutil.move(str(old_file), str(new_file))
            actions.append(('moved legacy GPT workspace file', Path(category) / relpath, new_file.relative_to(root)))
        for directory in sorted((x for x in old_dir.rglob('*') if x.is_dir()), key=lambda x: len(x.parts), reverse=True):
            if not any(directory.iterdir()):
                directory.rmdir()
        if not any(old_dir.iterdir()):
            old_dir.rmdir()
    # Previous releases bundled compile/build helpers in docs/tools/. Remove
    # their ACTIVE copies from the GPT workspace. Preserve original bytes as
    # historical records so no old user file is silently discarded.
    for name in sorted(OBSOLETE_BUILD_HELPERS):
        legacy = docs / 'tools' / name
        if not legacy.is_file():
            continue
        dest = docs / 'records' / 'legacy-imports' / 'old-build-tools' / name
        dest.parent.mkdir(parents=True, exist_ok=True)
        if dest.is_file() and sha256(dest) == sha256(legacy):
            legacy.unlink()
            actions.append(('removed duplicated legacy build helper', legacy.relative_to(root), dest.relative_to(root)))
            continue
        idx = 1
        while dest.exists():
            dest = dest.with_name(f'{dest.stem}.legacy-{idx}{dest.suffix}')
            idx += 1
        shutil.move(str(legacy), str(dest))
        actions.append(('archived legacy build helper', legacy.relative_to(root), dest.relative_to(root)))
    for path in sorted(docs.rglob('*')):
        if not path.is_file():
            continue
        rel = path.relative_to(docs)
        if rel.as_posix() in ALLOWED_DOC_FILES or rel.parts[0] in ('records', 'reference-data', 'tools'):
            continue
        target = record_destination(root, rel)
        target.parent.mkdir(parents=True, exist_ok=True)
        if target.is_file():
            if sha256(path) == sha256(target):
                path.unlink()  # the identical data is already preserved elsewhere
                actions.append(('removed duplicated old location', rel, target.relative_to(root)))
                continue
            # Preserve a differing old snapshot without replacing the new one.
            fallback = root / 'docs' / 'records' / 'legacy-imports' / rel
            fallback.parent.mkdir(parents=True, exist_ok=True)
            target = fallback
            idx = 1
            while target.exists():
                target = fallback.with_name(f'{fallback.stem}.legacy-{idx}{fallback.suffix}')
                idx += 1
        shutil.move(str(path), str(target))
        actions.append(('archived old location', rel, target.relative_to(root)))
    required_dirs = {docs / d for d in ('records', 'reference-data', 'tools', 'prompts')}
    for d in sorted((x for x in docs.rglob('*') if x.is_dir()), key=lambda x:len(x.parts),reverse=True):
        if d != docs and d not in required_dirs and not any(d.iterdir()):
            d.rmdir()
    return actions


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[2])
    opts=parser.parse_args()
    try:
        changes=migrate(opts.root)
    except (OSError, ValueError) as exc:
        print('FAIL: ' + str(exc),file=sys.stderr)
        return 1
    for kind,old,new in changes:
        print(f'{kind}: {old.as_posix()} -> {new.as_posix()}')
    print(f'OK: {len(changes)} legacy GPT files relocated/deduplicated; no records overwritten')
    return 0

if __name__=='__main__':
    raise SystemExit(main())
