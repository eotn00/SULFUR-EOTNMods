using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BepInEx;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using UnityEngine;

namespace SulfurRandomWeapons
{
    public sealed class MigrationPlan
    {
        public int version = 2;
        public int slot;
        public bool completed;
        public string inputHash;
        public Dictionary<string, string> markers = new Dictionary<string, string>();
    }

    internal static class Migration
    {
        private sealed class Row { public string path; public InventoryData data; }
        private static void Add(List<Row> rows, string path, List<InventoryData> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++) if (list[i] != null) rows.Add(new Row { path = path + "/" + i, data = list[i] });
        }
        private static List<Row> Rows(SulfurSaveState save)
        {
            var rows = new List<Row>();
            Add(rows, "inventory", save.INVENTORY); Add(rows, "insured", save.INSURED_ITEMS);
            if (save.STASH != null) foreach (var pair in save.STASH.OrderBy(p => p.Key, StringComparer.Ordinal)) if (pair.Value != null) Add(rows, "stash/" + pair.Key, pair.Value.list);
            if (save.SHOPKEEPER_INVENTORY != null) foreach (var pair in save.SHOPKEEPER_INVENTORY.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (pair.Value == null) continue;
                Add(rows, "shop/" + pair.Key, pair.Value.items); Add(rows, "shop-hidden/" + pair.Key, pair.Value.itemsHidden);
            }
            return rows.Where(r => RandomWeapons.Eligible(r.data.id.GetAsset())).ToList();
        }
        private static string Fingerprint(List<Row> rows)
        {
            string text = string.Join("\n", rows.Select(r => r.path + ":" + JsonUtility.ToJson(r.data)).ToArray());
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }
        internal static void WriteJson(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(value, Formatting.Indented));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        internal static string ReadGzip(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip)) return reader.ReadToEnd();
        }
        internal static HashSet<string> DiskMarkers(string path)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var marker in JToken.Parse(ReadGzip(path)).SelectTokens("$..identifier"))
                if (marker.Type == JTokenType.String) result.Add((string)marker);
            return result;
        }
        internal static void Ensure(SulfurSaveState save)
        {
            if (save == null || save.slotIndex < 1 || save.slotIndex > 5) return;
            var rows = Rows(save);
            string file = Path.Combine(Paths.ConfigPath, "RandomWeaponProgression", "migration-slot" + save.slotIndex + ".json");
            MigrationPlan plan;
            if (File.Exists(file))
            {
                plan = JsonConvert.DeserializeObject<MigrationPlan>(File.ReadAllText(file));
                if (plan == null || plan.version != 2 || plan.slot != save.slotIndex || plan.markers == null) throw new InvalidDataException("Invalid migration receipt; refusing to generate replacement identities.");
                if (plan.completed)
                {
                    int missing = rows.Count(r => { WeaponRecord ignored; return !Progression.TryDecode(r.data.identifier, out ignored); });
                    if (missing > 0) RandomWeapons.LogWarning("Slot " + save.slotIndex + ": " + missing + " saved firearms have missing/invalid records after migration. No IDs or rolls were regenerated.");
                    return;
                }
                bool alreadyApplied = plan.markers.All(p => rows.Any(r => r.path == p.Key && r.data.identifier == p.Value));
                if (!alreadyApplied && plan.inputHash != Fingerprint(rows)) throw new InvalidDataException("Pending migration input changed; keeping the saved migration plan without rerolling.");
                foreach (var pair in plan.markers)
                {
                    WeaponRecord saved;
                    if (!Progression.TryDecode(pair.Value, out saved) || !rows.Any(r => r.path == pair.Key && r.data.id.value == saved.weaponId))
                        throw new InvalidDataException("Pending migration contains an invalid record or changed weapon; no replacement IDs were generated.");
                }
            }
            else
            {
                plan = new MigrationPlan { slot = save.slotIndex, inputHash = Fingerprint(rows) };
                foreach (var row in rows)
                {
                    WeaponRecord record;
                    if (Progression.TryDecode(row.data.identifier, out record)) continue;
                    if (row.data.identifier != null && row.data.identifier.StartsWith(Progression.Prefix, StringComparison.Ordinal))
                        throw new InvalidDataException("An existing random weapon record is invalid; migration will not replace it.");
                    record = Progression.ConvertTest(row.data.identifier, row.data.id.value, RandomWeapons.Rules());
                    if (record == null && row.data.identifier != null && row.data.identifier.StartsWith(Progression.LegacyPrefix, StringComparison.Ordinal))
                        throw new InvalidDataException("A legacy test record is invalid; migration will not replace it.");
                    if (record == null) record = Progression.Create(row.data.id.value, RandomWeapons.Rules(), "legacy-migrated", row.data.identifier);
                    plan.markers.Add(row.path, Progression.Encode(record));
                }
                // Persist the exact rolls before touching the save, so an interrupted migration can replay them.
                WriteJson(file, plan);
                string original = SulfurSaveState.GetSlotFilePath(save.slotIndex);
                if (File.Exists(original)) File.Copy(original, Path.Combine(Path.GetDirectoryName(file), "before-migration-slot" + save.slotIndex + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".sss"), false);
            }
            foreach (var row in rows)
            {
                string marker;
                if (plan.markers.TryGetValue(row.path, out marker)) row.data.identifier = marker;
            }
            SulfurSaveState.WriteActiveToDisk();
            var written = DiskMarkers(SulfurSaveState.GetSlotFilePath(save.slotIndex));
            if (!plan.markers.Values.All(written.Contains)) throw new IOException("Migration metadata did not reach disk; saved migration plan retained.");
            plan.completed = true; WriteJson(file, plan);
            RandomWeapons.LogInfo("One-time migration committed: slot=" + save.slotIndex + "; firearms=" + plan.markers.Count + ". Existing IDs will not be regenerated on load.");
        }
    }
}
