using Dalamud.Game;
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

        public static int GetNumStainSlots(uint itemId) =>
            Service.DataManager.GetExcelSheet<Item>(ClientLanguage.English)!.GetRowOrDefault(itemId)?.DyeCount ?? 0;

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
