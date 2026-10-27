"""Local research harness. Reads game files; writes only to a fresh output folder.

Not an installer or a declaration of in-game compatibility.
"""
import argparse
import collections
import hashlib
import json
import pathlib
import shutil
import struct
import subprocess


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def index(path):
    length = path.stat().st_size
    with path.open('rb') as stream:
        def number(fmt):
            return struct.unpack(fmt, stream.read(struct.calcsize(fmt)))[0]

        def text():
            size = number('>q')
            if not 0 <= size <= 32768:
                raise ValueError('Invalid GFS string')
            return stream.read(size).decode('utf8')

        offset = number('>i')
        data_start = offset
        if (text(), text()) != ('Reverge Package File', '1.1'):
            raise ValueError('Unsupported GFS header')
        count = number('>q')
        if not 0 <= count <= 1000000 or not 0 <= offset <= length:
            raise ValueError('Invalid GFS table')
        entries, names = [], set()
        for _ in range(count):
            name, size, alignment = text(), number('>q'), number('>i')
            parts = pathlib.PurePosixPath(name.replace('\\', '/'))
            if (parts.is_absolute() or '..' in parts.parts or ':' in name
                    or name.casefold() in names or not name):
                raise ValueError('Invalid GFS path')
            if size < 0 or not 1 <= alignment <= 1048576:
                raise ValueError('Invalid GFS entry')
            names.add(name.casefold())
            offset += -offset % alignment
            if offset + size > length:
                raise ValueError('Truncated GFS payload')
            entries.append(dict(path=name, size=size, alignment=alignment, offset=offset))
            offset += size
        if stream.tell() > data_start:
            raise ValueError('Overlapping GFS table')
        return entries


def payload(archive, entry):
    with archive.open('rb') as stream:
        stream.seek(entry['offset'])
        data = stream.read(entry['size'])
        if len(data) != entry['size']:
            raise ValueError('Truncated payload')
        return data


def wav_info(data):
    if data[:4] != b'RIFF' or data[8:12] != b'WAVE':
        raise ValueError('Not RIFF WAVE')
    if struct.unpack_from('<I', data, 4)[0] + 8 != len(data):
        raise ValueError('RIFF length mismatch')
    info, chunks, offset = {}, [], 12
    while offset + 8 <= len(data):
        kind, size = struct.unpack_from('<4sI', data, offset)
        start = offset + 8
        if start + size > len(data):
            raise ValueError('Truncated RIFF chunk')
        chunk = data[start:start + size]
        chunks.append(dict(id=kind.decode('ascii', errors='replace'), size=size))
        if kind == b'fmt ':
            fields = struct.unpack_from('<HHIIHH', chunk)
            info.update(zip(('format_tag', 'channels', 'sample_rate', 'bytes_per_second',
                             'block_align', 'bits_per_sample'), fields))
            if fields[0] == 2:
                info['samples_per_block'] = struct.unpack_from('<H', chunk, 18)[0]
                info['coefficients'] = chunk[20:].hex()
        elif kind == b'fact':
            info['fact_samples'] = struct.unpack_from('<I', chunk)[0]
        elif kind == b'smpl':
            fields = struct.unpack_from('<9I', chunk)
            info['sampler'] = dict(zip(('manufacturer', 'product', 'sample_period', 'midi_unity_note',
                                      'midi_pitch_fraction', 'smpte_format', 'smpte_offset',
                                      'loop_count', 'sampler_data_bytes'), fields))
            info['sampler']['loops'] = [dict(zip(('cue_id', 'type', 'start', 'end', 'fraction', 'play_count'),
                struct.unpack_from('<6I', chunk, 36 + i * 24))) for i in range(fields[7])]
        elif kind == b'data':
            info['data_bytes'] = size
        offset = start + size + size % 2
    info['chunks'] = chunks
    if info.get('format_tag') == 2:
        info['block_count'] = info['data_bytes'] // info['block_align']
        info['data_remainder'] = info['data_bytes'] % info['block_align']
        info['block_samples'] = info['block_count'] * info['samples_per_block']
        info['block_duration_seconds'] = info['block_samples'] / info['sample_rate']
    return info


