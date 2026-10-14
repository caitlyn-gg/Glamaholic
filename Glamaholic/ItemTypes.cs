using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Glamaholic {
    [Flags]
    internal enum ItemTraits {
        None = 0,
        Visor = 1,
        ShowsHair = 2,
        HidesHair = 4,
        CoversOtherSlots = 8,
    }

    internal readonly record struct TraitOption(ItemTraits Trait, string Label, string Tooltip);

    // Sorts gear into types within its slot (helmets, circlets, skirts, ...) for the Fitting Room's type filter.
    // Weapons use the game's own categories; armour is sorted by the noun in its English name.
    internal static class ItemTypes {
        internal const string Other = "Other";

        // in display order; matched against the last word of the name, e.g. "Abyssos Helm of Fending" -> "helm"
        private static readonly Dictionary<PlateSlot, (string Type, string[] Nouns)[]> ArmourTypes = new() {
            [PlateSlot.Head] = [
                ("Helmets", ["helm", "helmet", "kabuto", "sallet", "armet", "elmo", "barbut", "celata", "burgeonet", "mesail", "met"]),
                ("Hats & caps", ["hat", "cap", "beret", "chapeau", "tricorne", "bicorne", "petasos", "mortarboard", "ushanka", "longcap", "nightcap", "calot", "toque", "kasa", "fedora", "sombrero", "jingasa", "eboshi", "gibus", "capeline", "klobuk"]),
                ("Hoods & coifs", ["hood", "coif", "cowl"]),
                ("Circlets & crowns", ["circlet", "crown", "coronet", "tiara", "diadem", "headdress", "chain", "chaplet", "wreath"]),
                ("Headbands & bandanas", ["headband", "bandana", "hachigane", "kerchief", "scarf"]),
                ("Turbans", ["turban"]),
                ("Flowers & hair accessories", ["corsage", "hairpin", "ribbon", "lily", "oldrose", "rose", "wings", "flower", "bow", "hairband", "comb", "kanzashi"]),
                ("Masks", ["mask", "halfmask", "facemask", "face", "mengu", "somen", "hanbo", "veil"]),
                ("Glasses, goggles & visors", ["goggles", "monocle", "spectacles", "eyeglasses", "glasses", "magnifiers", "blinder", "visor", "sunglasses", "shades"]),
                ("Eyepatches", ["eyepatch", "gantai"]),
                ("Horns & ears", ["horn", "horns", "ears", "antlers"]),
            ],
            [PlateSlot.Body] = [
                ("Coats & jackets", ["coat", "jacket", "longcoat", "overcoat", "coatee", "jackcoat", "redingote", "swallowtail", "justaucorps", "casaque", "dolman", "bolero"]),
                ("Robes", ["robe", "halfrobe", "dalmatica", "himation", "chiton", "bliaud", "chasuble", "kasaya", "cowl", "alb"]),
                ("Dresses & gowns", ["dress", "gown", "frock"]),
                ("Shirts, tops & vests", ["shirt", "top", "tunic", "neotunic", "vest", "gilet", "doublet", "blouse", "halter", "chestwrap", "smock", "sweater", "kurta", "bustier"]),
                ("Armour", ["mail", "cuirass", "armor", "armour", "corselet", "lorica", "chainmail", "chestpiece", "haubergeon", "hauberk", "harness", "oyoroi", "domaru", "hara-ate", "battlegarb", "acton", "gambison", "jerkin", "brigandine"]),
                ("Tabards & surcoats", ["tabard", "surcoat", "cyclas"]),
                ("Eastern wear", ["dogi", "togi", "haori", "jin-baori", "yukata", "osode", "jufuku", "joi", "kimono", "happi", "koromo"]),
                ("Cloaks & ponchos", ["cloak", "poncho", "cape", "mantle", "fur", "furs", "shawl"]),
                ("Aprons", ["apron"]),
            ],
            [PlateSlot.Hands] = [
                ("Gloves", ["gloves", "glove", "shortgloves", "wristgloves", "technogloves", "mitts", "mittens", "lightmitts", "yugake"]),
                ("Halfgloves & fingerless", ["halfgloves", "fingerstalls", "fingers", "fingerwraps"]),
                ("Gauntlets", ["gauntlets", "tekko", "kote", "manicae", "clawtips", "claws", "hook"]),
                ("Armguards & bracers", ["armguards", "armguard", "vambraces", "bracers", "armlets", "wristguards", "sleeves", "cuffs", "wristbands", "wristband", "wristwraps", "armwraps", "wristlets", "wristlet", "armband"]),
                ("Rings & chains", ["ringbands", "chaplets", "chains", "loops", "ring", "torque", "torques"]),
            ],
            [PlateSlot.Legs] = [
                ("Trousers", ["trousers", "breeches", "slops", "halfslops", "pantaloons", "slacks", "kecks", "sarouel", "culottes", "brais", "gaskins", "gaskin", "chausses", "hose", "bottoms", "galligaskins", "zouaves", "braccae", "halftrews", "quan", "jui"]),
                ("Skirts & kilts", ["skirt", "longkilt", "kilt", "pareo", "petticoat"]),
                ("Shorts & underwear", ["shorts", "trunks", "smalls", "briefs", "drawers", "bloomers", "knickers", "underpants", "tanga", "subligar", "loincloth", "waistclout", "maro", "waistwrap"]),
                ("Tights & leggings", ["tights", "quartertights", "halftights", "leggings", "pantalettes"]),
                ("Hakama", ["hakama", "tsutsu-hakama", "kohakama", "tonban", "momohiki", "kyakui"]),
                ("Armour", ["poleyns", "cuisses", "flanchard", "haidate", "chaps", "brayettes", "cuissots", "legstraps"]),
            ],
            [PlateSlot.Feet] = [
                ("Boots", ["boots", "workboots", "jackboots", "longboots", "hookboots", "bootlets", "highboots", "warboots", "halfboots", "clawboots", "kneeboots", "bootees", "waders", "buskins"]),
                ("Thigh-high boots", ["thighboots"]),
                ("Shoes", ["shoes", "crakows", "duckbills", "moccasins", "loafers", "espadrilles", "clogs", "pattens", "poulaines", "pigaches", "choka", "babouches", "slippers", "technoshoes"]),
                ("Sandals", ["sandals", "caligae", "zori", "geta", "obutozori", "tabi", "guoc"]),
                ("Heels", ["heels", "pumps"]),
                ("Armour", ["sabatons", "sollerets", "greaves", "sune-ate", "schynbalds", "gambieras"]),
                ("Gaiters & wraps", ["gaiters", "leggings", "kyahan", "wraps", "guards"]),
            ],
        };

        private static readonly Dictionary<PlateSlot, TraitOption[]> TraitOptions = new() {
            [PlateSlot.Head] = [
                new(ItemTraits.Visor, "Has a visor", "Can be toggled with /visor, or animates"),
                new(ItemTraits.ShowsHair, "Shows hair", "Doesn't hide your hair or scalp"),
                new(ItemTraits.HidesHair, "Hides hair", "Covers your hair completely"),
            ],
            [PlateSlot.Body] = [
                new(ItemTraits.CoversOtherSlots, "Full outfit", "Also covers other slots, like your legs, hands or head"),
            ],
            [PlateSlot.Legs] = [
                new(ItemTraits.CoversOtherSlots, "Covers feet", "Also covers the feet slot"),
            ],
        };

        private static readonly Regex RoleSuffix = new(@"\s+of\s+(Fending|Maiming|Striking|Aiming|Scouting|Healing|Casting|Slaying)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Decorations = new(@"(\s*[\(\[].*$)|(\s+\+\d+$)|(\s+of\s+.*$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Lazy<Dictionary<PlateSlot, Dictionary<string, string>>> TypesByNoun = new(() =>
            ArmourTypes.ToDictionary(
                slot => slot.Key,
                slot => slot.Value
                    .SelectMany(type => type.Nouns.Select(noun => (noun, type.Type)))
                    .ToDictionary(pair => pair.noun, pair => pair.Type)));

        internal static bool HasTypes(PlateSlot group) {
            return group is PlateSlot.MainHand or PlateSlot.OffHand || ArmourTypes.ContainsKey(group);
        }

        // display order for armour types, or null to sort by name (weapons)
        internal static IEnumerable<string>? TypeOrder(PlateSlot group) {
            return ArmourTypes.TryGetValue(group, out var types) ? types.Select(type => type.Type).Append(Other) : null;
        }

        internal static IReadOnlyList<TraitOption> GetTraitOptions(PlateSlot group) {
            return TraitOptions.TryGetValue(group, out var options) ? options : Array.Empty<TraitOption>();
        }

        // name is the English item name; null for slots without types
        internal static string? GetType(Item item, PlateSlot group, string name) {
            if (group is PlateSlot.MainHand or PlateSlot.OffHand) {
                return item.ItemUICategory.ValueNullable?.Name.ExtractText() is { Length: > 0 } category ? category : Other;
            }

            if (!TypesByNoun.Value.TryGetValue(group, out var types)) {
                return null;
            }

            var noun = Decorations.Replace(RoleSuffix.Replace(name, string.Empty), string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()?
                .ToLowerInvariant();

            return noun != null && types.TryGetValue(noun, out var type) ? type : Other;
        }

        internal static ItemTraits GetTraits(Item item, PlateSlot group) {
            var traits = ItemTraits.None;
            var slots = item.EquipSlotCategory.Value;
            var setId = (ushort) (item.ModelMain & 0xFFFF);

            switch (group) {
                case PlateSlot.Head:
                    if (EquipmentParameters.HasVisor(setId)) {
                        traits |= ItemTraits.Visor;
                    }

                    if (EquipmentParameters.HeadShowsHair(setId) == true) {
                        traits |= ItemTraits.ShowsHair;
                    }

                    if (EquipmentParameters.HeadHidesHair(setId) == true) {
                        traits |= ItemTraits.HidesHair;
                    }

                    break;
                case PlateSlot.Body:
                    // a negative value means the piece blocks that slot
                    if (slots.Head < 0 || slots.Gloves < 0 || slots.Legs < 0 || slots.Feet < 0) {
                        traits |= ItemTraits.CoversOtherSlots;
                    }

                    break;
                case PlateSlot.Legs:
                    if (slots.Feet < 0) {
                        traits |= ItemTraits.CoversOtherSlots;
                    }

                    break;
            }

            return traits;
        }
    }
}
