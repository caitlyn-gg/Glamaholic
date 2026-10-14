using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui.ContextMenu;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Glamaholic.Ui.Helpers;
using System;
using Character = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace Glamaholic {
    internal class ContextMenu : IDisposable {
        private const string ExamineAddon = "CharacterInspect";

        // windows that hide the item tooltip as soon as a slot is right-clicked
        private static readonly string[] TooltipClearingAddons = [ExamineAddon, "Tryon"];

        private Plugin Plugin { get; }

        private ulong _lastHoveredItem;
        private DateTime _hoverEndedAt = DateTime.MinValue;

        internal ContextMenu(Plugin plugin) {
            this.Plugin = plugin;

            Service.ContextMenu.OnMenuOpened += this.OnMenuOpened;
            Service.GameGui.HoveredItemChanged += this.OnHoveredItemChanged;
        }

        public void Dispose() {
            Service.GameGui.HoveredItemChanged -= this.OnHoveredItemChanged;
            Service.ContextMenu.OnMenuOpened -= this.OnMenuOpened;
        }

        private void OnHoveredItemChanged(object? sender, ulong itemId) {
            if (itemId != 0) {
                this._lastHoveredItem = itemId;
            } else {
                this._hoverEndedAt = DateTime.UtcNow;
            }
        }

        private void OnMenuOpened(IMenuOpenedArgs args) {
            if (this.Plugin.Config.ShowFavoriteMenu) {
                this.AddFavoriteMenuItem(args);
            }

            if (!this.Plugin.Config.ShowContextMenu || args.MenuType != ContextMenuType.Default) {
                return;
            }

            // the outfit is read from the character's model, so they have to be nearby
            if (args.Target is not MenuTargetDefault { TargetObject: ICharacter character }) {
                return;
            }

            var objectId = character.GameObjectId;
            args.AddMenuItem(new MenuItem {
                Name = "Save glamour",
                UseDefaultPrefix = true,
                OnClicked = _ => this.SaveGlamour(objectId),
            });
        }

        private void AddFavoriteMenuItem(IMenuOpenedArgs args) {
            var itemId = this.GetMenuItemId(args);
            if (itemId == 0 || Favorites.GetGear(itemId) is not { } item) {
                return;
            }

            var favorites = this.Plugin.Favorites;
            var isFavorite = favorites.Contains(item.RowId);
            args.AddMenuItem(new MenuItem {
                Name = isFavorite ? "Remove from Favorites" : "Add to Favorites",
                UseDefaultPrefix = true,
                OnClicked = _ => {
                    var name = item.Name.ExtractText();
                    if (isFavorite) {
                        favorites.Remove(item.RowId);
                        Service.ChatGui.Print($"[{Plugin.Name}] Removed {name} from favorites.");
                    } else {
                        favorites.Add(item.RowId);
                        Service.ChatGui.Print($"[{Plugin.Name}] Added {name} to favorites.");
                    }
                },
            });
        }

        private unsafe uint GetMenuItemId(IMenuOpenedArgs args) {
            // inventory, armoury chest, saddlebags and retainers
            if (args.Target is MenuTargetInventory inventory) {
                return inventory.TargetItem is { } item ? item.BaseItemId : 0;
            }

            // other item windows (shops, armoire, dresser, market board, fitting room, ...) have the item hovered.
            // this has to come before the player check below, since the Examine window's menu targets the examined character
            var hovered = this.GetHoveredItem(args);
            if (hovered != 0) {
                // an examined character's slot shows its glamour, so that's what should be favorited
                return args.AddonName == ExamineAddon ? GetExamineGlamour(hovered) : hovered;
            }

            // player menus (e.g. a name in chat) also come through as Default, so never treat those as items
            if (args.Target is MenuTargetDefault target && (target.TargetContentId != 0 || !string.IsNullOrEmpty(target.TargetName))) {
                return 0;
            }

            // item links in chat
            if (args.AddonName == "ChatLog") {
                var agent = AgentChatLog.Instance();
                if (agent != null) {
                    return (uint) (agent->ContextItemId % Util.ItemModifierMod);
                }
            }

            return 0;
        }

        private uint GetHoveredItem(IMenuOpenedArgs args) {
            var hovered = Service.GameGui.HoveredItem;

            // the tooltip may already be gone by the time the menu opens, so use the item that was just hovered
            if (hovered == 0
                && Array.IndexOf(TooltipClearingAddons, args.AddonName) >= 0
                && DateTime.UtcNow - this._hoverEndedAt < TimeSpan.FromSeconds(1)) {
                hovered = this._lastHoveredItem;
            }

            return (uint) (hovered % Util.ItemModifierMod);
        }

        // The examine tooltip shows the real item, so look up which glamour is on the slot holding it.
        private static unsafe uint GetExamineGlamour(uint itemId) {
            var inventory = InventoryManager.Instance()->GetInventoryContainer(InventoryType.Examine);
            if (inventory == null) {
                return itemId;
            }

            for (var i = 0; i < inventory->Size; i++) {
                var item = inventory->Items[i];
                if (item.ItemId % Util.ItemModifierMod == itemId && item.GlamourId != 0) {
                    return item.GlamourId;
                }
            }

            return itemId;
        }

        private unsafe void SaveGlamour(ulong objectId) {
            // look the character up again in case they left after the menu was opened
            if (Service.ObjectTable.SearchById(objectId) is not ICharacter character) {
                Service.ChatGui.PrintError($"[{Plugin.Name}] That character is no longer nearby.");
                return;
            }

            var name = character.Name.TextValue;
            var items = this.Plugin.Functions.GetCharacterGlamour((Character*) character.Address);
            if (items.Count == 0) {
                Service.ChatGui.PrintError($"[{Plugin.Name}] Couldn't find any glamour on {name}.");
                return;
            }

            HelperUtil.CopyToGlamourPlate(this.Plugin.Ui, name, items, null);
        }
    }
}
