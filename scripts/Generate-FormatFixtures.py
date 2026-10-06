#!/usr/bin/env python3
"""Regenerate development-only CC0 fixtures from the project's owned PCM signal.

Requires FFmpeg (encoding only) and a built Player.AudioSmoke. Container encoder
versions and Ogg serials can change hashes; the output manifest records fresh hashes.
Writes only to a new output directory, never over existing source media.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import importlib.util

ROOT = Path(__file__).resolve().parents[1]

def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    source = output / 'generated-source.wav'
    tool = ROOT / 'tools/Player.AudioSmoke/bin/Release/net10.0/Player.AudioSmoke.dll'
    subprocess.run(['dotnet', str(tool), '--generate-fixture', str(source)], check=True)
    baseline = json.loads((ROOT / 'tests/fixtures/audio/manifest.json').read_text())
    if sha256(source) != baseline['sourceSha256']:
        raise RuntimeError('Owned source signal checksum changed; review generator before encoding.')
    version = subprocess.run(['ffmpeg', '-version'], check=True, capture_output=True, text=True).stdout.splitlines()[0]
    for item in baseline['fixtures']:
        if item.get('generator') == 'dsd64-first-order-v1':
            spec = importlib.util.spec_from_file_location('dsd_fixture', ROOT / 'scripts/Generate-DsdFixtures.py')
            module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
            module.generate(output / item['path'])
        else:
            subprocess.run(['ffmpeg', '-v', 'error', '-nostdin', '-n', '-i', str(source),
                            *item['encoderArguments'], '-metadata', 'title=Fixture — Музыка',
                            '-metadata', 'artist=Local Audio Player tests', '-metadata', 'album=Generated signals',
                            str(output / item['path'])], check=True)
        item['sha256'] = sha256(output / item['path'])
        item['provenance'] = ('Owned continuous 440 Hz opposite-phase sine, first-order DSD64 generator v1' if item.get('generator') else 'Generated from Player.AudioSmoke 440 Hz opposite-phase PCM fixture using ' + version)
    (output / 'manifest.json').write_text(json.dumps(baseline, indent=2, ensure_ascii=False) + '\n')
    source.unlink()
    source.with_name(source.name + '.json').unlink()
    print(f'Created {len(baseline["fixtures"])} owned format fixtures in {output}')

if __name__ == '__main__':
    main()
