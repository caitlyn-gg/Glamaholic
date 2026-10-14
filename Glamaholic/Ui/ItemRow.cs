using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Numerics;

namespace Glamaholic.Ui {
    internal readonly record struct ItemRowResult(bool Clicked, bool DoubleClicked);

    internal readonly record struct ItemWidgetState(bool Clicked, bool DoubleClicked, bool RightClicked, bool MiddleClicked, bool Hovered);

    // What a row or tile shows. Variants > 1 marks an entry standing in for several items.
    internal readonly record struct ItemVisual(ushort Icon, string Label, bool Favorite, byte DyeCount, bool Owned, int Variants = 1, byte Rarity = 0);

    // Rows (list view) and tiles (grid view) for pieces of gear: favorite star, icon, name, dye channels and an owned
    // check, with a tooltip and a right-click menu for common actions. Shared by the Favorites tab and the Fitting Room.
    internal static class ItemRow {
        internal const uint FavoriteColour = 0xFF4DD9FF; // gold

        internal static float Height => RowHeight(IconSize.Small);

        // the colours the game uses for item names; common (white) items keep the normal text colour
        internal static uint? RarityColour(byte rarity) {
            return rarity switch {
                2 => 0xFFC0FFC0, // green
                3 => 0xFFFF9059, // blue
                4 => 0xFFFF8CB3, // purple
                7 => 0xFFB689FA, // pink
                _ => null,
            };
        }

        // e.g. "Female characters only" or "Viera (male) only"; null if anyone can wear it
        internal static string? RestrictionText(Item item) {
            if (item.EquipRestriction.RowId <= 1 || item.EquipRestriction.ValueNullable is not { } category) {
                return null;
            }

            var gender = category.Male == category.Female ? null : category.Male ? "male" : "female";
            var races = new List<string>();
            if (category.Hyur) races.Add("Hyur");
            if (category.Elezen) races.Add("Elezen");
            if (category.Lalafell) races.Add("Lalafell");
            if (category.Miqote) races.Add("Miqo'te");
            if (category.Roegadyn) races.Add("Roegadyn");
            if (category.AuRa) races.Add("Au Ra");
            if (category.Hrothgar) races.Add("Hrothgar");
            if (category.Viera) races.Add("Viera");

            if (races.Count == 8) {
                return gender == null ? null : $"{char.ToUpper(gender[0])}{gender[1..]} characters only";
            }

            return $"{string.Join("/", races)}{(gender != null ? $" ({gender})" : "")} only";
        }

        internal static string RarityName(byte rarity) {
            return rarity switch {
                1 => "Common (white)",
                2 => "Uncommon (green)",
                3 => "Rare (blue)",
                4 => "Relic (purple)",
                7 => "Aetherial (pink)",
                _ => $"Rarity {rarity}",
            };
        }

        internal static float RowHeight(IconSize size) {
            return size switch {
                IconSize.Small => ImGui.GetTextLineHeight() * 1.75f,
                IconSize.Medium => 40 * ImGuiHelpers.GlobalScale,
                _ => 56 * ImGuiHelpers.GlobalScale,
            };
        }

        internal static float TileSize(IconSize size) {
            return size switch {
                IconSize.Small => 40,
                IconSize.Medium => 56,
                _ => 80,
            } * ImGuiHelpers.GlobalScale;
        }

        // owned: base item ids in the glamour dresser; the armoire is checked separately.
        // details: extra tooltip lines; extraMenuItems: drawn at the top of the right-click menu
        internal static ItemRowResult Draw(PluginUi ui, Item item, bool selected, HashSet<uint> owned, string hint, string? details = null, System.Action? extraMenuItems = null, float? height = null, ImGuiSelectableFlags flags = ImGuiSelectableFlags.None) {
            ImGui.PushID((int) item.RowId);

            var visual = VisualFor(ui, item, owned);
            var state = DrawRowVisual(ui, visual, selected, height ?? Height, flags);
            HandleItem(ui, item, visual, state, hint, details, extraMenuItems);

            ImGui.PopID();
            return new ItemRowResult(state.Clicked, state.DoubleClicked);
        }

        internal static ItemRowResult DrawTile(PluginUi ui, Item item, bool selected, HashSet<uint> owned, string hint, string? details, System.Action? extraMenuItems, float size) {
            ImGui.PushID((int) item.RowId);

            var visual = VisualFor(ui, item, owned);
            var state = DrawTileVisual(ui, visual, selected, size);
            HandleItem(ui, item, visual, state, hint, details, extraMenuItems);

            ImGui.PopID();
            return new ItemRowResult(state.Clicked, state.DoubleClicked);
        }

