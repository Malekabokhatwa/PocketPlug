#!/usr/bin/env bash
# sig.sh <Il2Cpp type> <regex> [Core]: public members of the live game's interop type matching the regex.
I="${S1_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Schedule I}/MelonLoader/Il2CppAssemblies"
F="$I/Assembly-CSharp.dll"; [ "$3" = Core ] && F="$I/Il2CppScheduleOne.Core.dll"
~/.dotnet/tools/ilspycmd -t "$1" "$F" 2>/dev/null | grep -E "public (static )?(unsafe )?[^(=]*\b($2)\b" \
  | grep -vE "Native|_k__Backing|RpcWriter|RpcReader|NetworkInitialize" | sed -E 's/^\s+public //; s/unsafe //' \
  | sed "s|^|${1##*.}: |" | head -20
