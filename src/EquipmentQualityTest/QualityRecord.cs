using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;
using PerfectRandom.Sulfur.Core.CharacterStats;

namespace SulfurEquipmentQualityTest
{
    public sealed class QualityRecord
    {
        public int version = 1;
        public string guid;
        public int itemId;
        public int tier;
        public float[] rolls;
        public string originalIdentifier;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public EntityAttributes[] attributes;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public int? quality;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public List<ExtraAffix> extras;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)] public bool legacyPreserved;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public float? durabilityRoll;
    }
    public sealed class QualityRules
    {
        public float minimum = 5;
        public float maximum = 20;
        public float goodChance = 50;
    }
    public static class Quality
    {
        private sealed class EncodingEntry { internal QualityRecord snapshot; internal string marker; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<QualityRecord, EncodingEntry> encodings = new System.Runtime.CompilerServices.ConditionalWeakTable<QualityRecord, EncodingEntry>();
        private static bool Same(QualityRecord a,QualityRecord b)
        {
            if(a.rolls==null)return false;
            if(a.version!=b.version||a.guid!=b.guid||a.itemId!=b.itemId||a.tier!=b.tier||a.originalIdentifier!=b.originalIdentifier||a.quality!=b.quality||a.legacyPreserved!=b.legacyPreserved||a.durabilityRoll!=b.durabilityRoll||a.rolls.Length!=b.rolls.Length)return false;
            for(int i=0;i<a.rolls.Length;i++)if(a.rolls[i]!=b.rolls[i])return false;
            if((a.attributes==null)!=(b.attributes==null)||(a.extras==null)!=(b.extras==null))return false;
            if(a.attributes!=null){if(a.attributes.Length!=b.attributes.Length)return false;for(int i=0;i<a.attributes.Length;i++)if(a.attributes[i]!=b.attributes[i])return false;}
            if(a.extras!=null){if(a.extras.Count!=b.extras.Count)return false;for(int i=0;i<a.extras.Count;i++){var x=a.extras[i];var y=b.extras[i];if(x==null||x.key!=y.key||x.value!=y.value||x.subtype!=y.subtype)return false;}}
            return true;
        }
        public static bool CanInitialize(string marker) { return string.IsNullOrEmpty(marker); }
        public const uint DurabilitySource = 0xF174B038;
        public static void AssignDurability(QualityRecord record,EquipmentRules rules)
        {
            if(record.durabilityRoll.HasValue||!record.quality.HasValue)return;
            if(!Affixes.RulesValid(rules))throw new ArgumentException("Invalid armor durability rules");
            int grade=record.quality.Value;
            var rng=new Random(BitConverter.ToInt32(Guid.Parse(record.guid).ToByteArray(),0)^0x51D0);
            record.durabilityRoll=Affixes.RollOriginal(grade,rules,rng);
        }
        public static float WearFactor(float reduction,float cap){return 1-Math.Max(0,Math.Min(Math.Min(99,cap),reduction))/100f;}
        public static void ApplyDurability(ItemStats stats,QualityRecord record,bool enabled)
        {
            if(stats==null)return;
            stats.RemoveModifier(ItemAttributes.MaxDurability,DurabilitySource);
            if(enabled&&record.durabilityRoll.HasValue&&stats.activeItemAttributes.Contains((int)ItemAttributes.MaxDurability))
                stats.AddModifier(ItemAttributes.MaxDurability,new StatModifier(record.durabilityRoll.Value/100f,StatModType.PercentMult,DurabilitySource));
            if(stats.activeItemAttributes.Contains((int)ItemAttributes.MaxDurability)&&stats.activeItemAttributes.Contains((int)ItemAttributes.Durability))
            {
                float max=Math.Max(2,Math.Min(99999999,(int)stats.GetAttribute(ItemAttributes.MaxDurability)));
                if(stats.GetAttribute(ItemAttributes.Durability)>max)stats.SetAttributeBaseValue(ItemAttributes.Durability,max);
            }
        }
        public const string Prefix = "sulfur.equipment-quality-test.v1:";
        public static EntityAttributes[] Attributes(int id)
        {
            if (id == 701) return new[] { EntityAttributes.Stat_JumpPower, EntityAttributes.Stat_MovementSpeed, EntityAttributes.Resistance_Armor };
            if (id == 678) return new[] { EntityAttributes.ExtraDamage_Revolver };
            return new EntityAttributes[0];
        }
        public static bool Valid(QualityRecord r)
        {
            Guid guid;
            return r != null && (r.version >= 1 && r.version <= 4) && Guid.TryParse(r.guid, out guid) && r.itemId > 0 && r.itemId <= ushort.MaxValue &&
                r.tier >= 0 && r.tier < 3 && r.rolls != null && (r.version == 4 || r.rolls.Length > 0) && r.rolls.Length <= 64 &&
                r.rolls.All(v => !float.IsNaN(v) && !float.IsInfinity(v) && (r.quality==Affixes.Unreal?r.version==4&&v==100:Math.Abs(v) <= (r.version==4?90:50))) && (!r.durabilityRoll.HasValue || !float.IsNaN(r.durabilityRoll.Value)&&!float.IsInfinity(r.durabilityRoll.Value)&&(r.quality==Affixes.Unreal?r.version==4&&r.durabilityRoll.Value==100:Math.Abs(r.durabilityRoll.Value)<=90)) && Affixes.ValidExtras(r) && (r.version!=4 || r.quality.HasValue) &&
                (r.version >= 3 ? r.attributes != null && r.attributes.Length == r.rolls.Length && r.attributes.Distinct().Count() == r.attributes.Length && r.attributes.All(a => Direction(a) != 0) :
                    Attributes(r.itemId).Length == r.rolls.Length && (r.version == 1 ? r.attributes == null : r.attributes != null && r.attributes.SequenceEqual(Attributes(r.itemId)))) &&
                r.originalIdentifier != null && r.originalIdentifier.Length <= 4096;
        }
        public static bool RulesValid(QualityRules rules)
        {
            return rules != null && !float.IsNaN(rules.minimum) && !float.IsNaN(rules.maximum) && !float.IsNaN(rules.goodChance) &&
                rules.minimum >= .1f && rules.maximum <= 50 && rules.maximum >= rules.minimum &&
                Math.Ceiling(rules.minimum * 10) <= Math.Floor(rules.maximum * 10) && rules.goodChance >= 0 && rules.goodChance <= 100;
        }
        public static int Direction(EntityAttributes attribute)
        {
            string name = attribute.ToString();
            if (name.StartsWith("ExtraDamage_", StringComparison.Ordinal) || name.StartsWith("Resistance_", StringComparison.Ordinal)) return 1;
            switch(attribute)
            {
                case EntityAttributes.Stat_MaxHealth:
                case EntityAttributes.Stat_Charisma:
                case EntityAttributes.Stat_HealthRegen:
                case EntityAttributes.Stat_LungCapacity:
                case EntityAttributes.Stat_MeleeDamage:
                case EntityAttributes.Stat_CritChance:
                case EntityAttributes.Stat_CritChanceADS:
                case EntityAttributes.Stat_Lifesteal:
                case EntityAttributes.Stat_SprintBonus:
                case EntityAttributes.Stat_SwimSpeed:
                case EntityAttributes.Stat_CrouchSpeed:
                case EntityAttributes.Stat_BonusXP:
                case EntityAttributes.Stat_JumpShotDamage:
                case EntityAttributes.Stat_JumpPower:
                case EntityAttributes.Stat_MovementSpeed:
                case EntityAttributes.Resistance_Armor:
                case EntityAttributes.ExtraDamage_Revolver: return 1;
                case EntityAttributes.Stat_WeaponWeightPenalty: return -1;
                default: return 0;
            }
        }
        public static float Adjust(float value, float beneficialPercent, EntityAttributes attribute)
        {
            return value + Math.Abs(value) * beneficialPercent / 100f * Direction(attribute);
        }
        public static bool ExtendAttributes(QualityRecord record,EntityAttributes[] supported,EquipmentRules rules)
        {
            if(record.version<3||!record.quality.HasValue)return false;
            if(!Affixes.RulesValid(rules))throw new ArgumentException("Invalid coverage rules");
            var attributes=record.attributes.ToList();var rolls=record.rolls.ToList();int grade=record.quality.Value;bool changed=false;
            foreach(var attribute in supported)
            {
                if(attributes.Contains(attribute)||Direction(attribute)==0)continue;
                var rng=new Random(BitConverter.ToInt32(Guid.Parse(record.guid).ToByteArray(),0)^(int)attribute^0x4C71);
                float roll=Affixes.RollOriginal(grade,rules,rng);
                attributes.Add(attribute);rolls.Add(roll);changed=true;
            }
            record.attributes=attributes.ToArray();record.rolls=rolls.ToArray();return changed;
        }
        public static string Rating(QualityRecord record, bool chinese)
        {
            if (record.rolls.All(v => v == 0)) return chinese ? "无变化" : "Unchanged";
            if (record.rolls.All(v => v >= 0)) return chinese ? "全正向" : "All Benefits";
            if (record.rolls.All(v => v <= 0)) return chinese ? "全负向" : "All Drawbacks";
            return chinese ? "正负混合" : "Mixed";
        }
        public static QualityRecord Create(int id, string original, QualityRules rules, Random random)
        {
            return Create(id, original, rules, random, Attributes(id), 2);
        }
        public static QualityRecord Create(int id, string original, QualityRules rules, Random random, EntityAttributes[] attrs, int version = 3)
        {
            if (!RulesValid(rules) || random == null) throw new ArgumentException("Invalid quality rules");
            if (attrs == null || attrs.Length == 0) throw new ArgumentException("No supported numeric effects");
            var r = new QualityRecord { version = version, guid = Guid.NewGuid().ToString("N"), itemId = id, originalIdentifier = original ?? "", attributes = (EntityAttributes[])attrs.Clone(), rolls = new float[attrs.Length] };
            for (int i = 0; i < r.rolls.Length; i++)
            {
                bool good = random.NextDouble() * 100 < rules.goodChance;
                float magnitude = random.Next((int)Math.Ceiling(rules.minimum * 10), (int)Math.Floor(rules.maximum * 10) + 1) / 10f;
                r.rolls[i] = good ? magnitude : -magnitude;
            }
            if (!Valid(r)) throw new ArgumentException("Invalid generated record");
            return r;
        }
        public static QualityRecord Create(int id, string original)
        {
            var random = new Random(BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0));
            var r = new QualityRecord { guid = Guid.NewGuid().ToString("N"), itemId = id, tier = random.Next(3), originalIdentifier = original ?? "", rolls = new float[Attributes(id).Length] };
            if (r.rolls.Length == 0) throw new ArgumentException("Unsupported test item");
            for (int i = 0; i < r.rolls.Length; i++) r.rolls[i] = random.Next(50, (r.tier + 1) * 100 + 1) / 10f * (random.Next(2) == 0 ? -1 : 1);
            // The boot prototype deliberately includes both an upside and a downside.
            if (r.rolls.Length > 1 && r.rolls.All(v => v >= 0) || r.rolls.Length > 1 && r.rolls.All(v => v <= 0)) r.rolls[0] = -r.rolls[0];
            return r;
        }
        public static string Encode(QualityRecord r)
        {
            if(r==null)throw new ArgumentException("Invalid quality record");
            var entry=encodings.GetOrCreateValue(r);
            if(entry.snapshot!=null&&Same(r,entry.snapshot))return entry.marker;
            if (!Valid(r)) throw new ArgumentException("Invalid quality record");
            string json=JsonConvert.SerializeObject(r);
            entry.snapshot=JsonConvert.DeserializeObject<QualityRecord>(json);
            entry.marker=Prefix+Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            return entry.marker;
        }
        public static bool Decode(string marker, out QualityRecord record)
        {
            record = null;
            if (marker == null || marker.Length > 16384 || !marker.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            try
            {
                var r = JsonConvert.DeserializeObject<QualityRecord>(Encoding.UTF8.GetString(Convert.FromBase64String(marker.Substring(Prefix.Length))));
                if (!Valid(r)) return false;
                record = r; return true;
            }
            catch (Exception) { return false; }
        }
        public static List<ModifierContainer> Transform(List<ModifierContainer> source, QualityRecord r)
        {
            if (!Valid(r) || source == null) throw new ArgumentException("Invalid quality input");
            var attrs = r.version == 1 ? Attributes(r.itemId) : r.attributes;
            if (attrs.Any(a => source.Count(m => m.attribute == a) != 1)) throw new ArgumentException("Equipment effects differ from expected prototype");
            return source.Select(m => new ModifierContainer { attribute = m.attribute, modType = m.modType,
                disableEffectActivation = m.disableEffectActivation, applyItemDefinitionValue = m.applyItemDefinitionValue,
                value = Array.IndexOf(attrs, m.attribute) < 0 ? m.value : r.version == 1 ? m.value * (1 + r.rolls[Array.IndexOf(attrs, m.attribute)] / 100f) : Adjust(m.value, r.rolls[Array.IndexOf(attrs, m.attribute)], m.attribute) }).ToList();
        }
    }
}