        internal static bool IsOwned(PluginUi ui, uint itemId, HashSet<uint> dresser) {
            return dresser.Contains(itemId) || ui.Plugin.Functions.IsInArmoire(itemId);
        }

        private static ItemVisual VisualFor(PluginUi ui, Item item, HashSet<uint> owned) {
            return new ItemVisual(item.Icon, item.Name.ExtractText(), ui.Plugin.Favorites.Contains(item.RowId), item.DyeCount, IsOwned(ui, item.RowId, owned), 1, item.Rarity);
        }

        // Draws a row without any tooltip or menu, for callers that handle interaction themselves.
        internal static ItemWidgetState DrawRowVisual(PluginUi ui, ItemVisual visual, bool selected, float height, ImGuiSelectableFlags flags = ImGuiSelectableFlags.None) {
            var textOffset = (height - ImGui.GetTextLineHeight()) / 2;

            var clicked = ImGui.Selectable("##row", selected, flags, new Vector2(0, height));
            var state = CaptureState(clicked);

            ImGui.SameLine();
            var y = ImGui.GetCursorPosY();

            // gold star = favorite, dim star = not a favorite
            ImGui.SetCursorPosY(y + textOffset);
            ImGui.PushStyleColor(ImGuiCol.Text, visual.Favorite ? FavoriteColour : ImGui.GetColorU32(ImGuiCol.TextDisabled));
            Util.TextIcon(FontAwesomeIcon.Star);
            ImGui.PopStyleColor();

            ImGui.SameLine();
            ImGui.SetCursorPosY(y);
            var icon = ui.GetIcon(visual.Icon);
            if (icon != null) {
                ImGui.Image(icon.Handle, new Vector2(height));
            } else {
                ImGui.Dummy(new Vector2(height));
            }

            ImGui.SameLine();
            ImGui.SetCursorPosY(y + textOffset);
            if (RarityColour(visual.Rarity) is { } labelColour) {
                ImGui.PushStyleColor(ImGuiCol.Text, labelColour);
                ImGui.TextUnformatted(visual.Label);
                ImGui.PopStyleColor();
            } else {
                ImGui.TextUnformatted(visual.Label);
            }

            if (visual.Variants > 1) {
                ImGui.SameLine();
                ImGui.TextDisabled($"({visual.Variants} variants)");
            }

            if (visual.DyeCount > 0) {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
                Util.TextIcon(visual.DyeCount > 1 ? FontAwesomeIcon.Palette : FontAwesomeIcon.Tint);
                ImGui.PopStyleColor();
            }

            if (visual.Owned) {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.HealerGreen);
                Util.TextIcon(FontAwesomeIcon.Check);
                ImGui.PopStyleColor();
            }

            return state;
        }

        // Draws a square icon tile without any tooltip or menu, for callers that handle interaction themselves.
        internal static ItemWidgetState DrawTileVisual(PluginUi ui, ItemVisual visual, bool selected, float size) {
            var pos = ImGui.GetCursorScreenPos();
            var clicked = ImGui.Selectable("##tile", selected, ImGuiSelectableFlags.None, new Vector2(size));
            var state = CaptureState(clicked);

            var drawList = ImGui.GetWindowDrawList();
            var padding = 2 * ImGuiHelpers.GlobalScale;
            var max = pos + new Vector2(size);

            var icon = ui.GetIcon(visual.Icon);
            if (icon != null) {
                drawList.AddImage(icon.Handle, pos + new Vector2(padding), max - new Vector2(padding));
            }

            if (selected) {
                drawList.AddRect(pos, max, FavoriteColour, 0, ImDrawFlags.None, 2 * ImGuiHelpers.GlobalScale);
            }

            ImGui.PushFont(UiBuilder.IconFont);

            if (visual.Favorite) {
                DrawOutlinedText(drawList, pos + new Vector2(padding), FavoriteColour, FontAwesomeIcon.Star.ToIconString());
            }

            if (visual.Owned) {
                var check = FontAwesomeIcon.Check.ToIconString();
                var checkSize = ImGui.CalcTextSize(check);
                DrawOutlinedText(drawList, max - checkSize - new Vector2(padding), ImGui.ColorConvertFloat4ToU32(ImGuiColors.HealerGreen), check);
            }

            ImGui.PopFont();

            if (visual.Variants > 1) {
                var badge = $"x{visual.Variants}";
                var badgeSize = ImGui.CalcTextSize(badge);
                var badgePos = new Vector2(pos.X + padding, max.Y - badgeSize.Y - padding);
                drawList.AddRectFilled(badgePos - new Vector2(padding), badgePos + badgeSize + new Vector2(padding), 0xC0000000, 3 * ImGuiHelpers.GlobalScale);
                drawList.AddText(badgePos, 0xFFFFFFFF, badge);
            }

            return state;
        }

