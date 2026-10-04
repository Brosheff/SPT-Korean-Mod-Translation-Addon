"""Remove obsolete manual bilingual overrides and apply source-audited achievement types.
Dry-run unless --write. Source evidence is supplied as JSON: file/key/source/display_type.
Does not invent translations or remove arbitrary parentheses.
"""
import argparse, collections, copy, hashlib, json, pathlib, re
from locale_display import load, Resolver, validate_display

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--root',type=pathlib.Path,default=pathlib.Path(__file__).resolve().parents[1])
    ap.add_argument('--decisions',type=pathlib.Path,required=True)
    ap.add_argument('--report',type=pathlib.Path,required=True)
    ap.add_argument('--write',action='store_true')
    args=ap.parse_args()
    decisions={(r['file'],r['key']):r for r in load(args.decisions)}
    report={'changes':[],'rows':[],'files':[],'errors':[]}; pending=[]
    for path in sorted((args.root/'translations').glob('*.json')):
        old=load(path); doc=copy.deepcopy(old); name=doc.get('mod_name','')
        for channel,rows in doc['channels'].items():
            for row in rows:
                ident=row.get('key',row.get('id',''))
                before=copy.deepcopy(row)
                row.pop('translation_bilingual',None)
                decision=decisions.get((path.name,ident)) if channel=='locale' else None
                if decision:
                    if row.get('source')!=decision['source']:
                        report['errors'].append(path.name+': audited source changed: '+ident)
                    else: row['display_type']=decision['display_type']
                # Remove only a whole-source duplicate at the boundary, never FMJ/50-round
                # or other semantic parentheses. Every removed value is archived in report.
                ko,source=row.get('translation',''),row.get('source','')
                if isinstance(ko,str) and isinstance(source,str) and source.strip():
                    for sep in ('\n\n','\n',' '):
                        suffix=sep+'('+source.strip()+')'
                        if ko.rstrip().endswith(suffix) and ko.rstrip()[:-len(suffix)].strip():
                            ko=ko.rstrip()[:-len(suffix)];break
                if isinstance(ko,str) and name and ko.rstrip().endswith('\n('+name+')'):
                    ko=ko.rstrip()[:-len('\n('+name+')')]
                if 'translation' in row: row['translation']=ko
                if before!=row: report['changes'].append(dict(file=path.name,channel=channel,key=ident,before=before,after=row))
        rows=doc['channels'].get('locale',[]);resolver=Resolver(rows)
        for row in rows:
            report['rows'].append(dict(file=path.name,key=row['key'],type=resolver.resolve(row),source=row['source'],korean=row['translation'],mod_name=name))
        report['errors'].extend(path.name+': '+e for e in validate_display(doc))
        if doc==old: continue
        # Edit only changed flat row blocks, retaining file formatting and unchanged rows.
        changes={json.dumps(c['before'],ensure_ascii=False):c['after'] for c in report['changes'] if c['file']==path.name}
        raw=path.read_bytes();text=raw.decode('utf-8-sig');used=set()
        pattern=re.compile(r'(?m)^([ \t]+)\{\r?\n\1  "(?:key|id|source)": [\s\S]*?^\1\}')
        def replace(m):
            parsed=json.loads(m[0]); token=json.dumps(parsed,ensure_ascii=False)
            if token not in changes:return m[0]
            used.add(token);nl='\r\n' if '\r\n' in m[0] else '\n'
            lines=json.dumps(changes[token],ensure_ascii=False,indent=2).splitlines()
            return nl.join(m[1]+line for line in lines)
        updated=pattern.sub(replace,text)
        if used!=changes.keys() or json.loads(updated)!=doc: raise ValueError('Unmatched row edit: '+path.name)
        data=updated.encode('utf-8');data=(b'\xef\xbb\xbf'+data) if raw.startswith(b'\xef\xbb\xbf') else data
        pending.append((path,raw,data));report['files'].append(str(path.relative_to(args.root)))
    for path in sorted((args.root/'config-ui').glob('*.json')):
        if path.name.startswith('_'):continue
        old=load(path);doc=copy.deepcopy(old)
        def clean(node):
            if isinstance(node,dict):
                for k in list(node):
                    if k.endswith('_bilingual'):del node[k]
                    else:clean(node[k])
            elif isinstance(node,list):
                for v in node:clean(v)
        clean(doc)
        if old==doc:continue
        raw=path.read_bytes();text=raw.decode('utf-8-sig')
        text=re.sub(r'(?m)^[ \t]*"[^"\r\n]*_bilingual": "(?:\\.|[^"\\])*",?\r?\n','',text)
        text=re.sub(r',([ \t\r\n]*[}\]])',r'\1',text)
        assert json.loads(text)==doc,path.name
        data=text.encode('utf-8');data=(b'\xef\xbb\xbf'+data) if raw.startswith(b'\xef\xbb\xbf') else data
        pending.append((path,raw,data));report['files'].append(str(path.relative_to(args.root)))
    report['summary']=dict(changed_files=len(pending),changed_translation_rows=len(report['changes']),locale_rows=len(report['rows']),
                           types=dict(collections.Counter(r['type'] for r in report['rows'])))
    args.report.parent.mkdir(parents=True,exist_ok=True)
    args.report.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report['summary'],ensure_ascii=False,indent=2))
    if report['errors']:raise SystemExit('\n'.join(report['errors']))
    if args.write:
        for path,before,after in pending:
            if path.read_bytes()!=before:raise RuntimeError('Concurrent change: '+str(path))
            path.write_bytes(after)
        print('Applied')
    else:print('Dry run; no files written')

if __name__=='__main__':main()
