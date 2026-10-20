#!/usr/bin/env python3
"""Read-only evidence audit of the 1.0.3 handoff versus profiles and supplied mod archives.

Does not certify runtime locale equality or in-game UI application; use the separate
runtime coverage auditor with actual game locale snapshots for that stage.
"""
import argparse
import collections
import json
import re
import zipfile
from pathlib import Path

PREFIX_FILE = {
    'rsf': 'Russian Special Forces Pack.json',
    'tn': 'C11 - True North.json',
    'epicsaio502': "Epic's All in One.json",
    'sf': 'ISB Special Forces.json',
    'aishi': 'ISB Aishi.json',
    'rook': 'Rook.json',
    'uf': 'Under Fire.json',
}
ORIGINALS = {
    'rook': 'YDLonewolf-Rook-v.1.2.1.zip',
    'rsf': 'Russian-SF-Pack-2.0.1.zip',
    'tn': 'True-North-4.0.29.zip',
    'sf': 'ISB-Special-Forces-2.0.0.zip',
    'aishi': 'ISB-Aishi-2.0.3.zip',
    'epicsaio502': 'EpicRangeTime-Weapons-5.0.2.zip',
}


def original_data(zip_path, group):
    out = collections.defaultdict(set)
    with zipfile.ZipFile(zip_path) as z:
        for name in z.namelist():
            if not name.endswith('.json') or '/bin/' in name.lower() or '/debug/' in name.lower():
                continue
            if group == 'rsf':
                if '/Resources/db/CustomItems/' not in name: continue
                try: obj = json.loads(z.read(name))
                except (ValueError, UnicodeDecodeError): continue
                if not isinstance(obj,dict): continue
                for key, item in obj.items():
                    if not isinstance(item,dict): continue
                    loc = item.get('locales',{}).get('en',{})
                    if isinstance(loc,dict):
                        for f, suffix in [('name','Name'),('description','Description'),('shortName','ShortName')]:
                            if isinstance(loc.get(f),str):out[key+' '+suffix].add(loc[f])
            else:
                if not re.search(r'/en\.json$',name,re.I):continue
                if group == 'rook' and '/CustomLocales/' not in name: continue
                if group == 'tn' and '/Resources/db/' not in name:continue
                if group == 'epicsaio502' and '/Resources/db/' not in name:continue
                if group in ('sf','aishi') and not ('/db/' in name.lower() or '/subtitles/' in name.lower()):continue
                try: obj = json.loads(z.read(name))
                except (ValueError, UnicodeDecodeError): continue
                if not isinstance(obj,dict):continue
                for key,v in obj.items():
                    if isinstance(v,str):
                        # Locale collection metadata, not a user-visible text key.
                        if key.lower() in ('locale','language') and v.lower() in ('en','en-us'): continue
                        out[key].add(v)
    return out


