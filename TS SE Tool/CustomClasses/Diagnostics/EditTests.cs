/*
   Copyright 2026 TS SE Tool contributors

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/
/*
   Usage:  "TS SE Tool.exe" --edittest <saveFolder>

   For every edit operation: parse game.sii fresh, apply the same model changes the UI
   makes, serialise, and compare unit by unit, attribute by attribute with the original.
   A test fails when anything other than the expected attributes changed, when a unit
   appears or disappears, or when the output does not parse again.

   Regressions covered:
     #142  accessory paint_color must survive any edit
     #140  trailer repair keeps cargo and the wheel count
   It never writes into the save folder.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using TS_SE_Tool.Save.Items;

namespace TS_SE_Tool.Diagnostics
{
    internal static class EditTests
    {
        private class Test
        {
            internal string Name;
            //a control test that must be reported as failing
            internal bool ExpectFailure;
            //applies the edit and returns the "unitName|attribute" keys allowed to change
            internal Func<SiiNunit, HashSet<string>> Apply;
            //extra assertion on the output; null when fine
            internal Func<Dictionary<string, Dictionary<string, string>>, Dictionary<string, Dictionary<string, string>>, string> Check;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        internal static int Run(string[] args)
        {
            if (!AttachConsole(-1))
                AllocConsole();

            if (args.Length < 2)
            {
                Console.WriteLine("usage: \"TS SE Tool.exe\" --edittest <saveFolder>");
                return 2;
            }

            string saveDir = args[1].TrimEnd('\\', '/');

            string[] info = SelfTest.Decode(Path.Combine(saveDir, "info.sii"));
            string[] game = SelfTest.Decode(Path.Combine(saveDir, "game.sii"));

            if (info == null || game == null)
            {
                Console.WriteLine("FAIL could not decode " + saveDir);
                return 1;
            }

            SaveFileInfoData infoData = new SaveFileInfoData();
            infoData.ProcessData(info);
            uint version = infoData.Version;

            if (Globals.PlayerLevelUps.Length == 0)
                Globals.PlayerLevelUps = new int[] {200, 500, 700, 900, 1100, 1300, 1500, 1700, 1900, 2100,
                    2300, 2500, 2700, 2900, 3100, 3300, 3500, 3700, 4000, 4300,
                    4600, 4900, 5200, 5500, 5800, 6100, 6400, 6700, 7000, 7300};

            var original = Units(game);

            int failed = 0;

            foreach (Test test in Tests())
            {
                string result;

                try
                {
                    SiiNunit unit = new SiiNunit(game);
                    SiiNunit.HeadlessWrittenBlocks = unit.NamelessControlList;

                    HashSet<string> allowed = test.Apply(unit);

                    if (allowed == null)
                    {
                        Console.WriteLine("SKIP " + test.Name + " (nothing to edit in this save)");
                        continue;
                    }

                    string output = unit.PrintOut(version);
                    string[] outLines = output.Split(new[] { "\r\n" }, StringSplitOptions.None);

                    //optional: keep the output of every test for inspection
                    string dumpDir = Environment.GetEnvironmentVariable("TSSET_EDITTEST_DUMP");
                    if (!string.IsNullOrEmpty(dumpDir))
                    {
                        Directory.CreateDirectory(dumpDir);
                        File.WriteAllText(Path.Combine(dumpDir, new string(test.Name.Where(char.IsLetterOrDigit).ToArray()) + ".sii"), output);
                    }

                    var edited = Units(outLines);

                    result = Compare(original, edited, allowed);

                    if (result == null && test.Check != null)
                        result = test.Check(original, edited);

                    //the edit must actually reach the output
                    if (result == null && allowed.Count > 0 && !allowed.Any(k => Changed(original, edited, k)))
                    {
                        string k = allowed.First();
                        string[] p = k.Split('|');
                        original.TryGetValue(p[0], out var ou);
                        edited.TryGetValue(p[0], out var eu);
                        string ov = null, ev = null;
                        ou?.TryGetValue(p[1], out ov);
                        eu?.TryGetValue(p[1], out ev);
                        result = "none of the edited attributes changed in the output (" + k + ": " + (ov ?? "<none>") + " -> " + (ev ?? "<none>") + ")";
                    }

                    if (result == null)
                    {
                        SiiNunit reloaded = new SiiNunit(outLines);

                        if (reloaded.SiiNitems.Count != edited.Count)
                            result = "re-parse found " + reloaded.SiiNitems.Count + " of " + edited.Count + " units";
                    }

                    if (test.ExpectFailure)
                    {
                        //control test: the checks must catch a known-bad edit
                        if (result == null)
                        {
                            result = "control edit was NOT detected";
                            failed++;
                        }
                        else
                            result = "OK (correctly detected: " + result.Split('\n')[0].Trim() + ")";
                    }
                    else if (result == null)
                        result = "OK (" + allowed.Count + " attributes allowed to change)";
                    else
                        failed++;
                }
                catch (Exception ex)
                {
                    result = "EXCEPTION " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace;
                    failed++;
                }

                Console.WriteLine((result.StartsWith("OK") ? "PASS " : "FAIL ") + test.Name + " - " + result);
            }

            Console.WriteLine(failed == 0 ? "ALL PASSED" : failed + " FAILED");

            return failed == 0 ? 0 : 1;
        }

        private static IEnumerable<Test> Tests()
        {
            yield return new Test
            {
                Name = "no edit",
                Apply = u => new HashSet<string>()
            };

            yield return new Test
            {
                Name = "money",
                Apply = u =>
                {
                    u.Bank.money_account = u.Bank.money_account + 123456;
                    return Keys(u.Economy.bank, "money_account");
                }
            };

            yield return new Test
            {
                Name = "level / experience",
                Apply = u =>
                {
                    u.Economy.setPlayerExp(u.Economy.getPlayerLvl()[0] + 1);
                    return Keys(u.EconomyNameless, "experience_points");
                }
            };

            yield return new Test
            {
                Name = "skills",
                Apply = u =>
                {
                    //the Profile tab edits _playerSkills (ADR as a bit mask); PrintOut copies it back
                    byte[] skills = u.Economy._playerSkills;
                    skills[0] = (byte)(skills[0] == 63 ? 31 : 63);
                    for (int i = 1; i < 6; i++)
                        skills[i] = (byte)(skills[i] == 6 ? 5 : 6);
                    return Keys(u.EconomyNameless, "adr", "long_dist", "heavy", "fragile", "urgent", "mechanical");
                }
            };

            yield return new Test
            {
                Name = "truck repair + refuel (mirrors buttonTruckRepair_Click)",
                Apply = u =>
                {
                    string name = u.Player.trucks.FirstOrDefault(x => x != null && x != "null");
                    if (name == null)
                        return null;

                    Save.Items.Vehicle v = u.SiiNitems[name];

                    v.engine_wear = 0; v.transmission_wear = 0; v.chassis_wear = 0; v.cabin_wear = 0;
                    v.wheels_wear = v.wheels_wear.Select(x => (Save.DataFormat.SCS_Float)0f).ToList();
                    v.engine_wear_unfixable = 0; v.transmission_wear_unfixable = 0; v.chassis_wear_unfixable = 0; v.cabin_wear_unfixable = 0;
                    v.wheels_wear_unfixable = v.wheels_wear_unfixable.Select(x => (Save.DataFormat.SCS_Float)0f).ToList();
                    v.integrity_odometer = 0;
                    v.integrity_odometer_float_part = 0;
                    v.fuel_relative = 1;

                    return Keys(name, "engine_wear", "transmission_wear", "chassis_wear", "cabin_wear", "wheels_wear",
                                "engine_wear_unfixable", "transmission_wear_unfixable", "chassis_wear_unfixable", "cabin_wear_unfixable",
                                "wheels_wear_unfixable", "integrity_odometer", "integrity_odometer_float_part", "fuel_relative");
                },
                Check = (o, e) => SameCounts(o, e, "wheels_wear")
            };

            yield return new Test
            {
                Name = "trailer repair (mirrors buttonTrailerRepair_Click, #140)",
                Apply = u =>
                {
                    List<string> names = u.SiiNitems.Where(x => x.Value is Save.Items.Trailer).Select(x => x.Key).ToList();
                    if (names.Count == 0)
                        return null;

                    HashSet<string> allowed = new HashSet<string>();

                    foreach (string name in names)
                    {
                        Save.Items.Trailer t = u.SiiNitems[name];

                        t.cargo_damage = 0; t.trailer_body_wear = 0; t.chassis_wear = 0;
                        t.wheels_wear = t.wheels_wear.Select(x => (Save.DataFormat.SCS_Float)0f).ToList();
                        t.trailer_body_wear_unfixable = 0; t.chassis_wear_unfixable = 0;
                        t.wheels_wear_unfixable = t.wheels_wear_unfixable.Select(x => (Save.DataFormat.SCS_Float)0f).ToList();
                        t.integrity_odometer = 0;
                        t.integrity_odometer_float_part = 0;

                        allowed.UnionWith(Keys(name, "cargo_damage", "trailer_body_wear", "chassis_wear", "wheels_wear",
                                               "trailer_body_wear_unfixable", "chassis_wear_unfixable", "wheels_wear_unfixable",
                                               "integrity_odometer", "integrity_odometer_float_part"));
                    }

                    return allowed;
                },
                Check = (o, e) => SameCounts(o, e, "wheels_wear") ?? SameCounts(o, e, "wheels_wear_unfixable")
            };

            yield return new Test
            {
                Name = "control: old trailer repair emptied the wheel arrays (#140)",
                ExpectFailure = true,
                Apply = u =>
                {
                    List<string> names = u.SiiNitems.Where(x => x.Value is Save.Items.Trailer && ((Save.Items.Trailer)x.Value).wheels_wear.Count > 0)
                                                     .Select(x => x.Key).ToList();
                    if (names.Count == 0)
                        return null;

                    HashSet<string> allowed = new HashSet<string>();

                    foreach (string name in names)
                    {
                        Save.Items.Trailer t = u.SiiNitems[name];
                        t.wheels_wear = new List<Save.DataFormat.SCS_Float>();
                        allowed.UnionWith(Keys(name, "wheels_wear"));
                    }

                    return allowed;
                },
                Check = (o, e) => SameCounts(o, e, "wheels_wear")
            };

            yield return new Test
            {
                Name = "add freight market job (mirrors PrepareCompaniesJobWrite)",
                Apply = u =>
                {
                    //take a real offer from one company and write it into another company's first slot
                    var offers = u.SiiNitems.Where(x => x.Value is Job_offer_Data && ((Job_offer_Data)x.Value).cargo != "null").ToList();
                    var target = u.Economy.companies.Select(x => (Save.Items.Company)u.SiiNitems[x]).FirstOrDefault(x => x.job_offer.Count > 0);

                    if (offers.Count < 2 || target == null)
                        return null;

                    string slot = target.job_offer[0];
                    Job_offer_Data src = offers.First(x => x.Key != slot).Value;
                    Job_offer_Data job = u.SiiNitems[slot];

                    job.target = src.target;
                    job.expiration_time = u.Economy.game_time + 10000;
                    job.urgency = src.urgency;
                    job.shortest_distance_km = src.shortest_distance_km + 1;
                    job.ferry_time = src.ferry_time;
                    job.ferry_price = src.ferry_price;
                    job.cargo = src.cargo;
                    job.company_truck = src.company_truck;
                    job.trailer_variant = src.trailer_variant;
                    job.trailer_definition = src.trailer_definition;
                    job.units_count = src.units_count;

                    return Keys(slot, "target", "expiration_time", "urgency", "shortest_distance_km", "ferry_time", "ferry_price",
                                "cargo", "company_truck", "trailer_variant", "trailer_definition", "units_count");
                }
            };
        }

        private static HashSet<string> Keys(string _unit, params string[] _attributes)
        {
            return new HashSet<string>(_attributes.Select(x => _unit + "|" + x));
        }

        /// <summary>unit name -> (attribute key -> value). Array entries keep their index.</summary>
        private static Dictionary<string, Dictionary<string, string>> Units(string[] _lines)
        {
            var units = new Dictionary<string, Dictionary<string, string>>();
            Dictionary<string, string> current = null;

            foreach (string raw in _lines)
            {
                string line = raw.Trim();
                var header = OriginalBlockMerge.BlockHeader.Match(line);

                if (header.Success)
                {
                    current = new Dictionary<string, string>();
                    units[header.Groups["name"].Value] = current;
                    current["#type"] = header.Groups["tag"].Value;
                    continue;
                }

                if (line.StartsWith("}"))
                {
                    current = null;
                    continue;
                }

                if (current == null)
                    continue;

                string tag = OriginalBlockMerge.TagOf(line);

                if (tag != null)
                    current[tag] = OriginalBlockMerge.ValueOf(line);
            }

            return units;
        }

        private static string Compare(Dictionary<string, Dictionary<string, string>> _a,
                                      Dictionary<string, Dictionary<string, string>> _b,
                                      HashSet<string> _allowed)
        {
            List<string> problems = new List<string>();

            foreach (string name in _a.Keys.Except(_b.Keys))
                problems.Add("unit lost: " + _a[name]["#type"] + " " + name);

            foreach (string name in _b.Keys.Except(_a.Keys))
                problems.Add("unit added: " + _b[name]["#type"] + " " + name);

            foreach (string name in _a.Keys.Intersect(_b.Keys))
            {
                var a = _a[name];
                var b = _b[name];

                foreach (string key in a.Keys.Union(b.Keys))
                {
                    a.TryGetValue(key, out string va);
                    b.TryGetValue(key, out string vb);

                    if (va == vb)
                        continue;

                    string baseKey = OriginalBlockMerge.BaseTagOf(key);

                    if (_allowed.Contains(name + "|" + baseKey))
                        continue;

                    problems.Add(a["#type"] + " " + name + " " + key + ": " + (va ?? "<missing>") + " -> " + (vb ?? "<missing>"));
                }
            }

            if (problems.Count == 0)
                return null;

            return problems.Count + " unexpected differences:" + Environment.NewLine + "    " +
                   string.Join(Environment.NewLine + "    ", problems.Take(25));
        }

        private static bool Changed(Dictionary<string, Dictionary<string, string>> _a,
                                    Dictionary<string, Dictionary<string, string>> _b,
                                    string _key)
        {
            string[] parts = _key.Split('|');

            if (!_a.TryGetValue(parts[0], out var a) || !_b.TryGetValue(parts[0], out var b))
                return false;

            foreach (string attr in a.Keys.Union(b.Keys).Where(x => OriginalBlockMerge.BaseTagOf(x) == parts[1]))
            {
                a.TryGetValue(attr, out string va);
                b.TryGetValue(attr, out string vb);

                if (va != vb)
                    return true;
            }

            return false;
        }

        //array counts of _attribute must not change in any unit
        private static string SameCounts(Dictionary<string, Dictionary<string, string>> _a,
                                         Dictionary<string, Dictionary<string, string>> _b,
                                         string _attribute)
        {
            foreach (var unit in _a)
            {
                if (!unit.Value.TryGetValue(_attribute, out string before))
                    continue;

                _b[unit.Key].TryGetValue(_attribute, out string after);

                if (before != after)
                    return unit.Value["#type"] + " " + unit.Key + " " + _attribute + " count changed " + before + " -> " + after;
            }

            return null;
        }
    }
}
