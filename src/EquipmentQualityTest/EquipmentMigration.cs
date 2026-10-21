using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using UnityEngine;

namespace SulfurEquipmentQualityTest
{
    internal sealed class EquipmentMigrationPlan
    {
        public int version = 1;
        public bool completed;
        public string fingerprint;
        public Dictionary<string, string> markers = new Dictionary<string, string>();
    }
    internal static class EquipmentMigration
    {
        private sealed class Row { internal string path; internal InventoryData data; }
        private static void Add(List<Row> rows, string path, List<InventoryData> data)
        {
            if (data == null) return;
            for (int i = 0; i < data.Count; i++) if (data[i] != null) rows.Add(new Row { path = path + "/" + i, data = data[i] });
        }
        private static void WritePlan(string file, EquipmentMigrationPlan plan)
        {
            string temp = file + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(plan, Formatting.Indented));
            if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
        }
        internal static void Ensure(SulfurSaveState save)
        {
            if (save == null || save.slotIndex < 1 || save.slotIndex > 5) return;
            var rows = new List<Row>();
            Add(rows, "inventory", save.INVENTORY);
            Add(rows, "insured", save.INSURED_ITEMS);
            if (save.STASH != null) foreach (var pair in save.STASH.OrderBy(p => p.Key, StringComparer.Ordinal)) if (pair.Value != null) Add(rows, "stash/" + pair.Key, pair.Value.list);
            if(save.SHOPKEEPER_INVENTORY!=null)foreach(var pair in save.SHOPKEEPER_INVENTORY.OrderBy(p=>p.Key,StringComparer.Ordinal)){if(pair.Value==null)continue;Add(rows,"shop/"+pair.Key,pair.Value.items);Add(rows,"shop-hidden/"+pair.Key,pair.Value.itemsHidden);}
            rows = rows.Where(r => r.data.quantity == 1 && (EquipmentQualityTest.Eligible(r.data.id.GetAsset()) ||
                (r.data.identifier ?? "").StartsWith(Quality.Prefix, StringComparison.Ordinal))).ToList();
            string folder = Path.Combine(Paths.ConfigPath, "EquipmentQuality");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "rpg3-charisma-coverage-slot"+save.slotIndex+".json");
            string fingerprint;
            using (var sha = SHA256.Create()) fingerprint = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", rows.Select(r => r.path + ":" + JsonUtility.ToJson(r.data)).ToArray()))));
            EquipmentMigrationPlan plan;
            if (File.Exists(file))
            {
                plan = JsonConvert.DeserializeObject<EquipmentMigrationPlan>(File.ReadAllText(file));
                if (plan == null || plan.version != 1 || plan.markers == null) throw new InvalidDataException("Invalid migration receipt; no replacement rolls generated");
                if (plan.completed) return;
                bool applied = plan.markers.All(p => rows.Any(r => r.path == p.Key && r.data.identifier == p.Value));
                if (!applied && plan.fingerprint != fingerprint) throw new InvalidDataException("Pending migration input changed; restore backup rather than rerolling");
            }
            else
            {
                plan = new EquipmentMigrationPlan { fingerprint = fingerprint };
                foreach (var row in rows)
                {
                    QualityRecord record;
                    if (Quality.Decode(row.data.identifier, out record))
                    {
                        if (record.itemId != row.data.id.value) throw new InvalidDataException("Existing quality record has a mismatched item ID");
                        bool changed=EquipmentQualityTest.EnsureCoverage(record,row.data.id.GetAsset());
                        changed=EquipmentQualityTest.EnsureDurability(record,row.data.id.GetAsset())||changed;
                        if(changed)plan.markers.Add(row.path,Quality.Encode(record));
                        continue;
                    }
                    if ((row.data.identifier ?? "").StartsWith(Quality.Prefix, StringComparison.Ordinal)) throw new InvalidDataException("Corrupt existing record is not replaced");
                    // Foreign identifiers may belong to another mod; do not overwrite them.
                    if (!Quality.CanInitialize(row.data.identifier)) continue;
                    plan.markers.Add(row.path, Quality.Encode(EquipmentQualityTest.NewRecord(row.data.id.GetAsset(), row.data.identifier)));
                }
                string source = SulfurSaveState.GetSlotFilePath(save.slotIndex);
                if (File.Exists(source)) File.Copy(source, Path.Combine(folder, "before-rpg3-migration-slot"+save.slotIndex+"-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".sss"));
                // Commit exact identities and rolls first, so retrying an interrupted load cannot reroll them.
                WritePlan(file, plan);
            }
            foreach (var pair in plan.markers)
            {
                QualityRecord record;
                var row = rows.SingleOrDefault(r => r.path == pair.Key);
                bool valid=Quality.Decode(pair.Value,out record)&&row!=null&&record.itemId==row.data.id.value;
                if(!valid)throw new InvalidDataException("Migration target changed");
            }
            foreach (var row in rows)
            {
                string marker;
                if (plan.markers.TryGetValue(row.path, out marker)) row.data.identifier = marker;
            }
            if(plan.markers.Count==0){plan.completed=true;WritePlan(file,plan);return;}
            SulfurSaveState.WriteActiveToDisk();
            JToken disk;
            using (var stream = File.OpenRead(SulfurSaveState.GetSlotFilePath(save.slotIndex)))
            using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip)) disk = JToken.Parse(reader.ReadToEnd());
            var persisted = new HashSet<string>(disk.SelectTokens("$..identifier").Where(v => v.Type == JTokenType.String).Select(v => (string)v));
            if (!plan.markers.Values.All(persisted.Contains)) throw new IOException("Migration records did not reach disk; receipt retained for retry");
            plan.completed = true;
            WritePlan(file, plan);
            Debug.Log("[EquipmentQuality] One-time migration committed: " + plan.markers.Count + " existing armor/accessory records. Old UUIDs retained.");
        }
    }
}
