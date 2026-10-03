# TS SE Tool – Architecture notes

Short orientation for future work sessions. Based on `upstream/dev-branch` (440272b, v0.3.11.0, save versions 61–74).

## Layout

| Path | Role |
|---|---|
| `TS SE Tool/Program.cs` | Entry point, global exception handlers, then `FormMain`. |
| `FormMain*.cs`, `FormMethods.cs`, `Forms/FormMainControlsMethods.cs` | One big partial class `FormMain`: holds all state (save model, lists, dictionaries) and UI logic. Supported save range is set in `FormMethods.cs` (`SupportedSavefileVersionETS2`). |
| `Forms/MainTabs/*` | One partial file per tab: Profile, Company, Truck, Trailer, Freight Market, Cargo Market, Convoy Tools. |
| `MethodsDecodeSave.cs` | P/Invoke into native `libs/SII_Decrypt.dll` (TheLazyTomcat/ncs-sniper, MPL-2.0, last binary 2023-05). Returns the file as `string[]` lines split on `\r\n`. |
| `MethodsReadWrite.cs` | `LoadSaveFile` (BackgroundWorker): profile.sii, then info.sii, then game.sii. `NewWrireSaveFile` writes. Also loads lang/img and the gameref cargo cache. |
| `DataManipulation.cs` | Builds UI lists from the parsed save (`Prepare*Initial`) and pushes edits back into the model before writing (`Prepare*Write`). SQL CE databases (`dbs/<game>.<profile>.sdf` hold routes and distances; `gameref/cache/<game>/<dlc>.sdf` hold cargo defs). |
| `CustomClasses/Save/Items/*` | One class per SII unit type (`economy`, `company`, `job_offer_data`, `player`, `vehicle`, `trailer`, …). `SiiNunit` is the container. |
| `CustomClasses/Save/DataFormat/*` | Value types: `SCS_Float` (handles `&hex` floats), `SCS_Placement`, `SCS_Color`, `SCS_String`, vectors. |
| `CustomClasses/Save/SaveFileInfoData.cs`, `SaveFileProfileData.cs` | info.sii / profile.sii models (version, dependencies, user_data). |

## Data flow

