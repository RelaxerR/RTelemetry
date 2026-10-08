"""Synthetic NuGet archives: no platform SDKs, dependencies or network required."""
import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from zipfile import ZipFile

SCRIPT = Path(__file__).resolve().parents[1] / 'merge-maui-packages.py'
SPEC = importlib.util.spec_from_file_location('merge_maui_packages', SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)
NS = 'http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'


class MergeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.mac, self.win = self.root / 'mac', self.root / 'win'
        self.mac.mkdir()
        self.win.mkdir()

    def package(self, directory, tfm, extension='nupkg', identity='RTelemetry.Maui', version='1.0.0-rc.1',
                dependency='[1.0.0-rc.1, )', extra=None, types='', pretty=False):
        group = f'<group targetFramework="{tfm}"><dependency id="RTelemetry.Client" version="{dependency}" /></group>'
        spec = f'''<package xmlns="{NS}"><metadata><id>{identity}</id><version>{version}</version>
        <dependencies>{group}</dependencies>
        <frameworkReferences><group targetFramework="{tfm}"><frameworkReference name="Microsoft.NETCore.App" /></group></frameworkReferences>
        </metadata></package>'''
        if pretty:
            root = ET.fromstring(spec)
            ET.indent(root)
            spec = ET.tostring(root)
        suffix = 'pdb' if extension == 'snupkg' else 'dll'
        files = {
            'RTelemetry.Maui.nuspec': spec,
            '[Content_Types].xml': f'<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="{suffix}" ContentType="application/octet" />{types}</Types>',
            f'lib/{tfm}/RTelemetry.Maui.{suffix}': tfm.encode(),
            'README.md': b'shared',
            '_rels/.rels': b'platform relationships',
            f'package/services/metadata/core-properties/{tfm}.psmdcp': b'platform metadata',
        }
        files.update(extra or {})
        target = directory / f'RTelemetry.Maui.1.0.0-rc.1.{extension}'
        with ZipFile(target, 'w') as archive:
            for name, content in files.items():
                archive.writestr(name, content)
        return target

    def test_cli_merges_nupkg_and_snupkg_framework_assets_dependencies_and_types(self):
        for extension in ('nupkg', 'snupkg'):
            self.package(self.mac, 'net10.0-maccatalyst', extension)
            self.package(self.win, 'net10.0-windows10.0.19041.0', extension,
                         extra={'lib/net10.0-windows10.0.19041.0/resources.pri': b'pri'},
                         types='<Default Extension="pri" ContentType="application/octet" />')
        subprocess.run([sys.executable, str(SCRIPT), str(self.mac), str(self.win)], check=True)
        for extension in ('nupkg', 'snupkg'):
            with ZipFile(next(self.mac.glob('*.' + extension))) as merged:
                suffix = 'pdb' if extension == 'snupkg' else 'dll'
                for tfm in ('net10.0-maccatalyst', 'net10.0-windows10.0.19041.0'):
                    self.assertEqual(tfm.encode(), merged.read(f'lib/{tfm}/RTelemetry.Maui.{suffix}'))
                spec = ET.fromstring(merged.read('RTelemetry.Maui.nuspec'))
                for section in ('dependencies', 'frameworkReferences'):
                    groups = spec.findall(f'{{{NS}}}metadata/{{{NS}}}{section}/{{{NS}}}group')
                    self.assertEqual({'net10.0-maccatalyst', 'net10.0-windows10.0.19041.0'},
                                     {group.get('targetFramework') for group in groups})
                types = ET.fromstring(merged.read('[Content_Types].xml'))
                self.assertEqual({suffix, 'pri'}, {entry.get('Extension') for entry in types})
                self.assertEqual(1, sum(name.endswith('.psmdcp') for name in merged.namelist()))
                self.assertEqual(b'shared', merged.read('README.md'))

    def test_all_four_platforms_survive_merged_package(self):
        tfms = ['net10.0-android', 'net10.0-ios', 'net10.0-maccatalyst', 'net10.0-windows10.0.19041.0']
        left = self.package(self.mac, tfms[0])
        for tfm in tfms[1:]:
            MODULE.merge(left, self.package(self.win, tfm))
        with ZipFile(left) as archive:
            for tfm in tfms:
                self.assertEqual(tfm.encode(), archive.read(f'lib/{tfm}/RTelemetry.Maui.dll'))
            spec = ET.fromstring(archive.read('RTelemetry.Maui.nuspec'))
            groups = spec.findall(f'{{{NS}}}metadata/{{{NS}}}dependencies/{{{NS}}}group')
            self.assertEqual(set(tfms), {group.get('targetFramework') for group in groups})

    def assert_rejected(self, left, right, message):
        original = left.read_bytes()
        with self.assertRaisesRegex(ValueError, message):
            MODULE.merge(left, right)
        self.assertEqual(original, left.read_bytes())
        self.assertFalse(left.with_suffix(left.suffix + '.tmp').exists())

    def test_mismatched_identity_or_version_is_rejected(self):
        left = self.package(self.mac, 'mac')
        for kwargs in ({'identity': 'Another.Package'}, {'version': '2.0.0'}, {'version': ''}):
            with self.subTest(**kwargs):
                self.assert_rejected(left, self.package(self.win, 'windows', **kwargs), 'identity/version')

    def test_conflicting_shared_files_are_rejected(self):
        self.assert_rejected(self.package(self.mac, 'mac'),
                             self.package(self.win, 'windows', extra={'README.md': b'wrong'}), 'Conflicting package content')

    def test_conflicting_dependency_group_is_rejected(self):
        self.assert_rejected(self.package(self.mac, 'same'),
                             self.package(self.win, 'same', dependency='[2.0.0, )'), 'Conflicting dependencies')

    def test_same_group_with_different_indentation_is_not_duplicated_and_merge_is_deterministic(self):
        left, right = self.package(self.mac, 'same'), self.package(self.win, 'same', pretty=True)
        MODULE.merge(left, right)
        with ZipFile(left) as archive:
            spec = ET.fromstring(archive.read('RTelemetry.Maui.nuspec'))
            self.assertEqual(1, len(spec.findall(f'{{{NS}}}metadata/{{{NS}}}dependencies/{{{NS}}}group')))
        first = left.read_bytes()
        MODULE.merge(left, right)
        self.assertEqual(first, left.read_bytes())

    def test_conflicting_content_type_is_rejected(self):
        self.assert_rejected(self.package(self.mac, 'mac'), self.package(self.win, 'windows',
                             types='<Default Extension="dll" ContentType="different" />'), 'Conflicting content types')

    def test_signed_either_input_is_rejected(self):
        for signed_side in ('mac', 'win'):
            with self.subTest(signed_side=signed_side):
                left = self.package(self.mac, 'mac', extra={'.signature.p7s': b'sign'} if signed_side == 'mac' else None)
                right = self.package(self.win, 'windows', extra={'.signature.p7s': b'sign'} if signed_side == 'win' else None)
                self.assert_rejected(left, right, 'signed packages')

    def test_second_manifest_is_rejected(self):
        self.assert_rejected(self.package(self.mac, 'mac'),
                             self.package(self.win, 'windows', extra={'Other.nuspec': '<package />'}), 'exactly one package manifest')

    def test_ci_uses_only_project_scoped_framework_override(self):
        workflows = Path(__file__).resolve().parents[2] / '.github' / 'workflows'
        text = '\n'.join(path.read_text() for path in workflows.glob('*.yml'))
        self.assertNotRegex(text, r'(?i)(?:-p:|/p:)TargetFrameworks?=')
        self.assertIn('-p:RTelemetryMauiTargetFrameworks=net10.0-windows', text)
        self.assertIn('python3 -m unittest discover -s eng/tests -v', text)


if __name__ == '__main__':
    unittest.main()
