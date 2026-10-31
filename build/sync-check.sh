#!/usr/bin/env bash
# Measures the offset between sound and picture in a recording, by hand on a Mac, where the CI cannot record the screen
# (Windows has SyncTests for it). Play the clip full screen, record it with ShowTime (the Mac's sound only), measure.
#
# Usage: sync-check.sh clip <out.mp4>         a 14 s clip, black, with a white flash and a 1 kHz beep together every 2 s
#                                             from 2 s to 12 s (black before and after: going full screen or back to the
#                                             desktop is not taken for a flash)
#        sync-check.sh measure <recording>    for each flash, the offset of the nearest beep; fails past ±0.1 s
# ffmpeg: $FFMPEG, else the one on the PATH (on a Mac: /Applications/ShowTime.app/Contents/MacOS/ffmpeg).
set -euo pipefail

ffmpeg=${FFMPEG:-ffmpeg}
tolerance=0.1

usage() {
  echo "usage: $0 clip <out.mp4> | measure <recording>" >&2
  exit 2
}
[ $# -eq 2 ] || usage

case "$1" in
  clip)
    "$ffmpeg" -hide_banner -loglevel error -y \
      -f lavfi -i "color=c=black:s=640x360:r=30:d=14" \
      -f lavfi -i "sine=f=1000:sample_rate=48000:d=14" \
      -filter_complex "[0:v]drawbox=x=0:y=0:w=iw:h=ih:color=white:t=fill:enable='between(t,1.9,12.1)*lt(mod(t,2),0.1)'[v];[1:a]volume=volume=0:enable='not(between(t,1.9,12.1)*lt(mod(t,2),0.1))'[a]" \
      -map "[v]" -map "[a]" -c:v libx264 -pix_fmt yuv420p -c:a aac "$2"
    echo "Clip written: $2"
    ;;
  measure)
    # Whole outputs in variables: a pipe ending early could end ffmpeg with SIGPIPE, which pipefail reports.
    luma=$("$ffmpeg" -hide_banner -loglevel error -i "$2" -an \
      -vf "signalstats,metadata=print:key=lavfi.signalstats.YAVG:file=-" -f null -)
    silences=$("$ffmpeg" -hide_banner -nostats -i "$2" -vn -af "silencedetect=noise=-30dB:d=0.05" -f null - 2>&1)
    # A flash starts where the average luma rises past 128 after a dark frame; a beep where a silence ends (one at the
    # very start, with no silence before it, is not seen: neither is a flash on the first frame).
    flashes=$(awk -F'pts_time:' '/pts_time:/ { split($2, a, " "); t = a[1] }
      /YAVG=/ { split($0, b, "="); bright = b[2] > 128; if (seen && bright && !was) print t; was = bright; seen = 1 }' <<< "$luma")
    beeps=$(awk -F'silence_end: ' '/silence_end:/ { split($2, a, " "); print a[1] }' <<< "$silences")
    [ -n "$flashes" ] || { echo "No flash in $2" >&2; exit 1; }
    [ -n "$beeps" ] || { echo "No beep in $2" >&2; exit 1; }
    # Through the environment: macOS's awk refuses a newline in a -v value.
    BEEPS="$beeps" awk -v tolerance="$tolerance" '
      BEGIN { n = split(ENVIRON["BEEPS"], beep, "\n") }
      {
        best = ""
        for (i = 1; i <= n; i++) { d = beep[i] - $1; if (best == "" || (d < 0 ? -d : d) < (best < 0 ? -best : best)) best = d }
        # A brightening with no beep near it (full screen, the desktop after playback) is not one of the clip flashes.
        if (best == "" || (best < 0 ? -best : best) > 1) { printf "flash %7.3f s   no beep within 1 s: ignored\n", $1; next }
        printf "flash %7.3f s   beep - flash %+6.3f s\n", $1, best
        sum += best; count++
        if ((best < 0 ? -best : best) > tolerance) bad++
      }
      END {
        if (count == 0) { print "No flash with its beep: was the clip played with its sound?"; exit 1 }
        printf "mean offset %+6.3f s over %d flashes (tolerance ±%s s)\n", sum / count, count, tolerance
        exit bad > 0
      }' <<< "$flashes"
    ;;
  *)
    usage
    ;;
esac
