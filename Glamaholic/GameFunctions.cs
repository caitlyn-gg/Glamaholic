using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Lumina.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using static FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentMiragePrismMiragePlateData;
using Cabinet = FFXIVClientStructs.FFXIV.Client.Game.UI.Cabinet;

namespace Glamaholic {
    internal unsafe class GameFunctions : IDisposable {
        #region Dynamic
        private static class FunctionDelegates {
            internal unsafe delegate int GetCabinetItemIdDelegate(Cabinet* _this, uint baseItemId);
        }

        /* 
         * Returns the cabinet id for an item in the Armoire or -1 if not found.
         * 
         * _this should always be UIState::Cabinet
         * 
         * Updating:
         * - Xrefs:
         *   - AgentCabinet_ReceiveEvent: before call to SetSelectedItemData
         *   - ...
         */
        [Signature("E8 ?? ?? ?? ?? 44 8B 0B 44 8B C0")]
        private readonly FunctionDelegates.GetCabinetItemIdDelegate GetCabinetItemId = null!;

        #endregion

        private Plugin Plugin { get; }
        private readonly List<uint> _filterIds = new();
        private static List<PrismBoxCachedItem> _cachedDresserItems = [];
        private static int _dresserItemSlotsUsed = 0;

        private SavedPlate _retryLoadPlate = null;
        private int _retryLoadPlateCount = 0;
        
        private Hook<AgentMiragePrismMiragePlate.Delegates.SetSelectedItemStains> _setSelectedItemStainsHook;

        internal GameFunctions(Plugin plugin) {
            this.Plugin = plugin;
            Service.GameInteropProvider.InitializeFromAttributes(this);

            Service.ChatGui.ChatMessage += this.OnChat;
            Service.Framework.Update += OnFrameworkUpdate;

            _setSelectedItemStainsHook = Service.GameInteropProvider
                .HookFromAddress<AgentMiragePrismMiragePlate.Delegates.SetSelectedItemStains>(
                    AgentMiragePrismMiragePlate.MemberFunctionPointers.SetSelectedItemStains,
                    SetSelectedItemStainsHook);
            //_setSelectedItemStainsHook.Enable();
        }

        public void Dispose() {
            Service.ChatGui.ChatMessage -= this.OnChat;
            Service.Framework.Update -= OnFrameworkUpdate;
            _setSelectedItemStainsHook.Disable();
            _setSelectedItemStainsHook.Dispose();
        }

        private unsafe void SetSelectedItemStainsHook(
            AgentMiragePrismMiragePlate* self,
            InventoryItem* item0,
            byte pendingStain0Id,
            uint pendingStain0ItemId,
            InventoryItem* item1,
            byte pendingStain1Id,
            uint pendingStain1ItemId) {
            Service.Log.Debug($"SetSelectedItemStains({(nint)item0}, {pendingStain0Id}, {pendingStain0ItemId}, {(nint)item1}, {pendingStain1Id}, {pendingStain1ItemId})");
            _setSelectedItemStainsHook.Original(self, item0, pendingStain0Id, pendingStain0ItemId, item1, pendingStain1Id, pendingStain1ItemId);
        }
        
        private void OnChat(IHandleableChatMessage msg) {
            if (this._filterIds.Count == 0 || msg.LogKind != XivChatType.SystemMessage) {
                return;
            }

            if (msg.Message.Payloads.Any(payload => payload is ItemPayload item && this._filterIds.Remove(item.ItemId))) {
                msg.PreventOriginal();
            }
        }

