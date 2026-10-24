#!/usr/bin/env python3
"""Optional developer regression checks. Never used by the distribution build.

Confirm docs removal / archived uploads cannot break build checks, while real
translation errors and invalid release contents still fail. No project edits.
"""
from pathlib import Path
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
COPY = ('SPT_Mod_Korean_Addon.csproj', 'BUILD_DISTRIBUTION.ps1', 'build', 'src', 'server-src', 'translations',
        'config-ui', 'native-locales', 'contract-locales', 'note')
CLIENT = 'BepInEx/plugins/SPT_Mod_Korean_Addon/'
SERVER = 'SPT_Runtime/user/mods/SPT_Mod_Korean_Server/'

class BuildIndependence(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / 'source'
        self.root.mkdir()
        for name in COPY:
            original = ROOT / name
            destination = self.root / name
            if original.is_dir():
                shutil.copytree(original, destination, ignore=shutil.ignore_patterns('__pycache__'))
            else:
                shutil.copy2(original, destination)

    def check(self, *args):
        return subprocess.run([sys.executable, str(self.root / 'build' / 'verify_release.py'),
                               '--root', str(self.root), *args], capture_output=True, text=True)

    def test_without_docs_and_with_upload(self):
        self.assertFalse((self.root / 'docs').exists())
        history = self.root / 'Upload/Old release 1.0.0/tools'
        history.mkdir(parents=True)
        (history / 'old_check.py').write_text('legacy')
        (history.parent / 'README.md').write_text('historical')
        self.assertEqual(self.check('source').returncode, 0)

    def test_all_build_python_steps_without_docs(self):
        self.assertFalse((self.root / 'docs').exists())
        for name in ('validate_translations.py', 'verify_quartermaster_contracts.py',
                     'build_supported_mods.py'):
            result = subprocess.run([sys.executable, str(self.root / 'build' / name)],
                                    capture_output=True, text=True, timeout=40)
            self.assertEqual(result.returncode, 0, name + ': ' + result.stderr)
        self.assertTrue((self.root / 'note' / '번역지원모드목록.md').is_file())

    def test_broken_gpt_docs_are_not_build_inputs(self):
        documentation = self.root / 'docs' / 'broken'
        documentation.mkdir(parents=True)
        (documentation / 'bad_notes.md').write_text('bad GPТ notes')
        self.assertEqual(self.check('source').returncode, 0)

    def test_invalid_current_f12_must_fail(self):
        profile = next((self.root / 'config-ui').glob('*.json'))
        data = json.loads(profile.read_text(encoding='utf-8-sig'))
        data['plugin_guid'] = 'broken.guid'
        profile.write_text(json.dumps(data, ensure_ascii=False), encoding='utf-8')
        res = self.check('source')
        self.assertNotEqual(res.returncode, 0)
        self.assertIn('plugin_guid differs from filename', res.stderr)

    def test_unreferenced_eft_type_fails_before_compilation(self):
        # The project intentionally has no EFT/Assembly-CSharp reference DLL.
        # Regression for the Manimal Terminal CS0246 build failure.
        source = self.root / 'src' / 'ManimalTerminalUiHooks.cs'
        source.write_text(source.read_text(encoding='utf-8') +
                          '\ninternal class DeliberatelyBroken { private EFT.UI.AvailableInteractionState state; }\n',
                          encoding='utf-8')
        result = self.check('source')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('direct EFT type requires an absent game assembly', result.stderr)

    def test_invalid_current_translation_must_fail(self):
        profile = next((self.root / 'translations').glob('*.json'))
        data = json.loads(profile.read_text(encoding='utf-8-sig'))
        data['mod_id'] = ''
        profile.write_text(json.dumps(data, ensure_ascii=False), encoding='utf-8')
        res = self.check('source')
        self.assertNotEqual(res.returncode, 0)
        self.assertIn('missing mod_id', res.stderr)

    def test_release_verifier_blocks_extra_files(self):
        stage = Path(self.temp.name) / 'stage'
        stage.mkdir()
        for folder in ('translations','config-ui','native-locales','contract-locales'):
            for file in (self.root / folder).rglob('*.json'):
                out = stage / CLIENT / file.relative_to(self.root)
                out.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(file, out)
        for dll in (CLIENT + 'SPT_Mod_Korean_Addon.dll', SERVER + 'SPT_Mod_Korean_Server.dll'):
            p=stage / dll;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b'MZ' + b'\0'*128)
        dest = Path(self.temp.name) / 'valid.zip'
        def makezip():
            with zipfile.ZipFile(dest, 'w') as z:
                for file in stage.rglob('*'):
                    if file.is_file(): z.write(file,file.relative_to(stage).as_posix())
        makezip()
        result=self.check('distribution','--stage',str(stage),'--zip',str(dest))
        self.assertEqual(result.returncode, 0, result.stderr)
        bad=stage / 'docs' / 'random.md';bad.parent.mkdir();bad.write_text('oops')
        makezip()
        result=self.check('distribution','--stage',str(stage),'--zip',str(dest))
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('unauthorized release file', result.stderr)

    def test_powershell_build_has_no_docs_dependency(self):
        ps = (self.root / 'BUILD_DISTRIBUTION.ps1').read_text(encoding='utf-8-sig')
        self.assertIn("'build\\verify_release.py'", ps)
        self.assertIn("'build\\validate_translations.py'", ps)
        self.assertIn("'build\\build_supported_mods.py'", ps)
        self.assertIn('GetTempPath()', ps)
        self.assertNotIn("Join-Path $root 'docs\\", ps)
        self.assertNotIn("'NuGet.Offline.Config'", ps)

if __name__ == '__main__':
    unittest.main(verbosity=2)
