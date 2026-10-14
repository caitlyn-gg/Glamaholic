using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System;
using System.Collections.Generic;

namespace Glamaholic {
    // The most recent pieces tried on in each slot, from anywhere in the game, so outfits are easier to mix and match.
    internal class TryOnHistory : IDisposable {
        internal const int MaxPerSlot = 50;

        private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

        private Plugin Plugin { get; }
        private Dictionary<PlateSlot, List<uint>> History => this.Plugin.Config.TryOnHistory;

        private readonly Hook<AgentTryon.Delegates.TryOn> _tryOnHook;
        private DateTime? _saveAt;

        // bumped whenever the history changes so the UI can refresh
        internal int Version { get; private set; }

        // only bumped when pieces are added or removed, not when one moves back to the top
        internal int MembershipVersion { get; private set; }

        internal unsafe TryOnHistory(Plugin plugin) {
            this.Plugin = plugin;

            this._tryOnHook = Service.GameInteropProvider.HookFromAddress<AgentTryon.Delegates.TryOn>(
                AgentTryon.MemberFunctionPointers.TryOn,
                this.TryOnDetour);
            this._tryOnHook.Enable();

            Service.Framework.Update += this.OnFrameworkUpdate;
        }

        public void Dispose() {
            Service.Framework.Update -= this.OnFrameworkUpdate;
            this._tryOnHook.Dispose();

            if (this._saveAt != null) {
                this.Plugin.SaveConfig();
            }
        }

        // most recent first
        internal IReadOnlyList<uint> Get(PlateSlot group) {
            return this.History.TryGetValue(group, out var items) ? items : Array.Empty<uint>();
        }

        internal void Record(uint itemId) {
            if (Favorites.GetGear(itemId) is not { } item) {
                return;
            }

            var group = Favorites.GetGroup(item);
            if (!this.History.TryGetValue(group, out var items)) {
                this.History[group] = items = new List<uint>();
            }

            if (!items.Remove(item.RowId)) {
                this.MembershipVersion++;
            }

            items.Insert(0, item.RowId);
            if (items.Count > MaxPerSlot) {
                items.RemoveRange(MaxPerSlot, items.Count - MaxPerSlot);
            }

            this.Changed();
        }

        internal void Remove(uint itemId) {
            foreach (var items in this.History.Values) {
                if (items.Remove(itemId)) {
                    this.MembershipVersion++;
                    this.Changed();
                }
            }
        }

        internal void Clear(PlateSlot group) {
            if (this.History.Remove(group)) {
                this.MembershipVersion++;
                this.Changed();
            }
        }

        private void Changed() {
            this.Version++;

            // trying on a whole plate records every piece at once, so save once afterwards rather than per piece
            this._saveAt = DateTime.UtcNow + SaveDelay;
        }

        private void OnFrameworkUpdate(IFramework framework) {
            if (this._saveAt is { } saveAt && DateTime.UtcNow >= saveAt) {
                this._saveAt = null;
                this.Plugin.SaveConfig();
            }
        }

        private bool TryOnDetour(uint openerAddonId, uint itemId, byte stain0Id, byte stain1Id, uint glamourItemId, bool applyCompanyCrest) {
            var result = this._tryOnHook.Original(openerAddonId, itemId, stain0Id, stain1Id, glamourItemId, applyCompanyCrest);

            try {
                if (result) {
                    // a glamoured item shows its glamour, so that's the piece worth remembering
                    this.Record((glamourItemId != 0 ? glamourItemId : itemId) % Util.ItemModifierMod);
                }
            } catch (Exception ex) {
                Service.Log.Error(ex, "Failed to record try on history");
            }

            return result;
        }
    }
}
