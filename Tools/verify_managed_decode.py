"""Compare saved managed decoder PCM hashes with an independent FFmpeg decode."""
import hashlib
import json
import pathlib
import subprocess
import sys

results = pathlib.Path(sys.argv[1])
originals = pathlib.Path(sys.argv[2])
assets = json.loads(results.read_text(encoding='utf-8'))['assets']
mismatches = []
for i, asset in enumerate(assets, 1):
    source = originals / asset.get('sourceFile', pathlib.PurePosixPath(asset['Path']).name)
    decoded = subprocess.run(['ffmpeg', '-v', 'error', '-xerror', '-i', str(source),
                              '-f', 's16le', '-'], capture_output=True, check=True).stdout
    digest = hashlib.sha256(decoded).hexdigest()
    if digest.upper() != asset['pcmSha256'] or len(decoded) != asset['pcmBytes']:
        mismatches.append(asset['Path'])
    if i % 25 == 0:
        print(f'Compared {i}/{len(assets)} assets', flush=True)
report = dict(compared=len(assets), exact_match_count=len(assets) - len(mismatches), mismatches=mismatches)
results.with_name('ffmpeg-comparison.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report))
if mismatches:
    sys.exit(1)
