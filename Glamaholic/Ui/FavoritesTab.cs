using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Linq;

namespace Glamaholic.Ui {
    internal class FavoritesTab {
        private PluginUi Ui { get; }
        private Favorites Favorites => this.Ui.Plugin.Favorites;

        private string _search = string.Empty;
        private List<Item> _searchResults = new();
        private List<(PlateSlot Group, List<Item> Items)> _groups = new();
        private int _builtVersion = -1;
        private HashSet<uint> _owned = new();

        internal FavoritesTab(PluginUi ui) {
            this.Ui = ui;
        }

        internal void Draw() {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##favorite-search", "Search all gear to add favorites...", ref this._search, 512, ImGuiInputTextFlags.AutoSelectAll)) {
                this.Search();
            }

            this._owned = ItemRow.GetDresserItemIds();

            if (ImGui.BeginChild("favorites")) {
                if (this._search.Length > 0) {
                    this.DrawSearchResults();
                } else {
                    this.DrawFavorites();
                }
            }

            ImGui.EndChild();
        }

        private void DrawFavorites() {
            if (this._builtVersion != this.Favorites.Version) {
                this.RebuildGroups();
            }

            if (this._groups.Count == 0) {
                Util.TextUnformattedWrapped("You don't have any favorites yet. Search for gear above, or right-click an item in game (including another player's slots in the Examine window) and click \"Add to Favorites\".");
                return;
            }

            foreach (var (group, items) in this._groups) {
                if (!ImGui.CollapsingHeader($"{Favorites.GroupName(group)} ({items.Count})###favorite-group-{group}", ImGuiTreeNodeFlags.DefaultOpen)) {
                    continue;
                }

                foreach (var item in items) {
                    var row = ItemRow.Draw(this.Ui, item, false, this._owned, "Double-click to try on, right-click for more");
                    if (row.DoubleClicked) {
                        this.Ui.Plugin.Functions.TryOn(item.RowId, 0, 0);
                    }
                }
            }
        }

        private void DrawSearchResults() {
            if (this._searchResults.Count == 0) {
                ImGui.TextDisabled("No matching gear.");
                return;
            }

            ImGui.TextDisabled("Click an item to add it to or remove it from your favorites.");

            var clipper = ImGui.ImGuiListClipper();
            clipper.Begin(this._searchResults.Count, ItemRow.Height + ImGui.GetStyle().ItemSpacing.Y);
            while (clipper.Step()) {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++) {
                    var item = this._searchResults[i];
                    var isFavorite = this.Favorites.Contains(item.RowId);
                    var hint = $"Click to {(isFavorite ? "remove from" : "add to")} favorites, right-click for more";
                    if (ItemRow.Draw(this.Ui, item, false, this._owned, hint).Clicked) {
                        if (isFavorite) {
                            this.Favorites.Remove(item.RowId);
                        } else {
                            this.Favorites.Add(item.RowId);
                        }
                    }
                }
            }

            clipper.End();
        }

        private void Search() {
            var filter = this._search.ToLowerInvariant();
            this._searchResults = DataCache.GlamourItems.Value
                .Where(item => item.Name.ExtractText().ToLowerInvariant().Contains(filter))
                .ToList();
        }

        private void RebuildGroups() {
            this._groups = this.Ui.Plugin.Config.FavoriteItems
                .Select(Favorites.GetGear)
                .Where(item => item != null)
                .Select(item => item!.Value)
                .GroupBy(Favorites.GetGroup)
                .OrderBy(group => group.Key)
                .Select(group => (group.Key, group.OrderBy(item => item.Name.ExtractText()).ToList()))
                .ToList();
            this._builtVersion = this.Favorites.Version;
        }
    }
}
