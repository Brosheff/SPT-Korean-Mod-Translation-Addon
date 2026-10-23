#!/usr/bin/env python3
"""SPT Korean Addon: source layout, handoff coverage, and release ZIP guards.

No in-game claims: these are static checks and require an independently prepared
inventory to evaluate the completeness of a previous source audit.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path, PurePosixPath
import sys
import zipfile

PROJECT_ROOT = Path(__file__).resolve().parents[1]
TOP_DIRS = ('BepInEx/plugins/SPT_Mod_Korean_Addon/',
            'SPT_Runtime/user/mods/SPT_Mod_Korean_Server/')
CLIENT = TOP_DIRS[0]
SERVER = TOP_DIRS[1]
REQUIRED_DLLS = (CLIENT + 'SPT_Mod_Korean_Addon.dll',
                 SERVER + 'SPT_Mod_Korean_Server.dll')
DATA_DIRS = ('translations', 'config-ui', 'native-locales', 'contract-locales')


def json_file(path):
    def check_pairs(pairs):
        result = {}
        for key, val in pairs:
            if key in result:
                raise ValueError('duplicate JSON property: ' + key)
            result[key] = val
        return result
    return json.loads(Path(path).read_text(encoding='utf-8-sig'), object_pairs_hook=check_pairs)


def source_checks(root):
    errors = []
    for required in ('translations', 'config-ui', 'src', 'server-src', 'tools',
                     'native-locales', 'contract-locales', 'BUILD_DISTRIBUTION.ps1'):
        if not (root / required).exists():
            errors.append('missing project input: ' + required)
    if errors:
        return errors
    configs = sorted((root / 'config-ui').glob('*.json'))
    guids = set()
    for p in configs:
        try:
            doc = json_file(p)
            if 'source_mod_version' in doc or not isinstance(doc.get('mod_version'), str) or not doc['mod_version'].strip():
                errors.append(p.name + ': invalid/obsolete mod_version metadata')
            if doc.get('plugin_guid') != p.stem:
                errors.append(p.name + ': plugin_guid differs from filename')
            if p.stem in guids:
                errors.append(p.name + ': duplicate config GUID')
            guids.add(p.stem)
            entries = set()
            for i, entry in enumerate(doc.get('entries') or []):
                if not isinstance(entry, dict):
                    errors.append(f'{p.name}: entries[{i}] not an object'); continue
                ident = (entry.get('section'), entry.get('key'))
                # Empty key is valid for mods with an empty ConfigDefinition key.
                if not isinstance(ident[0], str) or not isinstance(ident[1], str):
                    errors.append(f'{p.name}: entries[{i}] invalid section/key')
                if ident in entries:
                    errors.append(f'{p.name}: duplicate F12 section/key {ident}')
                entries.add(ident)
                if entry.get('source_display_name') and not entry.get('translation_display_name'):
                    errors.append(f'{p.name}: untranslated F12 name {ident}')
                if entry.get('source_description') and not entry.get('translation_description'):
                    errors.append(f'{p.name}: untranslated F12 description {ident}')
                for j, value in enumerate(entry.get('values') or []):
                    if not isinstance(value, dict) or not isinstance(value.get('source'), str) or not isinstance(value.get('translation'), str) or not value['translation']:
                        errors.append(f'{p.name}: untranslated F12 value {ident}[{j}]')
            cats = set()
            for i, cat in enumerate(doc.get('categories') or []):
                if not isinstance(cat, dict) or not cat.get('source') or not cat.get('translation'):
                    errors.append(f'{p.name}: missing F12 category translation [{i}]')
                elif cat['source'] in cats:
                    errors.append(f'{p.name}: duplicate F12 category {cat["source"]}')
                else:
                    cats.add(cat['source'])
        except (ValueError, OSError, TypeError) as exc:
            errors.append(f'{p.name}: {exc}')
    # Current release targets Aishi 2.0.4 / Special Forces 2.0.1 only.
    # Deleted old-version compatibility profiles must not reappear unnoticed.
    deprecated_profiles = (
        'ISB Aishi - 2.0.3 Compatibility.json',
        'ISB Special Forces - 2.0.0 Compatibility.json',
    )
    for old_name in deprecated_profiles:
        if (root / 'translations' / old_name).is_file():
            errors.append('obsolete mod-version compatibility profile must be removed: ' + old_name)
    if (root / 'tools' / 'check_upgrade_compatibility.py').is_file():
        errors.append('obsolete old-version compatibility checker must be removed')
    ids = set()
    for p in sorted((root / 'translations').glob('*.json')):
        try:
            doc = json_file(p)
            if 'source_mod_version' in doc or not isinstance(doc.get('mod_version'), str) or not doc['mod_version'].strip():
                errors.append(p.name + ': invalid/obsolete mod_version metadata')
            mod_id = doc.get('mod_id')
            if not mod_id:
                errors.append(p.name + ': missing mod_id')
            elif mod_id in ids:
                errors.append(p.name + ': duplicate mod_id ' + str(mod_id))
            ids.add(mod_id)
        except (ValueError, OSError, TypeError) as exc:
            errors.append(f'{p.name}: {exc}')
    # A prior ArenaMode patch failed the user build with CS1026 although JSON
    # tests passed. Catch basic C# delimiter damage before invoking dotnet.
    from check_csharp_delimiters import check_sources
    errors.extend(check_sources(root))
    # Static checks for explicit capacities and identical-key/identical-source conflicts.
    # Different sources remain distinct candidates. Never report this as in-game proof.
    from audit_locale_integrity import audit
    errors.extend(audit(root / 'translations'))
    # Only guaranteed existing bridge, not a runtime proof of hook installation.
    return errors


# Authoritative working-document registry. AGENTS.md must link to each active
# document; histories are preserved but cannot serve as current instructions.
ACTIVE_WORKING_DOCS = (
    'docs/모드번역_작업절차.md',
    'docs/응답전_필수확인.md',
    'docs/번역작업안내서.md',
    'docs/TRANSLATION_FORMAT.md',
    'docs/prompts/translate_prompt.txt',
    'docs/번역대상_인계기록_양식.md',
    'docs/배포_업데이트노트_작성지침.md',
    'tools/VERIFY_WORKFLOW.md',
)
HISTORICAL_DOCS = (
    'docs/1.0.4_릴리스_인계_20261022.md',
    'docs/1.0.4_덮어쓰기_회귀복구_20261023.md',
    'docs/1.0.4_전면회귀_원본코드추적_20261023.md',
    'docs/1.0.4_최신버전_전용구성_20261023.md',
    'docs/3개_모드_신버전_인계_20261022.md',
    'docs/3개모드_퀘스트화자_정밀재검수_20261022.md',
    'docs/PARENT_PATTERN_AUDIT_1.7.14.md',
    'docs/LOCALE_DISPLAY_1.0.0_KO.md',
)
DOCUMENTED_UNBUNDLED_TESTS = (
    'tests/LocaleDisplayContract',
    'tests/ServerDisplayContract',
    'tests/CasinoWriteContract',
    'tests/WorkflowChecks',
)


def document_reference_checks(root):
    """Audit navigation, stale directions, links and claimed test availability.

    Does not prove reading compliance, translation quality, DLL build or game use.
    Docs under docs/patches are intentionally outside this audit.
    """
    errors = []
    entry = root / 'AGENTS.md'
    if not entry.is_file():
        return ['missing central document entrypoint: AGENTS.md']
    entry_text = entry.read_text(encoding='utf-8-sig')
    if '## 문서 읽기 순서와 역할 — 단일 진입점' not in entry_text:
        errors.append('AGENTS.md is missing authoritative reading-order/role index')
    if '역사·기술 참고 자료' not in entry_text or '현행 지시' not in entry_text:
        errors.append('AGENTS.md does not distinguish historical records from current rules')
    process = root / 'docs' / '모드번역_작업절차.md'
    if process.is_file() and 'AGENTS.md' not in process.read_text(encoding='utf-8-sig'):
        errors.append('top-level procedure does not delegate reading order to AGENTS.md')

    # Markdown links are tested as actual paths, not merely presence of text.
    markdown_link = re.compile(r'(?<!!)\[[^\]]+\]\(([^)]+)\)')
    for rel in ('AGENTS.md',) + ACTIVE_WORKING_DOCS:
        doc = root / rel
        if not doc.is_file():
            errors.append('missing active working document: ' + rel)
            continue
        body = doc.read_text(encoding='utf-8-sig')
        if rel != 'AGENTS.md' and rel not in entry_text:
            errors.append('working document absent from AGENTS.md central index: ' + rel)
        for link in markdown_link.findall(body):
            dest = link.split('#', 1)[0].strip()
            if not dest or dest.startswith(('http:', 'https:', 'mailto:', '/')):
                continue
            linked = (doc.parent / dest).resolve()
            if not linked.is_relative_to(root.resolve()) or not linked.is_file():
                errors.append(f'broken document link: {rel} -> {link}')
        # The oldest release numbers and former standalone Docs.zip must never
        # silently reappear as live steps in instructions.
        for stale in ('Docs.zip', '전체 1.0.3 신규·수정 항목'):
            if stale in body:
                errors.append(f'stale working-document instruction in {rel}: {stale}')
        if rel != 'AGENTS.md':
            # Unavailable test must be described as such at the point of use.
            for line_no, line in enumerate(body.splitlines(), 1):
                found = re.findall(r'tests/[A-Za-z0-9_.\-/]+', line)
                if not found:
                    continue
                if all((root / name.rstrip('./')).exists() for name in found):
                    continue
                if not any(status in line for status in ('미동봉', '미실시', '원본 없음', '없습니다', '없으므로', '없는', '존재할 때', '확보한 경우', '확보된 경우')):
                    errors.append(f'unqualified missing-test reference: {rel}:{line_no}')
                if rel != 'tools/VERIFY_WORKFLOW.md':
                    test_status_file = root / 'tools/VERIFY_WORKFLOW.md'
                    test_status = test_status_file.read_text(encoding='utf-8-sig') if test_status_file.is_file() else ''
                    for test_path in found:
                        if test_path.rstrip('./') not in test_status:
                            errors.append(f'missing-test path is absent from status index: {rel}:{line_no}: {test_path}')

    for rel in HISTORICAL_DOCS:
        doc = root / rel
        if not doc.is_file():
            errors.append('missing historical record: ' + rel)
            continue
        first = doc.read_text(encoding='utf-8-sig').splitlines()[:2]
        if not first or not first[0].startswith('> **') or not any(word in first[0] for word in ('이력', '폐기', '참고')):
            errors.append('historical record missing non-normative banner: ' + rel)

    status = root / 'tools' / 'VERIFY_WORKFLOW.md'
    if status.is_file():
        content = status.read_text(encoding='utf-8-sig')
        if (root / 'tests').is_dir() and '현재 제공된 **1.0.4 소스 ZIP에는 `tests/` 디렉터리가 없습니다.**' in content:
            errors.append('tools/VERIFY_WORKFLOW.md incorrectly says all tests are unbundled')
        if not (root / 'tests').is_dir():
            for name in DOCUMENTED_UNBUNDLED_TESTS:
                if name not in content:
                    errors.append('missing unbundled test status in tools/VERIFY_WORKFLOW.md: ' + name)
        # Unconditional test commands were previously copied into working docs.
        if 'python -m unittest discover -s tests/WorkflowChecks -v' in content and not (root / 'tests/WorkflowChecks').is_dir():
            errors.append('documentation recommends running tests/WorkflowChecks that is not bundled')
    return errors


def document_checks(root):
    errors = []
    # GPT/developer document QA is explicitly separate from DLL building.
    doc_files = ('번역작업안내서.md', '모드번역_작업절차.md', '응답전_필수확인.md',
                 '번역대상_인계기록_양식.md', '배포_업데이트노트_작성지침.md',
                 'prompts/translate_prompt.txt')
    note_files = ('README.md', '번역지원모드목록.md', 'update.md')
    for name in doc_files:
        if not (root / 'docs' / name).is_file():
            errors.append('missing GPT working document: docs/' + name)
    for name in note_files:
        if not (root / 'note' / name).is_file():
            errors.append('missing public document: note/' + name)
    # Check repository-facing entrypoint and required response-gate anchors.
    # This guards missing/mispackaged instructions; it does NOT prove that an
    # assistant read the files in a later conversation or that in-game text works.
    required_response_gate = {
        'AGENTS.md': ('매 턴마다', 'docs/응답전_필수확인.md', '실제'),
        'docs/모드번역_작업절차.md': ('0-A. 매 답변 전 문서 재확인', '응답전_필수확인.md'),
        'docs/응답전_필수확인.md': ('매 응답마다', '런타임', '게임'),
        'docs/번역작업안내서.md': ('매 답변 전 확인', '응답전_필수확인.md'),
        'docs/TRANSLATION_FORMAT.md': ('실제 게임 원문', 'VSK', 'VKS'),
        'docs/번역대상_인계기록_양식.md': ('응답 게이트 재확인', '실행 영어 원문'),
    }
    for path, phrases in required_response_gate.items():
        f = root / path
        if not f.is_file():
            errors.append('missing mandatory response-gate document: ' + path)
            continue
        try:
            content = f.read_text(encoding='utf-8-sig')
        except OSError as exc:
            errors.append('cannot read response-gate document ' + path + ': ' + str(exc))
            continue
        for phrase in phrases:
            if phrase not in content:
                errors.append('missing response-gate instruction in ' + path + ': ' + phrase)
    for misplaced in ('README.md', 'docs/번역지원모드목록.md',
                      'docs/RELEASE_1.0.1_KO.md', 'docs/RELEASE_1.0.2_KO.md'):
        if (root / misplaced).is_file():
            errors.append('document in obsolete location: ' + misplaced)
    try:
        import re
        project = (root / 'SPT_Mod_Korean_Addon.csproj').read_text(encoding='utf-8-sig')
        version_match = re.search(r'<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>', project)
        if version_match and (root / 'note' / 'update.md').is_file():
            actual = (root / 'note' / 'update.md').read_text(encoding='utf-8-sig')
            if not re.search(r'^# SPT Mod Korean Addon v' + re.escape(version_match.group(1)) + r'\s*$', actual, re.M):
                errors.append('note/update.md does not match client release version')
    except OSError as exc:
        errors.append('cannot inspect release documentation version: ' + str(exc))
    # The public support list must reflect the current per-profile versions.
    try:
        from build_supported_mods import render_supported_mods
        target = root / 'note' / '번역지원모드목록.md'
        if target.is_file():
            if target.read_text(encoding='utf-8-sig') != render_supported_mods(root):
                errors.append('note/번역지원모드목록.md differs from current mod_version profiles; run build_supported_mods.py')
    except (OSError, ValueError, KeyError, AssertionError) as exc:
        errors.append('cannot verify support list versions: ' + str(exc))
    # Cross-file consistency is independent from runtime and DLL validation.
    errors.extend(document_reference_checks(root))
    # extras is not an addon input. No optional original-mod patches in source packaging.
    if (root / 'extras').exists():
        errors.append('unsupported root extras/: independent mod patches are forbidden')
    return errors


def rel_file(root, relative):
    if not isinstance(relative, str):
        raise ValueError('file must be a relative path')
    cleaned = relative.replace('\\', '/')
    pp = PurePosixPath(cleaned)
    if pp.is_absolute() or '..' in pp.parts or not pp.parts:
        raise ValueError('path escapes project: ' + relative)
    p = root.joinpath(*pp.parts)
    if not p.is_file():
        raise ValueError('file not found: ' + relative)
    return p


def evidence_check(root, target):
    ev = target.get('evidence')
    if not isinstance(ev, dict):
        raise ValueError('translated target missing evidence object')
    kind = target.get('kind')
    path = rel_file(root, ev.get('file'))
    if kind == 'hook':
        if path.suffix != '.cs' or not str(path.relative_to(root)).replace('\\', '/').startswith(('src/', 'server-src/')):
            raise ValueError('hook evidence must be in addon C# sources')
        txt = path.read_text(encoding='utf-8-sig')
        tokens = ev.get('tokens')
        if not isinstance(tokens, list) or not tokens or not all(isinstance(s, str) and s.strip() for s in tokens):
            raise ValueError('hook evidence requires exact nonempty tokens')
        for token in tokens:
            if token not in txt:
                raise ValueError('missing hook evidence token: ' + token)
        registration = ev.get('registration')
        if not isinstance(registration, dict):
            raise ValueError('hook evidence requires separate registration evidence')
        reg_path = rel_file(root, registration.get('file'))
        if reg_path.suffix != '.cs' or not reg_path.relative_to(root).as_posix().startswith(('src/', 'server-src/')):
            raise ValueError('hook registration must be in addon C# sources')
        reg_text = reg_path.read_text(encoding='utf-8-sig')
        reg_tokens = registration.get('tokens')
        if not isinstance(reg_tokens, list) or not reg_tokens or not all(isinstance(s, str) and s.strip() for s in reg_tokens):
            raise ValueError('hook registration requires exact nonempty tokens')
        for token in reg_tokens:
            if token not in reg_text:
                raise ValueError('missing hook registration token: ' + token)
        return
    doc = json_file(path)
    if kind == 'locale':
        if not ev['file'].startswith('translations/'):
            raise ValueError('locale evidence must be a translations profile')
        rows = doc.get('channels', {}).get(ev.get('channel', 'locale'), [])
        row = next((r for r in rows if r.get('key') == ev.get('key')), None)
    elif kind == 'channel_row':
        if not ev['file'].startswith('translations/'):
            raise ValueError('channel row evidence must be a translations profile')
        rows = doc.get('channels', {}).get(ev.get('channel'), [])
        field = ev.get('field', 'id')
        if field not in ('id', 'key', 'source'):
            raise ValueError('unsupported channel selector')
        row = next((r for r in rows if r.get(field) == ev.get('selector')), None)
    elif kind in ('f12_entry', 'f12_value'):
        if not ev['file'].startswith('config-ui/'):
            raise ValueError('F12 evidence must be a config-ui profile')
        row = next((r for r in doc.get('entries', []) if r.get('section') == ev.get('section') and r.get('key') == ev.get('key')), None)
        if row is not None and kind == 'f12_value':
            row = next((v for v in row.get('values', []) if v.get('source') == ev.get('source')), None)
    elif kind == 'f12_category':
        if not ev['file'].startswith('config-ui/'):
            raise ValueError('F12 evidence must be a config-ui profile')
        row = next((r for r in doc.get('categories', []) if r.get('source') == ev.get('source')), None)
    elif kind == 'native_locale':
        if not ev['file'].startswith('native-locales/'):
            raise ValueError('native locale must use existing native-locales directory')
        row = {'translation': doc.get(ev.get('key'))} if isinstance(doc, dict) and ev.get('key') in doc else None
    else:
        raise ValueError('unknown target kind: ' + str(kind))
    if row is None:
        raise ValueError('evidence row missing: ' + str(ev))
    if kind == 'f12_entry':
        if not row.get('translation_display_name'):
            raise ValueError('F12 entry has no translated display name')
        if row.get('source_description') and not row.get('translation_description'):
            raise ValueError('F12 entry has no translated description')
        if ev.get('source') is not None and row.get('source_display_name') != ev['source']:
            raise ValueError('F12 entry source mismatch')
    else:
        if not row.get('translation') or not isinstance(row['translation'], str):
            raise ValueError('evidence row has no translation')
        if ev.get('source') is not None and row.get('source') != ev['source']:
            raise ValueError('translation source mismatch')


def handoff_checks(root, manifest_file, inventory_file, require_complete=False):
    errors = []
    try:
        manifest = json_file(manifest_file)
        if manifest.get('schema_version') != 1:
            errors.append('handoff schema_version must be 1')
        targets = manifest.get('targets')
        if not isinstance(targets, list) or not targets:
            return ['handoff requires nonempty targets list']
        ids = set()
        allowed = {'translated', 'source_kept', 'non_target', 'blocked'}
        for i, t in enumerate(targets):
            if not isinstance(t, dict):
                errors.append(f'target[{i}] is not an object'); continue
            ident = t.get('id')
            if not isinstance(ident, str) or not ident.strip():
                errors.append(f'target[{i}] missing id'); continue
            if ident in ids:
                errors.append('duplicate handoff id: ' + ident)
            ids.add(ident)
            status = t.get('status')
            if status not in allowed:
                errors.append(ident + ': invalid status')
            elif status == 'translated':
                try:
                    evidence_check(root, t)
                except (KeyError, ValueError, OSError, TypeError) as exc:
                    errors.append(ident + ': ' + str(exc))
            elif not isinstance(t.get('reason'), str) or not t['reason'].strip():
                errors.append(ident + ': non-translated status requires reason')
            if require_complete and status == 'blocked':
                errors.append(ident + ': blocked target prevents release-ready claim')
        if inventory_file:
            inv = json_file(inventory_file)
            candidates = inv.get('candidate_ids') if isinstance(inv, dict) else None
            if not isinstance(candidates, list) or not candidates or not all(isinstance(x, str) for x in candidates):
                errors.append('inventory candidate_ids must be a nonempty string list')
            else:
                invset = set(candidates)
                if len(invset) != len(candidates):
                    errors.append('duplicate inventory ids')
                errors.extend('candidate missing from handoff: ' + x for x in sorted(invset - ids))
                errors.extend('handoff target absent from inventory: ' + x for x in sorted(ids - invset))
    except (ValueError, OSError, TypeError) as exc:
        errors.append('handoff or inventory: ' + str(exc))
    return errors


def expected_source_data(root):
    files = {}
    for directory in DATA_DIRS:
        for p in (root / directory).rglob('*.json'):
            rel = p.relative_to(root).as_posix()
            files[CLIENT + rel] = p.read_bytes()
    return files


def distribution_files(root, files):
    errors = []
    expected = expected_source_data(root)
    authorized = set(expected) | set(REQUIRED_DLLS)
    seen = set()
    for name, data in files.items():
        pp = PurePosixPath(name)
        if name.startswith('/') or '\\' in name or '..' in pp.parts or pp.is_absolute():
            errors.append('unsafe ZIP/stage path: ' + name); continue
        if name in seen:
            errors.append('duplicate release path: ' + name)
        seen.add(name)
        if name not in authorized:
            errors.append('unauthorized release file: ' + name)
        if not name.startswith(TOP_DIRS):
            errors.append('not an install-layout path: ' + name)
        elif name not in REQUIRED_DLLS:
            if not name.startswith(CLIENT):
                errors.append('unexpected server output: ' + name)
            else:
                tail = name[len(CLIENT):]
                if not any(tail.startswith(d + '/') for d in DATA_DIRS) or not tail.endswith('.json'):
                    errors.append('development/unexpected client file: ' + name)
    for p in REQUIRED_DLLS:
        if p not in files:
            errors.append('missing runtime DLL: ' + p)
        elif not files[p].startswith(b'MZ'):
            errors.append('invalid PE DLL header: ' + p)
    for name, source_bytes in expected.items():
        if name not in files:
            errors.append('missing translated release data: ' + name)
        elif hashlib.sha256(files[name]).digest() != hashlib.sha256(source_bytes).digest():
            errors.append('stale/mismatched release data: ' + name)
    return errors


def stage_files(path):
    result = {}
    for p in path.rglob('*'):
        if p.is_file():
            result[p.relative_to(path).as_posix()] = p.read_bytes()
    return result


def zip_files(path):
    errors = []
    result = {}
    with zipfile.ZipFile(path) as z:
        for info in z.infolist():
            if info.is_dir(): continue
            name = info.filename
            if name in result:
                errors.append('duplicate ZIP entry: ' + name)
            result[name] = z.read(info)
    return result, errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=PROJECT_ROOT)
    sub = parser.add_subparsers(dest='mode', required=True)
    sub.add_parser('source', help='Check build inputs, F12 fields, GUIDs and duplicates (no documents)')
    sub.add_parser('documents', help='Optional GPT/developer document layout audit')
    handoff = sub.add_parser('handoff', help='Cross-check audited candidates with addon evidence')
    handoff.add_argument('--manifest', type=Path, required=True)
    handoff.add_argument('--inventory', type=Path, help='Independent candidate inventory; enables coverage gate')
    handoff.add_argument('--require-complete', action='store_true', help='Fail on targets marked blocked')
    dist = sub.add_parser('distribution', help='Validate staged install tree and generated release ZIP')
    dist.add_argument('--stage', type=Path, required=True)
    dist.add_argument('--zip', type=Path, required=True)
    args = parser.parse_args()
    root = args.root.resolve()
    if args.mode == 'source':
        errors = source_checks(root)
    elif args.mode == 'documents':
        errors = document_checks(root)
    elif args.mode == 'handoff':
        errors = handoff_checks(root, args.manifest, args.inventory, args.require_complete)
    else:
        try:
            stage = stage_files(args.stage)
            archived, errors = zip_files(args.zip)
            errors += distribution_files(root, stage)
            errors += distribution_files(root, archived)
            if archived != stage:
                errors.append('distribution ZIP is different from staged files')
        except (OSError, ValueError, zipfile.BadZipFile) as exc:
            errors = ['release inspection failed: ' + str(exc)]
    if errors:
        for msg in errors:
            print('FAIL: ' + msg, file=sys.stderr)
        print(f'FAILED: {len(errors)} issue(s)', file=sys.stderr)
        return 1
    print('OK: ' + {'source': 'runtime source/F12 checks',
                   'documents': 'documentation layout',
                   'handoff': 'handoff evidence' + (' and candidate coverage' if getattr(args, 'inventory', None) else ' (no inventory; coverage not certified)'),
                   'distribution': 'staged package/ZIP layout and current translation data'}[args.mode])
    return 0


if __name__ == '__main__':
    sys.exit(main())
