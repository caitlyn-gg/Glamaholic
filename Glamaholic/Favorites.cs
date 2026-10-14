using Lumina.Excel.Sheets;

namespace Glamaholic {
    // Items the user wants to collect. They're listed in the Favorites tab and starred in shops and loot windows.
    internal class Favorites {
        private Plugin Plugin { get; }

        // bumped whenever the list changes so the UI and star markers can refresh their caches
        internal int Version { get; private set; }

        internal Favorites(Plugin plugin) {
            this.Plugin = plugin;
        }

        internal bool Contains(uint itemId) {
            return this.Plugin.Config.FavoriteItems.Contains(itemId);
        }

        internal void Add(uint itemId) {
            if (this.Contains(itemId)) {
                return;
            }

            this.Plugin.Config.FavoriteItems.Add(itemId);
            this.Changed();
        }

        internal void Remove(uint itemId) {
            if (this.Plugin.Config.FavoriteItems.Remove(itemId)) {
                this.Changed();
            }
        }

        private void Changed() {
            this.Version++;
            this.Plugin.SaveConfig();
        }

        // Returns the item if it's gear that can be used as glamour, ignoring any HQ/collectible offset.
        internal static Item? GetGear(uint itemId) {
            var item = Service.DataManager.GetExcelSheet<Item>()!.GetRowOrDefault(itemId % Util.ItemModifierMod);
            if (item == null || Util.IsItemSkipped(item.Value) || Util.GetSlot(item.Value) == null) {
                return null;
            }

            return item;
        }

        // Favorites are grouped by slot, with both ring slots grouped together.
        internal static PlateSlot GetGroup(Item item) {
            var slot = Util.GetSlot(item) ?? PlateSlot.MainHand;
            return slot == PlateSlot.LeftRing ? PlateSlot.RightRing : slot;
        }

        internal static string GroupName(PlateSlot group) {
            return group switch {
                PlateSlot.MainHand => "Weapons",
                PlateSlot.RightRing or PlateSlot.LeftRing => "Rings",
                _ => group.Name(),
            };
        }
    }
}
