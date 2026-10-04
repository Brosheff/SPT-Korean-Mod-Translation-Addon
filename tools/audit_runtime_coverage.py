"""Read-only coverage audit using real server locale snapshots, not synthetic row sources.

English text is a review candidate, not automatically a missing translation:
ammo/model names, short names, internal keys and another mod's Korean need review.
"""
import argparse
import collections
import json
from pathlib import Path
import re
from locale_display import load

def payload(path):
    document = load(path)
    return document['data'] if isinstance(document.get('data'), dict) else document

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--raw', type=Path, required=True, help='Server Korean locale BEFORE the client overlay')
    parser.add_argument('--english', type=Path, required=True)
    parser.add_argument('--parent-english', type=Path, required=True)
    parser.add_argument('--translations', type=Path, default=Path(__file__).resolve().parents[1] / 'translations')
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    raw, english, parent = payload(args.raw), payload(args.english), payload(args.parent_english)
    raw_ci = {k.casefold(): v for k, v in raw.items()}
    candidates = collections.defaultdict(list)
    for path in sorted(args.translations.glob('*.json')):
        doc = load(path)
        for row in doc.get('channels', {}).get('locale', []):
            if row.get('enabled', True): candidates[row['key'].casefold()].append({'file': path.name, **row})
    unmatched = []
    for key, rows in candidates.items():
        current = raw_ci.get(key)
        if current is None:
            reason = 'not_present_in_this_server_snapshot'
        elif any(current.strip() == r['source'].strip() for r in rows):
            continue
        elif re.search(r'[가-힣]', current):
            reason = 'other_korean_or_already_translated_preserve'
        elif re.fullmatch(r'[a-fA-F0-9]{24}', current):
            reason = 'opaque_locale_reference_not_an_english_sentence'
        else:
            reason = 'source_mismatch_review_decorations_and_mod_version'
        unmatched.append({'key': rows[0]['key'], 'reason': reason, 'current': current,
                          'candidates': [{'file': r['file'], 'source': r['source']} for r in rows]})
    uncovered = []
    for key, source in english.items():
        if key in parent or key.casefold() in candidates or not isinstance(source, str): continue
        if raw_ci.get(key.casefold()) != source or not re.search('[A-Za-z]', source): continue
        kind = 'shortname_review_models_and_acronyms' if key.endswith(' ShortName') else 'missing_key_review_display_context'
        uncovered.append({'key': key, 'source': source, 'reason': kind})
    report = {'summary': {'server_keys': len(raw), 'registered_keys': len(candidates),
              'unmatched': dict(collections.Counter(x['reason'] for x in unmatched)),
              'uncovered': dict(collections.Counter(x['reason'] for x in uncovered))},
              'unmatched': unmatched, 'uncovered': uncovered}
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report['summary'], ensure_ascii=False, indent=2))

if __name__ == '__main__':
    main()
