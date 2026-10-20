using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PerfectRandom.Sulfur.Core.Stats;
using PerfectRandom.Sulfur.Core.CharacterStats;

namespace SulfurRandomWeapons
{
    [Serializable]
    public sealed class RollRules
    {
        public float initialRange = 15;
        public float upgradeMin = 2;
        public float upgradeMax = 6;
        public int minPicks = 1;
        public int maxPicks = 2;
    }

    [Serializable]
    public sealed class StatGain
    {
        public int stat;
        public float percent;
    }

    [Serializable]
    public sealed class RankGain
    {
        public int rank;
        public List<StatGain> gains = new List<StatGain>();
    }

    [Serializable]
    public sealed class WeaponRecord
    {
        public int version = 2;
        public string guid;
        public int weaponId;
        public string origin;
        public string originalIdentifier;
        public uint randomState;
        public RollRules rules;
        public float[] initial;
        public float inheritedDamage;
        public List<RankGain> upgrades = new List<RankGain>();
    }

    public static class Progression
    {
        public const string Prefix = "sulfur.random-weapons.v2:";
        public const string LegacyPrefix = "sulfur.single-weapon-test.v1:";
        public const uint SourceId = 0xF174B036;
        public const uint OldFixedSourceId = 0xF174B035;
        public const int MaxRank = 1000;
        public static readonly ItemAttributes[] Attributes = { ItemAttributes.Damage, ItemAttributes.RPM, ItemAttributes.ReloadSpeed, ItemAttributes.Spread, ItemAttributes.KickMultiplier };
        public static readonly string[] Labels = { "伤害", "射速", "换弹", "精准", "控枪" };