1. **Select**: game, then profile root (Documents `profiles` / `steam_profiles`, or Steam `userdata/<id>/<appid>/remote` = Steam Cloud), then profile, then save.
2. **Decode**: `GetMemoryFormat`. 1 = plain text, used as is. 2 = encrypted (ScsC), `DecryptAndDecodeMemory`. 3/4 = BSII/3nK binary, `DecodeMemory`. Output is text SII.
3. **Parse**: `SiiNunit(string[])` scans for lines containing both `:` and `{` (a unit header), collects lines up to the first line starting with `}`, then `DetectTag` creates a typed class. Unknown unit types become `Unidentified` and add to `UnidentifiedBlocks` (this is the "blocks wasn't recognized" dialog, #144). Each typed constructor is a `switch` over attribute names; unknown attributes go to `UnidentifiedLines`.
4. **Edit**: tabs change the typed objects or stage data (`AddedJobsDictionary`, `extraDrivers`, …).
5. **Write**: `Prepare*Write()` applies staged data. `SiiNunit.PrintOut(version)` **reconstructs** game.sii by walking the object graph from `economy` (bank, player, trailers, jobs, trucks, drivers, companies, garages, …). Each class prints a **fixed field list**. The result is written as plain text (the game accepts text saves).

## Jobs (Freight Market tab)

- Cities and companies come from the save: `economy.companies[]` = `company.volatile.<company>.<city>`. Mod and ProMods cities therefore appear automatically. Country grouping uses the static `lang/CityToCountry.csv`, so unknown cities become "unsorted" (#118 crash path).
- Cargo, trailer defs, variants and company trucks are harvested from **existing `job_offer_data` units in the save** (`PrepareCargoTrailerDefsVariantsLists`), plus the gameref cache for cargo properties.
- A created job **overwrites an existing `job_offer_data` slot** of the source company (`PrepareCompaniesJobWrite`, slot index = job index). It sets target, expiration_time, urgency, shortest_distance_km, ferry, cargo, company_truck (random from the same trailer class), trailer_variant/definition and units_count. `trailer_place` and `fill_ratio` are left unchanged.
- `shortest_distance_km` = DB route distance × `TimeMultiplier`. **If no route is known, the distance is 11111, which is replaced by 5 km.** The game derives the deadline and the pay from this distance, which is a likely root cause of #133.
- `expiration_time` = game_time + rand(180..1800) + n × pickup time, so the offer can expire before you reach it.

## Convoy facts (web research, still to be verified in game)

- Each player's freight market is generated locally. In Convoy, a job one player has taken can be taken by the others from the pause menu. The job definition travels from the player who took it. So in principle only that player's save needs the custom jobs.
- The "missing DLC required for this job" message also appears for **jobs from other third-party editors** (Virtual Speeditor, Steam thread 5154944131325371202). The game also shows it for missing skills (ADR, high value, …) and for routes into regions you don't own. #113 is therefore most likely caused by the content of the generated job (cargo/trailer/company_truck combination, `trailer_place`, distance or other fields), not by real DLC differences. This needs to be confirmed by experiment.

## Write path now (feature/ets2-1.61)

`NewWrireSaveFile` runs `Prepare*Write()`, then `SiiNunit.PrintOut` builds the text by graph walk. `OriginalBlockMerge.Apply` then merges that text onto the units as read (`OriginalBlockBodies`, `OriginalBlockOrder`):
- units are kept in their original order;
- a scalar is replaced only if its value changed semantically;
- arrays are replaced as a whole group;
- `ParseFailedTags` keep the original line;
- invented attributes are dropped.

profile.sii and info.sii use `ApplyToText`. Then `SafeSaveWriter` does the backup, verified temp files and `File.Replace`. After a failed write the UI forces a reload, because Prepare* already mutated the model. Headless checks: `--selftest`, `--edittest`, `--writetest` (see README).

Gotchas found while testing:
- skills are written from `Economy._playerSkills`. `PrintOut` copies the array back over `adr`…`mechanical`.
- repair used to empty `wheels_wear` arrays (#140).
- `user_data[11]` was tied to `ud15`.

## Known risks / bugs (dev-branch, most fixed above)

- **Write truncates game.sii before the content exists**: a `StreamWriter` is opened on the target, then `PrintOut` throws, leaving a 0-byte save (#148).
- **Lossy round-trip**: the fixed field lists drop new attributes (`player.my_vehicles`, `vehicle_addon_accessory.paint_color` → #142, cargo-related fields → likely #140) and invent removed ones. The `UnidentifiedLines` return value is discarded and stores values without keys (#147).
- **Parse abort**: each constructor's `catch { break; }` stops parsing the whole unit on the first bad value (`nil`, full uint32 values). For `economy`, `registry` then stays empty, which leads to a `KeyNotFoundException` on write.
- Unknown units are written at the end, not in place. Unit order differs from the original because of the graph walk.
- The header detection `Contains(':') && Contains('{')` would misfire on a string value containing `{`.
- `SII_Decrypt.dll` (2023) is unverified for 1.60+ binary saves (#149 reports `file_not_decoded`).
- The only backups are single `*_backup.sii` files that are overwritten on every save.

## Verified on a real ETS2 1.61 profile (2026-10-04)

- `g_save_format 0` writes game/info/profile as `ScsC` (encrypted). Configs and saves edited by a tool are plain text.
- **savefile version = 102** (info.sii `version:`). Not 97 like ATS 1.60.
- The bundled `SII_Decrypt.dll` (2023, **32-bit/i386**, so the exe must run as x86/Prefer32Bit) decoded all 66 encrypted files to valid text: `SiiNunit` header, balanced units, no binary residue.
- Unit types missing from `DetectTag`: `player_vehicles`, `car_job_generator`, `car_job_log` (cause of #144). `info.sii` has `dependencies` (121 entries, `dlc|`/`rdlc|`/`mod|<id>|<name>`).
- About 27k units and about 6.6k `nil` values per save.

## Community forks studied (all Apache-2.0)

- **danielrvieira `fix/savefile-v97`** (PR #147, on dev-branch): temp-file write, real exception surfacing, `catch → continue`, `nil`/uint raw fields, `OriginalBlockMerge` (re-applies original lines). Weakness: array entries that shrink survive from the original. Also adds a `--selftest` harness.
- **Satherov `master`** (on dev-branch): unknown lines kept with keys and appended at the end of the unit, `player_vehicles` unit, nullable `state_change_time`, version range 61–97.
- **omnizs38 `master`** (on **old master**, large refactor, .NET 4.8, CI, packaging, UI). Hard to merge. Ideas worth taking: CI workflow, high-DPI fixes, compatibility policy. Same old SII_Decrypt.dll.
