using System;
using System.Collections.Generic;
using System.Linq;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;
using PerfectRandom.Sulfur.Core.CharacterStats;

namespace SulfurEquipmentQualityTest
{
    public sealed class ExtraAffix
    {
        public string key;
        public float value;
        public int subtype;
    }
    public sealed class AffixDefinition
    {
        public string key, chinese, english;
        public float min, max;
        public EntityAttributes attribute;
        public bool integer;
        public SlotType[] slots;
    }
    public sealed class EquipmentRules
    {
        public float[] minimum = { 10, 0, 5, 10, 20, 35 };
        public float[] maximum = { 30, 0, 10, 20, 35, 50 };
        public float[] weights = { 40, 25, 20, 10, 4, 1 };
        public float[] scales = { 1, 1, 1, 1, 1.2f, 1.5f };
        public Dictionary<string, float[]> ranges = new Dictionary<string, float[]>();
    }
    public static class Affixes
    {
        private static AffixDefinition Def(string key,string cn,string en,float min,float max,EntityAttributes attr,bool integer,params SlotType[] slots)
        { return new AffixDefinition {key=key,chinese=cn,english=en,min=min,max=max,attribute=attr,integer=integer,slots=slots}; }
        public static readonly AffixDefinition[] Pool = {
            Def("armor","坚固","Sturdy",1,2,EntityAttributes.Resistance_Armor,false),
            Def("health","强健","Vigorous",3,8,EntityAttributes.Stat_MaxHealth,false),
            Def("fire","抗焰","Fireward",5,15,EntityAttributes.Resistance_Fire,false),
            Def("frost","抗寒","Frostward",5,15,EntityAttributes.Resistance_Frost,false),
            Def("poison","抗毒","Venomward",5,15,EntityAttributes.Resistance_Poison,false),
            Def("electric","绝缘","Insulated",5,15,EntityAttributes.Resistance_Electric,false),
            Def("care","养护","Preservation",7.5f,15,EntityAttributes.None,false),
            Def("crit","暴击","Critical",2,5,EntityAttributes.Stat_CritChance,false),
            Def("mastery","精通","Mastery",3,8,EntityAttributes.None,false),
            Def("melee","蛮力","Mighty",10,30,EntityAttributes.ExtraDamage_Melee,false),
            Def("breath","深息","Deep Breath",5,10,EntityAttributes.Stat_LungCapacity,false,SlotType.Head),
            Def("heavy","重甲","Reinforced",2,3,EntityAttributes.Resistance_Armor,false,SlotType.Torso),
            Def("move","轻快","Fleet",2,5,EntityAttributes.Stat_MovementSpeed,false,SlotType.Feet,SlotType.PassiveEnhancements),
            Def("sprint","疾驰","Sprinter",3,8,EntityAttributes.Stat_SprintBonus,false,SlotType.Feet),
            Def("jump","弹跃","Springy",5,10,EntityAttributes.Stat_JumpPower,false,SlotType.Feet),
            Def("extraJump","连跃","Extra Jump",1,1,EntityAttributes.Stat_ExtraJumps,true,SlotType.Feet),
            Def("swim","潜游","Aquatic",5,10,EntityAttributes.Stat_SwimSpeed,false,SlotType.Feet),
            Def("luck","幸运","Lucky",1,1,EntityAttributes.Stat_LuckGain,true,SlotType.Feet,SlotType.PassiveEnhancements)
        };
        public static readonly EntityAttributes[] Masteries = { EntityAttributes.ExtraDamage_AssaultRifle,EntityAttributes.ExtraDamage_Pistol,EntityAttributes.ExtraDamage_Revolver,EntityAttributes.ExtraDamage_Rifle,EntityAttributes.ExtraDamage_Shotgun,EntityAttributes.ExtraDamage_SMG,EntityAttributes.ExtraDamage_Sniper,EntityAttributes.ExtraDamage_LMG,EntityAttributes.Stat_GlobalDamageMultiplier };
        public static readonly string[] ChineseGrades={"破损","普通","优质","精良","史诗","传奇"};
        public static readonly string[] EnglishGrades={"Damaged","Common","Fine","Superior","Epic","Legendary"};
        public static readonly string[] GradeColors={"#A0A0A0","#FFFFFF","#69D675","#5AA7FF","#C483F5","#FFAA42"};
        public static string GradeLabel(int grade,bool chinese)
        {return "<color="+GradeColors[grade]+">"+(chinese?ChineseGrades[grade]:EnglishGrades[grade])+"</color>";}
        public static bool EligibleSlot(SlotType slot) { return slot==SlotType.Head || slot==SlotType.Torso || slot==SlotType.Legs || slot==SlotType.Hands || slot==SlotType.Feet || slot==SlotType.PassiveEnhancements; }
        public static bool RulesValid(EquipmentRules r)
        {
            if(r==null || r.minimum==null || r.maximum==null || r.weights==null || r.scales==null || r.ranges==null || r.minimum.Length!=6 || r.maximum.Length!=6 || r.weights.Length!=6 || r.scales.Length!=6) return false;
            for(int i=0;i<6;i++) if(!Finite(r.minimum[i]) || !Finite(r.maximum[i]) || r.minimum[i]<0 || r.maximum[i]>90 || r.minimum[i]>r.maximum[i] || !Finite(r.weights[i]) || r.weights[i]<0 || !Finite(r.scales[i]) || r.scales[i]<0 || r.scales[i]>5) return false;
            if(r.weights.Sum()<=0 || r.minimum[1]!=0 || r.maximum[1]!=0) return false;
            return Pool.All(d=>!r.ranges.ContainsKey(d.key) || r.ranges[d.key]!=null && r.ranges[d.key].Length==2 && Finite(r.ranges[d.key][0]) && Finite(r.ranges[d.key][1]) && r.ranges[d.key][0]>=0 && r.ranges[d.key][1]<=100 && r.ranges[d.key][0]<=r.ranges[d.key][1]);
        }
        private static bool Finite(float v){return !float.IsNaN(v)&&!float.IsInfinity(v);}
        private static float Roll(Random rng,float lo,float hi){return rng.Next((int)Math.Ceiling(lo*10),(int)Math.Floor(hi*10)+1)/10f;}
        public static void Enrich(QualityRecord record,SlotType slot,EquipmentRules rules,Random rng)
        {
            if(!RulesValid(rules)||!EligibleSlot(slot))throw new ArgumentException("Invalid quality rules or slot");
            if(record==null||record.version!=4||record.quality.HasValue)throw new InvalidOperationException("Only newly created records may roll quality");
            double pick=rng.NextDouble()*rules.weights.Sum();int grade=5;
            for(int i=0;i<6;i++){pick-=rules.weights[i];if(pick<0){grade=i;break;}}
            record.quality=grade;record.extras=new List<ExtraAffix>();record.legacyPreserved=false;
            for(int i=0;i<record.rolls.Length;i++)record.rolls[i]=grade==1?0:Roll(rng,rules.minimum[grade],rules.maximum[grade])*(grade==0?-1:1);
            var pool=Pool.Where(d=>d.slots.Length==0||Array.IndexOf(d.slots,slot)>=0).ToArray();
            for(int i=0;i<Math.Max(0,grade-2);i++)
            {
                var d=pool[rng.Next(pool.Length)];var range=rules.ranges.ContainsKey(d.key)?rules.ranges[d.key]:new[]{d.min,d.max};
                record.extras.Add(new ExtraAffix{key=d.key,value=d.integer?1:(float)Math.Round(Roll(rng,range[0],range[1])*rules.scales[grade],1),subtype=d.key=="mastery"?rng.Next(Masteries.Length):0});
            }
        }
        public static bool ValidExtras(QualityRecord r)
        {
            if(!r.quality.HasValue)return r.extras==null&&!r.legacyPreserved;
            int grade=r.quality.Value;
            return grade>=0&&grade<6&&r.extras!=null&&r.extras.Count==Math.Max(0,grade-2)&&r.extras.All(e=>e!=null&&Pool.Any(d=>d.key==e.key)&&Finite(e.value)&&e.value>=0&&e.value<=500&&(e.key!="mastery"||e.subtype>=0&&e.subtype<Masteries.Length)&&(!(e.key=="extraJump"||e.key=="luck")||e.value==1));
        }
        public static bool Percent(AffixDefinition d){return d.key!="armor"&&d.key!="heavy"&&d.key!="health"&&!d.integer;}
        public static string Describe(ExtraAffix e,bool chinese)
        {
            var d=Pool.Single(p=>p.key==e.key);
            string effect;
            switch(e.key)
            {
                case "armor":case "heavy":effect=chinese?"护甲":"Armor";break;
                case "health":effect=chinese?"最大生命":"Maximum Health";break;
                case "fire":effect=chinese?"火焰抗性":"Fire Resistance";break;
                case "frost":effect=chinese?"冰霜抗性":"Frost Resistance";break;
                case "poison":effect=chinese?"毒素抗性":"Poison Resistance";break;
                case "electric":effect=chinese?"电击抗性":"Electric Resistance";break;
                case "care":effect=chinese?"枪械与护甲耐久损耗":"Firearm and Armor Durability Loss";break;
                case "crit":effect=chinese?"暴击率":"Critical Chance";break;
                case "melee":effect=chinese?"近战伤害":"Melee Damage";break;
                case "breath":effect=chinese?"肺活量":"Lung Capacity";break;
                case "move":effect=chinese?"移动速度":"Move Speed";break;
                case "sprint":effect=chinese?"冲刺速度":"Sprint Speed";break;
                case "jump":effect=chinese?"跳跃能力":"Jump Power";break;
                case "extraJump":effect=chinese?"额外跳跃次数":"Extra Jumps";break;
                case "swim":effect=chinese?"游泳速度":"Swim Speed";break;
                case "luck":effect=chinese?"每分钟幸运增长":"Luck Gain per Minute";break;
                case "mastery":
                    var labels=chinese?new[]{"突击步枪伤害","手枪伤害","左轮伤害","步枪伤害","霰弹枪伤害","冲锋枪伤害","狙击枪伤害","轻机枪伤害","全武器伤害"}:new[]{"Assault Rifle Damage","Pistol Damage","Revolver Damage","Rifle Damage","Shotgun Damage","SMG Damage","Sniper Rifle Damage","LMG Damage","All Weapon Damage"};
                    effect=labels[e.subtype];break;
                default:throw new ArgumentException("Unknown affix");
            }
            string amount=(e.key=="care"?"-":"+")+e.value.ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+(Percent(d)?"%":"");
            return (chinese?d.chinese:d.english)+(chinese?"：":": ")+"<color=#93D7A0>"+amount+" "+effect+"</color>";
        }
        public static ModifierContainer Modifier(ExtraAffix e)
        {
            var d=Pool.Single(p=>p.key==e.key);
            if(e.key=="care")return null;
            var attr=e.key=="mastery"?Masteries[e.subtype]:d.attribute;
            bool points=e.key=="fire"||e.key=="frost"||e.key=="poison"||e.key=="electric";
            return new ModifierContainer{attribute=attr,modType=StatModType.Flat,value=Percent(d)&&!points?e.value/100f:e.value};
        }
        public static List<ModifierContainer> AddExtras(List<ModifierContainer> source,QualityRecord r)
        {
            var result=new List<ModifierContainer>(source);
            if(r.extras!=null)foreach(var e in r.extras){var m=Modifier(e);if(m!=null)result.Add(m);}
            // Duplicate affixes add together; grouping also keeps native removal-by-source correct.
            return result.GroupBy(m=>new{m.attribute,m.modType,m.disableEffectActivation,m.applyItemDefinitionValue}).Select(g=>new ModifierContainer{attribute=g.Key.attribute,modType=g.Key.modType,value=g.Sum(m=>m.value),disableEffectActivation=g.Key.disableEffectActivation,applyItemDefinitionValue=g.Key.applyItemDefinitionValue}).ToList();
        }
    }
}
