using Dalamud.Game;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Component.Exd;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Glamaholic {
    // Lazy loading and caching of important data which requires filtering or transforming
    internal class DataCache {
        public static Lazy<ImmutableList<Item>> EquippableItems { get; } =
            new(() => Service.DataManager.GetExcelSheet<Item>(ClientLanguage.English)!
                .Where(row => row.EquipSlotCategory.RowId != 0 &&
                       row.EquipSlotCategory.Value!.SoulCrystal == 0)
                .ToImmutableList());

        // Every item that can be used as glamour, sorted by name.
        public static Lazy<ImmutableList<Item>> GlamourItems { get; } =
            new(() => EquippableItems.Value
                .Where(item => !Util.IsItemSkipped(item) && Util.GetSlot(item) != null)
                .OrderBy(item => item.Name.ExtractText())
                .ToImmutableList());

        // Maps an item to its row in the Cabinet (armoire) sheet.
        public static Lazy<ImmutableDictionary<uint, uint>> ArmoireRowsByItem { get; } =
            new(() => Service.DataManager.GetExcelSheet<Cabinet>()!
                .Where(row => row.Item.RowId != 0)
                .GroupBy(row => row.Item.RowId)
                .ToImmutableDictionary(group => group.Key, group => group.First().RowId));

        // Maps each piece of an outfit set (as sold in the glamour dresser) to the sets containing it.
        // Sets are identified by the item id of the outfit itself.
        public static Lazy<ImmutableDictionary<uint, ImmutableArray<uint>>> OutfitSetsByPiece { get; } =
            new(() => {
                var sets = new Dictionary<uint, List<uint>>();
                foreach (var row in Service.DataManager.GetExcelSheet<MirageStoreSetItem>()!) {
                    uint[] pieces = [
                        row.MainHand.RowId, row.OffHand.RowId, row.Head.RowId, row.Body.RowId, row.Hands.RowId,
                        row.Legs.RowId, row.Feet.RowId, row.Earrings.RowId, row.Necklace.RowId, row.Bracelets.RowId,
                        row.Ring.RowId,
                    ];

                    foreach (var piece in pieces) {
                        if (piece == 0) {
                            continue;
                        }

                        if (!sets.TryGetValue(piece, out var list)) {
                            sets[piece] = list = new List<uint>();
                        }

                        list.Add(row.RowId);
                    }
                }

                return sets.ToImmutableDictionary(entry => entry.Key, entry => entry.Value.ToImmutableArray());
            });

        // Maps a slot and packed model id (as in Item.ModelMain) to the first item using that model.
        // Rings are always keyed as RightRing since either ring slot can show any ring.
        public static Lazy<ImmutableDictionary<(PlateSlot, ulong), Item>> ItemsByModel { get; } =
            new(() => {
                var items = new Dictionary<(PlateSlot, ulong), Item>();
                foreach (var item in EquippableItems.Value) {
                    if (item.ModelMain == 0 || Util.IsItemSkipped(item) || Util.GetSlot(item) is not { } slot) {
                        continue;
                    }

                    if (slot == PlateSlot.LeftRing) {
                        slot = PlateSlot.RightRing;
                    }

                    items.TryAdd((slot, item.ModelMain), item);
                }

                return items.ToImmutableDictionary();
            });

        public static Lazy<ImmutableDictionary<string, byte>> StainsByName { get; } =
            new (() =>
                Service.DataManager.GetExcelSheet<Stain>(ClientLanguage.English)!
                    .Where(static row => row.RowId != 0 && !row.Name.IsEmpty)
                    .ToImmutableDictionary(static row =>
                        row.Name.ExtractText().Trim().ToLower(), static row => (byte) row.RowId));

        public static Lazy<ImmutableDictionary<byte, CachedStain>> Stains { get; } =
            new(() =>
                Service.DataManager.GetExcelSheet<Stain>(ClientLanguage.English)!
                    .Where(static row => row.RowId != 0 && !row.Name.IsEmpty)
                    .ToImmutableDictionary(static row => (byte) row.RowId, static row => {
                        uint[] itemIds = new uint[row.Item.Count];
                        int i = 0;
                        foreach (RowRef<Item> itemRef in row.Item)
                            itemIds[i++] = itemRef.RowId;
                        
                        string itemName = row.Item.Count != 0 ? row.Item[0].Value.Name.ExtractText() : "(no items)";
                        return new CachedStain(row.Name.ExtractText(), itemIds, itemName);
                    }));

        public static int GetNumStainSlots(uint itemId) {
            var (id, kind) = ItemUtil.GetBaseId(itemId);
            return Service.DataManager.GetExcelSheet<Item>(ClientLanguage.English)!.GetRowOrDefault(id)?.DyeCount ?? 0;
        }

        public static HashSet<byte> ValuableStains { get; } = [101, 102, 103]; // Pure White, Jet Black, Pastel Pink
    }

    internal class CachedStain {
        public string StainName { get; }
        public uint[] ItemIds { get; }
        public string ItemName { get; }

        public CachedStain(string stainName, uint[] itemIds, string itemName) {
            StainName = stainName;
            ItemIds = itemIds;
            ItemName = itemName;
        }
    }
}