        private unsafe void OnFrameworkUpdate(IFramework framework) {
            var agent = AgentMiragePrismPrismBox.Instance();
            if (agent == null)
                return;

            if (!agent->IsAddonReady() || agent->Data == null)
                return;

            if (agent->Data->UsedSlots == _dresserItemSlotsUsed) {
                if (_retryLoadPlate != null) {
                    Plugin.LogTroubleshooting("Retrying LoadPlate()");
                    LoadPlate(_retryLoadPlate);
                    if (_retryLoadPlateCount >= 5) {
                        Plugin.LogTroubleshooting("Max retry count reached, giving up on loading plate");
                        _retryLoadPlate = null;
                        _retryLoadPlateCount = 0;
                    }
                }

                return;
            }

            int usedSlots = agent->Data->UsedSlots;

            _cachedDresserItems.Clear();
            foreach (var item in agent->Data->PrismBoxItems) {
                if (item.ItemId == 0 || item.Slot >= 800)
                    continue;

                _cachedDresserItems.Add(new PrismBoxCachedItem {
                    Name = item.Name.ToString(),
                    Slot = item.Slot,
                    ItemId = item.ItemId,
                    IconId = item.IconId,
                    Stain1 = item.Stains[0],
                    Stain2 = item.Stains[1],
                });
            }

            _dresserItemSlotsUsed = agent->Data->UsedSlots;
            
            if (_retryLoadPlate != null) {
                Plugin.LogTroubleshooting("Retrying LoadPlate()");
                LoadPlate(_retryLoadPlate);
                if (_retryLoadPlateCount >= 5) {
                    Plugin.LogTroubleshooting("Max retry count reached, giving up on loading plate");
                    _retryLoadPlate = null;
                    _retryLoadPlateCount = 0;
                }
            }
        }

        private static unsafe AgentMiragePrismMiragePlate* MiragePlateAgent => AgentMiragePrismMiragePlate.Instance();

        internal unsafe Cabinet* Armoire => &UIState.Instance()->Cabinet;

        internal unsafe bool ArmoireLoaded => this.Armoire->IsCabinetLoaded();

        internal unsafe string? ExamineName => UIState.Instance()->Inspect.NameString;

        internal static unsafe List<PrismBoxCachedItem> DresserContents {
            get => _cachedDresserItems;
        }

        internal static unsafe Dictionary<PlateSlot, SavedGlamourItem>? CurrentPlate {
            get {
                var agent = MiragePlateAgent;
                if (agent == null || agent->Data == null) {
                    return null;
                }

                var plate = new Dictionary<PlateSlot, SavedGlamourItem>();
                foreach (var slot in Enum.GetValues<PlateSlot>()) {
                    ref var item = ref agent->Data->CurrentItems[(int) slot];

                    if (item.ItemId == 0)
                        continue;

                    var stain1 =
                        item.PendingStainIds[0] != 0
                            ? item.PendingStainIds[0]
                            : item.StainIds[0];

                    var stain2 =
                         item.PendingStainIds[1] != 0
                            ? item.PendingStainIds[1]
                            : item.StainIds[1];

                    plate[slot] = new SavedGlamourItem {
                        ItemId = item.ItemId,
                        Stain1 = stain1,
                        Stain2 = stain2,
                    };
                }

                return plate;
            }
        }

        internal unsafe bool IsInArmoire(uint itemId) {
            var row = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Cabinet>()!.FirstOrNull(row => row.Item.RowId == itemId);
            if (row == null) {
                return false;
            }

            return this.Armoire->IsItemInCabinet(row.Value.RowId);
        }

