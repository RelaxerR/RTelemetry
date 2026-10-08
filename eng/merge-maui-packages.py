#!/usr/bin/env python3
"""Merge unsigned platform packages of the same identity/version (macOS + Windows)."""
import argparse
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED


def semantic(element):
    """Compare XML independent of indentation and dependency order."""
    return (element.tag, tuple(sorted(element.attrib.items())), (element.text or '').strip(),
            tuple(sorted(semantic(child) for child in element)))


def section_key(element):
    kind = element.tag.rsplit('}', 1)[-1]
    if kind == 'group':
        return kind, element.get('targetFramework', '').casefold()
    if kind == 'dependency':
        return kind, element.get('id', '').casefold()
    return kind, element.get('assemblyName', element.get('name', '')).casefold(), element.get('targetFramework', '').casefold()


def merge_children(target, source, key, description):
    existing = {}
    for child in list(target):
        identity = key(child)
        if identity in existing:
            raise ValueError('Duplicate ' + description)
        existing[identity] = child
    for child in source:
        identity = key(child)
        if identity in existing:
            if semantic(existing[identity]) != semantic(child):
                raise ValueError('Conflicting ' + description + ': ' + str(identity))
        else:
            target.append(child)
            existing[identity] = child


def merge(primary: Path, windows: Path):
    with ZipFile(primary) as left, ZipFile(windows) as right:
        for archive in (left, right):
            if len(archive.namelist()) != len(set(archive.namelist())):
                raise ValueError('Duplicate archive entries')
            if any(n.casefold() == '.signature.p7s' for n in archive.namelist()):
                raise ValueError('Cannot merge signed packages')
            if sum(n.endswith('.nuspec') for n in archive.namelist()) != 1:
                raise ValueError('Expected exactly one package manifest')
        names = [n for n in left.namelist() if n.endswith('.nuspec')]
        if len(names) != 1 or names[0] not in right.namelist():
            raise ValueError('Expected matching package manifests')
        name = names[0]
        a, b = ET.fromstring(left.read(name)), ET.fromstring(right.read(name))
        if a.tag != b.tag or not a.tag.endswith('}package'):
            raise ValueError('Expected matching manifest namespaces')
        ns = {'n': a.tag.split('}')[0].strip('{')}
        ET.register_namespace('', ns['n'])
        ma, mb = a.find('n:metadata', ns), b.find('n:metadata', ns)
        if ma is None or mb is None:
            raise ValueError('Missing package metadata')
        for field in ('id', 'version'):
            if not ma.findtext('n:' + field, namespaces=ns) or ma.findtext('n:' + field, namespaces=ns) != mb.findtext('n:' + field, namespaces=ns):
                raise ValueError('Package identity/version differs')
        for section in ('dependencies', 'frameworkReferences', 'frameworkAssemblies'):
            source = mb.find('n:' + section, ns)
            if source is None:
                continue
            target = ma.find('n:' + section, ns)
            if target is None:
                target = ET.SubElement(ma, '{' + ns['n'] + '}' + section)
            merge_children(target, source, section_key, section)
        files = {n: left.read(n) for n in left.namelist()}
        for n in right.namelist():
            if n == name or n == '[Content_Types].xml' or n.startswith(('_rels/', 'package/')):
                continue
            content = right.read(n)
            if n in files and files[n] != content:
                raise ValueError('Conflicting package content: ' + n)
            files[n] = content
        # Preserve any content types introduced by Windows (e.g. PRI resources).
        types = ET.fromstring(files['[Content_Types].xml'])
        merge_children(types, ET.fromstring(right.read('[Content_Types].xml')),
                       lambda e: (e.tag, e.get('Extension', e.get('PartName', ''))), 'content types')
        files['[Content_Types].xml'] = ET.tostring(types, encoding='utf-8', xml_declaration=True)
        files[name] = ET.tostring(a, encoding='utf-8', xml_declaration=True)
    target = primary.with_suffix(primary.suffix + '.tmp')
    try:
        with ZipFile(target, 'w', compression=ZIP_DEFLATED) as result:
            for name, content in sorted(files.items()):
                entry = ZipInfo(name, (2026, 1, 1, 0, 0, 0))
                entry.compress_type = ZIP_DEFLATED
                result.writestr(entry, content)
        target.replace(primary)
    finally:
        target.unlink(missing_ok=True)



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
