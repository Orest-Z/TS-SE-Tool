# TS SET
Truck Simulator Save Editor Tool

## Description
Small tool for editing save files of Euro Truck Simulator 2 and American Truck Simulator.

This fork (`feature/ets2-1.61`) brings the tool to **ETS2 1.61** and makes load/save lossless and non-destructive.

## Supported game versions

| Game | Save file version | Status |
|---|---|---|
| ETS2 1.43 – 1.61 (all DLCs, ProMods 2.84 incl. Maghreb, Middle-East, Great Steppe) | 61 – 102 | Load + save verified lossless on real 1.61 saves (see *Tests*); edited save loads in game. Release notes: [docs/RELEASE_NOTES_1.61.0.md](docs/RELEASE_NOTES_1.61.0.md) |
| ATS 1.43 – 1.60 | 61 – 97 | Same code path, not tested on a real ATS profile in this fork. |
| Newer | > 102 | Loads after a warning. Unknown data is kept unchanged. Test on a copy first. |

## Safety
- Before every write, `profile.sii`, `info.sii` and `game.sii` are copied to
  `%LOCALAPPDATA%\TS SE Tool\backups\<game>\<profile>\<save>\<timestamp>\`, alongside the usual `*_backup.sii` next to the save.
- New files are written to a temp file, read back and checked (structure, unit count, re-parse). Only then are they swapped in with `File.Replace`. If anything fails, your files stay as they were and an error dialog shows the details.
- The writer keeps every unit and attribute it does not edit exactly as it was read, including data from newer game versions, mods and DLCs.

## OS
Windows 10 / 11. The app runs as 32-bit, because `SII_Decrypt.dll` is 32-bit.

### Dependence
.NET Framework 4.8

## Build
Requirements: [Visual Studio 2022 Build Tools](https://aka.ms/vs/17/release/vs_BuildTools.exe) (or Visual Studio 2022) with the *.NET desktop build tools* workload and the .NET Framework 4.8 targeting pack, plus [nuget.exe](https://dist.nuget.org/win-x86-commandline/latest/nuget.exe).

```powershell
nuget restore "TS SE Tool.sln"
& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "TS SE Tool.sln" /p:Configuration=Release
# lang, img and gameref are not in the repository; this copies them from the v0.3.11.0 release:
powershell -ExecutionPolicy Bypass -File build\stage-runtime.ps1
```
The program is then in `TS SE Tool\bin\Release`.

## Tests
Headless modes of `TS SE Tool.exe`. None of them write into the save folder they read:

| Command | What it checks |
|---|---|
| `--selftest <saveFolder> [outFolder]` | Decode → parse → write without edits → compare, re-parse, second write identical. |
| `--edittest <saveFolder>` | Money, level, skills, truck/trailer repair, refuel, adding a job: only the expected attributes change. Covers #140 and #142. |
| `--writetest <saveFolder> <outFolder> [--money N] [--level N]` | Full write path (backup, verified temp file, swap) into a copy of the save. The output can be loaded in game. |

## You can:
* add Custom paths for save files.
* edit Local and Steam save files.
* edit Player level and skill.
* edit and share saved User Colors for truck and trailer.
* edit amount of Money on account.
* visit Cities and be able to grab cargo from discovered cities.
* buy and\or upgrade Garages.
* repair and\or refuel your Truck.
* Share truck paint job.
* repair Trailer.
* create custom jobs for Freight market.
* make basic edits to Cargo market.
* share Truck position.
* share GPS paths.
* share Multiple Truck positions as one Convoy Control pack.

## Credits
* **LIPtoH** – original author of TS SE Tool ([LIPtoH/TS-SE-Tool](https://github.com/LIPtoH/TS-SE-Tool)).
* **Daniel Vieira (danielrvieira)** – savefile v97 fixes, the no-truncation write path, the original-content merge and the `--selftest` harness (upstream PR #147). Reused and extended here.
* **Satherov** – 1.60 parser fixes and keeping unknown lines with their keys. Studied for the parser changes.
* **omnizs38** – .NET 4.8 / CI / high-DPI work. Studied for the build and UI plans.
* **František Milt (TheLazyTomcat)** – SII_Decrypt library (MPL-2.0).

Licensed under Apache-2.0. See [LICENSE](LICENSE).
