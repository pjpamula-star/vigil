#!/bin/sh
# review.sh <video.mp4> <outDir>: a still every 0.5 s (file NNN = second NNN/2), plus sheets of 20 (10 s each, 5 x 4).
v="$1"; out="$2"; ff="${FFMPEG:-ffmpeg}"
rm -rf "$out"; mkdir -p "$out"
"$ff" -loglevel error -i "$v" -vf "fps=2" -start_number 0 "$out/%03d.png"
n=$(ls "$out"/*.png | wc -l)
p=0; i=0
while [ $i -lt $n ]; do
  "$ff" -loglevel error -y -start_number $i -i "$out/%03d.png" -frames:v 20 -vf "scale=360:-1,tile=5x4:padding=6:color=0x333333" -frames:v 1 "$out/sheet-$p.jpg"
  i=$((i+20)); p=$((p+1))
done
echo "$n stills, $p sheets"
