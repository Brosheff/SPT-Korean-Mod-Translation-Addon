#!/usr/bin/env python3
"""Read-only latest-version 1.0.4 source and data audit.

Former Aishi 2.0.3 / Special Forces 2.0.0 translations are intentionally retired.
The old 1.0.3 reference is compared as a historical change report ONLY, never
as a backward-compatibility requirement. This does NOT test the game runtime.
"""
import argparse
import collections
import csv
import json
from pathlib import Path
import re

def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))

def list_profiles(folder):
    result=[]
    for file in sorted(folder.glob('*.json')):
        doc=read(file)
        for channel, rows in doc.get('channels',{}).items():
            last_mode=None
            for row in rows:
                if not isinstance(row, dict): continue
                last_mode=row.get('match_mode') or last_mode
                if row.get('enabled', True) and isinstance(row.get('source'),str) and row.get('translation'):
                    result.append({'file':file.name,'channel':channel,'key':row.get('key'),'source':row['source'],
                         'translation':row['translation'],'mode':last_mode,'variants':row.get('source_variants') or [],
                         'mod_id':doc.get('mod_id')})
    return result

def make_csv(path, rows, keys):
    with path.open('w',encoding='utf-8-sig',newline='') as dest:
        w=csv.DictWriter(dest,fieldnames=keys,extrasaction='ignore');w.writeheader();w.writerows(rows)

