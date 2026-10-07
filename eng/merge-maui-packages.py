#!/usr/bin/env python3
"""Merge unsigned platform packages of the same identity/version (macOS + Windows)."""
import argparse
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED


def merge(primary: Path, windows: Path):
    with ZipFile(primary) as left, ZipFile(windows) as right:
        names = [n for n in left.namelist() if n.endswith('.nuspec')]
        if len(names) != 1 or names[0] not in right.namelist():
            raise ValueError('Expected matching package manifests')
        name = names[0]
        a, b = ET.fromstring(left.read(name)), ET.fromstring(right.read(name))
        ns = {'n': a.tag.split('}')[0].strip('{')}
        ET.register_namespace('', ns['n'])
        ma, mb = a.find('n:metadata', ns), b.find('n:metadata', ns)
        for field in ('id', 'version'):
            if ma.findtext('n:' + field, namespaces=ns) != mb.findtext('n:' + field, namespaces=ns):
                raise ValueError('Package identity/version differs')
        for section in ('dependencies', 'frameworkReferences', 'frameworkAssemblies'):
            source = mb.find('n:' + section, ns)
            if source is None:
                continue
            target = ma.find('n:' + section, ns)
            if target is None:
                ma.append(source)
            else:
                existing = {ET.tostring(e) for e in target}
                for child in source:
                    if ET.tostring(child) not in existing:
                        target.append(child)
        files = {n: left.read(n) for n in left.namelist()}
        if '.signature.p7s' in files or '.signature.p7s' in right.namelist():
            raise ValueError('Cannot merge signed packages')
        for n in right.namelist():
            if n == name or n == '[Content_Types].xml' or n.startswith(('_rels/', 'package/')):
                continue
            content = right.read(n)
            if n in files and files[n] != content:
                raise ValueError('Conflicting package content: ' + n)
            files[n] = content
        # Preserve any content types introduced by Windows (e.g. PRI resources).
        types = ET.fromstring(files['[Content_Types].xml'])
        existing_types = {tuple(sorted(e.attrib.items())) for e in types}
        for e in ET.fromstring(right.read('[Content_Types].xml')):
            if tuple(sorted(e.attrib.items())) not in existing_types:
                types.append(e)
        files['[Content_Types].xml'] = ET.tostring(types, encoding='utf-8', xml_declaration=True)
        files[name] = ET.tostring(a, encoding='utf-8', xml_declaration=True)
    target = primary.with_suffix(primary.suffix + '.tmp')
    with ZipFile(target, 'w', compression=ZIP_DEFLATED) as result:
        for name, content in sorted(files.items()):
            entry = ZipInfo(name, (2026, 1, 1, 0, 0, 0))
            entry.compress_type = ZIP_DEFLATED
            result.writestr(entry, content)
    target.replace(primary)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('primary', type=Path)
    parser.add_argument('windows', type=Path)
    args = parser.parse_args()
    for extension in ('nupkg', 'snupkg'):
        matches = list(args.primary.glob('RTelemetry.Maui.*.' + extension))
        if len(matches) != 1:
            raise ValueError('Expected one MAUI ' + extension)
        merge(matches[0], args.windows / matches[0].name)
