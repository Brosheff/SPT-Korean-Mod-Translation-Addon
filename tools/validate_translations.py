import json, pathlib, re, sys
from locale_display import load, validate_display
root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else pathlib.Path(__file__).resolve().parents[1] / 'translations')
counts = {}
profiles = 0
errors = []
for path in sorted(root.glob('*.json')):
    try:
        d = load(path)
    except Exception as e:
        errors.append(f'{path.name}: JSON: {e}')
        continue
    profiles += 1
    if d.get('schema_version') != 1 or d.get('target_spt') != '4.1.6' or not d.get('mod_id'):
        errors.append(f'{path.name}: invalid profile header')
    channels = d.get('channels') or {}
    errors.extend(f'{path.name}: {error}' for error in validate_display(d))
    loc = channels.get('locale') or []
    inherited = None
    seen_keys = set()
    for i, r in enumerate(loc):
        if 'match_mode' in r and r.get('match_mode') not in (None, ''):
            inherited = r['match_mode']
        if inherited not in ('exact', 'item_decorated'):
            errors.append(f'{path.name}: locale[{i}] has no valid inherited match_mode')
        if not r.get('key') or r['key'] in seen_keys:
            errors.append(f'{path.name}: locale[{i}] missing/duplicate key')
        seen_keys.add(r.get('key'))
        if 'source' not in r or 'translation' not in r:
            errors.append(f'{path.name}: locale[{i}] missing source/translation')
    source_fallbacks = channels.get('locale_source_fallback') or []
    seen_fallback_sources = set()
    for i, r in enumerate(source_fallbacks):
        source = r.get('source')
        if not source or source in seen_fallback_sources:
            errors.append(f'{path.name}: locale_source_fallback[{i}] missing/duplicate source')
        seen_fallback_sources.add(source)
        if not r.get('translation'):
            errors.append(f'{path.name}: locale_source_fallback[{i}] missing translation')
        if r.get('match_mode') not in (None, '', 'exact'):
            errors.append(f'{path.name}: locale_source_fallback[{i}] must use exact match_mode')
    clone_suffixes = channels.get('locale_clone_suffix') or []
    seen_clone_keys = set()
    for i, r in enumerate(clone_suffixes):
        key = r.get('key')
        parent = r.get('parent_key')
        if not key or key in seen_clone_keys:
            errors.append(f'{path.name}: locale_clone_suffix[{i}] missing/duplicate key')
        seen_clone_keys.add(key)
        if not isinstance(key, str) or not re.fullmatch(r'[0-9a-fA-F]{24}', key or ''):
            errors.append(f'{path.name}: locale_clone_suffix[{i}] key must be a 24-hex MongoId')
        if not isinstance(parent, str) or not re.fullmatch(r'[0-9a-fA-F]{24}', parent or ''):
            errors.append(f'{path.name}: locale_clone_suffix[{i}] parent_key must be a 24-hex MongoId')
        if not r.get('source') or not r.get('translation'):
            errors.append(f'{path.name}: locale_clone_suffix[{i}] requires source/translation suffixes')
        if r.get('match_mode') not in (None, '', 'exact'):
            errors.append(f'{path.name}: locale_clone_suffix[{i}] must use exact match_mode')
        desc_source = r.get('description_source')
        desc_translation = r.get('description_translation')
        if desc_source not in (None, '') and not desc_translation:
            errors.append(f'{path.name}: locale_clone_suffix[{i}] description_source requires description_translation')

    additions = channels.get('locale_additions') or []
    seen_addition_keys = set()
    for i, r in enumerate(additions):
        if not r.get('key') or r['key'] in seen_addition_keys:
            errors.append(f'{path.name}: locale_additions[{i}] missing/duplicate key')
        seen_addition_keys.add(r.get('key'))
        if not r.get('source') or not r.get('translation'):
            errors.append(f'{path.name}: locale_additions[{i}] missing source/translation')
        if r.get('match_mode') not in (None, '', 'exact'):
            errors.append(f'{path.name}: locale_additions[{i}] must use exact match_mode')
    for ch, rows in channels.items():
        counts[ch] = counts.get(ch, 0) + len(rows or [])
        for i, r in enumerate(rows or []):
            source = r.get('source')
            translation = r.get('translation')
            if isinstance(source, str) and isinstance(translation, str):
                # Normalize boundary whitespace before testing. A trailing newline in source used to
                # let bilingual contamination slip through the validator (WTT Old house toilet key).
                source_cmp = source.strip()
                translation_cmp = translation.strip()
                if source_cmp and (translation_cmp.endswith('\n(' + source_cmp + ')') or translation_cmp.endswith('\n\n' + source_cmp)):
                    errors.append(f'{path.name}: {ch}[{i}] embeds the full source in translation; keep the Korean body and use only an audited locale display_type')
                if source_cmp and translation_cmp.startswith(source_cmp) and '에 대한 설명입니다' in translation_cmp:
                    errors.append(f'{path.name}: {ch}[{i}] contains generated placeholder Korean instead of a real translation')
        if ch in ('notifications', 'dialogs', 'server_errors'):
            for i, r in enumerate(rows or []):
                if r.get('match_mode') not in ('exact','template','segment','segment_template','regex_segment'):
                    errors.append(f'{path.name}: {ch}[{i}] requires explicit valid match_mode')
        if ch == 'npc_messages':
            for i, r in enumerate(rows or []):
                trader_id = r.get('trader_id')
                if not trader_id:
                    errors.append(f'{path.name}: npc_messages[{i}] requires trader_id for direct-message server scope')
                elif trader_id == '@modded_insurance_providers':
                    if d.get('mod_id') != 'RealisticInsurance':
                        errors.append(f'{path.name}: npc_messages[{i}] reserved modded-insurance scope is RealisticInsurance-only')
                elif not re.fullmatch(r'[0-9a-fA-F]{24}', trader_id):
                    errors.append(f'{path.name}: npc_messages[{i}] trader_id must be exact 24-hex MongoId or an audited reserved scope')
        if ch == 'server_messages':
            seen_server_sources = set()
            for i, r in enumerate(rows or []):
                if r.get('match_mode') not in ('exact', 'segment'):
                    errors.append(f'{path.name}: server_messages[{i}] must use exact or segment match_mode')
                if not r.get('id') or not r.get('source') or not r.get('translation'):
                    errors.append(f'{path.name}: server_messages[{i}] requires id/source/translation')
                if r.get('source') in seen_server_sources:
                    errors.append(f'{path.name}: server_messages[{i}] duplicate source')
                seen_server_sources.add(r.get('source'))
                if r.get('trader_id'):
                    errors.append(f'{path.name}: server_messages[{i}] must not define trader_id')
if errors:
    print('\n'.join(errors))
    raise SystemExit(1)
print(f'OK: {profiles} profiles')
for k in sorted(counts): print(f'{k}: {counts[k]}')