def main():
    cli=argparse.ArgumentParser()
    cli.add_argument('--baseline',type=Path,required=True,help='extracted 1.0.3 root')
    cli.add_argument('--current',type=Path,default=Path(__file__).resolve().parents[1])
    cli.add_argument('--aishi',type=Path,required=True,help='extracted Aishi 2.0.4 source root')
    cli.add_argument('--special',type=Path,required=True,help='extracted Special Forces 2.0.1 source root')
    cli.add_argument('--report',type=Path,required=True)
    a=cli.parse_args();a.report.mkdir(parents=True,exist_ok=True)
    old=list_profiles(a.baseline/'translations');now=list_profiles(a.current/'translations')
    bypair=collections.defaultdict(list)
    bykey=collections.defaultdict(list)
    for r in now:
        if r['key'] is not None:
            bypair[(r['channel'],r['key'].casefold(),r['source'])].append(r)
            bykey[(r['channel'],r['key'].casefold())].append(r)
    baseline=[]
    latest_only_mods={'com.samc137.aishi','com.samc137.isbspecialforces'}
    for row in old:
        key=row.get('key')
        if key is None: continue
        matches=bypair.get((row['channel'],key.casefold(),row['source']),[])
        status = 'retained' if matches else ('old_version_not_supported' if row['mod_id'] in latest_only_mods else 'missing')
        baseline.append({'file':row['file'],'channel':row['channel'],'key':key,'source':row['source'],
            'baseline_ko':row['translation'],'status':status,
            'ko_changed':bool(matches) and not any(c['translation']==row['translation'] for c in matches),
            'candidate_files':';'.join(c['file'] for c in matches)})
    make_csv(a.report/'baseline_comparison.csv',baseline, ['file','channel','key','source','baseline_ko','status','ko_changed','candidate_files'])
    conflicts=[]
    for (ch,key,source),candidates in bypair.items():
        if len({r['translation'] for r in candidates})>1:
            conflicts.append({'channel':ch,'key':key,'source':source,'files':[r['file'] for r in candidates]})
    (a.report/'same_source_conflicts.json').write_text(json.dumps(conflicts,ensure_ascii=False,indent=2),encoding='utf-8')

    trader_id='690766de550bc322a810ea1e'
    base=read(a.aishi/'db/base.json')
    source=(a.aishi/'aishi_trader.cs').read_text(encoding='utf-8-sig')
    call=re.search(r'AddTraderToLocales\(traderBase,\s*"([^"]+)",\s*"([^"]+)"\)',source)
    assert call, 'Direct Aishi trader introduction constructor not found; cannot complete audit'
    intro_name,intro=call.groups()
    raw={trader_id+' FullName':base['name'],trader_id+' FirstName':intro_name,
         trader_id+' Nickname':base['nickname'],trader_id+' Location':base['location'],trader_id+' Description':intro}
    en=read(a.aishi/'db/CustomLocales/en.json')
    missing_kr=[key for key,value in raw.items() if not any(value==r['source'] or value in r['variants'] for r in bykey.get(('locale',key.casefold()),[]))]
    missing_default=[key for key,value in raw.items() if not any(r['file']=='ISB Aishi.json' and (value==r['source'] or value in r['variants']) for r in bykey.get(('locale',key.casefold()),[]))]
    trader=[]
    for key,value in raw.items():
        row={'key':key,'code_injected_kr_source':value,'aishi_en_json_source':en.get(key),
             'primary_match':key not in missing_default,
             'all_candidates_match':key not in missing_kr}
        trader.append(row)
    make_csv(a.report/'aishi_trader_runtime_path.csv',trader, ['key','code_injected_kr_source','aishi_en_json_source','primary_match','all_candidates_match'])
    # Provided Aishi Korean language has no override file; default data only fills missing keys.
    kr_override=(a.aishi/'db/CustomLocales/kr.json').exists()
    if kr_override:
        raise AssertionError('Aishi kr.json appeared; revisit server data precedence')
    def locales(folder,tag):
        rows=[]
        for p in folder.rglob('en.json'):
            if {'bin','obj','.git'} & set(p.parts):continue
            d=read(p)
            if not isinstance(d,dict):continue
            for key,source in d.items():
                if not isinstance(source,str) or not source.strip():continue
                candidates=bykey.get(('locale',key.casefold()),[])
                status='exact_source_candidate' if any(source==r['source'] or source in r['variants'] for r in candidates) else 'key_registered_source_mismatch' if candidates else 'not_registered'
                rows.append({'key':key,'english':source,'source_file':str(p.relative_to(folder)),'status':status})
        make_csv(a.report/(tag+'_locale_candidates.csv'),rows,['key','english','source_file','status'])
        return dict(collections.Counter(r['status'] for r in rows))
    aishi_loc=locales(a.aishi,'aishi');sf_loc=locales(a.special/'Server','special')
    # Item fields not in Aishi/en.json can be provided by individual item definitions.
    item_rows=[]
    for p in (a.aishi/'db/CustomItems').rglob('*.json'):
        d=read(p)
        for item_id,obj in d.items():
            if not isinstance(obj,dict):continue
            fields=(obj.get('locales') or {}).get('en') or {}
            for source_field,locale_field in [('name','Name'),('shortName','ShortName'),('description','Description')]:
                source=fields.get(source_field)
                if not isinstance(source,str) or not source.strip():continue
                key=item_id+' '+locale_field
                candidates=bykey.get(('locale',key.casefold()),[])
                status='exact_source_candidate' if any(source==r['source'] or source in r['variants'] for r in candidates) else 'key_registered_source_mismatch' if candidates else 'not_registered'
                item_rows.append({'key':key,'english':source,'source_file':str(p.relative_to(a.aishi)),'status':status})
    make_csv(a.report/'aishi_custom_items.csv',item_rows,['key','english','source_file','status'])
    # Subtitle source uses entries[], not a flat key-to-value English locale.
    sf_sub=read(a.special/'ISBSOFExtras/ISBSpecialForces_Audios/Subtitles/locales/en.json')
    small=[r for r in now if r['channel']=='small_ui' and r['mod_id']=='com.samc137.isbspecialforces']
    # Also include other SF-related profiles. Only literal/exact matches are proven here;
    # segment/regex matching is intentionally not counted as proven without the hook test.
    small=[r for r in now if r['channel']=='small_ui' and ('isb' in r['mod_id'].lower() or 'special' in r['file'].lower())]
    sub=[]
    for e in sf_sub['entries']:
        s=e.get('text','')
        is_match=any(r['source']==s for r in small)
        sub.append({'key':e.get('key'),'english':s,'static_literal_match':is_match,
                    'status':'registered_literal' if is_match else 'needs_hook_or_translation_review'})
    make_csv(a.report/'special_subtitle_candidates.csv',sub,['key','english','static_literal_match','status'])
    summary={'baseline_active_key_source_rows':len(baseline),
      'baseline_missing_rows':sum(x['status']=='missing' for x in baseline),
      'old_version_intentionally_unmatched_rows':sum(x['status']=='old_version_not_supported' for x in baseline),
      'baseline_changed_Korean_rows':sum(x['ko_changed'] for x in baseline),'same_key_source_conflicts':len(conflicts),
      'aishi_trader_injected_keys':len(raw),'aishi_trader_default_profile_unmatched':missing_default,
      'aishi_trader_all_profiles_unmatched':missing_kr,'aishi_kr_override_exists':kr_override,
      'aishi_flat_english_source':aishi_loc,'aishi_custom_item_fields':dict(collections.Counter(x['status'] for x in item_rows)),
      'special_flat_english_source':sf_loc,'special_subtitle_source':dict(collections.Counter(x['status'] for x in sub)),
      'limits':['static extracted source data; no installed server Korean locale snapshot','no C# build/no game runtime proof',
        'other 163 supported entities lack authoritative current mod sources in supplied attachments',
        'historical Aishi 2.0.3 / Special Forces 2.0.0 English source candidates intentionally removed']}
    (a.report/'summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(summary,ensure_ascii=False,indent=2))
    if summary['baseline_missing_rows'] or conflicts or missing_default or missing_kr:
        raise SystemExit('FAIL: one or more baseline/conflict/Aishi trader source gates failed')

if __name__=='__main__':main()
