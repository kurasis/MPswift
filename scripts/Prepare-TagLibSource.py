#!/usr/bin/env python3
"""Prepare the exact upstream TagLibSharp release for source/build review, without executing it."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import stat
import urllib.request
import zipfile

COMMIT = 'b5ae84f2e84087bf160bb0471420200dd2b5d809'
PREFIX = 'taglib-sharp-' + COMMIT
URL = 'https://codeload.github.com/mono/taglib-sharp/zip/' + COMMIT
SHA256 = 'ab6fab7e22c7423e428f591d7525a38ca283d217a49f9772eb9a29cd682c175d'
BYTES = 102677407


def prepare(output, archive=None):
    output.mkdir(parents=True, exist_ok=False)
    destination = output / 'upstream-source.zip'
    # CreateNew semantics: existing review directories/files are never overwritten or recursively cleaned.
    if archive is None:
        incoming = urllib.request.urlopen(URL, timeout=45)
    else:
        incoming = archive.open('rb')
    digest = hashlib.sha256()
    size = 0
    with incoming, destination.open('xb') as target:
        while chunk := incoming.read(1024 * 1024):
            size += len(chunk)
            if size > BYTES:
                raise ValueError('Source download exceeds its pinned size; retained for review.')
            target.write(chunk)
            digest.update(chunk)
    if size != BYTES or digest.hexdigest() != SHA256:
        raise ValueError('Source archive does not match the reviewed upstream snapshot; no extraction performed.')
    with zipfile.ZipFile(destination) as source:
        entries = source.infolist()
        if len(entries) > 4096 or sum(item.file_size for item in entries) > 256 * 1024 * 1024:
            raise ValueError('Source archive exceeds extraction bounds.')
        seen = set()
        for item in entries:
            name = PurePosixPath(item.filename)
            key = item.filename.rstrip('/').casefold()
            if (name.is_absolute() or name.parts[0] != PREFIX or '..' in name.parts or
                    '\\' in item.filename or ':' in item.filename or key in seen or
                    stat.S_ISLNK(item.external_attr >> 16) or item.file_size > 32 * 1024 * 1024):
                raise ValueError('Unsupported/duplicate source archive entry.')
            seen.add(key)
        inventory = []
        for item in entries:
            target = output.joinpath(*PurePosixPath(item.filename).parts)
            if not target.resolve().is_relative_to(output.resolve()):
                raise ValueError('Source entry escapes the new review directory.')
            if item.is_dir():
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                with source.open(item) as incoming, target.open('xb') as result:
                    data = incoming.read(item.file_size + 1)
                    if len(data) != item.file_size:
                        raise ValueError('Source entry length differs.')
                    result.write(data)
                inventory.append({'path': item.filename, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()})
    inventory.sort(key=lambda item: item['path'])
    receipt = {'commit': COMMIT, 'url': URL, 'archiveBytes': size, 'archiveSha256': SHA256,
               'files': len(inventory), 'inventory': inventory, 'execution': 'not-run: source preparation only'}
    (output / 'source-receipt.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({key: value for key, value in receipt.items() if key != 'inventory'}, indent=2))
    return output / PREFIX


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path, help='New review directory; existing paths are refused')
    parser.add_argument('--archive', type=Path, help='Optional already-downloaded archive; same exact hash is required')
    arguments = parser.parse_args()
    prepare(arguments.output.absolute(), arguments.archive)
