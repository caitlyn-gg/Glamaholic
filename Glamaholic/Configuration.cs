using Dalamud.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Glamaholic {
    [Serializable]
    [JsonConverter(typeof(TreeNodeConverter))]
    internal abstract class TreeNode
    {
        public abstract string Name { get; set; }
        
        [JsonProperty]
        public Guid Id { get; set; } = Guid.NewGuid();
    }

    [Serializable]
    internal class FolderNode : TreeNode {
        public override string Name { get; set; } = "Unnamed Folder";

        [JsonConverter(typeof(TreeNodeListConverter))]
        public List<TreeNode> Children { get; set; } = new();
    }

    [Serializable]
    internal class PlateNode : TreeNode {
        public override string Name {
            get => Plate?.Name ?? "Unnamed Plate";
            set { Plate.Name = value; }
        }

        public SavedPlate Plate { get; set; }

        public PlateNode(SavedPlate plate) { Plate = plate; }

        public PlateNode() { Plate = new SavedPlate(""); }
    }

    // Custom JsonConverter for TreeNode
    internal class TreeNodeConverter : JsonConverter<TreeNode>
    {
        public override TreeNode ReadJson(JsonReader reader, Type objectType, TreeNode? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var jo = JObject.Load(reader);
            var nodeType = jo["NodeType"]?.ToString();
            TreeNode node = nodeType switch
            {
                "Folder" => new FolderNode(),
                "Plate" => new PlateNode(),
                _ => throw new JsonSerializationException($"Unknown NodeType: {nodeType}")
            };
            serializer.Populate(jo.CreateReader(), node);
            
            // Ensure ID exists for existing nodes without one
            if (node.Id == Guid.Empty)
                node.Id = Guid.NewGuid();
                
            return node;
        }

        public override void WriteJson(JsonWriter writer, TreeNode? value, JsonSerializer serializer)
        {
            JObject jo = new();
            if (value is FolderNode folder)
            {
                jo["NodeType"] = "Folder";
                jo["Id"] = folder.Id;
                jo["Name"] = folder.Name;
                jo["Children"] = JToken.FromObject(folder.Children, serializer);
            }
            else if (value is PlateNode plate)
            {
                jo["NodeType"] = "Plate";
                jo["Id"] = plate.Id;
                jo["Plate"] = JToken.FromObject(plate.Plate, serializer);
            }
            else
            {
                throw new JsonSerializationException($"Unknown node type: {(value != null ? value.GetType() : "null")}");
            }
            jo.WriteTo(writer);
        }
    }

    [Serializable]
    internal class Configuration : IPluginConfiguration {
        private const int CURRENT_VERSION = 3;

        public int Version { get; set; } = CURRENT_VERSION;

        [JsonConverter(typeof(TreeNodeListConverter))]
        public List<TreeNode> Plates { get; init; } = new();
        public bool ShowEditorMenu = true;
        public bool ShowExamineMenu = true;
        public bool ShowTryOnMenu = true;
        public bool ShowKofiButton = true;
        public bool ItemFilterShowObtainedOnly;
        public bool SyncTryOnWithSelection = true;
        public bool TroubleshootingMode = false;

        /// <summary>
        /// The last changelog version the user has seen. Stored as a string for JSON serialization.
        /// </summary>
        public string? LastSeenChangelogVersion { get; set; } = null;

        internal static void SanitisePlate(SavedPlate plate) {
            var valid = Enum.GetValues<PlateSlot>();
            foreach (var slot in plate.Items.Keys.ToArray()) {
                if (!valid.Contains(slot)) {
                    plate.Items.Remove(slot);
                }
            }
        }

        internal Guid AddPlate(SavedPlate plate, Guid? targetFolderId = null) {
            SanitisePlate(plate);
            var node = new PlateNode(plate);

            if (targetFolderId != null) {
                var folder = TreeUtils.FindNodeById(this.Plates, targetFolderId.Value) as FolderNode;
                if (folder != null) {
                    folder.Children.Add(node);
                    TreeUtils.EnsureFoldersFirst(folder.Children);
                    return node.Id;
                }
            }

            // Add as a leaf node at root (after all folders)
            this.Plates.Add(node);
            TreeUtils.EnsureFoldersFirst(this.Plates);
            return node.Id;
        }

        internal static Configuration LoadAndMigrate(FileInfo fileInfo) {
            if (!fileInfo.Exists)
                return new Configuration();

            JObject cfg;

            // i hate it so much
            using (var fileStream = fileInfo.OpenRead())
            using (var textReader = new StreamReader(fileStream))
            using (var jsonReader = new JsonTextReader(textReader))
                cfg = JObject.Load(jsonReader);

            int version = cfg.Value<int>("Version");

            if (version == CURRENT_VERSION)
            {
                return cfg.ToObject<Configuration>(JsonSerializer.Create())!;
            }

            CreateBackup(fileInfo);

            Service.Log.Info($"Migrating configuration from version {version} to {CURRENT_VERSION}");

            for (int newVersion = version + 1; newVersion <= CURRENT_VERSION; newVersion++) {
                switch (newVersion) {
                    case 2:
                        Migrate_1_2(cfg);
                        break;
                    case 3:
                        Migrate_2_3(cfg);
                        break;
                }

                cfg["Version"] = newVersion;
            }

            File.WriteAllText(fileInfo.FullName, cfg.ToString());

            return cfg.ToObject<Configuration>(JsonSerializer.Create())!;
        }

        internal static void CreateBackup(FileInfo fileInfo)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"{fileInfo.Name}.{timestamp}.bak";
            var backupPath = Path.Join(fileInfo.DirectoryName, backupFileName);
            File.Copy(fileInfo.FullName, backupPath, true);
        }

        /*
         * SavedGlamourItem renamed field: StainId -> Stain1
         * SavedGlamourItem new field: Stain2
         */
        internal static void Migrate_1_2(JObject cfg) {
            if (!cfg.ContainsKey("Plates"))
                return;

            var plates = cfg["Plates"] as JArray;
            foreach (var plate in plates!) {
                var items = plate["Items"] as JObject;
                if (items == null)
                    return;

                foreach (var kvp in items) {
                    if (kvp.Key == "$type")
                        continue;

                    var item = kvp.Value as JObject;
                    if (item == null || item.ContainsKey("Stain1") || !item.ContainsKey("StainId"))
                        continue;

                    // migrate StainId to Stain1 and add new field with default value
                    item["Stain1"] = item["StainId"];
                    item["Stain2"] = 0;

                    item.Remove("StainId");
                }
            }
        }

        /*
         * Plates changes from List<SavedPlate> to tree structure
         */
        internal static void Migrate_2_3(JObject cfg) {
            if (!cfg.ContainsKey("Plates"))
                return;

            var oldPlates = cfg["Plates"] as JArray;
            if (oldPlates == null)
                return;

            // Each plate becomes a PlateNode (leaf)
            var newPlates = new JArray();
            foreach (var plate in oldPlates) {
                var node = new JObject {
                    ["NodeType"] = "Plate",
                    ["Plate"] = plate
                };
                newPlates.Add(node);
            }

            cfg["Plates"] = newPlates;
        }
    }

    // Converter for List<TreeNode>
    internal class TreeNodeListConverter : JsonConverter<List<TreeNode>>
    {
        public override List<TreeNode> ReadJson(JsonReader reader, Type objectType, List<TreeNode>? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var array = JArray.Load(reader);
            var result = new List<TreeNode>();
            foreach (var token in array)
            {
                var node = token.ToObject<TreeNode>(serializer);
                if (node != null)
                    result.Add(node);
            }
            return result;
        }

        public override void WriteJson(JsonWriter writer, List<TreeNode>? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartArray();
            foreach (var node in value)
            {
                serializer.Serialize(writer, node);
            }
            writer.WriteEndArray();
        }
    }

    [Serializable]
    internal class SavedPlate {
        public string Name { get; set; } = string.Empty;
        public Dictionary<PlateSlot, SavedGlamourItem> Items { get; init; } = new();
        public List<string> Tags { get; } = new();
        public bool FillWithNewEmperor { get; set; } = false;

        public SavedPlate(string name) {
            this.Name = name;
        }

        internal SavedPlate Clone() {
            return new SavedPlate(this.Name) {
                Items = this.Items.ToDictionary(entry => entry.Key, entry => entry.Value.Clone()),
            };
        }
    }

    [Serializable]
    internal class SavedGlamourItem {
        public uint ItemId { get; set; }
        public byte Stain1 { get; set; }
        public byte Stain2 { get; set; }

        internal SavedGlamourItem Clone() {
            return new SavedGlamourItem() {
                ItemId = this.ItemId,
                Stain1 = this.Stain1,
                Stain2 = this.Stain2
            };
        }

        public override string ToString() {
            return $"SavedGlamourItem(ItemId={ItemId}, Stain1={Stain1}, Stain2={Stain2})";
        }
    }
}
