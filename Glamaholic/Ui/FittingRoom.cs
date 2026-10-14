using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Glamaholic.Ui {
    // Browse every piece of glamour in the game by slot and preview it with a click.
    internal class FittingRoom {
        private static readonly PlateSlot[] Groups = [
            PlateSlot.MainHand,
            PlateSlot.OffHand,
            PlateSlot.Head,
            PlateSlot.Body,
            PlateSlot.Hands,
            PlateSlot.Legs,
            PlateSlot.Feet,
            PlateSlot.Ears,
            PlateSlot.Neck,
            PlateSlot.Wrists,
            PlateSlot.RightRing,
        ];

        private static readonly string[] DyeFilterNames = ["Any dye channels", "Dyeable", "2 dye channels", "Not dyeable"];
        private static readonly string[] OwnedFilterNames = ["Owned or not", "Owned", "Not owned"];
        private static readonly string[] GenderFilterNames = ["Any gender or race", "Wearable by my character", "Hide gender/race-locked", "Male-only items", "Female-only items"];

        // SearchText is what search words are matched against; tags can be appended to it later
        // matches the role ending that separates otherwise identical items, e.g. "Abyssos Helm of Fending"
        private static readonly Regex RoleSuffix = new(@"\s+of\s+(Fending|Maiming|Striking|Aiming|Scouting|Healing|Casting|Slaying)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // matches a trailing colour, gem or style in brackets, e.g. "Aetherial Electrum Circlet (Turquoise)" or "[F]"
        private static readonly Regex VariantSuffix = new(@"\s*[\(\[][^()\[\]]*[\)\]]$", RegexOptions.Compiled);

        private static readonly string[] IconSizeNames = ["Small icons", "Medium icons", "Large icons"];

        // one entry in the list or grid: a single item, or a family of role variants shown together
        private record Listing(string Name, List<Entry> Variants) {
            internal Entry First => this.Variants[0];
            internal bool IsFamily => this.Variants.Count > 1;
        }

        private record Entry(Item Item, PlateSlot Group, string Name, string FamilyName, string SearchText, string? Patch, int Expansion, ImmutableArray<uint> OutfitSets, string? Type, ItemTraits Traits) {
            // items with the same key look the same
            internal (PlateSlot, ulong, ulong) LookKey => this.Item.ModelMain == 0
                ? (this.Group, this.Item.RowId, ulong.MaxValue)
                : (this.Group, this.Item.ModelMain, this.Item.ModelSub);
        }

        private PluginUi Ui { get; }
        private Configuration Config => this.Ui.Plugin.Config;
        private FittingRoomFilters Filters => this.Config.FittingRoomFilters;

        private bool _visible;
        private string _search = string.Empty;
        private PlateSlot _selectedGroup = PlateSlot.Head;
        private List<Entry>? _entries;

        // the catalog takes a moment to build, so it's built off the game thread the first time the window opens
        private Task<List<Entry>>? _entriesTask;
        private bool _loadErrorLogged;
        private Dictionary<PlateSlot, List<Listing>> _results = new();
        private Listing? _variantsListing;
        private Dictionary<uint, Entry>? _entriesById;

        // how many items of each type match everything except the type filter, for the type dropdown
        private Dictionary<(PlateSlot, string), int> _typeCounts = new();
        private List<byte>? _rarities;

        // show the try on history instead of every piece of glamour
        private bool _showHistory;
        private bool _resultsDirty = true;
        private HashSet<uint> _dresserItems = new();

        // how many other matching items share each shown item's look, when hiding duplicates
        private Dictionary<uint, int> _duplicateCounts = new();

        // what the current results were built from, to know when they need rebuilding
        private (int Favorites, int Dresser, bool Armoire, uint Job, int History) _resultsState;

        private readonly Dictionary<uint, HashSet<uint>> _jobCategories = new();
        private List<(uint Id, string Name)>? _jobs;
        private List<(uint Id, string Name)>? _outfitSets;
        private int _maxLevel;
        private string _outfitSetSearch = string.Empty;

        // the item currently previewed in each slot, so it can be highlighted
        private readonly Dictionary<PlateSlot, uint> _previewed = new();

        internal FittingRoom(PluginUi ui) {
            this.Ui = ui;
        }

        internal void Open() {
            if (!this._visible) {
                this.OnOpened();
            }

            this._visible = true;
        }

        internal void Toggle() {
            this._visible ^= true;

            if (this._visible) {
                this.OnOpened();
            }
        }

        private void OnOpened() {
            if (!this.PreviewOnCharacter) {
                OpenGameFittingRoom();
            }
        }

        private bool PreviewOnCharacter => this.Config.FittingRoomPreviewOnCharacter && Interop.Glamourer.IsAvailable();

        // opens the game's Fitting Room (Try On) window alongside this one, so previews have somewhere to show
        private static unsafe void OpenGameFittingRoom() {
            var addon = Service.GameGui.GetAddonByName("Tryon");
            if (addon != null && addon.IsVisible) {
                return;
            }

            var agent = AgentTryon.Instance();
            if (agent != null && !agent->IsAgentActive()) {
                agent->Show();
            }
        }

        internal void Draw() {
            if (!this._visible) {
                return;
            }

            ImGui.SetNextWindowSize(new Vector2(700, 600), ImGuiCond.FirstUseEver);

            if (!ImGui.Begin($"{Plugin.Name} Fitting Room", ref this._visible)) {
                ImGui.End();
                return;
            }

            this.DrawInner();

            ImGui.End();
        }

        private void DrawInner() {
            if (this._entries == null) {
                this._entriesTask ??= Task.Run(BuildEntries);
                if (!this._entriesTask.IsCompleted) {
                    ImGui.TextDisabled("Loading glamour...");
                    return;
                }

                if (this._entriesTask.IsFaulted) {
                    if (!this._loadErrorLogged) {
                        this._loadErrorLogged = true;
                        Service.Log.Error(this._entriesTask.Exception, "Failed to load the Fitting Room");
                    }

                    ImGui.TextColored(ImGuiColors.DalamudRed, "The Fitting Room couldn't load. Check /xllog for details.");
                    return;
                }

                this._entries = this._entriesTask.Result;
            }

            this._dresserItems = ItemRow.GetDresserItemIds();

            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##fitting-room-search", "Search all glamour...", ref this._search, 256)) {
                this._resultsDirty = true;
            }

            this.DrawFilters();
            this.DrawViewOptions();
            this.DrawPreviewOptions();

            if (this._resultsDirty || this.CurrentResultsState() != this._resultsState) {
                this.UpdateResults();
            }

            if (ImGui.BeginChild("fitting-room-slots", new Vector2(150 * ImGuiHelpers.GlobalScale, 0), true)) {
                if (ImGui.RadioButton("All", !this._showHistory)) {
                    this._showHistory = false;
                    this._resultsDirty = true;
                }

                ImGui.SameLine();
                if (ImGui.RadioButton("Recent", this._showHistory)) {
                    this._showHistory = true;
                    this._resultsDirty = true;
                }

                if (ImGui.IsItemHovered()) {
                    ImGui.SetTooltip($"The last {TryOnHistory.MaxPerSlot} pieces you tried on in each slot, from anywhere in the game");
                }

                ImGui.Separator();

                foreach (var group in Groups) {
                    var count = this._results[group].Count;
                    var label = $"{Favorites.GroupName(group)} ({count:N0})##fitting-room-slot-{group}";

                    if (count == 0) {
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
                    }

                    if (ImGui.Selectable(label, group == this._selectedGroup)) {
                        this._selectedGroup = group;
                    }

                    if (count == 0) {
                        ImGui.PopStyleColor();
                    }
                }
            }

            ImGui.EndChild();

            ImGui.SameLine();

            // the type filter and history button stay put; only the list underneath them scrolls
            if (ImGui.BeginChild("fitting-room-items", Vector2.Zero, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)) {
                this.DrawTypeFilter();

                var history = this.Ui.Plugin.TryOnHistory;
                if (this._showHistory && history.Get(this._selectedGroup).Count > 0) {
                    if (ImGui.SmallButton($"Clear {Favorites.GroupName(this._selectedGroup)} history")) {
                        history.Clear(this._selectedGroup);
                    }
                }

                if (ImGui.BeginChild("fitting-room-item-list")) {
                    this.DrawItems(this._results[this._selectedGroup]);
                }

                ImGui.EndChild();
            }

            ImGui.EndChild();
        }

        private void DrawFilters() {
            var filters = this.Filters;
            var active = filters.ActiveCount;
            var label = active > 0 ? $"Filters ({active} active)###fitting-room-filters" : "Filters###fitting-room-filters";

            if (!ImGui.CollapsingHeader(label)) {
                return;
            }

            var changed = false;
            var save = false;

            if (ImGui.BeginTable("fitting-room-filter-table", 3, ImGuiTableFlags.SizingStretchSame)) {
                ImGui.TableNextColumn();
                changed |= DrawCombo("##dyes", ref filters.Dyes, DyeFilterNames);

                ImGui.TableNextColumn();
                changed |= this.DrawExpansionCombo();

                ImGui.TableNextColumn();
                changed |= this.DrawJobCombo();

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                if (this._maxLevel == 0) {
                    this._maxLevel = DataCache.GlamourItems.Value.Max(item => item.LevelEquip);
                }

                if (ImGui.SliderInt("##max-level", ref filters.MaxLevel, 0, this._maxLevel, filters.MaxLevel == 0 ? "Any level" : "Up to level %d")) {
                    this._resultsDirty = true;
                }

                // only save once the slider is let go, rather than every frame while dragging
                save |= ImGui.IsItemDeactivatedAfterEdit();

                ImGui.TableNextColumn();
                changed |= this.DrawRarityCombo();

                ImGui.TableNextColumn();
                changed |= DrawCombo("##owned", ref filters.Owned, OwnedFilterNames);
                if (filters.Owned != OwnedFilter.Any && ImGui.IsItemHovered() && GameFunctions.DresserContents.Count == 0) {
                    ImGui.SetTooltip("Open your glamour dresser and armoire once so Glamaholic knows what you own.");
                }

                ImGui.TableNextColumn();
                changed |= DrawCombo("##gender", ref filters.Gender, GenderFilterNames);
                if (filters.Gender == GenderFilter.WearableByMe && ImGui.IsItemHovered()) {
                    ImGui.SetTooltip("Hides pieces your character's race or gender can't wear");
                }

                ImGui.TableNextColumn();
                changed |= this.DrawOutfitSetCombo();

                ImGui.TableNextColumn();
                changed |= ImGui.Checkbox("Favorites only", ref filters.FavoritesOnly);

                ImGui.TableNextColumn();
                changed |= ImGui.Checkbox("Outfit set pieces only", ref filters.OutfitSetPiecesOnly);

                ImGui.TableNextColumn();
                changed |= ImGui.Checkbox("Hide duplicate looks", ref filters.HideDuplicateLooks);
                if (ImGui.IsItemHovered()) {
                    ImGui.SetTooltip("Only show one item for each look. Hover an item to see how many others look the same.");
                }

                ImGui.EndTable();
            }

            if (active > 0 && ImGui.Button("Clear filters")) {
                filters.Dyes = DyeFilter.Any;
                filters.Expansion = -1;
                filters.Job = 0;
                filters.MaxLevel = 0;
                filters.Rarity = 0;
                filters.Owned = OwnedFilter.Any;
                filters.Gender = GenderFilter.Any;
                filters.FavoritesOnly = false;
                filters.OutfitSetPiecesOnly = false;
                filters.HideDuplicateLooks = false;
                filters.OutfitSet = 0;
                filters.Types.Clear();
                filters.Traits.Clear();
                changed = true;
            }

            if (changed) {
                this._resultsDirty = true;
            }

            if (changed || save) {
                this.Ui.Plugin.SaveConfig();
            }

            ImGui.Spacing();
        }

        private static bool DrawCombo<T>(string id, ref T value, string[] names, float width = -1) where T : struct, Enum {
            var index = Convert.ToInt32(value);
            ImGui.SetNextItemWidth(width);
            if (!ImGui.Combo(id, ref index, names, names.Length)) {
                return false;
            }

            value = (T) Enum.ToObject(typeof(T), index);
            return true;
        }

        private bool DrawExpansionCombo() {
            var sheet = Service.DataManager.GetExcelSheet<ExVersion>()!;
            var current = this.Filters.Expansion >= 0 && sheet.GetRowOrDefault((uint) this.Filters.Expansion) is { } ex
                ? ex.Name.ExtractText()
                : "Any expansion";

            var changed = false;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##expansion", current)) {
                if (ImGui.Selectable("Any expansion", this.Filters.Expansion < 0)) {
                    this.Filters.Expansion = -1;
                    changed = true;
                }

                foreach (var row in sheet) {
                    if (ImGui.Selectable(row.Name.ExtractText(), this.Filters.Expansion == row.RowId)) {
                        this.Filters.Expansion = (int) row.RowId;
                        changed = true;
                    }
                }

                ImGui.EndCombo();
            }

            return changed;
        }

        // the type dropdown and trait checkboxes for the selected slot
        private void DrawTypeFilter() {
            var group = this._selectedGroup;
            if (!ItemTypes.HasTypes(group)) {
                return;
            }

            var filters = this.Filters;
            var changed = false;

            var types = (ItemTypes.TypeOrder(group) ?? this._typeCounts.Keys
                    .Where(key => key.Item1 == group)
                    .Select(key => key.Item2)
                    .Order())
                .ToList();

            int Count(string type) => this._typeCounts.GetValueOrDefault((group, type));
            var total = types.Sum(Count);

            var selected = filters.Types.GetValueOrDefault(group);
            var preview = selected == null ? $"Any type ({total:N0})" : $"{selected} ({Count(selected):N0})";

            ImGui.SetNextItemWidth(220 * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginCombo("##item-type", preview, ImGuiComboFlags.HeightLarge)) {
                if (ImGui.Selectable($"Any type ({total:N0})", selected == null)) {
                    filters.Types.Remove(group);
                    changed = true;
                }

                foreach (var type in types) {
                    var count = Count(type);

                    // keep the current choice selectable even when nothing matches, so it can be seen and changed
                    var flags = count == 0 && type != selected ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None;
                    if (ImGui.Selectable($"{type} ({count:N0})", type == selected, flags)) {
                        filters.Types[group] = type;
                        changed = true;
                    }
                }

                ImGui.EndCombo();
            }

            var traits = filters.Traits.GetValueOrDefault(group);
            foreach (var option in ItemTypes.GetTraitOptions(group)) {
                ImGui.SameLine();

                var on = (traits & option.Trait) != 0;
                if (ImGui.Checkbox(option.Label, ref on)) {
                    traits = on ? traits | option.Trait : traits & ~option.Trait;
                    if (traits == ItemTraits.None) {
                        filters.Traits.Remove(group);
                    } else {
                        filters.Traits[group] = traits;
                    }

                    changed = true;
                }

                if (ImGui.IsItemHovered()) {
                    ImGui.SetTooltip(option.Tooltip);
                }
            }

            if (changed) {
                this._resultsDirty = true;
                this.Ui.Plugin.SaveConfig();
            }
        }

        private bool DrawRarityCombo() {
            this._rarities ??= DataCache.GlamourItems.Value.Select(item => item.Rarity).Distinct().Order().ToList();

            var rarity = this.Filters.Rarity;
            var changed = false;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##rarity", rarity == 0 ? "Any rarity" : ItemRow.RarityName(rarity))) {
                if (ImGui.Selectable("Any rarity", rarity == 0)) {
                    this.Filters.Rarity = 0;
                    changed = true;
                }

                foreach (var option in this._rarities) {
                    var colour = ItemRow.RarityColour(option);
                    if (colour != null) {
                        ImGui.PushStyleColor(ImGuiCol.Text, colour.Value);
                    }

                    if (ImGui.Selectable(ItemRow.RarityName(option), rarity == option)) {
                        this.Filters.Rarity = option;
                        changed = true;
                    }

                    if (colour != null) {
                        ImGui.PopStyleColor();
                    }
                }

                ImGui.EndCombo();
            }

            return changed;
        }

        private bool DrawJobCombo() {
            this._jobs ??= LoadJobs();

            var job = this.Filters.Job;
            var current = job switch {
                0 => "Any job",
                FittingRoomFilters.CurrentJob => "My current job",
                _ => this._jobs.FirstOrDefault(entry => entry.Id == job).Name ?? "Any job",
            };

            var changed = false;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##job", current, ImGuiComboFlags.HeightLarge)) {
                if (ImGui.Selectable("Any job", job == 0)) {
                    this.Filters.Job = 0;
                    changed = true;
                }

                if (ImGui.Selectable("My current job", job == FittingRoomFilters.CurrentJob)) {
                    this.Filters.Job = FittingRoomFilters.CurrentJob;
                    changed = true;
                }

                ImGui.Separator();

                foreach (var (id, name) in this._jobs) {
                    if (ImGui.Selectable(name, job == id)) {
                        this.Filters.Job = id;
                        changed = true;
                    }
                }

                ImGui.EndCombo();
            }

            return changed;
        }

        private bool DrawOutfitSetCombo() {
            this._outfitSets ??= LoadOutfitSets();

            var setId = this.Filters.OutfitSet;
            var current = setId == 0 ? "Any outfit set" : this._outfitSets.FirstOrDefault(set => set.Id == setId).Name ?? "Any outfit set";

            var changed = false;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##outfit-set", current, ImGuiComboFlags.HeightLarge)) {
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##outfit-set-search", "Search outfit sets...", ref this._outfitSetSearch, 128);
                if (ImGui.IsWindowAppearing()) {
                    ImGui.SetKeyboardFocusHere(-1);
                }

                if (ImGui.Selectable("Any outfit set", setId == 0)) {
                    this.Filters.OutfitSet = 0;
                    changed = true;
                }

                foreach (var (id, name) in this._outfitSets) {
                    if (this._outfitSetSearch.Length > 0 && !name.Contains(this._outfitSetSearch, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    if (ImGui.Selectable(name, setId == id)) {
                        this.Filters.OutfitSet = id;
                        changed = true;
                    }
                }

                ImGui.EndCombo();
            }

            return changed;
        }

        private void DrawPreviewOptions() {
            if (!Interop.Glamourer.IsAvailable()) {
                ImGui.TextDisabled("Click a piece to preview it in the game's Fitting Room.");
                return;
            }

            ImGui.TextUnformatted("Preview on:");

            ImGui.SameLine();
            if (ImGui.RadioButton("Fitting Room window", !this.Config.FittingRoomPreviewOnCharacter)) {
                this.SetPreviewOnCharacter(false);
            }

            ImGui.SameLine();
            if (ImGui.RadioButton("My character (Glamourer)", this.Config.FittingRoomPreviewOnCharacter)) {
                this.SetPreviewOnCharacter(true);
            }

            if (this.Config.FittingRoomPreviewOnCharacter) {
                ImGui.SameLine();
                if (ImGui.Button("Revert") && Service.ObjectTable.LocalPlayer is { } player) {
                    Interop.Glamourer.RevertState(player.ObjectIndex);
                    this._previewed.Clear();
                }

                if (ImGui.IsItemHovered()) {
                    ImGui.SetTooltip("Undo all previewed gear on your character");
                }
            }
        }

        private void SetPreviewOnCharacter(bool onCharacter) {
            if (this.Config.FittingRoomPreviewOnCharacter == onCharacter) {
                return;
            }

            this.Config.FittingRoomPreviewOnCharacter = onCharacter;
            this.Ui.Plugin.SaveConfig();
            this._previewed.Clear();

            if (!onCharacter) {
                OpenGameFittingRoom();
            }
        }

        private void DrawViewOptions() {
            var changed = false;

            if (ImGui.RadioButton("List", this.Config.FittingRoomView == FittingRoomView.List)) {
                this.Config.FittingRoomView = FittingRoomView.List;
                changed = true;
            }

            ImGui.SameLine();
            if (ImGui.RadioButton("Grid", this.Config.FittingRoomView == FittingRoomView.Grid)) {
                this.Config.FittingRoomView = FittingRoomView.Grid;
                changed = true;
            }

            ImGui.SameLine();
            changed |= DrawCombo("##icon-size", ref this.Config.FittingRoomIconSize, IconSizeNames, 120 * ImGuiHelpers.GlobalScale);

            ImGui.SameLine();
            if (ImGui.Checkbox("Group variants", ref this.Config.FittingRoomGroupVariants)) {
                this._resultsDirty = true;
                changed = true;
            }

            if (ImGui.IsItemHovered()) {
                ImGui.SetTooltip("Show items that only differ by role (\"of Fending\", \"of Healing\", ...) or by a colour or gem\nin brackets (\"(Grey)\", \"(Turquoise)\", ...) as one entry. Click it to pick a variant.");
            }

            if (changed) {
                this.Ui.Plugin.SaveConfig();
            }
        }

        private void DrawItems(List<Listing> listings) {
            if (listings.Count == 0) {
                var filtered = this._search.Length > 0 || this.Filters.ActiveCount > 0;
                if (this._showHistory && this.Ui.Plugin.TryOnHistory.Get(this._selectedGroup).Count == 0) {
                    ImGui.TextDisabled("You haven't tried anything on in this slot yet.");
                } else {
                    ImGui.TextDisabled(filtered ? "Nothing in this slot matches your search and filters." : "Nothing here.");
                }

                return;
            }

            Listing? openVariants = null;
            if (this.Config.FittingRoomView == FittingRoomView.Grid) {
                this.DrawGrid(listings, ref openVariants);
            } else {
                this.DrawList(listings, ref openVariants);
            }

            // opened out here so the popup survives the clicked row scrolling out of the clipper
            if (openVariants != null) {
                this._variantsListing = openVariants;
                ImGui.OpenPopup("fitting-room-variants");
            }

            this.DrawVariantsPopup();
        }

        private void DrawList(List<Listing> listings, ref Listing? openVariants) {
            var height = ItemRow.RowHeight(this.Config.FittingRoomIconSize);

            var clipper = ImGui.ImGuiListClipper();
            clipper.Begin(listings.Count, height + ImGui.GetStyle().ItemSpacing.Y);
            while (clipper.Step()) {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++) {
                    this.DrawListing(listings[i], false, height, ref openVariants);
                }
            }

            clipper.End();
        }

        private void DrawGrid(List<Listing> listings, ref Listing? openVariants) {
            var size = ItemRow.TileSize(this.Config.FittingRoomIconSize);
            var spacing = ImGui.GetStyle().ItemSpacing;
            var columns = Math.Max(1, (int) ((ImGui.GetContentRegionAvail().X + spacing.X) / (size + spacing.X)));
            var rows = (listings.Count + columns - 1) / columns;

            var clipper = ImGui.ImGuiListClipper();
            clipper.Begin(rows, size + spacing.Y);
            while (clipper.Step()) {
                for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; row++) {
                    for (var column = 0; column < columns; column++) {
                        var index = row * columns + column;
                        if (index >= listings.Count) {
                            break;
                        }

                        if (column > 0) {
                            ImGui.SameLine();
                        }

                        this.DrawListing(listings[index], true, size, ref openVariants);
                    }
                }
            }

            clipper.End();
        }

        private void DrawListing(Listing listing, bool tile, float size, ref Listing? openVariants) {
            var previewedId = this.PreviewedId(listing.First.Group);

            if (!listing.IsFamily) {
                var entry = listing.First;
                var selected = entry.Item.RowId == previewedId;
                var details = this.GetDetails(entry);
                var extraMenu = this.GetExtraMenu(entry);
                const string hint = "Click to preview, right-click for more";

                var result = tile
                    ? ItemRow.DrawTile(this.Ui, entry.Item, selected, this._dresserItems, hint, details, extraMenu, size)
                    : ItemRow.Draw(this.Ui, entry.Item, selected, this._dresserItems, hint, details, extraMenu, size);

                if (result.Clicked) {
                    this.Preview(entry);
                }

                return;
            }

            ImGui.PushID($"family-{listing.First.Item.RowId}");

            // show whichever variant is being previewed, so the icon matches what's on screen
            var shown = listing.Variants.FirstOrDefault(variant => variant.Item.RowId == previewedId);
            var visual = new ItemVisual(
                (shown ?? listing.First).Item.Icon,
                listing.Name,
                listing.Variants.Any(variant => this.Ui.Plugin.Favorites.Contains(variant.Item.RowId)),
                listing.Variants.Max(variant => variant.Item.DyeCount),
                listing.Variants.Any(variant => ItemRow.IsOwned(this.Ui, variant.Item.RowId, this._dresserItems)),
                listing.Variants.Count,
                listing.First.Item.Rarity
            );

            var state = tile
                ? ItemRow.DrawTileVisual(this.Ui, visual, shown != null, size)
                : ItemRow.DrawRowVisual(this.Ui, visual, shown != null, size);

            if (state.Hovered) {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(listing.Name);
                ImGui.TextDisabled($"{Favorites.GroupName(listing.First.Group)} · {listing.Variants.Count} variants");
                foreach (var variant in listing.Variants) {
                    ImGui.TextUnformatted($"  {variant.Name}");
                }

                ImGui.Separator();
                ImGui.TextDisabled("Click to choose a variant");
                ImGui.EndTooltip();
            }

            if (state.Clicked || state.RightClicked) {
                openVariants = listing;
            }

            ImGui.PopID();
        }

        private void DrawVariantsPopup() {
            if (!ImGui.BeginPopup("fitting-room-variants")) {
                return;
            }

            var listing = this._variantsListing;
            if (listing != null) {
                ImGui.TextUnformatted(listing.Name);
                ImGui.TextDisabled("Click a variant to preview it");
                ImGui.Separator();

                var height = ItemRow.RowHeight(this.Config.FittingRoomIconSize);
                foreach (var variant in listing.Variants) {
                    var result = ItemRow.Draw(
                        this.Ui,
                        variant.Item,
                        variant.Item.RowId == this.PreviewedId(variant.Group),
                        this._dresserItems,
                        "Click to preview, right-click for more",
                        this.GetDetails(variant),
                        this.GetExtraMenu(variant),
                        height,
                        // keep the list open so variants can be compared one after another
                        ImGuiSelectableFlags.DontClosePopups
                    );

                    if (result.Clicked) {
                        this.Preview(variant);
                    }
                }
            }

            ImGui.EndPopup();
        }

        private uint PreviewedId(PlateSlot group) {
            return this._previewed.GetValueOrDefault(group);
        }

        private string GetDetails(Entry entry) {
            var lines = new List<string>();

            if (entry.Patch != null) {
                var expansion = Service.DataManager.GetExcelSheet<ExVersion>()!.GetRowOrDefault((uint) entry.Expansion);
                lines.Add(expansion != null ? $"Added in patch {entry.Patch} ({expansion.Value.Name})" : $"Added in patch {entry.Patch}");
            }

            foreach (var setId in entry.OutfitSets) {
                lines.Add($"Part of {this.GetOutfitSetName(setId)}");
            }

            if (this._duplicateCounts.TryGetValue(entry.Item.RowId, out var duplicates) && duplicates > 0) {
                lines.Add($"{duplicates} other item{(duplicates == 1 ? "" : "s")} look{(duplicates == 1 ? "s" : "")} the same (middle-click to list)");
            }

            return string.Join('\n', lines);
        }

        private System.Action? GetExtraMenu(Entry entry) {
            if (!this._showHistory && entry.OutfitSets.Length == 0) {
                return null;
            }

            return () => {
                if (this._showHistory && ImGui.MenuItem("Remove from history")) {
                    this.Ui.Plugin.TryOnHistory.Remove(entry.Item.RowId);
                }

                this.DrawOutfitSetMenuItems(entry);
            };
        }

        private void DrawOutfitSetMenuItems(Entry entry) {
            foreach (var setId in entry.OutfitSets) {
                if (ImGui.MenuItem($"Show {this.GetOutfitSetName(setId)}")) {
                    this.Filters.OutfitSet = setId;
                    this._search = string.Empty;
                    this._resultsDirty = true;
                }
            }
        }

        private string GetOutfitSetName(uint setId) {
            this._outfitSets ??= LoadOutfitSets();
            return this._outfitSets.FirstOrDefault(set => set.Id == setId).Name ?? "an outfit set";
        }

        private void Preview(Entry entry) {
            if (this.PreviewOnCharacter) {
                if (Service.ObjectTable.LocalPlayer is not { } player) {
                    return;
                }

                Interop.Glamourer.SetItem(player.ObjectIndex, entry.Group, entry.Item.RowId);

                // Glamourer previews don't go through the game's try on, so the history hook doesn't see them
                this.Ui.Plugin.TryOnHistory.Record(entry.Item.RowId);
            } else {
                this.Ui.Plugin.Functions.TryOn(entry.Item.RowId, 0, 0);
            }

            this._previewed[entry.Group] = entry.Item.RowId;
        }

        private (int, int, bool, uint, int) CurrentResultsState() {
            return (
                this.Ui.Plugin.Favorites.Version,
                GameFunctions.DresserContents.Count,
                this.Ui.Plugin.Functions.ArmoireLoaded,
                this.ResolveJob(),
                // previewing from the Recent list moves the piece to the top of the history; reordering the list
                // under the cursor would be confusing, so only refresh when pieces are added or removed
                this.Ui.Plugin.TryOnHistory.MembershipVersion
            );
        }

        private uint ResolveJob() {
            if (this.Filters.Job != FittingRoomFilters.CurrentJob) {
                return this.Filters.Job;
            }

            return Service.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0;
        }

        private void UpdateResults() {
            this._resultsDirty = false;
            this._resultsState = this.CurrentResultsState();

            var filters = this.Filters;
            var favorites = this.Ui.Plugin.Favorites;

            // every word has to match, in any order
            var words = this._search.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var job = this.ResolveJob();
            var jobCategories = job != 0 ? this.GetJobCategories(job) : null;

            HashSet<uint>? owned = null;
            if (filters.Owned != OwnedFilter.Any || filters.HideDuplicateLooks) {
                owned = this.Ui.Plugin.Functions.GetArmoireItemIds();
                owned.UnionWith(this._dresserItems);
            }

            var wearable = filters.Gender == GenderFilter.WearableByMe ? GetPlayerWearableCheck() : null;

            bool Matches(Entry entry) {
                var item = entry.Item;

                if (!words.All(word => entry.SearchText.Contains(word))) {
                    return false;
                }

                var dyesMatch = filters.Dyes switch {
                    DyeFilter.Dyeable => item.DyeCount > 0,
                    DyeFilter.TwoChannels => item.DyeCount >= 2,
                    DyeFilter.NotDyeable => item.DyeCount == 0,
                    _ => true,
                };

                var ownedMatch = filters.Owned switch {
                    OwnedFilter.Owned => owned!.Contains(item.RowId),
                    OwnedFilter.NotOwned => !owned!.Contains(item.RowId),
                    _ => true,
                };

                return dyesMatch
                       && ownedMatch
                       && (filters.Expansion < 0 || entry.Expansion == filters.Expansion)
                       && (jobCategories == null || jobCategories.Contains(item.ClassJobCategory.RowId))
                       && (filters.MaxLevel == 0 || item.LevelEquip <= filters.MaxLevel)
                       && (filters.Rarity == 0 || item.Rarity == filters.Rarity)
                       && GenderMatches(item, filters.Gender, wearable)
                       && (!filters.Traits.TryGetValue(entry.Group, out var traits) || (entry.Traits & traits) == traits)
                       && (!filters.FavoritesOnly || favorites.Contains(item.RowId))
                       && (!filters.OutfitSetPiecesOnly || entry.OutfitSets.Length > 0)
                       && (filters.OutfitSet == 0 || entry.OutfitSets.Contains(filters.OutfitSet));
            }

            List<Entry> matches;
            this._duplicateCounts = new Dictionary<uint, int>();

            if (this._showHistory) {
                // most recently tried first, exactly as tried, so no merging of duplicates or variants
                var history = this.Ui.Plugin.TryOnHistory;
                this._entriesById ??= this._entries!.ToDictionary(entry => entry.Item.RowId);
                matches = Groups
                    .SelectMany(group => history.Get(group))
                    .Select(id => this._entriesById.GetValueOrDefault(id))
                    .Where(entry => entry != null && Matches(entry))
                    .Select(entry => entry!)
                    .ToList();
            } else {
                matches = this._entries!.Where(Matches).ToList();
            }

            // count types before filtering by type, so the dropdown shows what each choice would give
            this._typeCounts = matches
                .Where(entry => entry.Type != null)
                .GroupBy(entry => (entry.Group, entry.Type!))
                .ToDictionary(type => type.Key, type => type.Count());

            matches = matches
                .Where(entry => !filters.Types.TryGetValue(entry.Group, out var type) || entry.Type == type)
                .ToList();

            if (!this._showHistory && filters.HideDuplicateLooks) {
                matches = this.HideDuplicates(matches, owned!);
            }

            // role variants are grouped after filtering, so a family only lists the variants that match
            var listings = this.Config.FittingRoomGroupVariants && !this._showHistory
                ? matches
                    .GroupBy(entry => (entry.Group, entry.FamilyName))
                    .Select(family => new Listing(family.Count() > 1 ? family.Key.FamilyName : family.First().Name, family.ToList()))
                : matches.Select(entry => new Listing(entry.Name, [entry]));

            this._results = Groups.ToDictionary(group => group, _ => new List<Listing>());
            foreach (var listing in listings) {
                this._results[listing.First.Group].Add(listing);
            }

            // if the current slot has no matches, jump to the first one that does
            if (this._results[this._selectedGroup].Count == 0) {
                this._selectedGroup = Groups.FirstOrDefault(group => this._results[group].Count > 0, this._selectedGroup);
            }
        }

        private static bool GenderMatches(Item item, GenderFilter filter, Func<EquipRaceCategory, bool>? wearable) {
            if (filter == GenderFilter.Any) {
                return true;
            }

            // row 1 is "anyone"
            if (item.EquipRestriction.RowId <= 1 || item.EquipRestriction.ValueNullable is not { } category) {
                return filter is GenderFilter.WearableByMe or GenderFilter.Unrestricted;
            }

            return filter switch {
                GenderFilter.WearableByMe => wearable?.Invoke(category) ?? true,
                GenderFilter.MaleOnly => category.Male && !category.Female,
                GenderFilter.FemaleOnly => category.Female && !category.Male,
                _ => false,
            };
        }

        // checks a restriction against the player's race and gender, or null if there's no player to check
        private static Func<EquipRaceCategory, bool>? GetPlayerWearableCheck() {
            if (Service.ObjectTable.LocalPlayer is not { } player) {
                return null;
            }

            var race = player.Customize[(int) CustomizeIndex.Race];
            var female = player.Customize[(int) CustomizeIndex.Gender] == 1;

            return category => (female ? category.Female : category.Male) && race switch {
                1 => category.Hyur,
                2 => category.Elezen,
                3 => category.Lalafell,
                4 => category.Miqote,
                5 => category.Roegadyn,
                6 => category.AuRa,
                7 => category.Hrothgar,
                8 => category.Viera,
                _ => true,
            };
        }

        // Keeps one item per look, preferring one that's owned, then a favorite, then the first by name.
        private List<Entry> HideDuplicates(List<Entry> entries, HashSet<uint> owned) {
            var favorites = this.Ui.Plugin.Favorites;
            var shown = new List<Entry>();

            foreach (var look in entries.GroupBy(entry => entry.LookKey)) {
                var best = look
                    .OrderByDescending(entry => owned.Contains(entry.Item.RowId))
                    .ThenByDescending(entry => favorites.Contains(entry.Item.RowId))
                    .First();

                shown.Add(best);
                this._duplicateCounts[best.Item.RowId] = look.Count() - 1;
            }

            // grouping loses the name order
            return shown.OrderBy(entry => entry.SearchText).ToList();
        }

        // ClassJobCategory rows that include the given job, so items can be checked without reflection
        private HashSet<uint> GetJobCategories(uint jobId) {
            if (this._jobCategories.TryGetValue(jobId, out var cached)) {
                return cached;
            }

            var categories = new HashSet<uint>();
            var job = Service.DataManager.GetExcelSheet<ClassJob>(ClientLanguage.English)!.GetRowOrDefault(jobId);
            var property = job == null
                ? null
                : typeof(ClassJobCategory).GetProperty(job.Value.Abbreviation.ExtractText(), BindingFlags.Public | BindingFlags.Instance);

            if (property != null) {
                foreach (var category in Service.DataManager.GetExcelSheet<ClassJobCategory>(ClientLanguage.English)!) {
                    if (property.GetValue(category) is true) {
                        categories.Add(category.RowId);
                    }
                }
            }

            this._jobCategories[jobId] = categories;
            return categories;
        }

        private static List<Entry> BuildEntries() {
            var outfitSets = DataCache.OutfitSetsByPiece.Value;
            return DataCache.GlamourItems.Value
                .Select(item => {
                    var patch = ItemPatches.GetPatch(item.RowId);
                    var name = item.Name.ExtractText();
                    return new Entry(
                        item,
                        Favorites.GetGroup(item),
                        name,
                        RoleSuffix.Replace(VariantSuffix.Replace(name, string.Empty), string.Empty),
                        name.ToLowerInvariant(),
                        patch,
                        ItemPatches.GetExpansion(patch),
                        outfitSets.GetValueOrDefault(item.RowId, ImmutableArray<uint>.Empty),
                        ItemTypes.GetType(item, Favorites.GetGroup(item), name),
                        ItemTypes.GetTraits(item, Favorites.GetGroup(item))
                    );
                })
                .ToList();
        }

        // battle jobs first, then crafters and gatherers
        private static List<(uint, string)> LoadJobs() {
            return Service.DataManager.GetExcelSheet<ClassJob>()!
                .Where(job => job.RowId != 0 && (job.JobIndex > 0 || job.DohDolJobIndex >= 0))
                .OrderBy(job => job.JobIndex > 0 ? 0 : 1)
                .ThenBy(job => job.RowId)
                .Select(job => (job.RowId, $"{job.Abbreviation.ExtractText()} - {job.Name.ExtractText()}"))
                .ToList();
        }

        private static List<(uint, string)> LoadOutfitSets() {
            var items = Service.DataManager.GetExcelSheet<Item>()!;
            return Service.DataManager.GetExcelSheet<MirageStoreSetItem>()!
                .Select(row => (row.RowId, items.GetRowOrDefault(row.RowId)?.Name.ExtractText() ?? string.Empty))
                .Where(set => set.Item2.Length > 0)
                .OrderBy(set => set.Item2)
                .ToList();
        }
    }
}