        internal unsafe void LoadPlate(SavedPlate plate) {
            Plugin.LogTroubleshooting("Begin LoadPlate()");

            var agent = MiragePlateAgent;
            if (agent == null) {
                return;
            }

            var data = agent->Data;
            if (data == null)
                return;
            
            var usedStains = new Dictionary<(uint, uint), uint>();

            bool needsRetry = false;

            var initialSlot = data->SelectedItemIndex;
            foreach (var slot in Enum.GetValues<PlateSlot>()) {
                if (!plate.Items.TryGetValue(slot, out var wantedItem)) {
                    if (!plate.FillWithNewEmperor)
                        continue;

                    uint emperorId = Util.GetEmperorItemForSlot(slot);
                    if (emperorId != 0)
                        wantedItem = new SavedGlamourItem { ItemId = emperorId };
                    else
                        continue;
                }
                
                Plugin.LogTroubleshooting($"Slot {slot} wants {wantedItem}");

                data->SelectedItemIndex = (uint) slot;
                
                SourcedGlamourItem? bestItemN = FindBestItemSource(agent, slot, wantedItem);
                if (bestItemN == null)
                    continue;

                var bestItem = bestItemN.Value;
                
                if (bestItem.Source != SourcedGlamourItem.SourceType.Plate) {
                    ItemSource source = 
                        bestItem.Source == SourcedGlamourItem.SourceType.PrismBox
                            ? ItemSource.PrismBox
                            : ItemSource.Cabinet;
                
                    Plugin.LogTroubleshooting($"SetGlamourPlateItemSlot for source {bestItem.Source}: ({source}, {bestItem.SlotOrId}, {bestItem.ItemId}, {bestItem.Stain1}, {bestItem.Stain2})");
                    AgentMiragePrismMiragePlate.Instance()->SetSelectedItemData(
                        source,
                        bestItem.SlotOrId,
                        bestItem.ItemId,
                        bestItem.Stain1,
                        bestItem.Stain2);

                    needsRetry |= agent->Data->CurrentItems[(int) slot].ItemId != bestItem.ItemId;
                    needsRetry |= agent->Data->CurrentItems[(int) slot].Source != bestItem.SourceAsItemSource;
                }

                if (bestItem.Stain1 == wantedItem.Stain1 && bestItem.Stain2 == wantedItem.Stain2) {
                    Plugin.LogTroubleshooting($"Skipping stains for {slot}: already has matching stains {bestItem.Stain1}, {bestItem.Stain2}");
                    continue;
                }

                uint previousContextSlot = data->ContextMenuItemIndex;
                data->ContextMenuItemIndex = (uint) slot;
                
                Plugin.LogTroubleshooting($"Applying stains to {slot}: {wantedItem.Stain1}, {wantedItem.Stain2}");
                    
                // fixes some item data being loaded late in patch 7.1 and later
                if (wantedItem.Stain1 != 0)
                    data->CurrentItems[(int) slot].Flags |= ItemFlag.HasStain0;

                if (wantedItem.Stain2 != 0)
                    data->CurrentItems[(int) slot].Flags |= ItemFlag.HasStain1;

                ApplyStains(slot, wantedItem, [bestItem.Stain1, bestItem.Stain2], usedStains);
                
                data->ContextMenuItemIndex = previousContextSlot;
            }

            // restore initial slot, since changing this does not update the ui
            data->SelectedItemIndex = initialSlot;
            data->HasChanges = true;

            if (needsRetry) {
                _retryLoadPlate = plate;
                _retryLoadPlateCount += 1;
                
                Plugin.LogTroubleshooting("Could not fully load plate, retrying next tick");
            } else {
                _retryLoadPlate = null;
                _retryLoadPlateCount = 0;
            }

            Plugin.LogTroubleshooting("End LoadPlate()");
        }

