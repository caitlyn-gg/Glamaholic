using System;
using System.Collections.Generic;

namespace Glamaholic {
    // Which patch each item was added in, from item-patches.txt (see tools/update_item_patches.py).
    internal static class ItemPatches {
        private static readonly Lazy<(uint[] Starts, uint[] Ends, string[] Versions)> Ranges = new(Load);

        // Returns the patch an item was added in (e.g. "7.2"), or null if unknown.
        // Items newer than the bundled data get the patch of the closest older item, since ids are assigned in release order.
        internal static string? GetPatch(uint itemId) {
            var (starts, _, versions) = Ranges.Value;

            var index = Array.BinarySearch(starts, itemId);
            if (index < 0) {
                // the range starting just before this id
                index = ~index - 1;
            }

            return index >= 0 ? versions[index] : null;
        }

        // 0 for A Realm Reborn (2.x), 1 for Heavensward (3.x), ... matching the ExVersion sheet, or -1 if unknown
        internal static int GetExpansion(string? patch) {
            if (patch == null) {
                return -1;
            }

            var major = patch.Split('.')[0];
            return int.TryParse(major, out var version) ? version - 2 : -1;
        }

        private static (uint[], uint[], string[]) Load() {
            var starts = new List<uint>();
            var ends = new List<uint>();
            var versions = new List<string>();

            foreach (var rawLine in Resourcer.Resource.AsString("item-patches.txt").Split('\n')) {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) {
                    continue;
                }

                var parts = line.Split(' ');
                var ids = parts[0].Split('-');
                starts.Add(uint.Parse(ids[0]));
                ends.Add(uint.Parse(ids[^1]));
                versions.Add(parts[1]);
            }

            return (starts.ToArray(), ends.ToArray(), versions.ToArray());
        }
    }
}
