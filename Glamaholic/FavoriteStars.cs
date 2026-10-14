using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Glamaholic {
    // Puts a star in front of favorited item names in shop, market board and loot windows.
    internal unsafe class FavoriteStars : IDisposable {
        private static readonly string[] Addons = [
            "Shop",                 // gil shops
            "ShopExchangeCurrency", // tomestones, scrips, seals, ...
            "ShopExchangeItem",     // item trades (tokens, materials, ...)
            "ShopExchangeCoin",     // MGP
            "InclusionShop",        // category shops (e.g. Bicolor Gemstones)
            "GrandCompanyExchange",
            "FreeShop",
            "ItemSearch",           // market board search results
            "ItemSearchResult",     // market board listings
            "NeedGreed",            // loot rolls
        ];

        private static readonly byte[] Prefix = Encoding.UTF8.GetBytes(SeIconChar.BoxedStar.ToIconString() + " ");

        private Plugin Plugin { get; }

        // hash of the text each node had when it was last checked, so unchanged nodes are skipped
        private readonly Dictionary<nint, int> _checkedText = new();
        private HashSet<string> _names = new();
        private (int Version, bool Enabled) _namesState = (-1, false);

        internal FavoriteStars(Plugin plugin) {
            this.Plugin = plugin;

            Service.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, Addons, this.OnPostDraw);
            Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, Addons, this.OnPreFinalize);
        }

        public void Dispose() {
            Service.AddonLifecycle.UnregisterListener(this.OnPostDraw, this.OnPreFinalize);

            // strip any stars still showing in open windows
            this._names = new HashSet<string>();
            this._checkedText.Clear();
            foreach (var name in Addons) {
                var addon = (AtkUnitBase*) Service.GameGui.GetAddonByName(name).Address;
                if (addon != null) {
                    this.ProcessNodes(&addon->UldManager, 0);
                }
            }
        }

        private void OnPreFinalize(AddonEvent type, AddonArgs args) {
            this._checkedText.Clear();
        }

        private void OnPostDraw(AddonEvent type, AddonArgs args) {
            var state = (this.Plugin.Favorites.Version, this.Plugin.Config.ShowFavoriteStars);
            if (state != this._namesState) {
                this._names = state.ShowFavoriteStars ? GetFavoriteNames(this.Plugin.Config.FavoriteItems) : new HashSet<string>();
                this._namesState = state;
                this._checkedText.Clear();
            }

            var addon = (AtkUnitBase*) args.Addon.Address;
            if (addon != null && addon->IsVisible) {
                this.ProcessNodes(&addon->UldManager, 0);
            }
        }

        // names in the client's language, since that's what the game displays
        private static HashSet<string> GetFavoriteNames(IEnumerable<uint> itemIds) {
            var sheet = Service.DataManager.GetExcelSheet<Item>()!;
            return itemIds
                .Select(id => sheet.GetRowOrDefault(id))
                .Where(item => item != null)
                .Select(item => Normalize(item!.Value.Name.ExtractText()))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private void ProcessNodes(AtkUldManager* uld, int depth) {
            if (depth > 8) {
                return;
            }

            for (var i = 0; i < uld->NodeListCount; i++) {
                var node = uld->NodeList[i];
                if (node == null) {
                    continue;
                }

                if (node->Type == NodeType.Text) {
                    this.ProcessText((AtkTextNode*) node);
                } else if ((ushort) node->Type >= 1000) {
                    // list rows and other components keep their own node trees
                    var component = ((AtkComponentNode*) node)->Component;
                    if (component != null) {
                        this.ProcessNodes(&component->UldManager, depth + 1);
                    }
                }
            }
        }

        private void ProcessText(AtkTextNode* node) {
            var text = node->NodeText.AsSpan();
            if (text.IsEmpty || text.Length > 512) {
                return;
            }

            var hash = Hash(text);
            if (this._checkedText.TryGetValue((nint) node, out var lastHash) && lastHash == hash) {
                return;
            }

            var starred = text.StartsWith(Prefix);
            var rest = starred ? text[Prefix.Length..] : text;
            var wanted = this._names.Count > 0 && this._names.Contains(Normalize(new ReadOnlySeStringSpan(rest).ExtractText()));

            if (wanted != starred) {
                byte[] updated = wanted ? [.. Prefix, .. rest, 0] : [.. rest, 0];
                fixed (byte* ptr = updated) {
                    node->SetText(ptr);
                }

                hash = Hash(node->NodeText.AsSpan());
            }

            this._checkedText[(nint) node] = hash;
        }

        private static int Hash(ReadOnlySpan<byte> bytes) {
            var hash = new HashCode();
            hash.AddBytes(bytes);
            return hash.ToHashCode();
        }

        // drops the HQ/collectible glyphs and soft hyphens the game may add to displayed item names
        private static string Normalize(string name) {
            var builder = new StringBuilder(name.Length);
            foreach (var c in name) {
                if (c != '­' && c != (char) SeIconChar.HighQuality && c != (char) SeIconChar.Collectible) {
                    builder.Append(c);
                }
            }

            return builder.ToString().Trim();
        }
    }
}