        internal SourcedGlamourItem? FindBestItemSource(AgentMiragePrismMiragePlate* agent, PlateSlot slot, SavedGlamourItem wantedItem) {
            SavedGlamourItem? plateItem = GetCurrentPlateItem(slot);
            if (plateItem != null) {
                if (wantedItem.ItemId == 0) {
                    Plugin.LogTroubleshooting($"Clearing {slot}: slot is explicitly empty");

                    uint previousContextSlot = agent->Data->ContextMenuItemIndex;
                    agent->Data->ContextMenuItemIndex = (uint) slot;

                    AtkValue rv;
                    agent->ReceiveEvent(&rv, null, 0, 1); // "Remove Item Image from Plate"

                    agent->Data->ContextMenuItemIndex = previousContextSlot;
                    return null;
                }
                
                if (plateItem.ItemId != wantedItem.ItemId) {
                    plateItem = null; // remove from consideration
                } else if (plateItem.Stain1 == wantedItem.Stain1 &&
                           plateItem.Stain2 == wantedItem.Stain2) {
                    Plugin.LogTroubleshooting($"Skipping {slot}: already has matching item {wantedItem.ItemId} ({wantedItem.Stain1}, {wantedItem.Stain2})");
                    return null;
                }
            }

            PrismBoxCachedItem? prismBoxItem = FindBestPrismBoxMatch(wantedItem);
            if (prismBoxItem != null
                && prismBoxItem.Value.ItemId == wantedItem.ItemId
                && prismBoxItem.Value.Stain1 == wantedItem.Stain1
                && prismBoxItem.Value.Stain2 == wantedItem.Stain2)
            {
                Plugin.LogTroubleshooting($"Found exact match in dresser for {slot}: {wantedItem.ItemId} ({wantedItem.Stain1}, {wantedItem.Stain2})");
                AgentMiragePrismMiragePlate.Instance()->SetSelectedItemData(ItemSource.PrismBox, (uint) prismBoxItem.Value.Slot, prismBoxItem.Value.ItemId, prismBoxItem.Value.Stain1, prismBoxItem.Value.Stain2);
                return null;
            }

            int cabinetId = GetCabinetItemId(Armoire, wantedItem.ItemId);
            bool isInCabinet = cabinetId != -1 && Armoire->IsItemInCabinet((uint) cabinetId);
            
            if (plateItem == null && prismBoxItem == null && !isInCabinet) {
                Plugin.LogTroubleshooting($"Skipping {slot}: could not find item {wantedItem.ItemId} ({wantedItem.Stain1}, {wantedItem.Stain2})");
                return null;
            }
            
            Plugin.LogTroubleshooting(
                $"Evaluating options for {slot}: " +
                (plateItem != null ? "plate " : "") +
                (prismBoxItem != null ? "dresser " : "") +
                (isInCabinet ? "armoire " : "")
            );
            
            SourcedGlamourItem bestItem = PickBestItem(plateItem, prismBoxItem, isInCabinet, wantedItem);
            Plugin.LogTroubleshooting($"Picked best item for {slot}: {bestItem}");

            // never attempt to clear stains on invalid items
            // this can happen if CS offsets are wrong
            int numStainSlots = DataCache.GetNumStainSlots(bestItem.ItemId);
            if (numStainSlots == 0) {
                bestItem.Stain1 = 0;
                wantedItem.Stain1 = 0;
            }

            if (numStainSlots != 2) {
                bestItem.Stain2 = 0;
                wantedItem.Stain2 = 0;
            }

            return bestItem;
        }
        
        internal SavedGlamourItem? GetCurrentPlateItem(PlateSlot slot) =>
            CurrentPlate == null ? null : CurrentPlate!.GetValueOrDefault(slot, null);
            
        internal PrismBoxCachedItem? FindBestPrismBoxMatch(SavedGlamourItem item) {
            var dresser = DresserContents;

            var matches = dresser.FindAll(i => (i.ItemId % Util.ItemModifierMod) == item.ItemId);
            if (matches.Count == 0)
                return null;
            
            var exact = matches.FirstOrNull(i => i.Stain1 == item.Stain1 && i.Stain2 == item.Stain2);
            if (exact != null)
                return exact;

            var valuableStains = DataCache.ValuableStains;
            bool itemHasValuableStain = valuableStains.Contains(item.Stain1) || valuableStains.Contains(item.Stain2);
            
            int bestScore = -1;
            int bestIdx = -1;
            
            for (int i = 0; i < matches.Count; i++) {
                var match = matches[i];
                int score = 0;
                
                if (itemHasValuableStain) {
                    if ((item.Stain1 != 0 && valuableStains.Contains(item.Stain1) && match.Stain1 == item.Stain1) ||
                        (item.Stain2 != 0 && valuableStains.Contains(item.Stain2) && match.Stain2 == item.Stain2)) {
                        score = 2;
                    }
                }
                
                if (score < 2) {
                    if ((item.Stain1 != 0 && match.Stain1 == item.Stain1) ||
                        (item.Stain2 != 0 && match.Stain2 == item.Stain2)) {
                        score = 1;
                    }
                }
                
                if (score > bestScore) {
                    bestScore = score;
                    bestIdx = i;
                }
            }
            
            return bestIdx != -1 ? matches[bestIdx] : matches[0];
        }

