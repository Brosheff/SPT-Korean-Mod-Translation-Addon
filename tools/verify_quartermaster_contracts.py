#!/usr/bin/env python3
"""Static coverage and distribution wiring checks for version 25531.

Does not claim successful compilation or in-game Harmony activation.
"""
import hashlib
import json
import re
from pathlib import Path

root=Path(__file__).resolve().parent.parent
p=root/'contract-locales/TheQuartermaster.v25531.json'
catalog=json.loads(p.read_text(encoding='utf-8'))
assert catalog['schema_version']==1
assert catalog['source_snapshot_version']==25531
assert len(catalog['contracts'])==9
assert {x['origin'] for x in catalog['contracts']}=={'definitions','submissions'}
assert len({x['contract_id'] for x in catalog['contracts']})==9
assert len({x['fields']['title']['source'] for x in catalog['contracts']})==9
objective_count=0
field_count=0
known=0
for c in catalog['contracts']:
    assert 'title' in c['fields'] and 'description' in c['fields']
    for name,phrase in c['fields'].items():
        assert phrase['source'] and phrase['translation'], (c['contract_id'],name)
        field_count+=1
    indices=set()
    for obj in c['objectives']:
        assert obj['index'] not in indices
        indices.add(obj['index'])
        assert obj['source'] and obj['translation']
        objective_count+=1
    for qid in c['known_quest_ids']:
        assert re.fullmatch(r'[a-f0-9]{24}',qid)
        known+=1
        # Match ContractQuestBuilder's MD5(questId:objINDEX:cond)[:24]
        for obj in c['objectives']:
            key=hashlib.md5(f'{qid}:obj{obj["index"]}:cond'.encode()).hexdigest()[:24]
            assert re.fullmatch(r'[a-f0-9]{24}',key)
assert field_count==38 and objective_count==22 and known==5, (field_count,objective_count,known)
for tier in catalog['reward_tiers']:
    for name in ('name','description'):
        assert tier[name]['source'] and tier[name]['translation']
assert catalog['weekly_reward_message']['source'] and catalog['weekly_reward_message']['translation']
source=(root/'src/QuartermasterContractLocales.cs').read_text(encoding='utf-8')
runtime=(root/'src/QuartermasterRuntimeHooks.cs').read_text(encoding='utf-8')
plugin=(root/'src/Plugin.cs').read_text(encoding='utf-8-sig')
overlay=(root/'src/ClientLocaleModOverlay.cs').read_text(encoding='utf-8-sig')
build=(root/'BUILD_DISTRIBUTION.ps1').read_text(encoding='utf-8-sig')
assert 'MD5.Create()' in source and 'QM_EXPIRY' in source and 'TranslateCountdown' in source
assert 'QuartermasterContractLocales(root)' in overlay
assert 'quartermasterContracts.Apply(locale, raw, result)' in overlay
assert 'QuartermasterRuntimeHooks.Enable' in plugin and 'QuartermasterRuntimeHooks.Disable' in plugin
for name in ('LiveCountdownBehaviour','NotesTaskDescription','PopulateSubmissionList','ShowSubmissionDetails','CreateSubmissionRow'):
    assert name in runtime
assert "'contract-locales'" in build
print(f'OK: contract snapshot {catalog["source_snapshot_version"]}; {len(catalog["contracts"])} contracts, '
      f'{field_count} fields, {objective_count} objectives, {known} scheduled quest IDs, 9 reward texts')
print('OK: source locale bridge, live countdown, scoped CommunityPanel render hook, and distribution wiring')
