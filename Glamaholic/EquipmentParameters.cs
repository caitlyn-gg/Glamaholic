using System;
using System.Numerics;

namespace Glamaholic {
    // Per-model flags from the game's equipment parameter files (the same data Penumbra edits).
    // Entries are keyed by model set id, the low 16 bits of Item.ModelMain.
    internal static class EquipmentParameters {
        private const int BlockSize = 160;

        private const ulong HeadHideScalp = 0x02UL << 40;
        private const ulong HeadHideHair = 0x04UL << 40;
        private const ulong GimmickEnabled = 0x01;

        private static readonly Lazy<byte[]?> Eqp = new(() => Load("chara/xls/equipmentparameter/equipmentparameter.eqp"));
        private static readonly Lazy<byte[]?> Gmp = new(() => Load("chara/xls/equipmentparameter/gimmickparameter.gmp"));

        // null if the model has no entry
        internal static bool? HeadHidesHair(ushort setId) {
            return GetEntry(Eqp.Value, setId) is { } entry ? (entry & HeadHideHair) != 0 : null;
        }

        // null if the model has no entry
        internal static bool? HeadShowsHair(ushort setId) {
            return GetEntry(Eqp.Value, setId) is { } entry ? (entry & (HeadHideHair | HeadHideScalp)) == 0 : null;
        }

        // head pieces that can be toggled with /visor, or that animate
        internal static bool HasVisor(ushort setId) {
            return GetEntry(Gmp.Value, setId) is { } entry && (entry & GimmickEnabled) != 0;
        }

        // The files are split into blocks of 160 eight-byte entries, and only blocks in use are stored.
        // The first entry is a bitmask of which of the 64 blocks are present, in order.
        private static ulong? GetEntry(byte[]? data, ushort setId) {
            if (data == null || data.Length < 8) {
                return null;
            }

            var present = BitConverter.ToUInt64(data, 0);
            var block = setId / BlockSize;
            if (block >= 64 || (present & (1UL << block)) == 0) {
                return null;
            }

            var blockIndex = BitOperations.PopCount(present & ((1UL << block) - 1));
            var offset = (blockIndex * BlockSize + setId % BlockSize) * 8;
            return offset + 8 <= data.Length ? BitConverter.ToUInt64(data, offset) : null;
        }

        private static byte[]? Load(string path) {
            try {
                return Service.DataManager.GetFile(path)?.Data;
            } catch (Exception ex) {
                Service.Log.Warning(ex, $"Could not read {path}");
                return null;
            }
        }
    }
}