        internal SourcedGlamourItem PickBestItem(SavedGlamourItem? plateItem, PrismBoxCachedItem? prismBoxItem,
            bool isInCabinet, SavedGlamourItem wantedItem) {
            var valuableStains = DataCache.ValuableStains;
            
            int bestScore = -1;
            var result = new SourcedGlamourItem {
                Source = SourcedGlamourItem.SourceType.None,
                ItemId = 0,
                Stain1 = 0,
                Stain2 = 0,
            };
            
            if (plateItem != null) {
                bool firstStainMatch = wantedItem.Stain1 != 0 && plateItem.Stain1 == wantedItem.Stain1;
                bool secondStainMatch = wantedItem.Stain2 != 0 && plateItem.Stain2 == wantedItem.Stain2;
                bool firstStainValuable = valuableStains.Contains(wantedItem.Stain1) && firstStainMatch;
                bool secondStainValuable = valuableStains.Contains(wantedItem.Stain2) && secondStainMatch;
                
                int score = 0;
                if (firstStainValuable || secondStainValuable)
                    score = 2;
                else if (firstStainMatch || secondStainMatch)
                    score = 1;

                bestScore = score;
                result.Source = SourcedGlamourItem.SourceType.Plate;
                result.ItemId = plateItem.ItemId;
                result.Stain1 = plateItem.Stain1;
                result.Stain2 = plateItem.Stain2;
            }
            
            if (prismBoxItem != null) {
                bool firstStainMatch = wantedItem.Stain1 != 0 && prismBoxItem.Value.Stain1 == wantedItem.Stain1;
                bool secondStainMatch = wantedItem.Stain2 != 0 && prismBoxItem.Value.Stain2 == wantedItem.Stain2;
                bool firstStainValuable = valuableStains.Contains(wantedItem.Stain1) && firstStainMatch;
                bool secondStainValuable = valuableStains.Contains(wantedItem.Stain2) && secondStainMatch;
                
                int score = 0;
                if (firstStainValuable || secondStainValuable)
                    score = 2;
                else if (firstStainMatch || secondStainMatch)
                    score = 1;

                if (score > bestScore) {
                    bestScore = score;
                    result.Source = SourcedGlamourItem.SourceType.PrismBox;
                    result.SlotOrId = (uint) prismBoxItem.Value.Slot;
                    result.ItemId = prismBoxItem.Value.ItemId;
                    result.Stain1 = prismBoxItem.Value.Stain1;
                    result.Stain2 = prismBoxItem.Value.Stain2;
                }
            }
            
            if (isInCabinet && bestScore < 0) {
                result.Source = SourcedGlamourItem.SourceType.Cabinet;
                result.SlotOrId = (uint) GetCabinetItemId(Armoire, wantedItem.ItemId);
                result.ItemId = wantedItem.ItemId;
                result.Stain1 = 0;
                result.Stain2 = 0;
            }
            
            return result;
        }

        private static readonly InventoryType[] PlayerInventories = {
            InventoryType.Inventory1,
            InventoryType.Inventory2,
            InventoryType.Inventory3,
            InventoryType.Inventory4,
        };

