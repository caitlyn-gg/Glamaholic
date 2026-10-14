using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Glamaholic {
    internal enum DyeFilter {
        Any,
        Dyeable,
        TwoChannels,
        NotDyeable,
    }

    internal enum OwnedFilter {
        Any,
        Owned,
        NotOwned,
    }

    internal enum GenderFilter {
        Any,
        WearableByMe,
        Unrestricted,
        MaleOnly,
        FemaleOnly,
    }

    internal enum FittingRoomView {
        List,
        Grid,
    }

    internal enum IconSize {
        Small,
        Medium,
        Large,
    }

    [Serializable]
    internal class FittingRoomFilters {
        // job id stored in Job when filtering by whatever job the player is currently on
        internal const uint CurrentJob = uint.MaxValue;

        public DyeFilter Dyes = DyeFilter.Any;
        public int Expansion = -1;
        public uint Job = 0;
        public int MaxLevel = 0;
        public byte Rarity = 0;
        public OwnedFilter Owned = OwnedFilter.Any;
        public GenderFilter Gender = GenderFilter.Any;
        public bool FavoritesOnly;
        public bool OutfitSetPiecesOnly;
        public bool HideDuplicateLooks;

        // per slot: the type to show (e.g. "Helmets") and the traits pieces must have
        public Dictionary<PlateSlot, string> Types = new();
        public Dictionary<PlateSlot, ItemTraits> Traits = new();

        // the outfit set being browsed, if any; not saved, since it's usually a quick look
        [JsonIgnore]
        public uint OutfitSet;

        [JsonIgnore]
        public int ActiveCount =>
            (this.Dyes != DyeFilter.Any ? 1 : 0)
            + (this.Expansion >= 0 ? 1 : 0)
            + (this.Job != 0 ? 1 : 0)
            + (this.MaxLevel > 0 ? 1 : 0)
            + (this.Rarity != 0 ? 1 : 0)
            + (this.Owned != OwnedFilter.Any ? 1 : 0)
            + (this.Gender != GenderFilter.Any ? 1 : 0)
            + (this.FavoritesOnly ? 1 : 0)
            + (this.OutfitSetPiecesOnly ? 1 : 0)
            + (this.HideDuplicateLooks ? 1 : 0)
            + this.Types.Count
            + this.Traits.Values.Count(traits => traits != ItemTraits.None)
            + (this.OutfitSet != 0 ? 1 : 0);
    }
}
