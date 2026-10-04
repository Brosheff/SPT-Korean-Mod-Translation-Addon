"""Compare all parent locale values, including annotations shared by BOTH cultures.

Usage: python tools/audit_parent_patterns.py PARENT_LOCALE_DIR OUTPUT_JSON [SPT_DATABASE]
The database is used only to identify display surfaces, never as translation text.
"""
import collections, hashlib, json, pathlib, re, sys

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def main():
    folder, output = map(pathlib.Path, sys.argv[1:3])
    data = {name: read(folder / (name + '.json')) for name in ('en', 'kr', 'kr-en')}
    en, kr, bi = [data[n] for n in ('en', 'kr', 'kr-en')]
    assert en.keys() == kr.keys() == bi.keys(), 'Parent key sets differ'
    items, quests, achievements, clothing = {}, {}, set(), {}
    if len(sys.argv) > 3:
        db = pathlib.Path(sys.argv[3]) / 'templates'
        items, quests, clothing = [read(db / (n + '.json')) for n in ('items', 'quests', 'customization')]
        achievements = {r['id'] for r in read(db / 'achievements.json')}
    quest_ids = set(quests) | {k.rsplit(' ', 1)[0] for k in en if k.endswith(' startedMessageText')}
    trader_ids = {k.rsplit(' ', 1)[0] for k in en if k.endswith((' FirstName',' Nickname',' FullName'))}
    def category(key):
        base, _, field = key.rpartition(' ')
        if base in trader_ids: return 'trader/' + field
        if base in quest_ids: return 'quest/' + field
        if base in achievements: return 'achievement/' + field
        if base in items: return ('item/' if items[base].get('_type') == 'Item' else 'item_category/') + field
        if base in clothing: return 'customization/' + field
        if key.startswith('Subtitles/'): return 'subtitles'
        if key.startswith('ShellingWarningMessage/'): return 'shelling_location_annotation'
        if key.startswith('Trading/Dialog/PlayerTaxi/') and key.endswith('/Name'): return 'taxi_destination'
        if key.endswith('_DESC') and '_TRANSIT_' in key: return 'transit'
        if key.lower().endswith('description'): return 'named_description/skill_or_other'
        if key.startswith('colorgrading/'): return 'color_filter'
        return 'unclassified/' + (field if base else 'bare_key')
    def pattern(source, ko, value, key):
        if value == ko: return 'same'
        for sep, label in [('\n','source_newline'),(' ','source_inline'),('','source_inline_no_space'),(' / ','source_slash')]:
            if value.strip() == (ko.rstrip() + sep + '(' + source.strip() + ')').strip(): return label
        if value.strip() == (ko.rstrip() + '\n' + source.strip()).strip(): return 'source_newline_no_brackets'
        if ko and value.endswith(ko) and value.startswith('['): return 'square_header'
        if key.startswith('Subtitles/'):
            try:
                if json.loads(ko) == json.loads(value): return 'json_spacing_only'
            except (ValueError, TypeError): pass
        return 'specific_or_editorial_difference'
    totals, differences, shared_annotations = collections.defaultdict(collections.Counter), [], []
    for key, source in en.items():
        ko, value = kr[key], bi[key]
        cat, style = category(key), pattern(source, ko, value, key)
        totals[cat]['total'] += 1
        totals[cat][style] += 1
        if style != 'same': differences.append(dict(key=key,category=cat,pattern=style,en=source,kr=ko,bilingual=value))
        if not source: continue
        shared = None
        if ko == value and ko.rstrip().endswith(' ('+source.strip()+')'): shared = 'source_inline_both'
        if ko == value and ko.startswith('[') and '\n' in ko:
            header = ko.split('\n', 1)[0]
            name = en.get(key.rsplit(' ', 1)[0] + ' Name')
            shared = 'item_name_header_both' if name and header == '['+name+']' else 'specific_header_both'
        if shared:
            totals[cat][shared] += 1
            shared_annotations.append(dict(key=key,category=cat,pattern=shared,en=source,kr=ko))
    report = dict(parent=str(folder), sha256={n:hashlib.sha256((folder/(n+'.json')).read_bytes()).hexdigest() for n in data},
                  key_count=len(en), different_count=len(differences), totals=dict(totals),
                  difference_patterns=dict(collections.Counter(r['pattern'] for r in differences)),
                  differences=differences, shared_annotations=shared_annotations)
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:report[k] for k in ('key_count','different_count','difference_patterns')},ensure_ascii=False,indent=2))
    print('Full key-level audit:', output)

if __name__ == '__main__': main()