        private unsafe InventoryItem* SelectStainItem(byte stainId, Dictionary<(uint, uint), uint> usedStains, out uint bestItemId) {
            InventoryManager* inventory = InventoryManager.Instance();
            Stain? stainRow = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Stain>()!.GetRowOrDefault(stainId);

            var stainItems = stainRow!.Value.Item;

            InventoryItem* item = null;

            bestItemId = stainItems[0].RowId;
            
            foreach (var stainItem in stainItems) {
                if (stainItem.Value.RowId == 0)
                    continue;

                foreach (var type in PlayerInventories) {
                    InventoryContainer* inv = inventory->GetInventoryContainer(type);
                    if (inv == null)
                        continue;

                    for (int i = 0; i < inv->Size; i++) {
                        (uint, uint) address = ((uint) type, (uint) i);
                        InventoryItem invItem = inv->Items[i];

                        if (invItem.ItemId != stainItem.Value.RowId)
                            continue;

                        if (usedStains.TryGetValue(address, out var numUsed) && numUsed >= invItem.Quantity)
                            continue;

                        // first one that we find in the inventory is the one we'll use
                        item = &inv->Items[i];
                        bestItemId = invItem.ItemId;

                        if (usedStains.ContainsKey(address))
                            usedStains[address] += 1;
                        else
                            usedStains[address] = 1;

                        goto NoBreakLabels;
                    }
                }

            NoBreakLabels:
                {
                }
            }

            return item;
        }

        private unsafe void ApplyStains(PlateSlot slot, SavedGlamourItem item, byte[] currentItemStains, Dictionary<(uint, uint), uint> usedStains) {
            var stain1Item = SelectStainItem(item.Stain1, usedStains, out var stain1ItemId);
            var stain2Item = SelectStainItem(item.Stain2, usedStains, out var stain2ItemId);

            Plugin.LogTroubleshooting($"Item has stains: {currentItemStains[0]}, {currentItemStains[1]}; we need stains: {item.Stain1}, {item.Stain2}");
            // if the stain slot already matches, we have to keep the item id, but pass in null for the InventoryItem
            if (item.Stain1 == currentItemStains[0])
                stain1Item = null;
            
            if (item.Stain2 == currentItemStains[1])
                stain2Item = null;

            Plugin.LogTroubleshooting($"SetGlamourPlateSlotStains({(stain1Item != null ? stain1Item->Slot : 0)}, {item.Stain1}, {stain1ItemId}, {(stain2Item != null ? stain2Item->Slot : 0)}, {item.Stain2}, {stain2ItemId})");
            AgentMiragePrismMiragePlate.Instance()->SetSelectedItemStains(stain1Item, item.Stain1, stain1ItemId,
                                                                          stain2Item, item.Stain2, stain2ItemId);
        }

        internal void TryOn(uint itemId, byte stainId, byte stainId2, bool suppress = true) {
            if (suppress) {
                this._filterIds.Add(itemId);
            }

            AgentTryon.TryOn(0, itemId % Util.HqItemOffset, stainId, stainId2);
        }

        internal struct PrismBoxCachedItem {
            public string Name { get; set; }
            public uint Slot { get; set; }
            public uint ItemId { get; set; }
            public uint IconId { get; set; }
            public byte Stain1 { get; set; }
            public byte Stain2 { get; set; }
        }

        internal struct SourcedGlamourItem {
            public SourceType Source;
            public uint SlotOrId;
            public uint ItemId;
            public byte Stain1;
            public byte Stain2;

            /// <inheritdoc />
            public override string ToString() {
                return $"SourcedGlamourItem(Source={Source}, SlotOrId={SlotOrId}, ItemId={ItemId}, Stain1={Stain1}, Stain2={Stain2})";
            }

            public enum SourceType {
                None,
                Plate,
                PrismBox,
                Cabinet
            }
            
            public ItemSource SourceAsItemSource =>
                Source == SourceType.Plate
                    ? ItemSource.None
                    : (Source == SourceType.PrismBox
                        ? ItemSource.PrismBox
                        : (Source == SourceType.Cabinet
                            ? ItemSource.Cabinet
                            : ItemSource.None));
        }
    }
}
