using System;
using System.Collections.Generic;
using System.Linq;

namespace Glamaholic {
    internal static class Changelog {
        internal record ChangelogEntry(Version Version, string[] Changes);

        internal static readonly List<ChangelogEntry> Entries = [
            new ChangelogEntry(new Version(1, 12, 0, 0), [
                "Folders can now be renamed through the right-click context menu.",
                "Plates and folders can now be dragged and reordered manually, and automatic sorting has been removed.",
                "Improved third-party plugin support.",
            ]),
            
            new ChangelogEntry(new Version(1, 13, 0, 0), [
                "Valuable dyes are now factored into the item selection process when applying plates",
                "-Previously, the selection process was all or nothing. It will now attempt to find a match with a valuable dye, or an item with any matching dye first",
                "Dyes on items already in the current plate will no longer be ovewritten and waste a dye",
                "Plates should now always fully apply and refresh on first try",
            ]),
            
            new ChangelogEntry(new Version(1, 14, 0, 0), [
                "The '(Dye)' suffix is now present in clipboard exports for better interop with other plugins (thanks @SubaruYashiro)",
                "Added folder selection to plate creation (thanks @firstmagic)",
                "-'New Plate' button now allows picking a folder to create the plate in",
                "-Clipboard and EC imports now allow picking a folder to create the plate in",
                "-TryOn/Examine dropdowns now allow picking a folder to create the plate in",
                "-You can now move plates into folders via the right-click context menu in the list"
            ]),
        ];

        /// <summary>
        /// Gets all changelog entries that are newer than the specified version.
        /// </summary>
        internal static List<ChangelogEntry> GetEntriesSince(Version? lastSeenVersion) {
            if (lastSeenVersion == null) {
                return Entries;
            }

            return Entries
                .Where(e => e.Version > lastSeenVersion)
                .OrderByDescending(e => e.Version)
                .ToList();
        }

        /// <summary>
        /// Gets the latest version from the changelog.
        /// </summary>
        internal static Version? GetLatestVersion() {
            return Entries.Count > 0 
                ? Entries.Max(e => e.Version) 
                : null;
        }
    }
}
