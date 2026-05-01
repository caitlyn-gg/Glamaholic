using Dalamud.Game;
using Lumina.Excel.Sheets;
using Lumina.Extensions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Glamaholic.Interop {
    internal class EorzeaCollection {
        private const string BASE_URL = "https://ffxiv.eorzeacollection.com/api/glamour/";

        // The default HttpClient UA is blocked by CloudFlare
        private const string USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

        private static bool HasLoggedError { get; set; } = false;

        public static async Task<SavedPlate?> ImportFromURL(string userFacingURL) {
            var items = DataCache.EquippableItems.Value;
            var stains = DataCache.StainsByName.Value;

            string? url = ConvertURLForAPI(userFacingURL);
            if (url == null) {
                Service.Log.Error($"EorzeaCollection Import: Invalid URL '{userFacingURL}'");
                return null;
            }

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", USER_AGENT);

            HttpResponseMessage? resp = null;
            try {
                resp = await httpClient.GetAsync(url);
                if (!resp.IsSuccessStatusCode) {
                    Service.Log.Warning($"EorzeaCollection Import returned status code {resp.StatusCode}:\n{await resp.Content.ReadAsStringAsync()}");
                    return null;
                }
            } catch (Exception e) {
                Service.Log.Warning(e, $"EorzeaCollection Import: Request failed with Exception");
                return null;
            }

            if (resp == null) {
                Service.Log.Warning($"EorzeaCollection Import: Request failed with no response");
                return null;
            }

            try {
                var glam = JsonConvert.DeserializeObject<Glamour>(await resp.Content.ReadAsStringAsync());
                SavedPlate plate = new(glam.Name);

                foreach (var gearSlot in glam.Gear) {
                    var ecSlotName = gearSlot.Key;
                    if (ecSlotName == "fashion" || ecSlotName == "facewear")
                        continue;

                    if (!gearSlot.Value.HasValue)
                        continue;

                    var plateSlot = ParseSlot(ecSlotName);
                    if (plateSlot == null)
                        continue;

                    var name = gearSlot.Value.Value.Name.ToLower();
                    var dyes = gearSlot.Value.Value.Dyes;

                    if (name.Length == 0)
                        continue;

                    // Some items use NBSP (\xA0) instead of space (\x20) for some reason.
                    var item = items.FirstOrNull(i => i.Name.ExtractText().ToLower() == name);
                    if (item == null
                        && (item = items.FirstOrNull(i => i.Name.ExtractText().ToLower() == name.Replace("\x20", "\xA0"))) == null) {
                        Service.Log.Warning($"EorzeaCollection Import: Item '{name}' not found in Item sheet");
                        continue;
                    }

                    if (dyes == null) {
                        plate.Items.Add(plateSlot.Value, new SavedGlamourItem() { ItemId = item.Value.RowId });
                        continue;
                    }

                    byte stain1, stain2 = stain1 = 0;

                    if (dyes[0] != "none") {
                        if (!stains.TryGetValue(dyes[0], out stain1)) {
                            Service.Log.Warning($"EorzeaCollection Import: Stain '{dyes[0]}' not found in Stain sheet");
                        }   
                    }

                    if (dyes.Length > 1 && dyes[1] != "none") {
                        if (!stains.TryGetValue(dyes[1], out stain2)) {
                            Service.Log.Warning($"EorzeaCollection Import: Stain '{dyes[1]}' not found in Stain sheet");
                        }
                    }

                    plate.Items.Add(plateSlot.Value, new SavedGlamourItem() { ItemId = item.Value.RowId, Stain1 = stain1, Stain2 = stain2 });
                } // end foreach in gear

                return plate;
            } catch (Exception e) {
                Service.Log.Warning($"EorzeaCollection Import: Failed to parse response: {e.Message}");
                return null;
            }
        }

        public static bool IsEorzeaCollectionURL(string url) {
            return ConvertURLForAPI(url) != null;
        }

        private static string? ConvertURLForAPI(string userFacingURL) {
            if (!Uri.TryCreate(userFacingURL, UriKind.Absolute, out var uri))
                return null;

            if (uri.Host != "ffxiv.eorzeacollection.com")
                return null;

            var path = uri.AbsolutePath;
            if (!path.StartsWith("/glamour/"))
                return null;

            var parts = path.Split("/");
            // [0] is empty, [1] is "glamour", [2] is the ID

            if (!Int64.TryParse(parts[2], out _))
                return null;

            return BASE_URL + parts[2];
        }

        private static PlateSlot? ParseSlot(string slot) {
            switch (slot) {
                case "head":
                    return PlateSlot.Head;
                case "body":
                    return PlateSlot.Body;
                case "hands":
                    return PlateSlot.Hands;
                case "legs":
                    return PlateSlot.Legs;
                case "feet":
                    return PlateSlot.Feet;
                case "weapon":
                    return PlateSlot.MainHand;
                case "offhand":
                    return PlateSlot.OffHand;
                case "earrings":
                    return PlateSlot.Ears;
                case "necklace":
                    return PlateSlot.Neck;
                case "bracelets":
                    return PlateSlot.Wrists;
                case "left_ring":
                    return PlateSlot.LeftRing;
                case "right_ring":
                    return PlateSlot.RightRing;
                default:
                    Service.Log.Warning($"EorzeaCollection Import: Unknown slot '{slot}'");
                    return null;
            }
        }

        [Serializable]
        private struct Glamour {
            [JsonProperty("id")]
            public uint ID { get; private set; }

            [JsonProperty("name")]
            public string Name { get; private set; }

            [JsonProperty("character")]
            public string Character { get; private set; }

            [JsonProperty("server")]
            public string Server { get; private set; }

            [JsonProperty("gear")]
            public Dictionary<string, GlamourSlot?> Gear { get; private set; }
        }

        [Serializable]
        private struct GlamourSlot {
            [JsonProperty("name")]
            public string Name { get; private set; }

            [JsonProperty("dyes")]
            private string _Dyes { get; set; }

            public string[]? Dyes {
                get {
                    if (_Dyes == null)
                        return null;

                    string[] dyes = _Dyes.Split(",");
                    for (int i = 0; i < dyes.Length; i++)
                        dyes[i] = FixupDyeName(dyes[i]);

                    return dyes;
                }
            }

            private string FixupDyeName(string dye) {
                dye = dye.ToLower();
                if (dye == "opo-opo-brown")
                    return "opo-opo brown";
                return dye.Replace("-", " ");
            }
        }
    }
}