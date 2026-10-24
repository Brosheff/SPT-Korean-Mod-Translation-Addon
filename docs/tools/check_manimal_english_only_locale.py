"""Offline regression for opted-in English-only server locale keys.

This models the localized-key fallback decision; it is NOT a compiled client or
in-game localization test. Run from any cwd:
    python docs/tools/check_manimal_english_only_locale.py
"""
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[2]
PROFILE = ROOT / 'translations' / 'Manimal Terminal.json'
BRIDGE = ROOT / 'src' / 'ClientLocaleModOverlay.cs'
MODEL = ROOT / 'src' / 'TextRules.cs'


def selected(row, culture):
    if culture not in ('kr', 'kr-en'):
        return row['source']
    source, ko = row['source'], row['translation']
    t = row.get('display_type', 'default')
    if t == 'quest_title' or t == 'achievement_title':
        return ko.rstrip() + (' (' + source.strip() + ')' if source.strip() != ko.strip() else '')
    if t in ('quest_objective', 'achievement_condition'):
        return ko.rstrip() + ('\n(' + source.strip() + ')' if source.strip() != ko.strip() else '')
    if t in ('quest_description', 'achievement_description'):
        credit = '\n(Manimal Terminal)'
        return ko.rstrip() + ('' if ko.rstrip().endswith(credit) else credit)
    return ko


def simulate(row, raw, merged, culture, seed_enabled):
    key, source = row['key'], row['source']
    if culture not in ('kr', 'kr-en'):
        return merged.get(key, raw.get(key))
    if key in raw:
        incoming = raw[key]
    elif seed_enabled:
        incoming = merged.get(key, source)
        if incoming == key:
            incoming = source
    else:
        return merged.get(key)
    # C# Translate() checks the source exactly (or surrounding whitespace).
    if incoming != source:
        return merged.get(key)
    return selected(row, culture)


def run():
    doc = json.loads(PROFILE.read_text(encoding='utf-8'))
    assert doc['mod_id'] == 'com.manimal.terminal'
    rows = doc['channels']['locale']
    assert len(rows) == 171 and len({r['key'] for r in rows}) == len(rows)
    seeded = [r for r in rows if r.get('seed_missing')]
    assert len(seeded) == 103
    bridge = BRIDGE.read_text(encoding='utf-8')
    model = MODEL.read_text(encoding='utf-8')
    for guard in ('presentInRaw = raw.TryGetValue', 'r.seed_missing',
                  'result.TryGetValue(key, out incoming)',
                  'var candidateIncoming = presentInRaw ? incoming',
                  'Translate(key, candidateIncoming',
                  'if (!presentInRaw && !(bool)e["seed_missing"]) continue;'):
        assert guard in bridge, f'Missing C# fallback guard: {guard}'
    assert 'public bool seed_missing { get; set; }' in model
    for culture in ('kr', 'kr-en'):
        for row in seeded:
            key, source = row['key'], row['source']
            expected = selected(row, culture)
            assert simulate(row, {}, {}, culture, True) == expected, key + ' absent'
            assert simulate(row, {}, {key: source}, culture, True) == expected, key + ' merged English'
            assert simulate(row, {key: source}, {key: source}, culture, False) == expected, key + ' existing raw'
            assert simulate(row, {}, {key: '타 모드의 기존 번역'}, culture, True) == '타 모드의 기존 번역', key + ' preserve translated'
            assert simulate(row, {key: 'Changed English source'}, {key: 'Changed English source'}, culture, True) == 'Changed English source', key + ' preserve changed source'
            assert simulate(row, {}, {}, culture, False) is None, key + ' opt-in isolation'
            assert simulate(row, {key: source}, {key: source}, 'en', True) == source, key + ' English unchanged'
    first = next(r for r in rows if r['key'] == '6a8f4d5e90206485fa1d2d5a name')
    description = next(r for r in rows if r['key'] == '6a8f4d5e90206485fa1d2d5a description')
    assert selected(first, 'kr') == '추락한 하늘 - 1부 (Falling Skies - Part 1)'
    assert selected(description, 'kr').endswith('\n(Manimal Terminal)')
    assert all(not r.get('seed_missing') for r in rows if r not in seeded)
    print(f'OK: {len(seeded)} audited English-only keys (out of {len(rows)}) × 2 Korean modes × 7 cases; guard present (modeled, not runtime)')


if __name__ == '__main__':
    run()
