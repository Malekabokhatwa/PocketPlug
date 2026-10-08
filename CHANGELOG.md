# Changelog

Versions follow `MAJOR.MINOR.PATCH`: bug fixes bump PATCH, new features bump MINOR.
To release: bump `<Version>` in `src/PocketPlug.csproj` and the version in `src/Core.cs`'s `MelonInfo`, add an entry
here, commit, tag `vX.Y.Z`, and attach `PocketPlug.dll` to a GitHub release.

## 2.0.0 (2026-10-09)
**New**
- Mixing station limits: Mk2 up to 250 items per mix, Mk1 up to 125. Mix time scales with the batch.
- Drying racks hold 250. All three limits are adjustable in Settings, capped at the product stack limit, and the station sliders update right away.
- Payroll app: pay employees from your bank, per property, with a custom amount per employee, Pay / Pay all, and optional morning Auto-pay that tops lockers up.
- Employee alerts: out of seeds, soil, shroom spawn or product and packaging, or nowhere to put their output. Once per employee per day.
- Dealer auto-sweep: dealers' cash goes to your bank every morning.
- Daily report from **PocketPlug AAB** in Messages: bank in and out, your deals, and each dealer's earnings and product left. The last 7 reports are kept per save.

**Fixed**
- Ready alerts never fired for mixing stations (Mk1 and Mk2) or chemistry stations when a known recipe finished: the game empties the operation in the same moment it finishes. They now also watch the output slot.
- Ready alerts could keep stale entries for sold or destroyed stations.
- Deal reminders could be missed or repeated (keyed by memory address instead of the deal's ID).
- The deal compass rewrote its distance text every frame. It now has its own label that only updates when the number changes, which means less garbage.
- Per-save files are now keyed by the save's seed too, so a new game in a reused slot doesn't inherit old history. Existing history is migrated automatically.
- Settings showed a stack limit of 0 as "Off"; it means the game's value, so it now says "Game".
- One-time setup (delegates, saved reports) now happens behind the loading screen.

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