def run(*args):
    result = subprocess.run(args, capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError(f'{args[0]} failed: {result.stderr[-2000:]}')
    return result.stdout


def repack(source, destination, replacements):
    """Preserve original table/padding and rewrite sizes and aligned payloads."""
    entries = index(source)
    if not set(replacements).issubset({e['path'] for e in entries}):
        raise ValueError('Replacement target missing')
    with source.open('rb') as src, destination.open('xb') as dst:
        header = bytearray(src.read(entries[0]['offset']))
        # Locate table after identifier and version.
        pos = 4
        for _ in range(2):
            size = struct.unpack_from('>q', header, pos)[0]
            pos += 8 + size
        pos += 8
        for entry in entries:
            size = struct.unpack_from('>q', header, pos)[0]
            pos += 8 + size
            if entry['path'] in replacements:
                struct.pack_into('>q', header, pos, replacements[entry['path']].stat().st_size)
            pos += 12
        dst.write(header)
        for entry in entries:
            dst.write(b'\0' * (-dst.tell() % entry['alignment']))
            if entry['path'] in replacements:
                with replacements[entry['path']].open('rb') as changed:
                    shutil.copyfileobj(changed, dst)
            else:
                dst.write(payload(source, entry))
    after = index(destination)
    if [(e['path'], e['alignment']) for e in entries] != [(e['path'], e['alignment']) for e in after]:
        raise ValueError('Archive table changed unexpectedly')
    for before, built in zip(entries, after):
        expected = (replacements[before['path']].read_bytes() if before['path'] in replacements
                    else payload(source, before))
        if hashlib.sha256(expected).digest() != hashlib.sha256(payload(destination, built)).digest():
            raise ValueError(f'Payload mismatch: {before["path"]}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=pathlib.Path, required=True)
    parser.add_argument('--output', type=pathlib.Path, required=True)
    args = parser.parse_args()
    game, out = args.game.resolve(), args.output.resolve()
    if out == game or game in out.parents:
        parser.error('Output must be outside the game installation')
    out.mkdir(parents=True, exist_ok=False)
    loader = game / 'CoreMessaging.dll'
    loader_hash = sha(loader) if loader.exists() else None
    recognized = loader_hash == '014bfb8c48fa7afbcd585a34d814011b96b289c742482a01376f56aafa1e11d3'
    active = 'data02' if recognized and (game / 'data02').is_dir() else 'data01'
    report = dict(game=str(game), archive_folder=active, loader_sha256=loader_hash,
                  loader_rule_source='Sibling Mod Manager Core/ArchiveCompatibility.cs and Core/Manager.cs',
                  runtime_archive_selection_verified=False, archives={}, assets=[], tracks=[],
                  ffmpeg_version=run('ffmpeg', '-version').splitlines()[:3], in_game_verified=False)
    for folder in ('data01', 'data02'):
        for name in ('music-win.gfs', 'core.gfs'):
            path = game / folder / name
            if path.exists():
                report['archives'][f'{folder}/{name}'] = dict(sha256=sha(path), bytes=path.stat().st_size)
    archive, core = game / active / 'music-win.gfs', game / active / 'core.gfs'
    originals = out / 'originals'
    originals.mkdir()
    for entry in index(core):
        if entry['path'] in ('core/music.ini', 'core/stages.ini'):
            data = payload(core, entry)
            (out / pathlib.PurePosixPath(entry['path']).name).write_bytes(data)
    track = None
    for line in (out / 'music.ini').read_text().splitlines():
        key, sep, value = line.strip().partition(':')
        if not sep or key.startswith('#'):
            continue
        if key == 'Track':
            track = dict(name=value.strip(), stages=[])
            report['tracks'].append(track)
        elif key == 'Stage' and track is not None:
            track['stages'] = [s.strip() for s in value.split(',')]
        elif key == 'Files' and track is not None:
            track['files'] = ['music/' + s.strip() + '.wav' for s in value.split(',')]
    for entry in index(archive):
        data = payload(archive, entry)
        info = wav_info(data)
        path = originals / pathlib.PurePosixPath(entry['path']).name
        path.write_bytes(data)
        probe = json.loads(run('ffprobe', '-v', 'error', '-show_streams', '-of', 'json', str(path)))
        run('ffmpeg', '-v', 'error', '-xerror', '-i', str(path), '-f', 'null', '-')
        report['assets'].append(dict(**entry, sha256=hashlib.sha256(data).hexdigest(), wav=info,
                                     probe=probe, full_decode_passed=True))
    names = {a['path'].casefold() for a in report['assets']}
    report['missing_config_assets'] = [p for t in report['tracks'] for p in t.get('files', []) if p.casefold() not in names]
    report['format_groups'] = [dict(format=list(k), count=v) for k, v in collections.Counter(
        (a['wav']['format_tag'], a['wav']['channels'], a['wav']['sample_rate'], a['wav']['block_align'])
        for a in report['assets']).items()]
    print(f'Decoded {len(report["assets"])} assets; {len(report["tracks"])} configured tracks', flush=True)
    repack(archive, out / 'noop-music-win.gfs', {})
    report['noop_byte_identical'] = sha(archive) == sha(out / 'noop-music-win.gfs')
    generated = out / 'generated'
    generated.mkdir()
    replacements, conversions = {}, []
    # Attenuate the existing menu pair: representative structure, no invented track mapping.
    for name in ('menu_intro.wav', 'menu_loop.wav'):
        target = next(a for a in report['assets'] if a['path'] == 'music/' + name)
        info = target['wav']
        dest = generated / name
        command = ['ffmpeg', '-v', 'error', '-xerror', '-i', str(originals / name), '-map_metadata', '-1',
                   '-af', 'volume=0.25', '-ar', str(info['sample_rate']), '-ac', str(info['channels']),
                   '-c:a', 'adpcm_ms', '-block_size']
        native_error = None
        try:
            run(*command, str(info['block_align']), str(dest))
        except RuntimeError as error:
            native_error = str(error)
            # FFmpeg 6 requires a power of two; keep this candidate explicitly unverified.
            # Remove only the failed output created inside this fresh research directory.
            if dest.exists():
                dest.unlink()
            run(*command, '128', str(dest))
        converted = wav_info(dest.read_bytes())
        run('ffmpeg', '-v', 'error', '-xerror', '-i', str(dest), '-f', 'null', '-')
        for field in ('format_tag', 'channels', 'sample_rate', 'coefficients'):
            if converted[field] != info[field]:
                raise ValueError(f'Converted {field} differs')
        conversions.append(dict(path=target['path'], original=info, converted=converted,
                                sample_delta=converted['block_samples'] - info['block_samples'],
                                native_encode_error=native_error,
                                native_block_layout_matched=converted['block_align'] == info['block_align'],
                                full_decode_passed=True, in_game_verified=False))
        replacements[target['path']] = dest
    repack(archive, out / 'replacement-music-win.gfs', replacements)
    report['conversions'] = conversions
    report['repack_verified_payloads'] = len(report['assets'])
    # These fingerprints also catch unexpected writes during research.
    report['game_archives_unchanged'] = all(sha(game / p) == v['sha256'] for p, v in report['archives'].items())
    (out / 'inventory.json').write_text(json.dumps(report, indent=2), encoding='utf8')
    print(json.dumps({k: report[k] for k in ('format_groups', 'noop_byte_identical',
          'missing_config_assets', 'repack_verified_payloads', 'game_archives_unchanged')}, indent=2))


if __name__ == '__main__':
    main()
