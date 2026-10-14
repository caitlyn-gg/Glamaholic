using Dalamud.Interface.Textures.TextureWraps;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Glamaholic.Ui;
using Glamaholic.Ui.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Glamaholic {
    internal class PluginUi : IDisposable {
        internal Plugin Plugin { get; }

        private MainInterface MainInterface { get; }
        private FittingRoom FittingRoom { get; }
        private EditorHelper EditorHelper { get; }
        private ExamineHelper ExamineHelper { get; }
        public TryOnHelper TryOnHelper { get; }
        internal List<AlternativeFinder> AlternativeFinders { get; } = new();
        internal List<(string, string)> Help { get; } = new();

        internal PluginUi(Plugin plugin) {
            this.Plugin = plugin;

            foreach (var entry in Resourcer.Resource.AsString("help.txt").Split("---")) {
                var lines = entry.Trim().Split(new[] { "\n", "\r\n" }, StringSplitOptions.TrimEntries);
                if (lines.Length == 0 || !lines[0].StartsWith("#")) {
                    continue;
                }

                var title = lines[0][1..].Trim();
                var content = string.Join(" ", lines[1..]
                    .SkipWhile(string.IsNullOrWhiteSpace)
                    .Select(line => string.IsNullOrWhiteSpace(line) ? "\n" : line));
                this.Help.Add((title, content));
            }

            this.MainInterface = new MainInterface(this);
            this.FittingRoom = new FittingRoom(this);
            this.EditorHelper = new EditorHelper(this);
            this.ExamineHelper = new ExamineHelper(this);
            this.TryOnHelper = new TryOnHelper(this);

            Service.Interface.UiBuilder.Draw += this.Draw;
            Service.Interface.UiBuilder.OpenConfigUi += this.OpenMainInterface;
            Service.Interface.UiBuilder.OpenMainUi += this.OpenMainInterface;
        }

        public void Dispose() {
            Service.Interface.UiBuilder.OpenMainUi -= this.OpenMainInterface;
            Service.Interface.UiBuilder.OpenConfigUi -= this.OpenMainInterface;
            Service.Interface.UiBuilder.Draw -= this.Draw;
        }

        internal void OpenMainInterface() {
            this.MainInterface.Open();
        }

        internal void ToggleMainInterface() {
            this.MainInterface.Toggle();
        }

        internal void OpenFittingRoom() {
            this.FittingRoom.Open();
        }

        internal void ToggleFittingRoom() {
            this.FittingRoom.Toggle();
        }

        internal IDalamudTextureWrap? GetIcon(ushort id) {
            var icon = Service.TextureProvider.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(id)).GetWrapOrDefault();
            return icon;
        }

        private void Draw() {
            this.MainInterface.Draw();
            this.FittingRoom.Draw();
            this.EditorHelper.Draw();
            this.ExamineHelper.Draw();
            this.TryOnHelper.Draw();

            this.AlternativeFinders.RemoveAll(finder => {
                finder.Draw();
                return !finder.Visible;
            });
        }

        internal void SwitchPlate(Guid plateId, bool scrollTo = false) =>
            this.MainInterface.SwitchPlate(plateId, scrollTo);

        internal unsafe void TryOn(IEnumerable<SavedGlamourItem> items) {
            void SetTryOnSave(bool save) {
                var tryOnAgent = AgentTryon.Instance();
                if (tryOnAgent != null)
                    *(byte*) ((nint) tryOnAgent + 0x366) = (byte) (save ? 1 : 0);
            }

            SetTryOnSave(false);
            foreach (var mirage in items) {
                if (mirage.ItemId == 0) {
                    continue;
                }

                this.Plugin.Functions.TryOn(mirage.ItemId, mirage.Stain1, mirage.Stain2);
                SetTryOnSave(true);
            }
        }
    }
}