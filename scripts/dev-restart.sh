#!/usr/bin/env bash
# Dev loop: kill the game (exact PID), deploy PocketPlug, launch via Steam, load the first save, wait until in-game.
# Needs UserData/PocketPlug.dev to exist (enables the dev command file).
G="${S1_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Schedule I}"; L="$G/MelonLoader/Latest.log"
PID=$(ps -eo pid,comm | awk '/Schedule I\.exe$/ {print $1}')
if [ -n "$PID" ]; then kill $PID; for i in $(seq 1 40); do ps -p $PID >/dev/null || break; sleep 0.5; done; fi
(cd "$(dirname "$0")/../src" && dotnet build -c Release -p:DeployToGame=true 2>&1 | grep -E "Deployed| error ") || exit 1
rm -f "$G/UserData/PocketPlug.cmd" "$G/UserData/PocketPlug-dump.txt" "$G"/UserData/PocketPlug-*.png
sleep 3; steam -applaunch 3164500 >/dev/null 2>&1 &
start=$(date +%s)
until [ "$(stat -c %Y "$L" 2>/dev/null || echo 0)" -ge "$start" ] && grep -q "Scene ready: Menu" "$L"; do sleep 2; [ $(( $(date +%s) - start )) -gt 240 ] && { echo "TIMEOUT menu"; exit 1; }; done
echo load > "$G/UserData/PocketPlug.cmd"
until grep -q "Scene ready: Main" "$L"; do sleep 2; [ $(( $(date +%s) - start )) -gt 420 ] && { echo "TIMEOUT main"; exit 1; }; done
sleep 25; echo "in game after $(( $(date +%s) - start ))s"
grep -aE "PocketPlug\]|ERROR|Exception" "$L" | grep -v "dev>" | tail -5