        private static void DrawOutlinedText(ImDrawListPtr drawList, Vector2 pos, uint colour, string text) {
            drawList.AddText(pos + new Vector2(1), 0xFF000000, text);
            drawList.AddText(pos, colour, text);
        }

        private static ItemWidgetState CaptureState(bool clicked) {
            var hovered = ImGui.IsItemHovered();
            return new ItemWidgetState(
                clicked,
                hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left),
                ImGui.IsItemClicked(ImGuiMouseButton.Right),
                Util.IsItemMiddleOrCtrlClicked(),
                hovered
            );
        }

        private static void HandleItem(PluginUi ui, Item item, ItemVisual visual, ItemWidgetState state, string hint, string? details, System.Action? extraMenuItems) {
            if (state.Hovered) {
                DrawTooltip(item, visual.Owned, hint, details);
            }

            if (state.MiddleClicked) {
                ui.AlternativeFinders.Add(new AlternativeFinder(ui, item));
            }

            if (state.RightClicked) {
                ImGui.OpenPopup("item-row-menu");
            }

            if (ImGui.BeginPopup("item-row-menu")) {
                DrawMenu(ui, item, extraMenuItems);
                ImGui.EndPopup();
            }
        }

        private static void DrawTooltip(Item item, bool owned, string hint, string? details) {
            ImGui.BeginTooltip();
            if (RarityColour(item.Rarity) is { } nameColour) {
                ImGui.PushStyleColor(ImGuiCol.Text, nameColour);
                ImGui.TextUnformatted(item.Name.ExtractText());
                ImGui.PopStyleColor();
            } else {
                ImGui.TextUnformatted(item.Name.ExtractText());
            }

            ImGui.TextDisabled($"{Favorites.GroupName(Favorites.GetGroup(item))} · Lv. {item.LevelEquip} · {RarityName(item.Rarity)}");
            ImGui.TextUnformatted(item.DyeCount switch {
                0 => "Not dyeable",
                1 => "1 dye channel",
                _ => $"{item.DyeCount} dye channels",
            });

            if (owned) {
                ImGui.TextUnformatted("In your glamour dresser or armoire");
            }

            if (RestrictionText(item) is { } restriction) {
                ImGui.TextColored(ImGuiColors.DalamudOrange, restriction);
            }

            if (!string.IsNullOrEmpty(details)) {
                ImGui.TextUnformatted(details);
            }

            ImGui.Separator();
            ImGui.TextDisabled(hint);
            ImGui.EndTooltip();
        }

        private static void DrawMenu(PluginUi ui, Item item, System.Action? extraMenuItems) {
            if (extraMenuItems != null) {
                extraMenuItems();
                ImGui.Separator();
            }

            if (ImGui.MenuItem("Try on")) {
                ui.Plugin.Functions.TryOn(item.RowId, 0, 0);
            }

            if (ImGui.MenuItem("Copy name")) {
                ImGui.SetClipboardText(item.Name.ExtractText());
            }

            if (ImGui.MenuItem("Find items with the same model")) {
                ui.AlternativeFinders.Add(new AlternativeFinder(ui, item));
            }

            ImGui.Separator();

            var favorites = ui.Plugin.Favorites;
            var isFavorite = favorites.Contains(item.RowId);
            if (ImGui.MenuItem(isFavorite ? "Remove from favorites" : "Add to favorites")) {
                if (isFavorite) {
                    favorites.Remove(item.RowId);
                } else {
                    favorites.Add(item.RowId);
                }
            }
        }

        internal static HashSet<uint> GetDresserItemIds() {
            var ids = new HashSet<uint>();
            foreach (var saved in GameFunctions.DresserContents) {
                ids.Add(saved.ItemId % Util.HqItemOffset);
            }

            return ids;
        }
    }
}
