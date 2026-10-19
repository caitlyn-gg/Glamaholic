using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;

namespace Glamaholic {
    // ReSharper disable once ClassNeverInstantiated.Global
    public class Plugin : IDalamudPlugin {
        internal static string Name => "Glamaholic";

        internal Configuration Config { get; }
        internal GameFunctions Functions { get; }
        internal PluginUi Ui { get; }
        private Commands Commands { get; }

        private DateTime LastInteropCheckTime { get; set; } = DateTime.Now;

#pragma warning disable 8618
        public Plugin(IDalamudPluginInterface pluginInterface) {
            pluginInterface.Create<Service>();

            pluginInterface.UiBuilder.DisableCutsceneUiHide = true;
            pluginInterface.UiBuilder.DisableGposeUiHide = true;

            this.Config = Configuration.LoadAndMigrate(Service.Interface!.ConfigFile);

            this.Functions = new GameFunctions(this);
            this.Ui = new PluginUi(this);
            this.Commands = new Commands(this);

            Interop.Glamourer.Initialize(Service.Interface);
            Service.Framework.Update += OnFrameworkUpdate;
        }

        private void OnFrameworkUpdate(IFramework framework) {
            var now = DateTime.Now;
            if (now.Subtract(LastInteropCheckTime).TotalSeconds < 5)
                return;

            Interop.Glamourer.RefreshStatus(Service.Interface);

            LastInteropCheckTime = now;
        }
#pragma warning restore 8618

        public void Dispose() {
            this.Commands.Dispose();
            this.Ui.Dispose();
            this.Functions.Dispose();

            Service.Framework.Update -= OnFrameworkUpdate;
        }

        public void LogTroubleshooting(string message) {
            if (!Config.TroubleshootingMode)
                return;

            Service.Log.Info($"[Troubleshooting] {message}");
        }

        internal void SaveConfig() {
            Service.Interface.SavePluginConfig(this.Config);
        }
    }
}
