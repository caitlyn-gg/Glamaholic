using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.NativeWrapper;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Glamaholic.Ui.Helpers {
    internal static class HelperUtil {
        internal const ImGuiWindowFlags HelperWindowFlags = ImGuiWindowFlags.NoBackground
                                                            | ImGuiWindowFlags.NoDecoration
                                                            | ImGuiWindowFlags.NoCollapse
                                                            | ImGuiWindowFlags.NoTitleBar
                                                            | ImGuiWindowFlags.NoNav
                                                            | ImGuiWindowFlags.NoNavFocus
                                                            | ImGuiWindowFlags.NoNavInputs
                                                            | ImGuiWindowFlags.NoResize
                                                            | ImGuiWindowFlags.NoScrollbar
                                                            | ImGuiWindowFlags.NoSavedSettings
                                                            | ImGuiWindowFlags.NoFocusOnAppearing
                                                            | ImGuiWindowFlags.AlwaysAutoResize
                                                            | ImGuiWindowFlags.NoDocking;

        internal static unsafe Vector2? DrawPosForAddon(AtkUnitBasePtr addon, bool right = false) {
            if (addon == null) {
                return null;
            }

            var xModifier = right
                ? addon.ScaledWidth - DropdownWidth()
                : 0;

            return ImGuiHelpers.MainViewport.Pos
                   + new Vector2(addon.X, addon.Y)
                   + Vector2.UnitX * xModifier
                   - Vector2.UnitY * ImGui.CalcTextSize("A")
                   - Vector2.UnitY * (ImGui.GetStyle().FramePadding.Y + ImGui.GetStyle().FrameBorderSize);
        }

        internal static float DropdownWidth() {
            // arrow size is GetFrameHeight
            return (ImGui.CalcTextSize(Plugin.Name).X + ImGui.GetStyle().ItemInnerSpacing.X * 2 + ImGui.GetFrameHeight()) * ImGuiHelpers.GlobalScale;
        }

        internal class HelperStyles : IDisposable {
            internal HelperStyles() {
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.Zero);
            }

            public void Dispose() {
                ImGui.PopStyleVar(3);
            }
        }

        internal static unsafe void DrawHelper(AtkUnitBasePtr addon, string id, bool right, Action dropdown) {
            var drawPos = DrawPosForAddon(addon, right);
            if (drawPos == null) {
                return;
            }

            using (new HelperStyles()) {
                // get first frame
                ImGui.SetNextWindowPos(drawPos.Value, ImGuiCond.Appearing);
                if (!ImGui.Begin($"##{id}", HelperWindowFlags)) {
                    ImGui.End();
                    return;
                }
            }

            ImGui.SetNextItemWidth(DropdownWidth());
            if (ImGui.BeginCombo($"##{id}-combo", Plugin.Name)) {
                try {
                    dropdown();
                } catch (Exception ex) {
                    Service.Log.Error(ex, "Error drawing helper combo");
                }

                ImGui.EndCombo();
            }

            ImGui.SetWindowPos(drawPos.Value);

            ImGui.End();
        }

        internal static Guid? DrawFolderMenuItems(List<TreeNode> rootNodes) {
            var folders = TreeUtils.GetAllFolders(rootNodes);

            if (ImGui.MenuItem("Root (No Folder)"))
                return Guid.Empty;

            if (folders.Count > 0)
                ImGui.Separator();

            foreach (var (id, path) in folders) {
                if (ImGui.MenuItem(path))
                    return id;
            }

            if (folders.Count == 0) {
                ImGui.Separator();
                ImGui.TextDisabled("No folders created yet.");
            }

            return null;
        }

        internal static Guid? DrawFolderCombo(List<TreeNode> rootNodes, Guid? currentSelection, string comboId = "##folder-select") {
            Guid? result = null;
            var folders = TreeUtils.GetAllFolders(rootNodes);

            var preview = "Root (No Folder)";
            if (currentSelection.HasValue && currentSelection.Value != Guid.Empty) {
                foreach (var (id, path) in folders) {
                    if (id == currentSelection.Value) {
                        preview = path;
                        break;
                    }
                }
            }

            if (ImGui.BeginCombo(comboId, preview)) {
                if (ImGui.Selectable("Root (No Folder)", !currentSelection.HasValue || currentSelection.Value == Guid.Empty))
                    result = Guid.Empty;

                foreach (var (id, path) in folders) {
                    if (ImGui.Selectable(path, currentSelection.HasValue && currentSelection.Value == id))
                        result = id;
                }

                ImGui.EndCombo();
            }

            return result;
        }

        private static Guid? _createPlateTargetFolder = null;

        internal static bool DrawCreatePlateMenu(PluginUi ui, Func<Dictionary<PlateSlot, SavedGlamourItem>?> getter, ref string nameInput) {
            var ret = false;

            if (!ImGui.BeginMenu("Create glamour plate")) {
                return ret;
            }

            const string msg = "Enter a name and press Enter to create a new plate, or choose a plate below to overwrite.";
            ImGui.PushTextWrapPos(250);
            if (Util.DrawTextInput("current-name", ref nameInput, message: msg, flags: ImGuiInputTextFlags.AutoSelectAll)) {
                var items = getter();
                if (items != null) {
                    var folderId = (_createPlateTargetFolder.HasValue && _createPlateTargetFolder.Value != Guid.Empty)
                        ? _createPlateTargetFolder : null;
                    CopyToGlamourPlate(ui, nameInput, items, null, folderId);
                    ret = true;
                }
            }

            ImGui.TextUnformatted("Folder:");
            ImGui.SetNextItemWidth(-1);
            var folderChoice = DrawFolderCombo(ui.Plugin.Config.Plates, _createPlateTargetFolder, "##create-plate-folder");
            if (folderChoice.HasValue)
                _createPlateTargetFolder = folderChoice.Value;

            ImGui.PopTextWrapPos();

            if (ImGui.IsWindowAppearing()) {
                ImGui.SetKeyboardFocusHere(-1);
            }

            ImGui.Separator();

            if (ImGui.BeginChild("helper-overwrite", new Vector2(250, 350))) {
                void DrawPlateNodes(List<TreeNode> nodes) {
                    foreach (var node in nodes) {
                        if (node is PlateNode leaf) {
                            var plate = leaf.Plate;
                            var ctrl = ImGui.GetIO().KeyCtrl;
                            if (ImGui.Selectable($"{plate.Name}##{node.Id}") && ctrl) {
                                var items = getter();
                                if (items != null) {
                                    CopyToGlamourPlate(ui, plate.Name, items, node.Id);
                                    ret = true;
                                }
                            }

                            if (!ctrl && ImGui.IsItemHovered()) {
                                ImGui.BeginTooltip();
                                ImGui.TextUnformatted("Hold Control and click to overwrite.");
                                ImGui.EndTooltip();
                            }
                        } else if (node is FolderNode folder) {
                            ImGui.TextUnformatted($"[Folder] {folder.Name}");

                            if (folder.Children.Count > 0) {
                                DrawPlateNodes(folder.Children);
                            }
                        }
                    }
                }

                DrawPlateNodes(ui.Plugin.Config.Plates);
                ImGui.EndChild();
            }

            ImGui.EndMenu();

            return ret;
        }

        private static void CopyToGlamourPlate(PluginUi ui, string name, Dictionary<PlateSlot, SavedGlamourItem> items, Guid? plateId, Guid? targetFolderId = null) {
            var plate = new SavedPlate(name) {
                Items = items,
            };

            Configuration.SanitisePlate(plate);

            if (plateId == null) {
                var id = ui.Plugin.Config.AddPlate(plate, targetFolderId);
                ui.Plugin.SaveConfig();
                ui.OpenMainInterface();
                var allPlates = TreeUtils.GetAllPlates(ui.Plugin.Config.Plates);
                if (allPlates.Count > 0) {
                    ui.SwitchPlate(id, true);
                }
            } else {
                TreeUtils.ReplacePlate(ui.Plugin.Config.Plates, plateId.Value, plate);
                TreeUtils.Sort(ui.Plugin.Config.Plates);
                ui.Plugin.SaveConfig();
                ui.OpenMainInterface();
                ui.SwitchPlate(plateId.Value, true);
            }
        }
    }
}
