#!/usr/bin/env python3
"""Read-only pattern audit of every local Git blob and nonignored working file; no matched values printed."""
import argparse
import datetime
import json
from pathlib import Path
import re
import subprocess
import sys

FAMILIES = {
    'github-classic': (rb'\bgh[pousr]_[A-Za-z0-9]{36}\b', b'ghp_' + b'A' * 36),
    'github-fine-grained': (rb'\bgithub_pat_[A-Za-z0-9_]{82}\b', b'github_pat_' + b'A' * 82),
    'aws-access-key-id': (rb'\b(?:AKIA|ASIA)[A-Z0-9]{16}\b', b'AKIA' + b'A' * 16),
    'slack-token': (rb'\bxox[baprs]-[A-Za-z0-9-]{20,200}\b', b'xoxb-' + b'A' * 30),
    'google-api-key': (rb'\bAIza[A-Za-z0-9_-]{35}\b', b'AIza' + b'A' * 35),
    'private-key-pem': (rb'-----BEGIN (?:RSA |EC |DSA |OPENSSH )?PRIVATE KEY-----', b'-----BEGIN ' + b'PRIVATE KEY-----'),
}
PATTERNS = {name: re.compile(rule) for name, (rule, _) in FAMILIES.items()}


def safe_origin(origin):
    encoded = origin.encode('utf-8', 'surrogateescape')
    for name, pattern in PATTERNS.items():
        encoded = pattern.sub(('<redacted-' + name + '>').encode(), encoded)
    return encoded.decode('utf-8', 'surrogateescape')


def scan(stream, size, origin):
    remaining, previous, seen = size, b'', set()
    while remaining:
        chunk = stream.read(min(remaining, 1024 * 1024))
        if not chunk:
            raise IOError('Audit input ended before its declared length.')
        remaining -= len(chunk)
        for name, pattern in PATTERNS.items():
            if pattern.search(previous + chunk):
                seen.add(name)
        previous = chunk[-256:]
    return [{'origin': safe_origin(origin), 'family': name} for name in sorted(seen)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, help='Optional new JSON receipt; existing paths are refused')
    arguments = parser.parse_args()
    root = Path(subprocess.check_output(['git', 'rev-parse', '--show-toplevel']).decode().strip()).resolve()
    if Path.cwd().resolve() != root:
        sys.exit('Run this audit from the repository root.')
    for name, (_, sample) in FAMILIES.items():
        if not PATTERNS[name].search(sample):
            raise RuntimeError('A secret-pattern positive control failed.')
    listing = subprocess.check_output(['git', 'cat-file', '--batch-all-objects', '--batch-check=%(objectname) %(objecttype) %(objectsize)']).decode()
    objects = [(sha, int(size)) for sha, kind, size in (line.split() for line in listing.splitlines()) if kind == 'blob']
    findings, total = [], 0
    with subprocess.Popen(['git', 'cat-file', '--batch'], stdin=subprocess.PIPE, stdout=subprocess.PIPE) as process:
        for sha, size in objects:
            process.stdin.write((sha + '\n').encode())
            process.stdin.flush()
            header = process.stdout.readline().split()
            if len(header) != 3 or header[1] != b'blob' or int(header[2]) != size:
                raise IOError('Git returned an unexpected blob header.')
            findings.extend(scan(process.stdout, size, 'git-blob:' + sha))
            if process.stdout.read(1) != b'\n':
                raise IOError('Git returned an unexpected blob terminator.')
            total += size
        process.stdin.close()
        if process.wait() != 0:
            raise RuntimeError('Git blob reading failed.')
    files = subprocess.check_output(['git', 'ls-files', '-z']).split(b'\0')
    files += subprocess.check_output(['git', 'ls-files', '--others', '--exclude-standard', '-z']).split(b'\0')
    files = sorted({path.decode('utf-8', 'surrogateescape') for path in files if path})
    working_bytes, skipped = 0, []
    for name in files:
        path = Path(name)
        if not path.is_file() or path.is_symlink() or not path.resolve().is_relative_to(root):
            skipped.append(safe_origin(name))
            continue
        with path.open('rb') as stream:
            size = path.stat().st_size
            findings.extend(scan(stream, size, 'working-file:' + name))
            working_bytes += size
    head = subprocess.run(['git', 'rev-parse', '--verify', '--quiet', 'HEAD'], capture_output=True, text=True)
    receipt = {
        'checkedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'baselineCommit': head.stdout.strip() if head.returncode == 0 else None,
        'shallowRepository': subprocess.check_output(['git', 'rev-parse', '--is-shallow-repository']).strip() == b'true',
        'scope': 'All locally present Git blobs including unreachable/unpushed and binary bytes, plus current nonignored working files. Bounded reads overlap for cross-boundary matches; no matched values printed.',
        'gitObjects': len(listing.splitlines()), 'gitBlobs': len(objects), 'gitBlobBytes': total,
        'workingFiles': len(files), 'workingBytes': working_bytes, 'families': list(FAMILIES),
        'positiveControlsPassed': len(FAMILIES), 'findings': findings, 'skippedWorkingPaths': skipped,
        'limitations': 'Not remote-only/unavailable server objects, arbitrary/custom/obfuscated secrets, rotation or GitHub settings. A pattern match requires review; absence is not complete secrecy proof.',
    }
    if arguments.output:
        with arguments.output.open('x', encoding='utf-8') as output:
            json.dump(receipt, output, indent=2)
            output.write('\n')
    print(json.dumps(receipt, indent=2))
    return 1 if findings else 0


if __name__ == '__main__':
    sys.exit(main())
