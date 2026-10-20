using System;
using System.Runtime.CompilerServices;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;

namespace SulfurRandomWeapons
{
    public static class WeaponState
    {
        private static readonly ConditionalWeakTable<ItemStats, WeaponRecord> stats = new ConditionalWeakTable<ItemStats, WeaponRecord>();
        private static readonly ConditionalWeakTable<ItemAttributeCollectionData, WeaponRecord> collections = new ConditionalWeakTable<ItemAttributeCollectionData, WeaponRecord>();
        public static bool TryGet(ItemStats key, out WeaponRecord record) { record = null; return key != null && stats.TryGetValue(key, out record); }
        public static void Register(ItemStats key, WeaponRecord record)
        {
            if (key == null || !Progression.Valid(record)) throw new ArgumentException("Invalid weapon state.");
            stats.Remove(key); stats.Add(key, record);
        }
        public static void Attach(ItemStats key, ItemAttributeCollectionData data)
        {
            WeaponRecord record;
            if (data == null || !TryGet(key, out record)) return;
            collections.Remove(data); collections.Add(data, record);
        }
        public static void ExportConstructor(InventoryData data)
        {
            WeaponRecord record;
            if (data != null && data.attributes != null && collections.TryGetValue(data.attributes, out record) && data.id.value == record.weaponId)
                data.identifier = Progression.Encode(record);
        }
        public static void Export(ItemStats key, InventoryData data)
        {
            WeaponRecord record;
            if (data != null && TryGet(key, out record) && data.id.value == record.weaponId) data.identifier = Progression.Encode(record);
        }
        public static bool Import(ItemStats key, InventoryData data)
        {
            WeaponRecord record;
            if (data == null || !Progression.TryDecode(data.identifier, out record) || record.weaponId != data.id.value) return false;
            Register(key, record); return true;
        }
    }
}
