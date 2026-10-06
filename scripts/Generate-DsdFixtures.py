#!/usr/bin/env python3
"""Owned CC0 440 Hz, opposite-phase DSD64 stereo fixtures; no runtime encoder dependency."""
import math
import struct
from pathlib import Path

def generate(path):
    path = Path(path)
    rate, seconds = 2822400, 3
    samples = rate * seconds
    # First-order one-bit pulse-density modulation of an owned continuous sine.
    left = bytearray(samples // 8)
    accumulator = 0.0
    angle = 2 * math.pi * 440 / rate
    for byte in range(len(left)):
        value = 0
        for bit in range(8):
            accumulator += 0.05 * math.sin(angle * (byte * 8 + bit))
            high = accumulator >= 0
            accumulator -= 1 if high else -1
            value |= int(high) << bit
        left[byte] = value
    if path.suffix == '.dsf':
        block = 4096
        payload = bytearray()
        for start in range(0, len(left), block):
            chunk = left[start:start+block]
            payload.extend(chunk + bytes([0x69]) * (block-len(chunk)))
            payload.extend(bytes(v ^ 255 for v in chunk) + bytes([0x69]) * (block-len(chunk)))
        data = b'data' + struct.pack('<Q', 12+len(payload)) + payload
        fmt = b'fmt ' + struct.pack('<QIIIIIIQII', 52, 1, 0, 2, 2, rate, 1, samples, block, 0)
        content = b'DSD ' + struct.pack('<QQQ', 28, 28+len(fmt)+len(data), 0) + fmt + data
    elif path.suffix == '.dff':
        def chunk(kind, data): return kind + struct.pack('>Q', len(data)) + data + (b'\0' if len(data) % 2 else b'')
        reverse = bytes(int(f'{v:08b}'[::-1], 2) for v in range(256))
        payload = bytearray()
        for value in left:
            msb = reverse[value]; payload.extend((msb, msb ^ 255))
        prop = b'SND ' + chunk(b'FS  ', struct.pack('>I', rate)) + chunk(b'CHNL', struct.pack('>H', 2)+b'SLFTSRGT') + chunk(b'CMPR', b'DSD '+bytes([14])+b'not compressed')
        body = b'DSD ' + chunk(b'FVER', struct.pack('>I', 0x01050000)) + chunk(b'PROP', prop) + chunk(b'DSD ', payload)
        content = b'FRM8' + struct.pack('>Q', len(body)) + body
    else: raise ValueError('Only .dsf and .dff outputs are supported')
    with path.open('xb') as output: output.write(content)

if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('path', type=Path)
    generate(parser.parse_args().path)
