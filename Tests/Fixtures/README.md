# Synthetic import fixtures

These files contain generated 440 Hz sine waves, not game audio or third-party music.
The twelve short fixtures are two seconds at 48 kHz before encoding, in stereo except
the named mono MP3 and six-channel FLAC. The long FLAC is 370 seconds at 44.1 kHz stereo,
used to verify decoded-size rejection. Created with the local FFmpeg 6.0 build.

Typical generation command (substitute codec, channels and filename):

`ffmpeg -f lavfi -i sine=frequency=440:sample_rate=48000:duration=2 -ac 2 -c:a libmp3lame source.mp3`

Codecs: libmp3lame, flac, libvorbis, libopus, aac, wmav2, pcm_s16be,
pcm_s24le and pcm_f32le. No encoder is needed to run the checked-in fixtures.
