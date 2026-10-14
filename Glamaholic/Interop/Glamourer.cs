using Dalamud.Game.Command;
using Dalamud.Plugin;
using Glamourer.Api.Enums;
using Glamourer.Api.IpcSubscribers;
using System;
using System.Threading.Tasks;

namespace Glamaholic.Interop {
    internal class Glamourer {
        private static SetItem _SetItem { get; set; } = null!;
        private static RevertState _RevertState { get; set; } = null!;

        private static bool Initialized { get; set; } = false;
        private static bool Available { get; set; } = false;

        public static void TryOn(int playerIndex, SavedPlate plate) {
            if (!IsAvailable())
                return;

            try {
                Service.Framework.Run(() => {
                    _RevertState.Invoke(playerIndex, flags: ApplyFlag.Equipment);

                    foreach (var slot in Enum.GetValues<PlateSlot>()) {
                        if (!plate.Items.TryGetValue(slot, out var item)) {
                            if (!plate.FillWithNewEmperor)
                                continue;

                            uint empItem = Util.GetEmperorItemForSlot(slot);
                            if (empItem != 0)
                                _SetItem.Invoke(playerIndex, ConvertSlot(slot), empItem, [0, 0]);

                            continue;
                        }

                        _SetItem.Invoke(playerIndex, ConvertSlot(slot), item.ItemId, [item.Stain1, item.Stain2]);
                    }
                });
            } catch (Exception) { }
        }

        public static void SetItem(int playerIndex, PlateSlot slot, uint itemId, byte stain1 = 0, byte stain2 = 0) {
            if (!IsAvailable())
                return;

            try {
                Service.Framework.Run(() => {
                    _SetItem.Invoke(playerIndex, ConvertSlot(slot), itemId, [stain1, stain2]);
                });
            } catch (Exception) { }
        }

        public static void RevertState(int playerIndex) {
            if (!IsAvailable()) return;

            try {
                Service.Framework.Run(() => {
                    _RevertState.Invoke(playerIndex, flags: ApplyFlag.Equipment);
                });
            } catch (Exception) { }
        }

        private static ApiEquipSlot ConvertSlot(PlateSlot slot) {
            switch (slot) {
                case PlateSlot.LeftRing:
                    return ApiEquipSlot.LFinger;

                case >= (PlateSlot) 5:
                    return (ApiEquipSlot) ((int) slot + 2);

                default:
                    return (ApiEquipSlot) ((int) slot + 1);
            }
        }

        private static void TryOnCommand(string _, string args) {
            if (!IsAvailable())
                return;

            string url = args.Trim();
            if (url.Length == 0 || !EorzeaCollection.IsEorzeaCollectionURL(url)) {
                url = Util.GetClipboardText();
                if (url.Length == 0 || !EorzeaCollection.IsEorzeaCollectionURL(url)) {
                    Service.ChatGui.PrintError("[Glamaholic] No valid Eorzea Collection URL provided or found in clipboard");
                    return;
                }
            }

            Task.Run(async () => {
                var import = await EorzeaCollection.ImportFromURL(url);
                if (import == null) {
                    Service.ChatGui.PrintError("[Glamaholic] Failed to import glamour from Eorzea Collection URL, check /xllog for more information.");
                    return;
                }

                TryOn(0, import);
            });
        }

        public static void Initialize(IDalamudPluginInterface pluginInterface) {
            if (Initialized)
                return;

            _SetItem = new SetItem(pluginInterface);
            _RevertState = new RevertState(pluginInterface);

            Initialized = true;

            RefreshStatus(pluginInterface);
        }

        public static void RefreshStatus(IDalamudPluginInterface pluginInterface) {
            var prev = Available;

            Available = false;

            foreach (var plugin in pluginInterface.InstalledPlugins) {
                if (plugin.Name == "Glamourer") {
                    Available = plugin.IsLoaded;
                    break;
                }
            }

            if (prev == Available)
                return;

            // per PAC guidelines, this command must only show up if the user has Glamourer loaded already
            if (Available) {
                Service.CommandManager.AddHandler("/tryonglamourer", new CommandInfo(TryOnCommand) { 
                    ShowInHelp = true,
                    HelpMessage = "Try on an EC glamour using Glamourer, link can be provided in the command or in the clipboard"
                });

                return;
            }

            Service.CommandManager.RemoveHandler("/tryonglamourer");
        }

        public static bool IsAvailable() {
            return Available && IsIPCValid();
        }

        public static bool IsIPCValid() {
            return _SetItem.Valid && _RevertState.Valid;
        }
    }
}
