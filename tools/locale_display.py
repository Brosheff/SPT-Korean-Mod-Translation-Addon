"""Offline resolver. Full-data tests compare this classification with the actual C# code."""
import json
TYPES = {'default', 'item_name', 'item_description', 'quest_title', 'quest_objective', 'quest_description', 'achievement_title', 'achievement_description', 'achievement_condition'}
QUEST_FIELDS = ('startedmessagetext', 'successmessagetext', 'acceptplayermessage', 'completeplayermessage')
TRADER_FIELDS = {'firstname', 'lastname', 'fullname', 'nickname'}

def unique_object(pairs):
    result = {}
    for k,v in pairs:
        if k in result: raise ValueError('Duplicate JSON property: '+k)
        result[k] = v
    return result

def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'), object_pairs_hook=unique_object)

class Resolver:
    def __init__(self, rows):
        self.keys = {r.get('key','').lower() for r in rows}
        self.traders = {k.rsplit(' ',1)[0] for k in self.keys if ' ' in k and k.rsplit(' ',1)[1] in TRADER_FIELDS}
    def resolve(self, row):
        explicit = row.get('display_type')
        if explicit is not None:
            if explicit not in TYPES: raise ValueError('Invalid display_type: '+row.get('key',''))
            return explicit
        key = row.get('key','')
        if ' ' not in key: return 'default'
        ident,field = key.rsplit(' ',1)
        if not ident or ident.lower() in self.traders: return 'default'
        if any(ident.lower()+' '+f in self.keys for f in QUEST_FIELDS):
            return 'quest_title' if field.lower() == 'name' else 'default'
        if field == 'Name': return 'item_name'
        if field == 'Description': return 'item_description'
        return 'default'

    def name_key(self, row):
        if row.get('name_key'): return row['name_key']
        key=row.get('key','')
        if ' ' not in key: return None
        ident=key.rsplit(' ',1)[0]
        return ident if ident.lower() in self.keys and ident.lower()+' name' not in self.keys else ident+' Name'

def validate_display(doc):
    errors=[]
    rows=doc.get('channels',{}).get('locale',[]) or []
    resolver=Resolver(rows)
    sources={r.get('key','').lower():r.get('source') for r in rows}
    for row in rows:
        try: kind=resolver.resolve(row)
        except ValueError as exc: errors.append(str(exc)); continue
        key=row.get('key','<missing>')
        if row.get('translation_bilingual'): errors.append(key+': legacy bilingual override is not supported; use an audited display_type')
        if row.get('name_key') is not None and (kind!='item_description' or not isinstance(row['name_key'],str) or not row['name_key'].strip()):
            errors.append(key+': name_key must identify an item description name source')
        if kind == 'default': continue
        ko,source=row.get('translation',''),row.get('source','')
        if not isinstance(ko,str) or not isinstance(source,str):
            errors.append(key+': source/translation must be strings'); continue
        annotation=doc.get('mod_name','') if kind in ('item_description','quest_description','achievement_description') else source
        if not isinstance(annotation,str) or not annotation.strip():
            errors.append(key+': missing mod_name/source'); continue
        if ko.rstrip().endswith('\n('+annotation.strip()+')'): errors.append(key+': embedded automatic annotation')
        if kind in ('quest_title','achievement_title') and ko.rstrip().endswith(' ('+source.strip()+')'):
            errors.append(key+': embedded automatic title annotation')
        if kind=='item_description':
            name=sources.get((resolver.name_key(row) or '').lower())
            if isinstance(name,str) and name.strip() and ko.startswith('['+name.strip()+']\n'):
                errors.append(key+': embedded automatic item-name header')
    for channel,entries in doc.get('channels',{}).items():
        if any(r.get('translation_bilingual') for r in entries or []):
            errors.append(channel+': remove legacy translation_bilingual; generic text is Korean in both cultures')
        if channel!='locale' and any(r.get('display_type') is not None for r in entries or []):
            errors.append(channel+': display_type is only supported in locale')
    return errors
