#!/usr/bin/env python3
"""Build preflight for the key-scoped alternative selection + mandatory item formatter.

Static source contract only: C# compilation and the real SPT locale merge remain
separate mandatory tests. Does not depend on docs/ or user-specific snapshots.
"""
from pathlib import Path
import json


def check(root):
    errors = []
    addon = (root / 'src/ClientLocaleModOverlay.cs').read_text(encoding='utf-8-sig')
    model = (root / 'src/TextRules.cs').read_text(encoding='utf-8-sig')
    formatter = (root / 'src/LocaleDisplayRules.cs').read_text(encoding='utf-8-sig')
    profile = json.loads((root / 'translations/Make Magnifier Great Again.json').read_text(encoding='utf-8-sig'))
    item = '6a668154ed607881737e22c4'
    rows = {r['key']: r for r in profile['channels']['locale']}
    if not ('source_alternatives' in model and 'LocaleSourceAlternative' in model):
        errors.append('missing C# typed source alternative schema')
    required = (
        'r.source_alternatives != null',
        'r.display_type != LocaleDisplayRules.ItemName &&',
        'r.display_type != LocaleDisplayRules.ItemDescription',
        'displayType != r.display_type',
        'alternative.translation.TrimEnd().EndsWith',
        'alternative_item_description_requires_verified_live_name',
        'string.Equals(nameSource, actualItemName, StringComparison.Ordinal)',
        'candidate["source_alternatives"] is JArray alternatives',
        'e["source_alternatives"] is JArray alternatives',
        'alternativeTranslation = LocaleDisplayRules.Select(',
        'value = Translate(key, candidateIncoming, alternativeSource,',
        'VerifiedSource(n, runtimeName)',
    )
    for text in required:
        if text not in addon:
            errors.append('missing key-scoped selection/formatting guard: '+text)
    if addon.index('alternativeTranslation = LocaleDisplayRules.Select(') > addon.index('value = Translate(key, candidateIncoming, alternativeSource,'):
        errors.append('item formatter must be called BEFORE translating alternate source')
    if 'if (!LocaleMode.IsKoreanCulture(culture) || string.IsNullOrWhiteSpace(korean)) return source;' not in formatter:
        errors.append('English culture output must remain untouched')
    if 'if (displayType == ItemDescription && !string.IsNullOrWhiteSpace(itemNameSource))' not in formatter or '"\\n(" + modName.Trim() + ")"' not in formatter:
        errors.append('item header or mod credit contract missing')
    for field in ('Name', 'Description'):
        r = rows.get(item+' '+field)
        if not isinstance(r, dict) or r.get('display_type') != 'item_'+('name' if field == 'Name' else 'description'):
            errors.append('missing explicit item display type for '+field)
        elif len(r.get('source_alternatives', [])) != 1:
            errors.append('missing exact independent translation for '+field)
    # The source-only fallback must never silently become the solution for
    # this verified item. Its older global path still exists for other data.
    screen_name = 'EOTech EXPS3 (Multicam Black) on LaRue riser w/ Trijicon SkeetIR'
    screen_description = 'An EOTech EXPS3 holographic sight finished in Multicam Black Cerakote, mounted on a LaRue Tactical Picatinny riser to bring the reticle up to a comfortable heads-up position. A Trijicon SkeetIR thermal clip-on sits in front on a flip-to-side mount, swinging into line behind the holo when you need to see heat and folding out of the way when you don\'t.'
    for field, observed in [('Name', screen_name), ('Description', screen_description)]:
        row = rows.get(item+' '+field, {})
        alternatives = row.get('source_alternatives') or []
        if not any(a.get('source') == observed for a in alternatives):
            errors.append('SIC-verified source changed for '+field)
        if observed == row.get('source'):
            errors.append('original MMGA source was overwritten')
    fallback = profile['channels'].get('locale_source_fallback', [])
    if any(x.get('source') in (screen_name, screen_description) for x in fallback):
        errors.append('keyless/global fallback may not handle verified item source')
    diagnostics = (
        '#define LOCALE_DIAGNOSTICS', 'source_fallback_item_display_review',
        'unregistered_server_item_candidates', '"source_changed_or_other_translation"',
        'File.WriteAllText(auditPath, reportJson)',
    )
    for item_required in diagnostics:
        if item_required not in addon:
            errors.append('runtime locale audit path missing: '+item_required)
    return errors


if __name__ == '__main__':
    errors = check(Path(__file__).resolve().parents[1])
    for e in errors:print('FAIL: '+e)
    if errors:raise SystemExit(1)
    print('OK: key-scoped source alternatives, required item formatter and runtime audit wiring (static only)')
