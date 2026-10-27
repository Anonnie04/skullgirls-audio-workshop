#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
work="$root/.build/ffmpeg-release"
out="$root/vendor/ffmpeg"
mkdir -p "$work" "$out" "$root/artifacts"
archive="$work/ffmpeg-8.1.3.tar.xz"
curl --fail --location --retry 3 https://ffmpeg.org/releases/ffmpeg-8.1.3.tar.xz -o "$archive"
echo "7138d28c96d9d3e3af4ee3d8cad72741f8ffb40da90c1112235dea3ecd3178a3  $archive" | sha256sum -c -
tar -xf "$archive" -C "$work"
cd "$work/ffmpeg-8.1.3"
./configure --target-os=mingw32 --arch=x86_64 --cross-prefix=x86_64-w64-mingw32- \
  --enable-cross-compile --disable-autodetect --disable-everything --disable-doc \
  --disable-debug --disable-network --disable-x86asm --disable-avdevice --disable-swscale \
  --disable-shared --enable-static --disable-pthreads --enable-w32threads --extra-ldflags=-static \
  --enable-ffmpeg --enable-avcodec --enable-avformat --enable-avfilter --enable-swresample \
  --enable-protocol=file,pipe --enable-demuxer=wav,mp3,flac,ogg,mov,aac,asf,aiff \
  --enable-decoder=pcm_s8,pcm_u8,pcm_s16le,pcm_s24le,pcm_s32le,pcm_f32le,pcm_f64le,pcm_s16be,pcm_s24be,pcm_s32be,pcm_f32be,pcm_f64be,adpcm_ms,mp3,mp3float,flac,vorbis,opus,aac,wmav1,wmav2,wmapro,wmalossless,alac \
  --enable-parser=aac,aac_latm,flac,mpegaudio,opus,vorbis \
  --enable-muxer=s16le --enable-encoder=pcm_s16le --enable-filter=aresample,aformat,anull
make -j"$(nproc)"
cp ffmpeg.exe "$out/ffmpeg.exe"
cp COPYING.LGPLv2.1 COPYING.LGPLv3 LICENSE.md "$out/"
cp ffbuild/config.log "$out/build-configuration.txt"
printf 'Unmodified FFmpeg 8.1.3 source: https://ffmpeg.org/releases/ffmpeg-8.1.3.tar.xz\nSHA256: 7138d28c96d9d3e3af4ee3d8cad72741f8ffb40da90c1112235dea3ecd3178a3\nBuild script: Tools/build-ffmpeg.sh\nNo optional external libraries or GPL components enabled.\n' > "$out/SOURCE.txt"
for notice in /usr/share/doc/mingw-w64*/copyright /usr/share/doc/gcc-mingw-w64*/copyright; do
  if [[ -f "$notice" ]]; then cat "$notice" >> "$out/toolchain-notices.txt"; fi
done
cp "$archive" "$root/artifacts/ffmpeg-8.1.3-source.tar.xz"
cp "$root/Tools/build-ffmpeg.sh" "$root/artifacts/build-ffmpeg.sh"
sha256sum "$out/ffmpeg.exe" > "$out/SHA256SUMS.txt"
