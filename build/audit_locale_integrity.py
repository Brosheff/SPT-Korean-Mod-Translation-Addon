#!/usr/bin/env python3
"""Read-only static guard for explicit magazine capacities and ambiguous translations.

Checks the authored JSON, not merged SPT runtime locales or in-game rendering.
Different English sources for the same key are preserved as distinct candidates.
"""
import argparse
import collections
import json
import pathlib
import re

ROUND_IN_ENGLISH = re.compile(r'\b(\d{1,3})\s*[- ]?\s*(?:rounds?|rd)\b', re.I)
ROUND_IN_KOREAN = re.compile(r'(?<!\d)(\d{1,3})\s*발\b')


def check_rows(profiles):
    errors = []
    # Verified upstream inconsistency (True North 4.0.29, vz58.json):
    # 69b5698b... cartridge _max_count=31, VisibleAmmoRangesString='1-31',
    # and locale Name='...(31 rounds)', whereas Description and ShortName say 30.
    # Do not falsely flag the corrected player-facing 31-round translation.
    # Exception applies only when BOTH other item labels also state 31 in Korean.
    true_north_rows = next((rows for name, rows in profiles if name == 'C11 - True North.json'), [])
    tn_index = {r.get('key'): r for r in true_north_rows if isinstance(r, dict)}
    tn_id = '69b5698b5422bd822228ae5d'
    verified_capacity_exception = (
        '(31 rounds)' in tn_index.get(tn_id + ' Name', {}).get('source', '')
        and '31발' in tn_index.get(tn_id + ' Name', {}).get('translation', '')
        and tn_index.get(tn_id + ' ShortName', {}).get('source') == 'CSA 5.56 30rd'
        and tn_index.get(tn_id + ' ShortName', {}).get('translation') == 'CSA 5.56 31발'
    )
    seen = collections.defaultdict(list)
    for name, rows in profiles:
        for row in rows:
            key, source, trans = (row.get(p) for p in ('key', 'source', 'translation'))
            if not all(isinstance(s, str) for s in (key, source, trans)):
                continue
            seen[(key.casefold(), source)].append((name, key, trans))
            # Only report if each side has a single unambiguous expressed capacity.
            # For complex descriptions (multiple capacities), a human must check context.
            original = set(int(x) for x in ROUND_IN_ENGLISH.findall(source))
            translated = set(int(x) for x in (ROUND_IN_KOREAN.findall(trans) + ROUND_IN_ENGLISH.findall(trans)))
            if len(original) == len(translated) == 1 and original != translated:
                is_verified_true_north_typo = (
                    verified_capacity_exception
                    and name == 'C11 - True North.json'
                    and key in (tn_id + ' Description', tn_id + ' ShortName')
                    and original == {30} and translated == {31}
                    and (
                        (key.endswith(' Description') and source.startswith('This is the Czech Small Arms (CSA) 30 round '))
                        or (key.endswith(' ShortName') and source == 'CSA 5.56 30rd')
                    )
                )
                if not is_verified_true_north_typo:
                    errors.append(f'{name}: {key}: explicit capacity {sorted(original)} differs from translation {sorted(translated)}')
    for (key, source), candidates in seen.items():
        if len(set(t for _, _, t in candidates)) > 1:
            errors.append('same-key/same-source translation conflict: ' + key + ' / ' + repr(source[:70]) +
                          ' / files: ' + ', '.join(n for n, _, _ in candidates))
    return errors


def audit(root):
    profiles = []
    for path in sorted(pathlib.Path(root).glob('*.json')):
        try:
            doc = json.loads(path.read_text(encoding='utf-8-sig'))
            profiles.append((path.name, (doc.get('channels') or {}).get('locale') or []))
        except (OSError, ValueError) as exc:
            return [f'{path.name}: unreadable profile: {exc}']
    return check_rows(profiles)


if __name__ == '__main__':
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--translations', type=pathlib.Path,
                    default=pathlib.Path(__file__).resolve().parents[2] / 'translations')
    args = ap.parse_args()
    errors = audit(args.translations)
    if errors:
        for error in errors:
            print('FAIL', error)
        raise SystemExit(1)
    print('PASS: explicit capacity consistency and identical-source translation uniqueness (static check only)')
