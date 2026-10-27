"""Experimental Windows ACM encoding using a reference WAV's exact ADPCM format.

Requires Windows and FFmpeg on PATH. Writes a new output; never edits the reference.
"""
import ctypes
import pathlib
import struct
import subprocess
import sys
from ctypes import wintypes


class StreamHeader(ctypes.Structure):
    _fields_ = [('cbStruct', wintypes.DWORD), ('fdwStatus', wintypes.DWORD),
                ('dwUser', ctypes.c_size_t), ('pbSrc', ctypes.c_void_p),
                ('cbSrcLength', wintypes.DWORD), ('cbSrcLengthUsed', wintypes.DWORD),
                ('dwSrcUser', ctypes.c_size_t), ('pbDst', ctypes.c_void_p),
                ('cbDstLength', wintypes.DWORD), ('cbDstLengthUsed', wintypes.DWORD),
                ('dwDstUser', ctypes.c_size_t), ('dwReservedDriver', ctypes.c_size_t * 10)]


def encode(source, reference, destination, gain=0.25):
    raw = reference.read_bytes()
    position, fmt = 12, None
    while position + 8 <= len(raw):
        kind, size = struct.unpack_from('<4sI', raw, position)
        if kind == b'fmt ':
            fmt = raw[position + 8:position + 8 + size]
            break
        position += 8 + size + size % 2
    if fmt is None or len(fmt) < 22:
        raise ValueError('Missing ADPCM format')
    tag, channels, rate, _, block, _ = struct.unpack_from('<HHIIHH', fmt)
    if tag != 2 or channels != 2 or block != 140:
        raise ValueError('Research encoder is limited to observed stereo 140-byte MS ADPCM')
    pcm = subprocess.run(['ffmpeg', '-v', 'error', '-xerror', '-i', str(source),
                          '-af', f'volume={gain}', '-ar', str(rate), '-ac', str(channels),
                          '-f', 's16le', '-'], capture_output=True, check=True).stdout
    samples_per_block = struct.unpack_from('<H', fmt, 18)[0]
    frame_bytes = channels * 2
    # Explicit zero padding, recorded by callers through the resulting sample count.
    pcm += b'\0' * (-len(pcm) % (samples_per_block * frame_bytes))
    if len(pcm) > 0x7fffffff:
        raise ValueError('Research encoder input exceeds 2 GiB')
    acm = ctypes.WinDLL('msacm32')
    handle = ctypes.c_void_p()
    ptr, uint, uptr = ctypes.c_void_p, wintypes.UINT, ctypes.c_size_t
    acm.acmStreamOpen.argtypes = [ctypes.POINTER(ptr), ptr, ptr, ptr, ptr, uptr, uptr, uint]
    acm.acmStreamSize.argtypes = [ptr, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD), uint]
    for name in ('acmStreamPrepareHeader', 'acmStreamConvert', 'acmStreamUnprepareHeader'):
        getattr(acm, name).argtypes = [ptr, ctypes.POINTER(StreamHeader), uint]
    acm.acmStreamClose.argtypes = [ptr, uint]

    def check(result):
        if result:
            raise RuntimeError(f'Windows ACM error {result}')

    source_fmt = ctypes.create_string_buffer(struct.pack('<HHIIHHH', 1, channels, rate,
                                                       rate * frame_bytes, frame_bytes, 16, 0))
    target_fmt = ctypes.create_string_buffer(fmt)
    check(acm.acmStreamOpen(ctypes.byref(handle), None, source_fmt, target_fmt, None, 0, 0, 4))
    try:
        capacity = wintypes.DWORD()
        check(acm.acmStreamSize(handle, len(pcm), ctypes.byref(capacity), 0))
        src_buffer = ctypes.create_string_buffer(pcm)
        dst_buffer = ctypes.create_string_buffer(capacity.value)
        header = StreamHeader()
        header.cbStruct = ctypes.sizeof(header)
        header.pbSrc, header.cbSrcLength = ctypes.addressof(src_buffer), len(pcm)
        header.pbDst, header.cbDstLength = ctypes.addressof(dst_buffer), capacity.value
        check(acm.acmStreamPrepareHeader(handle, ctypes.byref(header), 0))
        try:
            check(acm.acmStreamConvert(handle, ctypes.byref(header), 4 | 16 | 32))
            if header.cbSrcLengthUsed != len(pcm):
                raise RuntimeError('ACM did not consume all input')
            encoded = dst_buffer.raw[:header.cbDstLengthUsed]
        finally:
            check(acm.acmStreamUnprepareHeader(handle, ctypes.byref(header), 0))
    finally:
        check(acm.acmStreamClose(handle, 0))
    if len(encoded) != len(pcm) // (samples_per_block * frame_bytes) * block:
        raise RuntimeError('Unexpected encoded block count')
    # This diagnostic is deliberately fixed-length: preserve sampler metadata and
    # every other chunk verbatim. General editing needs an explicit metadata policy.
    position, data_position, data_length = 12, None, None
    while position + 8 <= len(raw):
        kind, size = struct.unpack_from('<4sI', raw, position)
        if kind == b'data':
            data_position, data_length = position + 8, size
            break
        position += 8 + size + size % 2
    if data_position is None or data_length != len(encoded):
        raise ValueError('Fixed-length research conversion cannot change the reference block count')
    result = bytearray(raw)
    result[data_position:data_position + data_length] = encoded
    with destination.open('xb') as output:
        output.write(result)


if __name__ == '__main__':
    if len(sys.argv) != 4:
        sys.exit('Usage: acm_encode.py SOURCE REFERENCE NEW_OUTPUT')
    encode(*(pathlib.Path(p) for p in sys.argv[1:]))
