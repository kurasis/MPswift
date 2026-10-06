#!/usr/bin/env python3
"""Create new CC0 development fixtures from the existing owned PCM generator.

Requires FFmpeg, fdkaac 1.0.6/libfdk-aac 2.0.3, mac 3.99.6,
mpcenc 1.30.1 (SV8) and wavpack 5.8.1. Encoders are development tools only.
See docs/EXTENDED_FIXTURES.md for source archive pins and build instructions.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    for name in ('fdkaac', 'mac', 'mpcenc', 'wavpack'):
        parser.add_argument('--' + name, required=True)
    args = parser.parse_args()
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=False)
    source = output / 'source.wav'
    subprocess.run(['dotnet', str(ROOT / 'tools/Player.AudioSmoke/bin/Release/net10.0/Player.AudioSmoke.dll'), '--generate-fixture', str(source)], check=True)
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    expected = json.loads((ROOT / 'tests/fixtures/audio/manifest.json').read_text())['sourceSha256']
    if source_hash != expected:
        raise RuntimeError('Owned PCM source changed.')
    fixtures = []
    def encode(name, profile, codec, command, rate=48000, channels=2, **facts):
        subprocess.run(command, check=True)
        fixtures.append(dict(path=name, profile=profile, expectedBassCodec=codec,
                             sampleRate=rate, channels=channels, sourceDurationSeconds=3,
                             durationToleranceSeconds=0.2, sha256=hashlib.sha256((output/name).read_bytes()).hexdigest(),
                             license='CC0-1.0', provenance='Owned 440 Hz opposite-phase PCM; pinned development encoders',
                             generator='Generate-ExtendedFixtures.py', **facts))
    for name, profile, aot, bitrate, transport in (
            ('he-aac.m4a', 'HE-AAC SBR MP4', 5, 64000, 0),
            ('he-aac-v2.m4a', 'HE-AAC v2 SBR+PS MP4', 29, 32000, 0)):
        encode(name, profile, 'AAC' if transport == 2 else 'MP4',
               [args.fdkaac, '-S', '--no-timestamp', '-p', str(aot), '-b', str(bitrate), '-f', str(transport), '-o', str(output/name), str(source)])
        facts = json.loads(subprocess.check_output(['ffprobe', '-v', 'error', '-show_streams', '-of', 'json', str(output/name)]))['streams'][0]
        if facts['profile'] != ('HE-AAC' if aot == 5 else 'HE-AACv2'):
            raise RuntimeError('Encoder did not produce the requested HE-AAC profile.')
        fixtures[-1]['independentCodecProfile'] = facts['profile']
    encode('monkey.ape', "Monkey's Audio normal", 'APE', [args.mac, str(source), str(output/'monkey.ape'), '-c2000'], losslessReference='source-pcm16.wav')
    encode('musepack.mpc', 'Musepack SV8 standard', 'MPC', [args.mpcenc, '--silent', str(source), str(output/'musepack.mpc')])
    encode('hybrid-corrected.wv', 'WavPack hybrid with correction', 'WV', [args.wavpack, '-q', '-b3', '-c', str(source), '-o', str(output/'hybrid-corrected.wv')], losslessReference='source-pcm16.wav')
    fixtures[-1]['correction'] = dict(path='hybrid-corrected.wvc', sha256=hashlib.sha256((output/'hybrid-corrected.wvc').read_bytes()).hexdigest())
    shutil.copyfile(output/'hybrid-corrected.wv', output/'hybrid-lossy.wv')
    fixtures.append({**fixtures[-1], 'path':'hybrid-lossy.wv', 'profile':'WavPack hybrid without correction'})
    fixtures[-1].pop('correction'); fixtures[-1].pop('losslessReference')
    for name, profile, rate, channels, codec, bass, extra in (
            ('pcm24-192k.wav', 'PCM24 192 kHz stereo', 192000, 2, 'pcm_s24le', 'WavePCM', []),
            ('flac24-96k-6ch.flac', 'FLAC24 96 kHz 5.1', 96000, 6, 'flac', 'FLAC', ['-sample_fmt','s32']),
            ('pcm24-96k-8ch.wav', 'PCM24 96 kHz 7.1', 96000, 8, 'pcm_s24le', 'WavePCM', [])):
        encode(name, profile, bass, ['ffmpeg','-v','error','-nostdin','-n','-i',str(source),'-ar',str(rate),'-ac',str(channels),'-c:a',codec,*extra,str(output/name)], rate, channels, expectedBitDepth=24)
    shutil.copyfile(source, output/'source-pcm16.wav')
    (output/'manifest.json').write_text(json.dumps(dict(schemaVersion=1, sourceSha256=source_hash, fixtures=fixtures), indent=2)+'\n')
    source.unlink(); source.with_suffix('.wav.json').unlink()
    print(f'Generated {len(fixtures)} extended profiles.')

if __name__ == '__main__':
    main()
