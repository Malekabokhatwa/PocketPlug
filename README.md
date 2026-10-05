# PocketPlug

A quality-of-life [MelonLoader](https://github.com/LavaGang/MelonLoader) mod for **Schedule I** that puts more of the business in your phone. It's built for the **0.4.7 beta (IL2CPP)**. Every feature can be switched on or off from a new **Settings** app on the phone.

## Features
- **Deal compass.** A deal's marker on the compass shows the customer's photo (the one from the Contacts app) in a green ring, with their name and distance underneath at any range. The distance uses the game's own meters/feet setting.
- **Bank app.** Deposit and withdraw from your phone instead of walking to an ATM. It also shows your recent transactions, kept per save.
- **No deposit limit.** Removes the weekly $10,000 deposit limit, both in the Bank app and at ATMs. Deposits still count toward the week, so quests that depend on it still work.
- **Dealer transfers.** Recruited dealers get a new message option, "Send my money to my bank account." They send the cash they're holding straight to your bank account, and you get a "*Name* sent you $X" notification.
- **Endless skating.** Infinite stamina while riding a skateboard.
- **Ready alerts.** Phone notifications when plants or shrooms are ready to harvest, and when mixing stations, drying racks, chemistry stations, lab ovens and cauldrons finish.
- **Deal reminders.** A notification when one of your deals has about an in-game hour left.
- **Bigger stacks.** The stack limits from [IncreasedStackLimit-Latest](https://github.com/Malekabokhatwa/IncreasedStackLimit-Latest), built in. Every stackable item goes to 250 by default, with a limit for each item type that you can change in the Settings app.
  - Guns, melee weapons, ammo and items that don't stack are never changed.

## Requirements
- Schedule I on the default or `beta` branch (IL2CPP). Tested on **0.4.7f9**.
- MelonLoader **0.7.3** or newer. Start the game with MelonLoader once before adding mods.

PocketPlug is made for single-player. The Mono (`alternate`) branch isn't supported.

## Install
1. Download `PocketPlug.dll` from [Releases](https://github.com/Malekabokhatwa/PocketPlug/releases).
2. Put it in `Schedule I/Mods/`.
3. If you use IncreasedStackLimit-Latest, remove it. PocketPlug already includes it.

## Settings
Open **Settings** on the phone to turn features on and off and to change the stack limits. Changes apply right away.

Everything is also stored in `UserData/PocketPlug.cfg`, which is picked up while the game is running if you edit it by hand. A stack limit of `0` keeps the game's value for that item type.

## Good to know
- **Removing the mod:** the game cuts stacks above the normal limit down when a save loads, so split big stacks before uninstalling.
- **Bank history:** stored next to the mod's settings, in `UserData/PocketPlug/history/`. It isn't saved inside your game save.

## Building
You need the .NET SDK (6.0 or newer) and an IL2CPP copy of the game that has run once with MelonLoader. The project references `MelonLoader/net6` and `MelonLoader/Il2CppAssemblies` from that copy.

```sh
dotnet build src -c Release -p:GameDir="/path/to/Schedule I"
# add -p:DeployToGame=true to copy the DLL into the game's Mods folder
```

The icons are drawn by `scripts/make-icons.py` from [Lucide](https://lucide.dev) SVGs.

## Credits
- [Lucide](https://lucide.dev) for the icons (ISC, see `assets/icons/LICENSE-lucide.txt`).
- [S1API](https://github.com/ifBars/S1API) (MIT), whose phone app code showed how to add apps to the phone in IL2CPP.
- The [LavaGang](https://github.com/LavaGang) team for MelonLoader, and the Schedule I modding community for its [docs](https://s1modding.github.io/docs/moddevs/).
- Written with the help of an AI coding assistant (Claude).

No game files or decompiled game code are included in this repository.

## License
[MIT](LICENSE)
