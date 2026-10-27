"""Finish native-block research against an existing research_music.py output."""
import array
import json
import math
import pathlib
import sys
import zipfile
import subprocess
from acm_encode import encode
from research_music import index, repack, sha, wav_info


def levels(path):
    data = subprocess.run(['ffmpeg', '-v', 'error', '-xerror', '-i', str(path),
                           '-f', 's16le', '-'], capture_output=True, check=True).stdout
    samples = array.array('h', data)
    peak = max(abs(v) for v in samples)
    rms = math.sqrt(sum(v * v for v in samples) / len(samples))
    return dict(interleaved_samples=len(samples), peak=peak, rms=rms,
                full_scale_samples=sum(v in (-32768, 32767) for v in samples))


def main():
    root = pathlib.Path(sys.argv[1]).resolve()
    report = json.loads((root / 'inventory.json').read_text())
    game = pathlib.Path(report['game'])
    archive = game / report['archive_folder'] / 'music-win.gfs'
    native = root / 'native-preserved'
    native.mkdir(exist_ok=False)
    replacements, results = {}, []
    # This relationship is explicitly documented by the installed music.ini.
    track = next(t for t in report['tracks'] if t['name'] == 'Moonlit Melee')
    for asset in track['files']:
        name = pathlib.PurePosixPath(asset).name
        source, dest = root / 'originals' / name, native / name
        encode(source, source, dest)
        original, converted = wav_info(source.read_bytes()), wav_info(dest.read_bytes())
        for key in ('format_tag', 'channels', 'sample_rate', 'block_align', 'samples_per_block',
                    'coefficients', 'block_samples', 'data_bytes', 'chunks', 'sampler'):
            if original[key] != converted[key]:
                raise ValueError(f'{asset}: {key} changed')
        before, after = levels(source), levels(dest)
        if before['interleaved_samples'] != after['interleaved_samples']:
            raise ValueError('Decoded duration changed')
        results.append(dict(path=asset, original=original, converted=converted,
                            sha256=sha(dest), original_levels=before, converted_levels=after,
                            rms_change_db=20 * math.log10(after['rms'] / before['rms']),
                            independent_ffmpeg_decode_passed=True, in_game_verified=False))
        replacements[asset] = dest
    built = root / 'native-music-win.gfs'
    repack(archive, built, replacements)
    package = root / 'Moonlit-Melee-Native-ADPCM-Test.zip'
    metadata = dict(id='local.audio-workshop.native-adpcm-research',
                    name='Audio Workshop — Moonlit Melee codec test', author='Local research',
                    desc='Experimental: existing Moonlit Melee attenuated by approximately 12 dB. '
                         'Native 140-byte MS ADPCM blocks. Playback is not yet verified. '
                         'Local game-derived test assets; do not redistribute.', version='0.1.0', url=None)
    with zipfile.ZipFile(package, 'x', zipfile.ZIP_DEFLATED) as output:
        output.writestr('mod.json', json.dumps(metadata, indent=2))
        for asset, file in replacements.items():
            output.write(file, 'music-win.gfs/' + asset)
    final = dict(encoder='Windows msacm32 ACM; exact reference WAVEFORMAT',
                 source_gain=0.25, track=track, assets=results, package=str(package),
                 rebuilt_archive=str(built), verified_payloads=len(index(archive)),
                 game_archives_unchanged=all(sha(game / p) == v['sha256'] for p, v in report['archives'].items()),
                 in_game_verified=False)
    (root / 'native-validation.json').write_text(json.dumps(final, indent=2), encoding='utf8')
    print(json.dumps(dict(track=track, results=[dict(path=r['path'], rms_change_db=r['rms_change_db'])
          for r in results], game_archives_unchanged=final['game_archives_unchanged']), indent=2))


if __name__ == '__main__':
    main()
