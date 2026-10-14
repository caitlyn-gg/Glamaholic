using Dalamud.Game.Command;
using System;

namespace Glamaholic {
    internal class Commands : IDisposable {
        private Plugin Plugin { get; }

        internal Commands(Plugin plugin) {
            this.Plugin = plugin;

            Service.CommandManager.AddHandler("/glamaholic", new CommandInfo(this.OnCommand) {
                HelpMessage = $"Toggle visibility of the {Plugin.Name} window. Use \"/glamaholic fit\" for the Fitting Room.",
            });
        }

        public void Dispose() {
            Service.CommandManager.RemoveHandler("/glamaholic");
        }

        private void OnCommand(string command, string arguments) {
            switch (arguments.Trim().ToLowerInvariant()) {
                case "fit":
                case "fittingroom":
                    this.Plugin.Ui.ToggleFittingRoom();
                    break;
                default:
                    this.Plugin.Ui.ToggleMainInterface();
                    break;
            }
        }
    }
}
