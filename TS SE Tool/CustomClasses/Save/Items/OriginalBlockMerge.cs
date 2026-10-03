/*
   Originally added by Daniel Vieira (danielrvieira, PR #147) during the 2026
   save-format investigation; reworked in feature/ets2-1.61 to keep the original unit
   order, treat arrays as a whole and protect values that failed to parse.

   TS SE Tool does not rewrite the save file it read - it *reconstructs* it from a
   fixed set of hand written fields per block type, walking the object graph from
   `economy`. On its own that drops attributes SCS added in newer versions, invents
   ones SCS removed, reformats floats and moves units the walk does not reach.

   This class merges the reconstructed text back onto the units exactly as they were
   read, so that a load + save without edits reproduces the file, and an edit changes
   only the attributes it touched:

     * units are written in their original order; units the tool created are placed
       after the unit the graph walk printed before them; units the tool removed
       (absent from the reconstructed text) are dropped;
     * inside a unit every original line keeps its position;
     * a scalar the tool re-emitted replaces the original only if its value is
       semantically different (so "1.5" vs "&3fc00000" is not a change);
     * an array (count line plus [i] entries) is one group: if any element differs the
       whole generated group replaces the original group, so shrinking an array never
       leaves stale entries behind;
     * attributes whose value failed to parse keep the original line;
     * attributes the tool invents that the original unit lacks are dropped and logged.
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using TS_SE_Tool.Utilities;

namespace TS_SE_Tool.Save.Items
{
    internal static class OriginalBlockMerge
    {
        //"tag : name {"
        internal static readonly Regex BlockHeader =
            new Regex(@"^(?<tag>[A-Za-z_][A-Za-z_0-9]*)\s*:\s*(?<name>[^\s{}]+)\s*\{\s*$", RegexOptions.Compiled);

        //trailing array index, e.g. "companies[17]" or "companies[]" -> base "companies"
        private static readonly Regex ArrayIndex =
            new Regex(@"\[\d*\]$", RegexOptions.Compiled);

        private static readonly Regex TokenSplit =
            new Regex(@"[\s(),;]+", RegexOptions.Compiled);

        private class Unit
        {
            internal string Header;
            internal string Tag;
            internal string Name;
            internal List<string> Body = new List<string>();
        }

        /// <summary>Tag of an attribute line, or null when the line carries no attribute.</summary>
        internal static string TagOf(string _line)
        {
            int colon = _line.IndexOf(':');

            if (colon <= 0)
                return null;

            string tag = _line.Substring(0, colon).Trim();

            return tag.Length == 0 || tag.Contains(' ') ? null : tag;
        }

        internal static string ValueOf(string _line)
        {
            int colon = _line.IndexOf(':');

            return colon < 0 ? "" : _line.Substring(colon + 1).Trim();
        }

        internal static string BaseTagOf(string _tag)
        {
            return ArrayIndex.Replace(_tag, "");
        }

        private static bool IsArrayTag(string _tag)
        {
            return _tag.EndsWith("]");
        }

        /// <summary>
        /// Rebuilds <paramref name="_generated"/> (output of SiiNunit.PrintOut) on top of
        /// the units as read from the save.
        /// </summary>
        internal static string Apply(string _generated,
                                     Dictionary<string, List<string>> _originalBodies,
                                     List<string> _originalOrder,
                                     Dictionary<string, dynamic> _items)
        {
            if (_originalBodies == null || _originalBodies.Count == 0 || string.IsNullOrEmpty(_generated))
                return _generated;

            string[] lines = _generated.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            //--- split the generated text into prefix, units and suffix
            List<Unit> generatedUnits = new List<Unit>();
            List<string> prefix = new List<string>();
            int lastUnitEnd = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                Match header = BlockHeader.Match(lines[i].Trim());

                if (!header.Success)
                {
                    if (generatedUnits.Count == 0 && lines[i].Trim().Length > 0)
                        prefix.Add(lines[i]);
                    continue;
                }

                Unit unit = new Unit
                {
                    Header = lines[i],
                    Tag = header.Groups["tag"].Value,
                    Name = header.Groups["name"].Value
                };

                int j = i + 1;

                while (j < lines.Length && !lines[j].TrimStart().StartsWith("}"))
                {
                    unit.Body.Add(lines[j]);
                    j++;
                }

                generatedUnits.Add(unit);

                lastUnitEnd = j;
                i = j;
            }

            List<string> suffix = new List<string>();

            for (int i = lastUnitEnd + 1; i < lines.Length; i++)
                if (lines[i].Trim().Length > 0)
                    suffix.Add(lines[i]);

            //--- final unit order: original order, new units after their generated predecessor
            Dictionary<string, Unit> generatedByName = new Dictionary<string, Unit>();

            foreach (Unit unit in generatedUnits)
                if (!generatedByName.ContainsKey(unit.Name))
                    generatedByName.Add(unit.Name, unit);

            List<string> order = _originalOrder.Where(x => generatedByName.ContainsKey(x)).ToList();
            HashSet<string> placed = new HashSet<string>(order);
            Dictionary<string, int> position = new Dictionary<string, int>();

            for (int i = 0; i < order.Count; i++)
                position[order[i]] = i;

            string previous = null;
            int newUnits = 0;

            foreach (Unit unit in generatedUnits)
            {
                if (!placed.Contains(unit.Name))
                {
                    int insertAt = previous == null ? 0 : order.IndexOf(previous) + 1;

                    order.Insert(insertAt, unit.Name);
                    placed.Add(unit.Name);
                    newUnits++;
                }

                previous = unit.Name;
            }

            int droppedUnits = _originalOrder.Count(x => !generatedByName.ContainsKey(x));

            //--- emit
            List<string> droppedTags = new List<string>();
            StringBuilder output = new StringBuilder();

            foreach (string line in prefix)
                output.Append(line).Append("\r\n");

            foreach (string name in order)
            {
                Unit unit = generatedByName[name];

                output.Append(unit.Header).Append("\r\n");

                IEnumerable<string> body = unit.Body;

                if (_originalBodies.TryGetValue(name, out List<string> originalBody))
                {
                    HashSet<string> failed = null;

                    if (_items != null && _items.TryGetValue(name, out dynamic item) && item is SiiNBlockCore core)
                        failed = core.ParseFailedTags;

                    body = MergeBody(unit.Tag, originalBody, unit.Body, failed, droppedTags);
                }

                foreach (string line in body)
                    output.Append(line).Append("\r\n");

                output.Append("}\r\n\r\n");
            }

            for (int i = 0; i < suffix.Count; i++)
            {
                output.Append(suffix[i]);

                if (i < suffix.Count - 1)
                    output.Append("\r\n");
            }

            if (droppedTags.Count > 0)
            {
                IO_Utilities.ErrorLogWriter(
                    "Save write | attributes not present in the loaded save were dropped (" + droppedTags.Count + "):" +
                    Environment.NewLine +
                    string.Join(Environment.NewLine, droppedTags.Distinct().OrderBy(x => x)));
            }

            IO_Utilities.LogWriter("Save write | merged " + order.Count + " units (" + newUnits + " new, " + droppedUnits + " removed)");

            return output.ToString();
        }

        /// <summary>
        /// Same merge for a whole small file (profile.sii, info.sii) whose original lines
        /// are at hand. Returns <paramref name="_generated"/> unchanged when there is no original.
        /// </summary>
        internal static string ApplyToText(string _generated, string[] _originalLines)
        {
            if (_originalLines == null || _originalLines.Length == 0)
                return _generated;

            Dictionary<string, List<string>> bodies = new Dictionary<string, List<string>>();
            List<string> order = new List<string>();

            for (int i = 0; i < _originalLines.Length; i++)
            {
                Match header = BlockHeader.Match(_originalLines[i].Trim());

                if (!header.Success)
                    continue;

                string name = header.Groups["name"].Value;
                List<string> body = new List<string>();

                int j = i + 1;

                while (j < _originalLines.Length && !_originalLines[j].TrimStart().StartsWith("}"))
                    body.Add(_originalLines[j++]);

                if (!bodies.ContainsKey(name))
                {
                    bodies.Add(name, body);
                    order.Add(name);
                }

                i = j;
            }

            return Apply(_generated, bodies, order, null);
        }

        /// <summary>Attribute groups of a unit body: scalars by tag, arrays by base tag.</summary>
        private static Dictionary<string, List<string>> Groups(List<string> _body)
        {
            Dictionary<string, List<string>> groups = new Dictionary<string, List<string>>();

            foreach (string line in _body)
            {
                string tag = TagOf(line);

                if (tag == null)
                    continue;

                string key = IsArrayTag(tag) ? BaseTagOf(tag) : tag;

                if (!groups.TryGetValue(key, out List<string> group))
                    groups.Add(key, group = new List<string>());

                group.Add(line);
            }

            return groups;
        }

        private static List<string> MergeBody(string _blockTag,
                                              List<string> _originalBody,
                                              List<string> _generatedBody,
                                              HashSet<string> _failedTags,
                                              List<string> _droppedTags)
        {
            Dictionary<string, List<string>> original = Groups(_originalBody);
            Dictionary<string, List<string>> generated = Groups(_generatedBody);

            List<string> merged = new List<string>();
            HashSet<string> emitted = new HashSet<string>();

            foreach (string line in _originalBody)
            {
                string tag = TagOf(line);

                if (tag == null)
                {
                    //blank line or comment - keep verbatim
                    if (line.Trim().Length > 0)
                        merged.Add(line);
                    continue;
                }

                string key = IsArrayTag(tag) ? BaseTagOf(tag) : tag;

                if (emitted.Contains(key))
                    continue;

                emitted.Add(key);

                List<string> originalGroup = original[key];

                bool keepOriginal =
                    !generated.TryGetValue(key, out List<string> generatedGroup) ||
                    (_failedTags != null && originalGroup.Any(x => _failedTags.Contains(TagOf(x)))) ||
                    GroupsEqual(originalGroup, generatedGroup);

                merged.AddRange(keepOriginal ? originalGroup : generatedGroup);
            }

            //attributes the original unit did not have at all
            foreach (KeyValuePair<string, List<string>> group in generated)
            {
                if (emitted.Contains(group.Key))
                    continue;

                _droppedTags.Add(_blockTag + " | " + group.Key);
            }

            return merged;
        }

        private static bool GroupsEqual(List<string> _a, List<string> _b)
        {
            if (_a.Count != _b.Count)
                return false;

            for (int i = 0; i < _a.Count; i++)
            {
                if (TagOf(_a[i]) != TagOf(_b[i]))
                    return false;

                if (!ValuesEqual(ValueOf(_a[i]), ValueOf(_b[i])))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// True when two SII values mean the same thing: identical text, or the same
        /// sequence of tokens where numeric tokens are compared as 32 bit floats (decimal
        /// and &amp;hex forms alike).
        /// </summary>
        internal static bool ValuesEqual(string _a, string _b)
        {
            if (_a == _b)
                return true;

            //quoted strings must match exactly
            if (_a.StartsWith("\"") || _b.StartsWith("\""))
                return false;

            string[] ta = TokenSplit.Split(_a.Trim()).Where(x => x.Length > 0).ToArray();
            string[] tb = TokenSplit.Split(_b.Trim()).Where(x => x.Length > 0).ToArray();

            if (ta.Length != tb.Length)
                return false;

            for (int i = 0; i < ta.Length; i++)
            {
                if (ta[i] == tb[i])
                    continue;

                //Integers (ids, uint32 flags, money) are compared exactly; floats would lose precision above 2^24.
                if (IsInteger(ta[i]) && IsInteger(tb[i]))
                {
                    if (decimal.Parse(ta[i], CultureInfo.InvariantCulture) != decimal.Parse(tb[i], CultureInfo.InvariantCulture))
                        return false;
                    continue;
                }

                if (!TryNumber(ta[i], out float fa) || !TryNumber(tb[i], out float fb))
                    return false;

                if (fa != fb && !(float.IsNaN(fa) && float.IsNaN(fb)))
                    return false;
            }

            return true;
        }

        private static readonly Regex IntegerToken = new Regex(@"^-?\d{1,28}$", RegexOptions.Compiled);

        private static bool IsInteger(string _token)
        {
            return IntegerToken.IsMatch(_token);
        }

        private static bool TryNumber(string _token, out float _value)
        {
            if (_token.StartsWith("&") && _token.Length == 9 &&
                uint.TryParse(_token.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint bits))
            {
                _value = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
                return true;
            }

            return float.TryParse(_token, NumberStyles.Float, CultureInfo.InvariantCulture, out _value);
        }
    }
}
