# TS SE Tool 1.61.0 – ETS2 1.61 support

TS SE Tool works again with the **current Euro Truck Simulator 2 (1.61, savefile version 102)**. Loading and saving no longer damages your save.

## Tested with
- Euro Truck Simulator 2 **1.61** with **all official DLCs**
- **ProMods 2.84** (Europe), **ProMods Maghreb**, **ProMods Middle-East Add-on**, **ProMods The Great Steppe**, **ProMods 2.83 Convoy Mode Support**
- *All Trucks 850HP, 1500HP & 2500HP Engines* mod
- A real profile with 33 saves (savefile versions 90 to 102)

In game: the edited save loads, money edits are applied and custom freight market jobs appear.

## What's fixed
- **Saves are no longer destroyed.** Old versions could leave `game.sii` at 0 bytes when saving failed (upstream #148). The new file is now built in memory, written to a temporary file, read back and checked, and only then swapped in. If anything goes wrong, your save is left as it was.
- **Automatic backups.** Before every save, `game.sii`, `info.sii` and `profile.sii` are copied to `%LOCALAPPDATA%\TS SE Tool\backups\<game>\<profile>\<save>\<date-time>\`.
- **Nothing is lost on save.** Previously one load + save, without any edits, dropped hundreds of attributes and invented others. Now everything the tool doesn't edit is written back exactly as it was read, including data from new game versions, DLCs and mods. A save with no edits comes back byte for byte identical.
- **"Some of the blocks in save file wasn't recognized"** no longer appears for 1.60/1.61 saves (upstream #144). Data types the tool doesn't know are kept unchanged.
- **Truck and trailer accessory paint colours** are kept (upstream #142).
- **Repairing a truck or trailer** no longer empties the wheel list. This is the likely cause of cargo disappearing after a repair (upstream #140). *Confirmation in game is welcome.*
- **profile.sii** no longer loses your current truck when it is saved.
- **Clearer errors.** If saving fails you get a dialog with the details, a *Copy details* button and a pointer to the backup.
- Runs on .NET Framework 4.8 (included in Windows 10/11).

## Known issues
- **Custom jobs may give far too little time** (upstream #133). When the tool doesn't know the road distance between the two cities, it writes 5 km into the job, and the game sets the deadline and pay from that distance. The fix is next on the list.
- **Convoy: "you are missing the DLC required for this job"** (upstream #113) is not fixed yet.
- American Truck Simulator, Steam Cloud profiles and the Cargo Market tab were not tested with this build.

## Install
1. Download `TS-SE-Tool-1.61.0.zip`, extract it anywhere, and run `TS SE Tool.exe`.
2. **Close the game before saving** with the tool.
3. If the game shows a save as corrupted, restore it from the backup folder above, or rename `game_backup.sii` to `game.sii` in that save's folder.

If Windows SmartScreen warns you, click *More info → Run anyway* (the exe isn't code-signed).

## Credits
- **LIPtoH**, original author of TS SE Tool
- **Daniel Vieira (danielrvieira)**, savefile v97 fixes and the self-test harness (upstream PR #147), included here
- **Satherov** and **omnizs38**, whose 1.60/1.61 forks were studied for this release
- **František Milt**, SII_Decrypt library

Developed by Orest-Z with Claude (Anthropic) as co-author.
