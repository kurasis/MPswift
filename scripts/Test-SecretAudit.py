#!/usr/bin/env python3
"""Exercise the read-only scanner with synthetic tokens in a fresh, disposable Git repository."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile

if not __debug__:
    raise RuntimeError('Run secret controls without Python optimization.')

scanner = Path(__file__).with_name('Check-Secrets.py').resolve()
with tempfile.TemporaryDirectory(prefix='mpswift-secret-controls-') as owned:
    root = Path(owned)
    subprocess.run(['git', 'init', '--quiet', str(root)], check=True)
    def run(*arguments):
        return subprocess.run([sys.executable, str(scanner), *arguments], cwd=root, text=True, capture_output=True)
    clean = run()
    assert clean.returncode == 0 and not json.loads(clean.stdout)['findings']
    samples = [b'ghp_' + b'A' * 36, b'github_pat_' + b'A' * 82, b'AKIA' + b'A' * 16,
               b'xoxb-' + b'A' * 30, b'AIza' + b'A' * 35, b'-----BEGIN ' + b'PRIVATE KEY-----']
    # No commit/ref points at these binary blobs; tokens cross the 1 MiB scanning boundary.
    for sample in samples:
        subprocess.run(['git', 'hash-object', '-w', '--stdin'], cwd=root,
                       input=b'\x00' * (1024 * 1024 - 20) + sample + b'\x00', stdout=subprocess.DEVNULL, check=True)
    binary = run()
    proof = json.loads(binary.stdout)
    assert binary.returncode == 1 and proof['gitBlobs'] == 6 and len(proof['findings']) == 6
    assert {finding['family'] for finding in proof['findings']} == set(proof['families'])
    assert all(finding['origin'].startswith('git-blob:') for finding in proof['findings'])
    (root / 'working.txt').write_bytes(samples[0])
    working = run()
    assert working.returncode == 1 and any(f['origin'] == 'working-file:working.txt' for f in json.loads(working.stdout)['findings'])
    (root / samples[0].decode()).write_bytes(samples[0])
    named = run()
    assert named.returncode == 1 and '<redacted-github-classic>' in named.stdout and samples[0].decode() not in named.stdout
    sentinel = root / 'receipt.json'
    sentinel.write_text('owned sentinel', encoding='utf-8')
    refused = run('--output', str(sentinel))
    assert refused.returncode != 0 and sentinel.read_text(encoding='utf-8') == 'owned sentinel'
    for captured in [binary.stdout, binary.stderr, working.stdout, working.stderr, named.stdout, named.stderr, refused.stdout, refused.stderr]:
        assert all(sample.decode() not in captured for sample in samples), 'Scanner printed a matched value.'
print('Secret-pattern controls passed: clean repository, six unreachable binary cross-boundary families, working file, no overwrite, no matched values printed.')
