#!/usr/bin/env python3
"""Build-owned checks: active translation sources and installable release contents.

Does not inspect GPT docs or backups. Works with docs/ completely absent.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path, PurePosixPath
import sys
import zipfile

PROJECT_ROOT = Path(__file__).resolve().parents[1]
TOP_DIRS = ('BepInEx/plugins/SPT_Mod_Korean_Addon/', 'SPT_Runtime/user/mods/SPT_Mod_Korean_Server/')
CLIENT = TOP_DIRS[0]
SERVER = TOP_DIRS[1]
REQUIRED_DLLS = (CLIENT + 'SPT_Mod_Korean_Addon.dll', SERVER + 'SPT_Mod_Korean_Server.dll')
DATA_DIRS = ('translations', 'config-ui', 'native-locales', 'contract-locales')

def json_file(path):
    def check_pairs(pairs):
        result = {}
        for key, val in pairs:
            if key in result:
                raise ValueError('duplicate JSON property: ' + key)
            result[key] = val
        return result
    return json.loads(Path(path).read_text(encoding='utf-8-sig'), object_pairs_hook=check_pairs)

def source_checks(root):
    errors = []
    for required in ('translations', 'config-ui', 'src', 'server-src', 'build',
                     'native-locales', 'contract-locales', 'BUILD_DISTRIBUTION.ps1'):
        if not (root / required).exists():
            errors.append('missing project input: ' + required)
    if errors:
        return errors
    configs = sorted((root / 'config-ui').glob('*.json'))
    guids = set()
    for p in configs:
        try:
            doc = json_file(p)
            if 'source_mod_version' in doc or not isinstance(doc.get('mod_version'), str) or not doc['mod_version'].strip():
                errors.append(p.name + ': invalid/obsolete mod_version metadata')
            if doc.get('plugin_guid') != p.stem:
                errors.append(p.name + ': plugin_guid differs from filename')
            if p.stem in guids:
                errors.append(p.name + ': duplicate config GUID')
            guids.add(p.stem)
            entries = set()
            for i, entry in enumerate(doc.get('entries') or []):
                if not isinstance(entry, dict):
                    errors.append(f'{p.name}: entries[{i}] not an object'); continue
                ident = (entry.get('section'), entry.get('key'))
                # Empty key is valid for mods with an empty ConfigDefinition key.
                if not isinstance(ident[0], str) or not isinstance(ident[1], str):
                    errors.append(f'{p.name}: entries[{i}] invalid section/key')
                if ident in entries:
                    errors.append(f'{p.name}: duplicate F12 section/key {ident}')
                entries.add(ident)
                if entry.get('source_display_name') and not entry.get('translation_display_name'):
                    errors.append(f'{p.name}: untranslated F12 name {ident}')
                if entry.get('source_description') and not entry.get('translation_description'):
                    errors.append(f'{p.name}: untranslated F12 description {ident}')
                for j, value in enumerate(entry.get('values') or []):
                    if not isinstance(value, dict) or not isinstance(value.get('source'), str) or not isinstance(value.get('translation'), str) or not value['translation']:
                        errors.append(f'{p.name}: untranslated F12 value {ident}[{j}]')
            cats = set()
            for i, cat in enumerate(doc.get('categories') or []):
                if not isinstance(cat, dict) or not cat.get('source') or not cat.get('translation'):
                    errors.append(f'{p.name}: missing F12 category translation [{i}]')
                elif cat['source'] in cats:
                    errors.append(f'{p.name}: duplicate F12 category {cat["source"]}')
                else:
                    cats.add(cat['source'])
        except (ValueError, OSError, TypeError) as exc:
            errors.append(f'{p.name}: {exc}')
    # Current release targets Aishi 2.0.4 / Special Forces 2.0.1 only.
    # Deleted old-version compatibility profiles must not reappear unnoticed.
    deprecated_profiles = (
        'ISB Aishi - 2.0.3 Compatibility.json',
        'ISB Special Forces - 2.0.0 Compatibility.json',
    )
    for old_name in deprecated_profiles:
        if (root / 'translations' / old_name).is_file():
            errors.append('obsolete mod-version compatibility profile must be removed: ' + old_name)
    ids = set()
    for p in sorted((root / 'translations').glob('*.json')):
        try:
            doc = json_file(p)
            if 'source_mod_version' in doc or not isinstance(doc.get('mod_version'), str) or not doc['mod_version'].strip():
                errors.append(p.name + ': invalid/obsolete mod_version metadata')
            mod_id = doc.get('mod_id')
            if not mod_id:
                errors.append(p.name + ': missing mod_id')
            elif mod_id in ids:
                errors.append(p.name + ': duplicate mod_id ' + str(mod_id))
            ids.add(mod_id)
        except (ValueError, OSError, TypeError) as exc:
            errors.append(f'{p.name}: {exc}')
    # A prior ArenaMode patch failed the user build with CS1026 although JSON
    # tests passed. Catch basic C# delimiter damage before invoking dotnet.
    from check_csharp_delimiters import check_sources
    errors.extend(check_sources(root))
    # Static checks for explicit capacities and identical-key/identical-source conflicts.
    # Different sources remain distinct candidates. Never report this as in-game proof.
    from audit_locale_integrity import audit
    errors.extend(audit(root / 'translations'))
    # Build inputs only. GPT workspace/document location is independently checked
    # by the optional `documents` command and must never block a DLL build.
    # In particular, Upload/, backups and archived releases are not build inputs.
    # Only guaranteed existing bridge, not a runtime proof of hook installation.
    return errors

def expected_source_data(root):
    files = {}
    for directory in DATA_DIRS:
        for p in (root / directory).rglob('*.json'):
            rel = p.relative_to(root).as_posix()
            files[CLIENT + rel] = p.read_bytes()
    return files


def distribution_files(root, files):
    errors = []
    expected = expected_source_data(root)
    authorized = set(expected) | set(REQUIRED_DLLS)
    seen = set()
    for name, data in files.items():
        pp = PurePosixPath(name)
        if name.startswith('/') or '\\' in name or '..' in pp.parts or pp.is_absolute():
            errors.append('unsafe ZIP/stage path: ' + name); continue
        if name in seen:
            errors.append('duplicate release path: ' + name)
        seen.add(name)
        if name not in authorized:
            errors.append('unauthorized release file: ' + name)
        if not name.startswith(TOP_DIRS):
            errors.append('not an install-layout path: ' + name)
        elif name not in REQUIRED_DLLS:
            if not name.startswith(CLIENT):
                errors.append('unexpected server output: ' + name)
            else:
                tail = name[len(CLIENT):]
                if not any(tail.startswith(d + '/') for d in DATA_DIRS) or not tail.endswith('.json'):
                    errors.append('development/unexpected client file: ' + name)
    for p in REQUIRED_DLLS:
        if p not in files:
            errors.append('missing runtime DLL: ' + p)
        elif not files[p].startswith(b'MZ'):
            errors.append('invalid PE DLL header: ' + p)
    for name, source_bytes in expected.items():
        if name not in files:
            errors.append('missing translated release data: ' + name)
        elif hashlib.sha256(files[name]).digest() != hashlib.sha256(source_bytes).digest():
            errors.append('stale/mismatched release data: ' + name)
    return errors


def stage_files(path):
    result = {}
    for p in path.rglob('*'):
        if p.is_file():
            result[p.relative_to(path).as_posix()] = p.read_bytes()
    return result


def zip_files(path):
    errors = []
    result = {}
    with zipfile.ZipFile(path) as z:
        for info in z.infolist():
            if info.is_dir(): continue
            name = info.filename
            if name in result:
                errors.append('duplicate ZIP entry: ' + name)
            result[name] = z.read(info)
    return result, errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=PROJECT_ROOT)
    sub = parser.add_subparsers(dest='mode', required=True)
    sub.add_parser('source', help='Check only current project source, translations and F12')
    dist = sub.add_parser('distribution', help='Validate staged install tree and release ZIP')
    dist.add_argument('--stage', type=Path, required=True)
    dist.add_argument('--zip', type=Path, required=True)
    args = parser.parse_args()
    root = args.root.resolve()
    if args.mode == 'source':
        errors = source_checks(root)
    else:
        try:
            stage = stage_files(args.stage)
            archived, errors = zip_files(args.zip)
            errors += distribution_files(root, stage)
            errors += distribution_files(root, archived)
            if archived != stage:
                errors.append('distribution ZIP is different from staged files')
        except (OSError, ValueError, zipfile.BadZipFile) as exc:
            errors = ['release inspection failed: ' + str(exc)]
    if errors:
        for msg in errors:
            print('FAIL: ' + msg, file=sys.stderr)
        print(f'FAILED: {len(errors)} issue(s)', file=sys.stderr)
        return 1
    print('OK: ' + ('runtime source/F12 checks' if args.mode == 'source' else 'staged release and ZIP contents'))
    return 0

if __name__ == '__main__':
    sys.exit(main())
