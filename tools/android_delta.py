#!/usr/bin/env python3
"""Generate and verify streaming Lockvyn APK deltas without third-party modules.

Format: gzip { 'LVD1', old_sha256[32], new_sha256[32],
               old_size:uint64, new_size:uint64,
               ( 'C', old_offset:uint64, len:uint32 |
                 'A', len:uint32, literal[len] )* }
All integers are network byte order. There is no executable code in the patch.
"""
import gzip
import hashlib
import os
import pathlib
import struct
import tempfile

MAX_APK = 64 * 1024 * 1024
MAGIC = b"LVD1"
MIN_MATCH = 32
INDEX_STEP = 8
WINDOW = 16


def digest(data):
    return hashlib.sha256(data).digest()


def _match_length(old, new, oi, ni):
    limit = min(len(old) - oi, len(new) - ni)
    if limit < MIN_MATCH or old[oi:oi + MIN_MATCH] != new[ni:ni + MIN_MATCH]:
        return 0
    # Compare powers of two, then binary search. Avoid a Python per-byte loop.
    lower = MIN_MATCH
    step = 64
    while lower + step <= limit and old[oi:oi + lower + step] == new[ni:ni + lower + step]:
        lower += step
        step *= 2
    upper = min(limit, lower + step)
    while lower < upper:
        middle = (lower + upper + 1) // 2
        if old[oi:oi + middle] == new[ni:ni + middle]:
            lower = middle
        else:
            upper = middle - 1
    return lower


def create(old_path, new_path, patch_path):
    old = pathlib.Path(old_path).read_bytes()
    new = pathlib.Path(new_path).read_bytes()
    if not 0 < len(old) <= MAX_APK or not 0 < len(new) <= MAX_APK:
        raise ValueError("Only nonempty APKs up to 64 MiB are supported.")
    index = {}
    for offset in range(0, len(old) - WINDOW + 1, INDEX_STEP):
        key = old[offset:offset + WINDOW]
        candidates = index.setdefault(key, [])
        if len(candidates) < 4:
            candidates.append(offset)
        else:
            # Retain the beginning and the latest three occurrences.
            candidates.pop(1)
            candidates.append(offset)
    result = pathlib.Path(patch_path)
    result.parent.mkdir(parents=True, exist_ok=True)
    literal = bytearray()

    def flush(out):
        if literal:
            out.write(b"A" + struct.pack(">I", len(literal)))
            out.write(literal)
            literal.clear()

    try:
        with result.open("wb") as raw:
            with gzip.GzipFile(fileobj=raw, mode="wb", compresslevel=9, mtime=0, filename="") as out:
                out.write(MAGIC + digest(old) + digest(new) + struct.pack(">QQ", len(old), len(new)))
                pos = 0
                while pos < len(new):
                    key = new[pos:pos + WINDOW]
                    best_length, best_offset = 0, 0
                    for offset in index.get(key, ()):
                        length = _match_length(old, new, offset, pos)
                        if length > best_length:
                            best_length, best_offset = length, offset
                    if best_length >= MIN_MATCH:
                        flush(out)
                        out.write(b"C" + struct.pack(">QI", best_offset, best_length))
                        pos += best_length
                    else:
                        literal.append(new[pos])
                        pos += 1
                flush(out)
    finally:
        literal.clear()
    return result.stat().st_size


def reconstruct(old_path, patch_path, output_path):
    old = pathlib.Path(old_path).read_bytes()
    if not 0 < len(old) <= MAX_APK:
        raise ValueError("Invalid source APK.")
    out = pathlib.Path(output_path)
    with gzip.open(patch_path, "rb") as stream:
        if stream.read(4) != MAGIC:
            raise ValueError("Invalid patch format.")
        basehash, newhash = stream.read(32), stream.read(32)
        lengths = stream.read(16)
        if len(lengths) != 16:
            raise ValueError("Truncated delta header.")
        base_size, target_size = struct.unpack(">QQ", lengths)
        if base_size != len(old) or basehash != digest(old) or not 0 < target_size <= MAX_APK:
            raise ValueError("Patch source does not match.")
        with out.open("wb") as result:
            written = 0
            while written < target_size:
                op = stream.read(1)
                if op == b"C":
                    raw = stream.read(12)
                    if len(raw) != 12:
                        raise ValueError("Truncated copy instruction.")
                    offset, length = struct.unpack(">QI", raw)
                    if length < 1 or offset > len(old) - length or length > target_size - written:
                        raise ValueError("Patch copy out of bounds.")
                    result.write(old[offset:offset + length])
                elif op == b"A":
                    raw = stream.read(4)
                    if len(raw) != 4:
                        raise ValueError("Truncated literal instruction.")
                    length = struct.unpack(">I", raw)[0]
                    if length < 1 or length > target_size - written:
                        raise ValueError("Patch literal out of bounds.")
                    data = stream.read(length)
                    if len(data) != length:
                        raise ValueError("Truncated literal.")
                    result.write(data)
                else:
                    raise ValueError("Unknown delta instruction.")
                written += length
            if stream.read(1):
                raise ValueError("Trailing delta content.")
    if digest(out.read_bytes()) != newhash:
        out.unlink(missing_ok=True)
        raise ValueError("Patch target SHA-256 mismatch.")
    return out