        private static uint Next(ref uint state)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return state;
        }
        private static int Int(ref uint state, int min, int max)
        {
            return min + (int)(Next(ref state) % (uint)(max - min + 1));
        }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        public static bool RulesValid(RollRules rules)
        {
            return rules != null && Finite(rules.initialRange) && rules.initialRange >= 0 && rules.initialRange <= 50 &&
                Finite(rules.upgradeMin) && Finite(rules.upgradeMax) && rules.upgradeMin >= 0.1f &&
                rules.upgradeMax >= rules.upgradeMin && rules.upgradeMax <= 50 && Math.Ceiling(rules.upgradeMin * 10) <= Math.Floor(rules.upgradeMax * 10) &&
                rules.minPicks >= 1 && rules.maxPicks <= 2 && rules.maxPicks >= rules.minPicks;
        }
        public static WeaponRecord Create(int weaponId, RollRules rules, string origin, string previous, string guid = null, uint seed = 0)
        {
            if (!RulesValid(rules) || weaponId <= 0 || weaponId > ushort.MaxValue) throw new ArgumentException("Invalid birth rules or weapon ID.");
            if (guid == null) guid = Guid.NewGuid().ToString("N");
            if (seed == 0) { seed = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0); if (seed == 0) seed = 1; }
            var record = new WeaponRecord { guid = guid, weaponId = weaponId, origin = origin, originalIdentifier = previous ?? "",
                randomState = seed, rules = JsonConvert.DeserializeObject<RollRules>(JsonConvert.SerializeObject(rules)), initial = new float[5] };
            int range = (int)Math.Round(rules.initialRange * 10);
            for (int i = 0; i < 5; i++) record.initial[i] = Int(ref record.randomState, -range, range) / 10f;
            return record;
        }
        public static bool Valid(WeaponRecord record)
        {
            Guid parsed;
            if (record == null || record.version != 2 || !Guid.TryParse(record.guid, out parsed) || record.weaponId <= 0 || record.weaponId > ushort.MaxValue ||
                record.randomState == 0 || !RulesValid(record.rules) || record.initial == null || record.initial.Length != 5 ||
                record.initial.Any(v => !Finite(v) || Math.Abs(v) > 50) || record.upgrades == null || record.upgrades.Count > MaxRank ||
                (record.inheritedDamage != 0 && record.inheritedDamage != 10) || (record.originalIdentifier != null && record.originalIdentifier.Length > 4096)) return false;
            for (int i = 0; i < record.upgrades.Count; i++)
            {
                var level = record.upgrades[i];
                if (level == null || level.rank != i + 1 || level.gains == null || level.gains.Count < record.rules.minPicks || level.gains.Count > record.rules.maxPicks ||
                    level.gains.Any(g => g == null || g.stat < 0 || g.stat >= 5 || !Finite(g.percent) || g.percent < 0.1f || g.percent > 50) ||
                    level.gains.Select(g => g.stat).Distinct().Count() != level.gains.Count) return false;
            }
            return true;
        }
        public static bool Advance(WeaponRecord record, int rank)
        {
            if (!Valid(record)) throw new ArgumentException("Invalid saved weapon record.");
            rank = Math.Max(0, Math.Min(MaxRank, rank));
            bool changed = record.upgrades.Count < rank;
            while (record.upgrades.Count < rank)
            {
                var level = new RankGain { rank = record.upgrades.Count + 1 };
                int picks = Int(ref record.randomState, record.rules.minPicks, record.rules.maxPicks);
                var pool = new List<int> { 0, 1, 2, 3, 4 };
                for (int pick = 0; pick < picks; pick++)
                {
                    int index = Int(ref record.randomState, 0, pool.Count - 1);
                    int stat = pool[index]; pool.RemoveAt(index);
                    int value = Int(ref record.randomState, (int)Math.Ceiling(record.rules.upgradeMin * 10), (int)Math.Floor(record.rules.upgradeMax * 10));
                    level.gains.Add(new StatGain { stat = stat, percent = value / 10f });
                }
                record.upgrades.Add(level);
            }
            return changed;
        }
        public static string Encode(WeaponRecord record)
        {
            if (!Valid(record)) throw new ArgumentException("Invalid weapon record.");
            return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(record)));
        }
        public static bool TryDecode(string marker, out WeaponRecord record)
        {
            record = null;
            if (marker == null || marker.Length > 524288 || !marker.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            try
            {
                var value = JsonConvert.DeserializeObject<WeaponRecord>(Encoding.UTF8.GetString(Convert.FromBase64String(marker.Substring(Prefix.Length))));
                if (!Valid(value)) return false;
                record = value; return true;
            }
            catch (FormatException) { return false; }
            catch (JsonException) { return false; }
            catch (ArgumentException) { return false; }
        }
        public static WeaponRecord ConvertTest(string marker, int weaponId, RollRules rules)
        {
            if (marker == null || !marker.StartsWith(LegacyPrefix, StringComparison.Ordinal)) return null;
            try
            {
                var legacy = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(marker.Substring(LegacyPrefix.Length))));
                if ((int?)legacy["version"] != 1 || (int?)legacy["weaponId"] != weaponId || weaponId != 919 ||
                    (string)legacy["weaponGuid"] != "c8d7bf69-3ec8-49e1-b08c-46d8ac4091fa" || (float?)legacy["damagePercent"] != 10) return null;
                var record = Create(weaponId, rules, "test-migrated", (string)legacy["originalIdentifier"], (string)legacy["weaponGuid"]);
                record.inheritedDamage = 10; return record;
            }
            catch (FormatException) { return null; }
            catch (JsonException) { return null; }
            catch (ArgumentException) { return null; }
        }
        public static float[] Totals(WeaponRecord record)
        {
            var totals = new float[5];
            foreach (var level in record.upgrades) foreach (var gain in level.gains) totals[gain.stat] += gain.percent;
            return totals;
        }
        public static float Factor(WeaponRecord record, int stat)
        {
            double factor = (1.0 + record.initial[stat] / 100.0) * (1.0 + Totals(record)[stat] / 100.0);
            if (stat == 0) factor *= 1.0 + record.inheritedDamage / 100.0;
            return (float)(stat >= 3 ? 1.0 / factor : factor);
        }
        public static void Apply(ItemStats stats, WeaponRecord record, bool enabled)
        {
            for (int i = 0; i < Attributes.Length; i++)
            {
                stats.RemoveModifier(Attributes[i], SourceId);
                stats.RemoveModifier(Attributes[i], OldFixedSourceId);
                if (enabled && stats.activeItemAttributes.Contains((int)Attributes[i]))
                    stats.AddModifier(Attributes[i], new StatModifier(Factor(record, i) - 1, StatModType.PercentMult, SourceId));
            }
        }
        public static string Percent(float value)
        {
            return (value > 0 ? "+" : "") + value.ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }
        public static string Colored(float value) { return "<color=" + (value >= 0 ? "#93D7A0" : "#F09D94") + ">" + Percent(value) + "</color>"; }
        public static string Summary(float[] values)
        {
            return Labels[0] + " " + Colored(values[0]) + "  " + Labels[1] + " " + Colored(values[1]) + "\n" +
                Labels[2] + " " + Colored(values[2]) + "  " + Labels[3] + " " + Colored(values[3]) + "  " + Labels[4] + " " + Colored(values[4]);
        }
        public static string History(WeaponRecord record)
        {
            return string.Join("\n", record.upgrades.AsEnumerable().Reverse().Select(level => "Lv." + level.rank.ToString("00") + "  " +
                string.Join("  ", level.gains.Select(gain => Labels[gain.stat] + " " + Colored(gain.percent)).ToArray())).ToArray());
        }
    }
}
