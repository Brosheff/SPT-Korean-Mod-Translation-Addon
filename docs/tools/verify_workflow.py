#!/usr/bin/env python3
"""Optional GPT workspace/document and handoff-evidence checks only.

Never called from BUILD_DISTRIBUTION.cmd/ps1. Build checks live in build/.
"""
import argparse
import json
import re
from pathlib import Path, PurePosixPath
import sys

PROJECT_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(PROJECT_ROOT / 'build'))

def json_file(path):
    def check_pairs(pairs):
        result = {}
        for key, val in pairs:
            if key in result:
                raise ValueError('duplicate JSON property: ' + key)
            result[key] = val
        return result
    return json.loads(Path(path).read_text(encoding='utf-8-sig'), object_pairs_hook=check_pairs)

ACTIVE_WORKING_DOCS = (
    'docs/모드번역_작업절차.md',
    'docs/응답전_필수확인.md',
    'docs/번역작업안내서.md',
    'docs/TRANSLATION_FORMAT.md',
    'docs/prompts/translate_prompt.txt',
    'docs/번역대상_인계기록_양식.md',
    'docs/배포_업데이트노트_작성지침.md',
    'docs/tools/VERIFY_WORKFLOW.md',
)
# docs/ is the only workspace for GPT-facing manuals, references, tools and
# work records. Root-level docs/ still has a controlled current-manual registry;
# every file inside the designated subtrees is allowed.
ALLOWED_DOC_FILES = frozenset({
    'AGENTS.md',
    'DISTRIBUTION_LAYOUT.txt',
    'TRANSLATION_FORMAT.md',
    '모드번역_작업절차.md',
    '배포_업데이트노트_작성지침.md',
    '번역대상_인계기록_양식.md',
    '번역작업안내서.md',
    '응답전_필수확인.md',
    'prompts/translate_prompt.txt',
})
GPT_SUBDIRS = frozenset({'records', 'reference-data', 'tools'})
# Legacy versions incorrectly placed build-owned helpers under docs/tools/.
# A source ZIP overwrite cannot delete them. The optional GPT audit detects
# leftovers; use migrate_docs_records.py to archive them without touching builds.
OBSOLETE_BUILD_HELPERS = frozenset({
    'NuGet.Offline.Config', 'validate_translations.py', 'locale_display.py',
    'verify_quartermaster_contracts.py', 'build_supported_mods.py',
    'check_csharp_delimiters.py', 'audit_locale_integrity.py',
})



def docs_purity_checks(root):
    """Ensure GPT resources have no root-level twins and stay under docs/.

    Historical record files are deliberately *not* treated as current manuals.
    """
    errors = []
    docroot = root / 'docs'
    if not docroot.is_dir():
        return errors # DLL source builds need no doc tree.
    for name in GPT_SUBDIRS:
        if (root / name).exists():
            errors.append(f'GPT workspace remains outside docs/: {name}/')
        if not (docroot / name).is_dir():
            errors.append(f'missing GPT workspace: docs/{name}/')
    for name in OBSOLETE_BUILD_HELPERS:
        if (docroot / 'tools' / name).is_file():
            errors.append('obsolete build helper under GPT docs/tools/: ' + name + '; run migrate_docs_records.py')
    for path in docroot.rglob('*'):
        relative = path.relative_to(docroot)
        rel = relative.as_posix()
        if relative.parts[0] in GPT_SUBDIRS:
            continue
        if path.is_file() and rel not in ALLOWED_DOC_FILES:
            errors.append('unclassified file directly in docs manuals tree: ' + rel)
        if path.is_dir() and rel != 'prompts':
            errors.append('unclassified directory in docs manuals tree: ' + rel)
    # Never classify arbitrary project files by extension. The former
    # root.rglob('*') scan treated Upload/ release archives, third-party READMEs,
    # license files, and backups as GPT documents and even blocked compilation.
    # Only the explicitly retired, first-level GPT workspace locations are
    # governed by this rule. docs/ is checked above, independently of builds.
    return errors


DOCUMENTED_UNBUNDLED_TESTS = (
    'tests/LocaleDisplayContract',
    'tests/ServerDisplayContract',
    'tests/CasinoWriteContract',
    'tests/WorkflowChecks',
)


