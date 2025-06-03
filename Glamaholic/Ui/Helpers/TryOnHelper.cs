using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Glamaholic.Ui.Helpers {
    internal class TryOnHelper {
        private const string PlateName = "Fitting Room";

        private PluginUi Ui { get; }
        private string _nameInput = PlateName;

        internal TryOnHelper(PluginUi ui) {
            this.Ui = ui;
        }

        internal unsafe void Draw() {
            if (!this.Ui.Plugin.Config.ShowTryOnMenu) {
                return;
            }

            var tryOnAddon = (AtkUnitBase*) Service.GameGui.GetAddonByName("Tryon", 1);
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (tryOnAddon == null || !tryOnAddon->IsVisible) {
                return;
            }

            var right = Service.Interface.InstalledPlugins.Any(state => state.InternalName == "ItemSearchPlugin");
            HelperUtil.DrawHelper(tryOnAddon, "glamaholic-helper-try-on", right, this.DrawDropdown);
        }

        private void DrawDropdown() {
            if (ImGui.Selectable($"Open {Plugin.Name}")) {
                this.Ui.OpenMainInterface();
            }

            if (ImGui.IsWindowAppearing()) {
                this._nameInput = PlateName;
            }

            if (HelperUtil.DrawCreatePlateMenu(this.Ui, GetTryOnItems, ref this._nameInput)) {
                this._nameInput = PlateName;
            }

            if (Interop.Glamourer.IsAvailable()) {
                if (ImGui.Selectable("Try on Glamourer")) {
                    this.TryOnGlamourer(false);
                }

                if (ImGui.Selectable("Try on Glamourer - New Emperor")) {
                    this.TryOnGlamourer(true);
                }
            }
        }

        private void TryOnGlamourer(bool withEmperor) {
            if (Service.ClientState.LocalPlayer == null) return;
            var plate = new SavedPlate("Glamourer") {
                Items = GetTryOnItems(),
                FillWithNewEmperor = withEmperor
            };

            Configuration.SanitisePlate(plate);
            Interop.Glamourer.TryOn(Service.ClientState.LocalPlayer.ObjectIndex, plate);
        }

        private static unsafe Dictionary<PlateSlot, SavedGlamourItem> GetTryOnItems() {
            var agent = AgentTryon.Instance();
            var firstItem = (nint) agent + 0x368;

            var items = new Dictionary<PlateSlot, SavedGlamourItem>();

            for (var i = 0; i < 12; i++) {
                var item = (TryOnItem*) (firstItem + i * 28);
                if (item->Slot == 14 || item->ItemId == 0) {
                    continue;
                }

                int slot = item->Slot > 5 ? item->Slot - 1 : item->Slot;

                var itemId = item->ItemId;
                if (item->GlamourId != 0) {
                    itemId = item->GlamourId;
                }

                var stain1 = item->StainPreview1 == 0
                    ? item->Stain1
                    : item->StainPreview1;

                var stain2 = item->StainPreview2 == 0
                    ? item->Stain2
                    : item->StainPreview2;

                // for some reason, this still accounts for belts in EW

                items[(PlateSlot) slot] = new SavedGlamourItem {
                    ItemId = itemId % Util.HqItemOffset,
                    Stain1 = stain1,
                    Stain2 = stain2,
                };
            }

            return items;
        }

        [StructLayout(LayoutKind.Explicit, Size = 28)]
        private readonly struct TryOnItem {
            [FieldOffset(0)]
            internal readonly byte Slot;

            [FieldOffset(2)]
            internal readonly byte Stain1;

            [FieldOffset(3)]
            internal readonly byte Stain2;

            [FieldOffset(4)]
            internal readonly byte StainPreview1;

            [FieldOffset(5)]
            internal readonly byte StainPreview2;

            [FieldOffset(12)]
            internal readonly uint ItemId;

            [FieldOffset(16)]
            internal readonly uint GlamourId;
        }
    }
}
