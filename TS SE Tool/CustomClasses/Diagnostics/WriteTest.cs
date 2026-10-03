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
   Usage:  "TS SE Tool.exe" --writetest <saveFolder> <outFolder> [--money N] [--level N]

   Copies the save folder (game.sii, info.sii, preview) to <outFolder>, applies the
   optional edits and writes game.sii through the same SafeSaveWriter path the UI uses
   (backup, verified temp file, File.Replace). Then it decodes the written file again
   and checks that it parses and that the edits are there.
   The result is a complete save folder that can be copied into a profile's save
   directory for an in-game test. The source folder is never written to.

   Backups go to TSSET_BACKUP_ROOT when that variable is set.
*/
using System;
using System.IO;
using System.Linq;

using TS_SE_Tool.Save;
using TS_SE_Tool.Save.Items;

namespace TS_SE_Tool.Diagnostics
{
    internal static class WriteTest
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        internal static int Run(string[] args)
        {
            if (!AttachConsole(-1))
                AllocConsole();

            if (args.Length < 3)
            {
                Console.WriteLine("usage: \"TS SE Tool.exe\" --writetest <saveFolder> <outFolder> [--money N] [--level N]");
                return 2;
            }

            string source = Path.GetFullPath(args[1].TrimEnd('\\', '/'));
            string target = Path.GetFullPath(args[2].TrimEnd('\\', '/'));

            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("FAIL output folder must differ from the source folder");
                return 2;
            }

            long? money = null;
            int level = -1;

            for (int i = 3; i < args.Length - 1; i++)
            {
                if (args[i] == "--money") money = long.Parse(args[i + 1]);
                if (args[i] == "--level") level = int.Parse(args[i + 1]);
            }

            try
            {
                Directory.CreateDirectory(target);

                foreach (string file in Directory.GetFiles(source).Where(x => !x.EndsWith(SafeSaveWriter.TempSuffix)))
                {
                    string copy = Path.Combine(target, Path.GetFileName(file));

                    if (File.Exists(copy))
                        File.SetAttributes(copy, FileAttributes.Normal);

                    File.Copy(file, copy, true);
                    File.SetAttributes(copy, FileAttributes.Normal); //test-data is read-only
                }

                string[] info = SelfTest.Decode(Path.Combine(target, "info.sii"));
                string[] game = SelfTest.Decode(Path.Combine(target, "game.sii"));

                if (info == null || game == null)
                {
                    Console.WriteLine("FAIL could not decode the copied save");
                    return 1;
                }

                SaveFileInfoData infoData = new SaveFileInfoData();
                infoData.ProcessData(info);

                if (Globals.PlayerLevelUps.Length == 0)
                    Globals.PlayerLevelUps = new int[] {200, 500, 700, 900, 1100, 1300, 1500, 1700, 1900, 2100,
                        2300, 2500, 2700, 2900, 3100, 3300, 3500, 3700, 4000, 4300,
                        4600, 4900, 5200, 5500, 5800, 6100, 6400, 6700, 7000, 7300};

                SiiNunit unit = new SiiNunit(game);
                SiiNunit.HeadlessWrittenBlocks = unit.NamelessControlList;

                if (money != null)
                    unit.Bank.money_account = money.Value;

                if (level >= 0)
                    unit.Economy.setPlayerExp(level);

                long expectedMoney = unit.Bank.money_account;
                uint expectedXp = unit.Economy.experience_points;

                string text = unit.PrintOut(infoData.Version);
                string gamePath = Path.Combine(target, "game.sii");

                string backupRoot = Environment.GetEnvironmentVariable("TSSET_BACKUP_ROOT");
                string backup = string.IsNullOrEmpty(backupRoot)
                    ? SafeSaveWriter.Backup("test", Path.GetDirectoryName(target), target, new[] { gamePath })
                    : SafeSaveWriter.BackupTo(backupRoot, "test", Path.GetDirectoryName(target), target, new[] { gamePath });

                Console.WriteLine("backup      : " + backup);

                int expectedUnits = SafeSaveWriter.CountUnits(text);

                SafeSaveWriter.WriteAll(new[]
                {
                    new SafeSaveWriter.PendingFile
                    {
                        Path = gamePath,
                        Content = text,
                        Validate = lines => SafeSaveWriter.CheckSiiStructure(lines, expectedUnits)
                    }
                });

                //read back exactly like a load
                string[] reread = SelfTest.Decode(gamePath);
                SiiNunit reloaded = new SiiNunit(reread);

                string problem = null;

                if (reloaded.SiiNitems.Count != unit.SiiNitems.Count)
                    problem = "unit count " + reloaded.SiiNitems.Count + " != " + unit.SiiNitems.Count;
                else if (reloaded.Bank.money_account != expectedMoney)
                    problem = "money " + reloaded.Bank.money_account + " != " + expectedMoney;
                else if (reloaded.Economy.experience_points != expectedXp)
                    problem = "experience " + reloaded.Economy.experience_points + " != " + expectedXp;
                else if (Directory.GetFiles(target, "*" + SafeSaveWriter.TempSuffix).Length > 0)
                    problem = "temp file left behind";

                Console.WriteLine("written     : " + gamePath + " (" + new FileInfo(gamePath).Length + " bytes, plain text)");
                Console.WriteLine("units       : " + reloaded.SiiNitems.Count);
                Console.WriteLine("money       : " + reloaded.Bank.money_account);
                Console.WriteLine("experience  : " + reloaded.Economy.experience_points + " (level " + reloaded.Economy.getPlayerLvl()[0] + ")");
                Console.WriteLine(problem == null ? "PASS" : "FAIL " + problem);

                return problem == null ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace);
                return 1;
            }
        }
    }
}