def document_reference_checks(root):
    """Audit navigation, stale directions, links and claimed test availability.

    Does not prove reading compliance, translation quality, DLL build or game use.
    Archived work records under docs/records/ are not current instructions.
    """
    errors = []
    entry = root / 'docs' / 'AGENTS.md'
    if not entry.is_file():
        return ['missing central document entrypoint: docs/AGENTS.md']
    entry_text = entry.read_text(encoding='utf-8-sig')
    if '## 문서 읽기 순서와 역할 — 단일 진입점' not in entry_text:
        errors.append('docs/AGENTS.md is missing authoritative reading-order/role index')
    if 'docs/records/' not in entry_text or '현행 지시' not in entry_text:
        errors.append('docs/AGENTS.md does not distinguish historical records from current rules')
    process = root / 'docs' / '모드번역_작업절차.md'
    if process.is_file() and 'docs/AGENTS.md' not in process.read_text(encoding='utf-8-sig'):
        errors.append('top-level procedure does not delegate reading order to docs/AGENTS.md')

    # Markdown links are tested as actual paths, not merely presence of text.
    markdown_link = re.compile(r'(?<!!)\[[^\]]+\]\(([^)]+)\)')
    for rel in ('docs/AGENTS.md',) + ACTIVE_WORKING_DOCS:
        doc = root / rel
        if not doc.is_file():
            errors.append('missing active working document: ' + rel)
            continue
        body = doc.read_text(encoding='utf-8-sig')
        if rel != 'docs/AGENTS.md' and rel not in entry_text:
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
        if rel != 'docs/AGENTS.md':
            # Unavailable test must be described as such at the point of use.
            for line_no, line in enumerate(body.splitlines(), 1):
                found = re.findall(r'tests/[A-Za-z0-9_.\-/]+', line)
                if not found:
                    continue
                if all((root / name.rstrip('./')).exists() for name in found):
                    continue
                if not any(status in line for status in ('미동봉', '미실시', '원본 없음', '없습니다', '없으므로', '없는', '존재할 때', '확보한 경우', '확보된 경우')):
                    errors.append(f'unqualified missing-test reference: {rel}:{line_no}')
                if rel != 'docs/tools/VERIFY_WORKFLOW.md':
                    test_status_file = root / 'docs/tools/VERIFY_WORKFLOW.md'
                    test_status = test_status_file.read_text(encoding='utf-8-sig') if test_status_file.is_file() else ''
                    for test_path in found:
                        if test_path.rstrip('./') not in test_status:
                            errors.append(f'missing-test path is absent from status index: {rel}:{line_no}: {test_path}')

    # Historical records are no longer mandatory current documents.
    # Do not open or enforce their content in the document check.

    status = root / 'docs/tools' / 'VERIFY_WORKFLOW.md'
    if status.is_file():
        content = status.read_text(encoding='utf-8-sig')
        if (root / 'tests').is_dir() and '현재 제공된 **1.0.4 소스 ZIP에는 `tests/` 디렉터리가 없습니다.**' in content:
            errors.append('docs/tools/VERIFY_WORKFLOW.md incorrectly says all tests are unbundled')
        if not (root / 'tests').is_dir():
            for name in DOCUMENTED_UNBUNDLED_TESTS:
                if name not in content:
                    errors.append('missing unbundled test status in docs/tools/VERIFY_WORKFLOW.md: ' + name)
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
        'docs/AGENTS.md': ('매 턴마다', 'docs/응답전_필수확인.md', '실제'),
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
    for misplaced in ('AGENTS.md', 'README.md', 'docs/번역지원모드목록.md',
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
    # Enforce that GPT records and tools stay inside their docs/ subdirectories.
    errors.extend(docs_purity_checks(root))
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




def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=PROJECT_ROOT)
    sub = parser.add_subparsers(dest='mode', required=True)
    sub.add_parser('documents', help='Audit GPT workspace only (optional)')
    handoff = sub.add_parser('handoff', help='Check audited candidate inventory and addon evidence')
    handoff.add_argument('--manifest', type=Path, required=True)
    handoff.add_argument('--inventory', type=Path)
    handoff.add_argument('--require-complete', action='store_true')
    args = parser.parse_args()
    root = args.root.resolve()
    if args.mode == 'documents':
        errors = document_checks(root)
    else:
        errors = handoff_checks(root, args.manifest, args.inventory, args.require_complete)
    if errors:
        for msg in errors:
            print('FAIL: ' + msg, file=sys.stderr)
        print(f'FAILED: {len(errors)} issue(s)', file=sys.stderr)
        return 1
    print('OK: ' + ('GPT document layout' if args.mode == 'documents' else 'GPT handoff evidence'))
    return 0

if __name__ == '__main__':
    sys.exit(main())
