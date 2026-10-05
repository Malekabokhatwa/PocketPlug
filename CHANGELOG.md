# Changelog

Versions follow `MAJOR.MINOR.PATCH`: bug fixes bump PATCH, new features bump MINOR.
To release: bump `<Version>` in `src/PocketPlug.csproj` and the version in `src/Core.cs`'s `MelonInfo`, add an entry
here, commit, tag `vX.Y.Z`, and attach `PocketPlug.dll` to a GitHub release.

## 1.0.2 (2026-10-05)
- **Removed the small one-time blip** (~11 ms) a few seconds after loading. The game's interop does one-time setup the first time each station type is checked, and the history file opens the first time it's read; both now happen behind the loading screen. Measured in game: after loading, nothing from PocketPlug takes more than ~1.5 ms in a frame.
- Ready alerts work out at most 3 new items' types per frame.
- Dev tools: a timing probe in dev mode, and `tools/HitchProbe`, a standalone freeze logger with GC counts. Comparing the game with and without the mod showed no freezes from vanilla during play.

## 1.0.1 (2026-10-05)
- **Fixed regular freezes (stutter).** Ready alerts searched the whole scene for stations and pots every 2 seconds, freezing the game for about 190 ms each time. It now only checks items at your owned properties, spread over several frames. Measured in game: no hitches after loading (before: a ~200 ms hitch every 2 s).

## 1.0.0 (2026-10-05)
First release, for Schedule I 0.4.7f9 (IL2CPP):
- deal compass;
- Bank and Settings phone apps;
- no deposit limit;
- dealer transfers;
- endless skating;
- ready alerts and deal reminders;
- per-type stack limits.