def audit(root, handoff_path, archive_root):
    with zipfile.ZipFile(handoff_path) as z:
        targets = json.loads(z.read('handoff.json'))['targets']
    profile_path = root/'translations'
    profiles={p.name:json.loads(p.read_text(encoding='utf-8-sig')) for p in profile_path.glob('*.json')}
    indexed={}
    mode_issues=[]
    all_entries=collections.defaultdict(list)
    for filename,p in profiles.items():
        previous=None
        for idx,r in enumerate(p.get('channels',{}).get('locale',[])):
            if r.get('match_mode') is not None:previous=r['match_mode']
            if previous not in ('exact','item_decorated'):
                mode_issues.append({'file':filename,'row':idx,'key':r.get('key'),'mode':previous})
            indexed[(filename,'locale',r['key'])] = (r,previous)
            if r.get('enabled',True): all_entries[r['key'].casefold()].append({'file':filename,'key':r['key'],'source':r['source'],'translation':r['translation'],'mode':previous,'variants':r.get('source_variants',[])})
        for channel in ('locale_additions', 'locale_source_fallback', 'locale_clone_suffix'):
            for r in p.get('channels', {}).get(channel, []):
                if r.get('key'): indexed[(filename,channel,r['key'])] = (r,'exact')
    mods={}
    for group, name in ORIGINALS.items():
        p=archive_root/name
        if p.is_file(): mods[group]=original_data(p,group)

    errors=[]; warnings=[]; per_group=collections.defaultdict(collections.Counter)
    unverified=[]; english_residual=[]
    for candidate in targets:
        prefix=candidate['id'].split(':',1)[0]
        status=candidate.get('status'); kind=candidate.get('kind'); per_group[prefix][status]+=1
        if kind!='locale' or status not in ('translated','source_kept'):continue
        ev=candidate.get('evidence') or {}
        path=ev.get('file') or ('translations/'+PREFIX_FILE[prefix] if prefix in PREFIX_FILE else None)
        key=ev.get('key') or candidate['id'].split(':locale:',1)[-1]
        if path is None:
            errors.append({'type':'unresolved_profile','id':candidate['id']});continue
        channel = ev.get('channel','locale')
        result=indexed.get((Path(path).name,channel,key))
        if not result:
            errors.append({'type':'missing_locale_row','id':candidate['id'],'file':path,'key':key});continue
        row,mode=result
        source=row.get('source'); translation=row.get('translation')
        if 'source' in ev and source!=ev['source']:
            # Changed English sources in True North are expected to differ from pre-update handoff only when documented.
            warnings.append({'type':'handoff_evidence_source_differs','id':candidate['id'],'handoff':ev['source'],'current':source})
        if not translation:
            errors.append({'type':'empty_translation','id':candidate['id'],'file':path,'key':key})
        if status=='translated' and source==translation:
            warnings.append({'type':'translated_status_but_source_kept','id':candidate['id'],'file':path,'key':key})
        if status=='source_kept' and source!=translation:
            warnings.append({'type':'source_kept_status_but_translation_differs','id':candidate['id'],'file':path,'key':key,'source':source,'translation':translation})
        if status=='translated' and isinstance(translation,str) and re.search(r'\b(?:special|scope|assault|sniper|magazine|handguard|trigger|stock|barrel|muzzle brake|shotgun)\b', translation, re.I):
            english_residual.append({'id':candidate['id'],'file':path,'key':key,'translation':translation[:180]})
        if prefix in mods and channel=='locale':
            originals=mods[prefix].get(key)
            if not originals:
                unverified.append({'type':'not_found_in_original_english','id':candidate['id'],'key':key,'file':path})
            elif source not in originals:
                errors.append({'type':'original_source_mismatch','id':candidate['id'],'key':key,'file':path,'source':source,'originals':sorted(originals)[:3]})
    active_original_coverage={}
    for group,src in mods.items():
        fn=PREFIX_FILE[group]
        rows=profiles.get(fn,{}).get('channels',{}).get('locale',[])
        index={r['key']:r for r in rows if r.get('enabled',True)}
        matched=[k for k,vals in src.items() if k in index and index[k]['source'] in vals]
        missing=[k for k,v in src.items() if k not in index and any(x.strip() for x in v)]
        mismatch=[{'key':k,'profile_source':index[k]['source'],'originals':sorted(vals)} for k,vals in src.items() if k in index and index[k]['source'] not in vals]
        active_original_coverage[group]={'original_nonempty_keys':sum(any(x.strip() for x in v) for v in src.values()),'source_matched_keys':len(matched),'missing_profile_keys':len(missing),'profile_source_mismatch':len(mismatch),'missing_sample':missing[:15],'mismatch_sample':mismatch[:10]}
    collisions=[]
    for key,rows in all_entries.items():
        if len(rows)>1 and len({(x['source'],x['translation']) for x in rows})>1:
            collisions.append({'key_casefold':key,'candidates':rows})
    result={
        'scope':'1.0.3 cumulative handoff batches 1-11, base source batch 15',
        'evidence_note':'Original source ZIPs and translation profiles are NOT actual live server locale snapshots. No claim of in-game coverage.',
        'handoff_count':len(targets),'status_summary':dict(collections.Counter(t.get('status') for t in targets)),
        'per_mod':{k:dict(v) for k,v in per_group.items()},
        'profile_count':len(profiles),'active_locale_keys':len(all_entries),
        'audited_original_packages':sorted(mods),
        'original_coverage':active_original_coverage,
        'errors':errors,'warnings':warnings,'missing_original_evidence':unverified,
        'english_residual_review':english_residual,
        'invalid_mode_inheritance':mode_issues,
        'conflicting_key_groups':collisions,
        'runtime_snapshot_provided':False,
        'csharp_built_in_this_audit':False,
        'in_game_verified_in_this_audit':False,
    }
    return result


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1]);p.add_argument('--handoff',type=Path,required=True)
    p.add_argument('--archives',type=Path,required=True);p.add_argument('--report',type=Path,required=True)
    args=p.parse_args(); result=audit(args.root,args.handoff,args.archives)
    args.report.parent.mkdir(parents=True,exist_ok=True)
    args.report.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:(len(v) if isinstance(v,list) else v) for k,v in result.items() if k in ['handoff_count','status_summary','active_locale_keys','original_coverage','errors','warnings','english_residual_review','invalid_mode_inheritance','conflicting_key_groups','missing_original_evidence']},ensure_ascii=False,indent=2))

if __name__=='__main__':main()
