#!/usr/bin/env bash
# Send dev commands (one per argument) to the running game, then print new PocketPlug log lines.
# Screenshots taken with "shot <name>" are moved to .shots/ at half size.
G="${S1_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Schedule I}"; L="$G/MelonLoader/Latest.log"
OUT="$(dirname "$0")/../.shots"; mkdir -p "$OUT"
n=$(wc -l < "$L"); printf '%s\n' "$@" > "$G/UserData/PocketPlug.cmd"; sleep "${WAIT:-3}"
tail -n +$((n+1)) "$L" | grep -aE "PocketPlug\]|ERROR|Exception|rror" | head -30
for f in "$G"/UserData/PocketPlug-*.png; do
  [ -f "$f" ] && magick "$f" -resize 50% "$OUT/$(basename "$f")" && rm "$f" && echo "shot: $OUT/$(basename "$f")"
done
true
