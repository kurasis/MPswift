#!/usr/bin/env python3
"""Generate CC0 long-duration fixtures in a NEW owned directory using pinned tools.
Requires the owned PCM generator, FFmpeg 7.1.5 and the Monkey's Audio port pinned
in docs/EXTENDED_FIXTURES.md. No encoder is an application dependency.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import wave

ROOT = Path(__file__).resolve().parents[1]

def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1048576), b''):
            digest.update(chunk)
    return digest.hexdigest()

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    parser.add_argument('--mac', required=True)
    args = parser.parse_args()
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=False)
    source = output / 'owned-source.wav'
    subprocess.run(['dotnet', str(ROOT / 'tools/Player.AudioSmoke/bin/Release/net10.0/Player.AudioSmoke.dll'), '--generate-fixture', str(source)], check=True)
    expected = json.loads((ROOT / 'tests/fixtures/audio/manifest.json').read_text())['sourceSha256']
    if sha(source) != expected:
        raise RuntimeError('Owned source PCM checksum changed.')
    duration = 7201; rate = 48000; channels = 2
    with wave.open(str(source), 'rb') as source_wave:
        marker = source_wave.readframes(rate)
    sparse = output / 'owned-long-source.wav'
    size = duration * rate * channels * 2
    with sparse.open('xb') as stream:
        stream.write(struct.pack('<4sI4s4sIHHIIHH4sI', b'RIFF', size + 36, b'WAVE', b'fmt ', 16, 1, channels, rate, rate * channels * 2, channels * 2, 16, b'data', size))
        stream.truncate(size + 44)
        for second in (0, 3600, 7199):
            stream.seek(44 + second * rate * channels * 2); stream.write(marker)
    subprocess.run([args.mac, str(sparse), str(output / 'long-markers.ape'), '-c2000'], check=True)
    version = subprocess.check_output(['ffmpeg', '-version'], text=True).splitlines()[0]
    arguments = ['-v','error','-nostdin','-n','-stream_loop','-1','-i',str(source),'-t',str(duration),'-af','pan=mono|c0=c0','-c:a','aac','-b:a','16k','-ar','48000','-fflags','+bitexact','-flags:a','+bitexact','-metadata','title=Owned two-hour audiobook','-metadata','artist=MPswift tests','-f','ipod',str(output / 'long-audiobook.m4b')]
    subprocess.run(['ffmpeg', *arguments], check=True)
    facts = json.loads(subprocess.check_output(['ffprobe','-v','error','-show_streams','-show_format','-of','json',str(output/'long-audiobook.m4b')]))
    if facts['streams'][0]['profile'] != 'LC' or abs(float(facts['format']['duration']) - duration) > .1:
        raise RuntimeError('Long audiobook independent profile/duration differs.')
    fixtures = []
    for filename, codec, ch, mode in [('long-markers.ape','APE',2,'markers'),('long-audiobook.m4b','MP4',1,'continuous')]:
        fixtures.append(dict(path=filename, sha256=sha(output/filename), codec=codec, sampleRate=rate, channels=ch, decodedChannels=2,
                             durationSeconds=duration, signal=mode, markerSeconds=[0,3600,7199] if mode == 'markers' else [],
                             expectedPeakMinimum=.04, expectedPeakMaximum=.2, license='CC0-1.0'))
    manifest = dict(schemaVersion=1, sourceSha256=expected, durationSeconds=duration, fixtures=fixtures,
                    generator='Generate-LongFixtures.py', apeEncoder='Monkey\'s Audio 3.99.6 port at bac9b6bb4714a6de85a990a6916d1abc4c171271; assembly disabled; normal',
                    apeDecodedFloatBytes=size*2, sparsePcmInputBytes=size+44, m4bEncoder=version,
                    m4bArguments=arguments[arguments.index('-t'): -1], independentM4bProfile=facts['streams'][0]['profile'],
                    independentM4bDuration=float(facts['format']['duration']), provenance='Owned PCM only; APE sparse silent source with one-second markers; M4B repeated left channel avoids opposite-phase mono cancellation')
    (output/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
    source.unlink(); Path(str(source) + '.json').unlink(); sparse.unlink()

if __name__ == '__main__':
    main()
