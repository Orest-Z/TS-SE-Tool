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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using TS_SE_Tool.Utilities;

namespace TS_SE_Tool.Save
{
    /// <summary>
    /// Writes save files so that a failure at any point leaves the originals untouched:
    /// timestamped backup first, then every new file goes to a temp file next to its
    /// target, is read back and validated, and only when all of them passed are they
    /// swapped in with File.Replace.
    /// </summary>
    internal static class SafeSaveWriter
    {
        internal static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        internal const string TempSuffix = ".tsset_tmp";

        internal class PendingFile
        {
            internal string Path;
            internal string Content;
            //Gets the lines read back from disk; returns null when they are fine, an error otherwise.
            internal Func<string[], string> Validate;
        }

        internal static string BackupRoot
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TS SE Tool", "backups"); }
        }

        /// <summary>
        /// Copies the given files into BackupRoot\game\profile\save\yyyyMMdd-HHmmss and returns that folder.
        /// A manifest.txt lists where every file came from, for restoring.
        /// </summary>
        internal static string Backup(string _game, string _profileFolder, string _saveFolder, IEnumerable<string> _files)
        {
            string folder = System.IO.Path.Combine(BackupRoot,
                                                   SafeName(_game),
                                                   SafeName(System.IO.Path.GetFileName(_profileFolder.TrimEnd('\\', '/'))),
                                                   SafeName(System.IO.Path.GetFileName(_saveFolder.TrimEnd('\\', '/'))),
                                                   DateTime.Now.ToString("yyyyMMdd-HHmmss"));

            //two saves within the same second
            string unique = folder;
            for (int i = 2; Directory.Exists(unique); i++)
                unique = folder + "-" + i;

            Directory.CreateDirectory(unique);

            StringBuilder manifest = new StringBuilder();

            foreach (string file in _files.Where(File.Exists))
            {
                string target = System.IO.Path.Combine(unique, System.IO.Path.GetFileName(file));
                File.Copy(file, target, false);

                if (new FileInfo(target).Length != new FileInfo(file).Length)
                    throw new IOException("Backup copy of " + file + " is incomplete.");

                manifest.AppendLine(System.IO.Path.GetFileName(file) + "\t" + file);
            }

            File.WriteAllText(System.IO.Path.Combine(unique, "manifest.txt"), manifest.ToString(), Utf8NoBom);

            IO_Utilities.LogWriter("Backup written to " + unique);

            return unique;
        }

        internal static void WriteAll(IList<PendingFile> _files)
        {
            List<string> temps = new List<string>();

            try
            {
                //1. write and verify every temp file
                foreach (PendingFile file in _files)
                {
                    if (string.IsNullOrEmpty(file.Content))
                        throw new InvalidOperationException("Refusing to write empty content to " + file.Path);

                    string temp = file.Path + TempSuffix;
                    temps.Add(temp);

                    byte[] expected = Utf8NoBom.GetBytes(file.Content);
                    File.WriteAllBytes(temp, expected);

                    byte[] actual = File.ReadAllBytes(temp);

                    if (!actual.SequenceEqual(expected))
                        throw new IOException("Read-back of " + temp + " does not match what was written.");

                    if (file.Validate != null)
                    {
                        string[] lines = Utf8NoBom.GetString(actual).Split(new string[] { "\r\n" }, StringSplitOptions.None);
                        string error = file.Validate(lines);

                        if (error != null)
                            throw new InvalidDataException("Validation of the new " + System.IO.Path.GetFileName(file.Path) + " failed: " + error);
                    }
                }

                //2. swap them in
                foreach (PendingFile file in _files)
                {
                    string temp = file.Path + TempSuffix;

                    if (File.Exists(file.Path))
                        File.Replace(temp, file.Path, null, true);
                    else
                        File.Move(temp, file.Path);
                }
            }
            finally
            {
                foreach (string temp in temps)
                {
                    try
                    {
                        if (File.Exists(temp))
                            File.Delete(temp);
                    }
                    catch (Exception ex)
                    {
                        IO_Utilities.ErrorLogWriter("Could not remove temp file " + temp + ": " + ex.Message);
                    }
                }
            }
        }

        /// <summary>Structural check of a written SII text file; null when fine.</summary>
        internal static string CheckSiiStructure(string[] _lines, int _expectedUnits)
        {
            if (_lines.Length < 3 || _lines[0] != "SiiNunit" || _lines[1] != "{")
                return "missing SiiNunit header";

            if (_lines[_lines.Length - 1] != "}")
                return "missing closing brace";

            int units = 0, open = 0;

            for (int i = 2; i < _lines.Length - 1; i++)
            {
                string line = _lines[i].Trim();

                if (Items.OriginalBlockMerge.BlockHeader.IsMatch(line))
                {
                    if (open != 0)
                        return "unit opened inside another unit at line " + (i + 1);
                    open++;
                    units++;
                }
                else if (line.StartsWith("}"))
                {
                    if (open != 1)
                        return "unbalanced closing brace at line " + (i + 1);
                    open--;
                }
                else if (line.Length > 0 && open == 0)
                {
                    return "text outside of a unit at line " + (i + 1);
                }
            }

            if (open != 0)
                return "last unit is not closed";

            if (_expectedUnits >= 0 && units != _expectedUnits)
                return "expected " + _expectedUnits + " units, found " + units;

            return null;
        }

        internal static int CountUnits(string _content)
        {
            int units = 0;

            foreach (string line in _content.Split(new string[] { "\r\n" }, StringSplitOptions.None))
                if (Items.OriginalBlockMerge.BlockHeader.IsMatch(line.Trim()))
                    units++;

            return units;
        }

        private static string SafeName(string _name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                _name = _name.Replace(c, '_');

            return _name;
        }
    }
}
