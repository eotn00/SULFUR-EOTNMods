using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Mono.Cecil;
using SulfurEquipmentQualityTest;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;
using PerfectRandom.Sulfur.Core.CharacterStats;

class Program
{
    static int checks;
    sealed class EasterRandom:Random
    {
        readonly int ticket;
        public EasterRandom(int ticket,int seed):base(seed){this.ticket=ticket;}
        public override int Next(int maxValue){return maxValue==10000?ticket:base.Next(maxValue);}
    }
    static void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); checks++; }
    static string Read(string path) { using var f = File.OpenRead(path); using var g = new GZipStream(f, CompressionMode.Decompress); using var r = new StreamReader(g); return r.ReadToEnd(); }
    static void Main(string[] args)
    {
        var rules = new QualityRules();
        Check(Quality.CanInitialize(null)&&Quality.CanInitialize(""),"only unmarked items initialize");
        Check(!Quality.CanInitialize("foreign-mod-id")&&!Quality.CanInitialize("sulfur.random-weapons.v2:record"),"foreign IDs and gun records never initialize");
        Check(!Quality.CanInitialize(Quality.Encode(Quality.Create(701,""))),"existing equipment quality cannot initialize again");
        Check(rules.minimum == 5 && rules.maximum == 20 && rules.goodChance == 50, "defaults 5 to 20 percent, independent 50 percent benefits");
        var rng = new Random(123456);
        int benefits = 0, allGood = 0, allBad = 0;
        for(int trial=0;trial<10000;trial++)
        {
            var r=Quality.Create(701,"",rules,rng);
            Check(r.version==2 && r.rolls.All(v=>Math.Abs(v)>=5 && Math.Abs(v)<=20),"new roll bounds");
            benefits+=r.rolls.Count(v=>v>0);
            if(r.rolls.All(v=>v>0))allGood++;
            if(r.rolls.All(v=>v<0))allBad++;
        }
        Check(Math.Abs(benefits/30000.0-.5)<.015,"benefit probability matches 50 percent over seeded sample");
        Check(allGood>1000 && allGood<1500 && allBad>1000 && allBad<1500,"all-good/all-bad are allowed without forced mixed rolls");
        var good=Quality.Create(701,"",new QualityRules { minimum=10,maximum=10,goodChance=100 },rng);
        var bad=Quality.Create(701,"",new QualityRules { minimum=10,maximum=10,goodChance=0 },rng);
        Check(good.rolls.All(v=>v==10) && bad.rolls.All(v=>v==-10),"probability endpoints and fixed magnitude");
        Check(Quality.Rating(good,false)=="All Benefits" && Quality.Rating(bad,false)=="All Drawbacks","quality derived from actual results");
        Check(Math.Abs(Quality.Adjust(-.1f,20,EntityAttributes.Stat_MovementSpeed)-(-.08f))<.00001,"good roll softens original movement penalty");
        Check(Math.Abs(Quality.Adjust(-.1f,-20,EntityAttributes.Stat_MovementSpeed)-(-.12f))<.00001,"bad roll worsens original movement penalty");
        Check(Math.Abs(Quality.Adjust(.2f,20,EntityAttributes.Stat_WeaponWeightPenalty)-.16f)<.00001,"good roll reduces lower-is-better weight penalty");
        Check(Quality.Adjust(1,20,EntityAttributes.Stat_Blindfolded)==1,"unsupported special effects untouched");
        var encoded=Quality.Encode(good);
        for(int pass=0;pass<3;pass++){Check(Quality.Decode(encoded,out var restored),"v2 record decode");Check(Quality.Encode(restored)==encoded,"v2 record stable");}
        Check(!Quality.RulesValid(new QualityRules { minimum=20,maximum=5 }) && !Quality.RulesValid(new QualityRules { goodChance=float.NaN }),"invalid configuration rejected");
        var gearRules=new EquipmentRules();
        Check(Affixes.RulesValid(gearRules),"six-tier defaults valid");
        Check(Quality.Direction(EntityAttributes.Stat_Charisma)==1,"charisma higher is beneficial");
        var missing=new QualityRecord{version=4,guid=Guid.NewGuid().ToString("N"),itemId=999,originalIdentifier="",attributes=new[]{EntityAttributes.Resistance_Armor},rolls=new[]{6.3f},quality=2,extras=new List<ExtraAffix>()};
        string savedId=missing.guid;
        Check(Quality.ExtendAttributes(missing,new[]{EntityAttributes.Resistance_Armor,EntityAttributes.Stat_Charisma},gearRules),"existing missing charisma roll appended");
        Check(missing.guid==savedId&&missing.rolls[0]==6.3f&&missing.quality==2&&missing.extras.Count==0&&missing.rolls[1]>=5&&missing.rolls[1]<=10,"coverage repair preserves old rolls and follows saved quality range");
        var repaired=Quality.Encode(missing);
        Check(!Quality.ExtendAttributes(missing,new[]{EntityAttributes.Stat_Charisma},gearRules)&&Quality.Encode(missing)==repaired,"coverage repair is idempotent");
        Check(Quality.Adjust(25,missing.rolls[1],EntityAttributes.Stat_Charisma)>25&&Quality.Adjust(25,-20,EntityAttributes.Stat_Charisma)==20,"charisma benefit and penalty actually change modifier value");
        for(int tier=0;tier<7;tier++)
        {
            Check(Affixes.GradeLabel(tier,true)=="<color="+Affixes.GradeColors[tier]+">"+Affixes.ChineseGrades[tier]+"</color>","only Chinese quality name receives grade color");
            Check(Affixes.GradeLabel(tier,false)=="<color="+Affixes.GradeColors[tier]+">"+Affixes.EnglishGrades[tier]+"</color>","English quality name uses matching color");
        }
        Check(Affixes.Describe(new ExtraAffix{key="armor",value=2},true).Contains("+2 护甲"),"Chinese affix explicitly names armor");
        Check(Affixes.Describe(new ExtraAffix{key="armor",value=2},false).Contains("+2 Armor"),"English affix explicitly names armor");
        Check(Affixes.Describe(new ExtraAffix{key="care",value=10},true).Contains("-10% 枪械与护甲耐久损耗"),"preservation describes firearm and armor wear");
        Check(Affixes.Describe(new ExtraAffix{key="mastery",value=5,subtype=7},true).Contains("轻机枪伤害"),"LMG mastery has Chinese attribute name");
        foreach(var d in Affixes.Pool)
        {
            var e=new ExtraAffix{key=d.key,value=d.min,subtype=0};
            Check(Affixes.Describe(e,true).Contains("：")&&Affixes.Describe(e,false).Contains(": "),"both languages have named effect separator");
            Check(Affixes.Describe(e,false).All(c=>c<128),"English affix description stays English");
        }
        bool repeatAffixes=false,allWeaponSeen=false,luckSeen=false,jumpSeen=false;
        foreach(var slot in new[]{PerfectRandom.Sulfur.Core.SlotType.Head,PerfectRandom.Sulfur.Core.SlotType.Torso,PerfectRandom.Sulfur.Core.SlotType.Feet,PerfectRandom.Sulfur.Core.SlotType.PassiveEnhancements})
        for(int grade=0;grade<6;grade++)
        for(int sample=0;sample<80;sample++)
        {
            var forced=new EquipmentRules();forced.weights=new float[6];forced.weights[grade]=1;
            var r=new QualityRecord{version=4,guid=Guid.NewGuid().ToString("N"),itemId=999,originalIdentifier="",attributes=new[]{EntityAttributes.Stat_MaxHealth},rolls=new float[1]};
            Affixes.Enrich(r,slot,forced,new EasterRandom(1,rng.Next()));
            Check(Quality.Valid(r)&&r.quality==grade&&r.extras.Count==Math.Max(0,grade-2),"tier grade/count valid");
            string beforeArmor=Quality.Encode(r);var extrasBefore=JsonConvert.SerializeObject(r.extras);
            Quality.AssignDurability(r,forced);float armorRoll=r.durabilityRoll.Value;
            Check(grade==1?armorRoll==0:Math.Abs(armorRoll)>=forced.minimum[grade]&&Math.Abs(armorRoll)<=forced.maximum[grade]&&(grade==0?armorRoll<0:armorRoll>0),"armor durability follows grade direction and range");
            Check(JsonConvert.SerializeObject(r.extras)==extrasBefore,"adding armor durability does not reroll affixes");
            Quality.AssignDurability(r,new EquipmentRules());Check(r.durabilityRoll==armorRoll,"saved armor durability does not reroll after config change");
            Check(grade==1?r.rolls[0]==0:Math.Abs(r.rolls[0])>=forced.minimum[grade]&&Math.Abs(r.rolls[0])<=forced.maximum[grade]&&(grade==0?r.rolls[0]<=0:r.rolls[0]>=0),"grade-specific original stat range");
            foreach(var e in r.extras)
            {
                var d=Affixes.Pool.Single(d=>d.key==e.key);
                Check(d.slots.Length==0||Array.IndexOf(d.slots,slot)>=0,"affix respects slot restrictions");
                Check(d.integer?e.value==1:e.value>=Math.Round(d.min*forced.scales[grade],1)-.01&&e.value<=Math.Round(d.max*forced.scales[grade],1)+.01,"affix range and quality strength");
                if(e.key=="luck")luckSeen=true;if(e.key=="extraJump")jumpSeen=true;if(e.key=="mastery"&&e.subtype==Affixes.Masteries.Length-1)allWeaponSeen=true;
            }
            repeatAffixes|=r.extras.Select(e=>e.key).Distinct().Count()<r.extras.Count;
            string exact=Quality.Encode(r);Check(Quality.Decode(exact,out var saved)&&Quality.Encode(saved)==exact,"tier/affix save roundtrip exact");
        }
        Check(repeatAffixes&&allWeaponSeen&&luckSeen&&jumpSeen,"repeat rolls, all-weapon mastery, luck and extra jump reachable");
        var redRules=new EquipmentRules();redRules.ranges["mastery"]=new[]{4f,9f};redRules.ranges["crit"]=new[]{2f,6f};
        bool randomValuesDiffer=false;float firstRandom=-1;
        foreach(var slot in new[]{PerfectRandom.Sulfur.Core.SlotType.Head,PerfectRandom.Sulfur.Core.SlotType.Torso,PerfectRandom.Sulfur.Core.SlotType.Legs,PerfectRandom.Sulfur.Core.SlotType.Hands,PerfectRandom.Sulfur.Core.SlotType.Feet,PerfectRandom.Sulfur.Core.SlotType.PassiveEnhancements})
        for(int sample=0;sample<100;sample++)
        {
            var red=new QualityRecord{version=4,guid=Guid.NewGuid().ToString("N"),itemId=999,originalIdentifier="",attributes=new[]{EntityAttributes.Stat_MaxHealth,EntityAttributes.Stat_WeaponWeightPenalty},rolls=new float[2]};
            Affixes.Enrich(red,slot,redRules,new EasterRandom(0,sample));
            Check(Quality.Valid(red)&&red.quality==6&&red.rolls.All(v=>v==100)&&red.extras.Count==5,"unreal has fixed 100 percent originals and five extras");
            Check(red.extras[0].key=="mastery"&&red.extras[0].subtype==Affixes.Masteries.Length-1&&red.extras[0].value==18&&red.extras[1].key=="crit"&&red.extras[1].value==12,"fixed extras use configured maxima doubled");
            foreach(var e in red.extras.Skip(2))
            {
                var d=Affixes.Pool.Single(d=>d.key==e.key);var range=redRules.ranges.TryGetValue(d.key,out var configured)?configured:new[]{d.min,d.max};
                Check(d.slots.Length==0||Array.IndexOf(d.slots,slot)>=0,"unreal random extras respect slot pool");
                Check(d.integer?e.value==1:e.value>=Math.Round(range[0]*2,1)-.01&&e.value<=Math.Round(range[1]*2,1)+.01,"unreal random range doubled with integer exceptions");
                if(firstRandom<0)firstRandom=e.value;else randomValuesDiffer|=e.value!=firstRandom;
            }
            Quality.AssignDurability(red,redRules);Check(red.durabilityRoll==100,"unreal armor max durability doubled");
            Check(Quality.ExtendAttributes(red,new[]{EntityAttributes.Stat_Charisma},redRules)&&red.rolls.Last()==100,"unreal future attribute coverage stays fixed");
            var marker=Quality.Encode(red);Check(Quality.Decode(marker,out var roundtrip)&&Quality.Encode(roundtrip)==marker,"unreal marker exact roundtrip");
            Check(!Quality.CanInitialize(marker),"unreal saved records cannot reroll");
            red.rolls[0]=99;Check(!Quality.Valid(red),"unreal malformed original roll rejected");red.rolls[0]=100;
            red.extras[0].subtype=0;Check(!Quality.Valid(red),"unreal missing global fixed affix rejected");
        }
        Check(randomValuesDiffer,"unreal random affixes are not fixed maximum");
        Check(Quality.Adjust(10,100,EntityAttributes.Stat_MaxHealth)==20&&Quality.Adjust(-10,100,EntityAttributes.Stat_MaxHealth)==0,"unreal doubles benefits and clears negative stats");
        Check(Quality.Adjust(.2f,100,EntityAttributes.Stat_WeaponWeightPenalty)==0&&Quality.Adjust(-.2f,100,EntityAttributes.Stat_WeaponWeightPenalty)==-.4f,"unreal handles lower-is-better stats");
        var edgeRules=new EquipmentRules();edgeRules.weights=new[]{0f,1f,0f,0f,0f,0f};
        foreach(int ticket in new[]{0,1,9999})
        {
            var edge=new QualityRecord{version=4,guid=Guid.NewGuid().ToString("N"),itemId=999,originalIdentifier="",attributes=new EntityAttributes[0],rolls=new float[0]};
            Affixes.Enrich(edge,PerfectRandom.Sulfur.Core.SlotType.Head,edgeRules,new EasterRandom(ticket,1));
            Check(edge.quality==(ticket==0?6:1),"independent 10000-ticket gate preserves six-tier weights even at boundaries");
        }
        var armorStats=new ItemStats();armorStats.SetAttributeBaseValue(ItemAttributes.MaxDurability,500);armorStats.SetAttributeBaseValue(ItemAttributes.Durability,450);
        var armorRecord=Quality.Create(701,"");armorRecord.durabilityRoll=20;
        for(int i=0;i<10;i++)Quality.ApplyDurability(armorStats,armorRecord,true);
        Check(Math.Abs(armorStats.GetAttribute(ItemAttributes.MaxDurability)-600)<.001&&armorStats.GetAttribute(ItemAttributes.Durability)==450,"armor max durability scales without stacking or free repair");
        armorRecord.durabilityRoll=-20;Quality.ApplyDurability(armorStats,armorRecord,true);
        Check(Math.Abs(armorStats.GetAttribute(ItemAttributes.MaxDurability)-400)<.001&&armorStats.GetAttribute(ItemAttributes.Durability)==400,"damaged armor cap clamps excess current durability");
        Quality.ApplyDurability(armorStats,armorRecord,false);
        Check(armorStats.GetAttribute(ItemAttributes.MaxDurability)==500&&armorStats.GetAttribute(ItemAttributes.Durability)==400,"equipment disable removes durability effect and retains damage");
        Check(Math.Abs(Quality.WearFactor(30,80)-.7f)<.0001&&Math.Abs(Quality.WearFactor(120,80)-.2f)<.0001,"preservation reduction stacks up to configured cap");
        var legacyGear=Quality.Create(701,"");string legacyMarker=Quality.Encode(legacyGear);bool refused=false;
        try{Affixes.Enrich(legacyGear,PerfectRandom.Sulfur.Core.SlotType.Feet,gearRules,rng);}catch(InvalidOperationException){refused=true;}
        Check(refused&&Quality.Encode(legacyGear)==legacyMarker,"legacy reroll rejected and original record unchanged");
        var repeats=new QualityRecord{version=4,guid=Guid.NewGuid().ToString("N"),itemId=999,originalIdentifier="",attributes=new EntityAttributes[0],rolls=new float[0],quality=5,extras=new List<ExtraAffix>{new ExtraAffix{key="armor",value=2},new ExtraAffix{key="armor",value=2},new ExtraAffix{key="heavy",value=3}}};
        Check(Quality.Valid(repeats),"extra-only equipment records supported");
        var grouped=Affixes.AddExtras(new List<ModifierContainer>(),repeats);
        Check(grouped.Count==1&&grouped[0].value==7,"duplicate sturdy/heavy affixes coexist and sum");
        Check(Math.Abs(Affixes.Modifier(new ExtraAffix{key="crit",value=5}).value-.05)<.00001,"crit percentage points converted to native probability");
        Check(Affixes.Modifier(new ExtraAffix{key="fire",value=15}).value==15,"resistance percentage points use native units");
        Check(Affixes.Modifier(new ExtraAffix{key="mastery",subtype=Affixes.Masteries.Length-1,value=8}).attribute==EntityAttributes.Stat_GlobalDamageMultiplier,"all-weapon mastery uses global native damage once");
        var expanded=Quality.Create(999,"",rules,rng,new[]{EntityAttributes.Stat_MaxHealth,EntityAttributes.Resistance_Fire,EntityAttributes.Stat_WeaponWeightPenalty});
        Check(expanded.version==3 && Quality.Valid(expanded),"generic equipment IDs and attribute lists supported");
        var expandedSource=new List<ModifierContainer>{
            new ModifierContainer {attribute=EntityAttributes.Stat_MaxHealth,value=-10,modType=StatModType.Flat},
            new ModifierContainer {attribute=EntityAttributes.Resistance_Fire,value=.1f,modType=StatModType.Flat},
            new ModifierContainer {attribute=EntityAttributes.Stat_WeaponWeightPenalty,value=.2f,modType=StatModType.PercentAdd},
            new ModifierContainer {attribute=EntityAttributes.Stat_Blindfolded,value=1,modType=StatModType.Flat}};
        expanded.rolls=new[]{20f,-10f,20f};
        var expandedResult=Quality.Transform(expandedSource,expanded);
        Check(Math.Abs(expandedResult[0].value-(-8))<.00001,"negative max-health effect softened by benefit");
        Check(Math.Abs(expandedResult[1].value-.09f)<.00001 && Math.Abs(expandedResult[2].value-.16f)<.00001,"resistance drawback and weight benefit use correct directions");
        Check(expandedResult[3].value==1 && expandedSource[0].value==-10,"special effect and original shared values unchanged");
        Check(Quality.Decode(Quality.Encode(expanded),out var expandedSaved) && Quality.Encode(expandedSaved)==Quality.Encode(expanded),"v3 arbitrary equipment roundtrip stable");
        expandedSaved.attributes[0]=expandedSaved.attributes[1];Check(!Quality.Valid(expandedSaved),"duplicate saved attributes rejected");
        var boot = Quality.Create(701, ""); var badge = Quality.Create(678, "");
        Check(Quality.Valid(boot) && Quality.Valid(badge), "both targets supported");
        Check(boot.guid != badge.guid, "individual IDs");
        Check(boot.rolls.Any(v => v > 0) && boot.rolls.Any(v => v < 0), "boot has upside and downside");
        for (int i = 0; i < 100; i++)
        {
            var sample = Quality.Create(i % 2 == 0 ? 701 : 678, "original");
            Check(Quality.Decode(Quality.Encode(sample), out var restored) && Quality.Encode(restored) == Quality.Encode(sample), "record roundtrip " + i);
        }
        var source = Quality.Attributes(701).Select((a,i) => new ModifierContainer { attribute = a, value = i == 0 ? .25f : i == 1 ? .1f : 1f, modType = i == 2 ? StatModType.Flat : StatModType.PercentAdd }).ToList();
        string originalModifiers = JsonConvert.SerializeObject(source);
        var transformed = Quality.Transform(source, boot);
        Check(originalModifiers == JsonConvert.SerializeObject(source), "shared definition unchanged");
        Check(transformed.Zip(source, (a,b) => !ReferenceEquals(a,b)).All(v => v), "modifier objects copied independently");
        for (int i=0;i<3;i++) Check(Math.Abs(transformed[i].value-source[i].value*(1+boot.rolls[i]/100)) < .00001f && transformed[i].modType == source[i].modType, "scale and operation preserved " + i);
        Check(JsonConvert.SerializeObject(Quality.Transform(source,boot)) == JsonConvert.SerializeObject(transformed), "rebuild starts from original, no stacking");
        foreach(var modifier in transformed)
        {
            var stat = new CharacterStat(100);
            stat.AddModifier(new StatModifier(.1f, StatModType.PercentAdd, 111));
            float baseline=stat.Value;
            for(int cycle=0;cycle<10;cycle++)
            {
                stat.AddModifier(new StatModifier(modifier.value,modifier.modType,222));
                Check(stat.statModifiers.Count(v=>v.SourceId==222)==1,"equip one own modifier");
                stat.RemoveAllModifiersFromSource(222);
                Check(Math.Abs(stat.Value-baseline)<.0001f && stat.statModifiers.Count==1,"unequip fully restores baseline and external effects");
            }
        }
        var badgeSource = new List<ModifierContainer> { new ModifierContainer { attribute = EntityAttributes.ExtraDamage_Revolver, modType = StatModType.PercentAdd, value = .15f } };
        Check(Math.Abs(Quality.Transform(badgeSource,badge)[0].value - .15f*(1+badge.rolls[0]/100)) < .00001f, "revolver bonus is relative, not global damage");
        Check(!Quality.Decode("", out _) && !Quality.Decode(Quality.Prefix+"!",out _) && !Quality.Decode(Quality.Prefix+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{}")),out _), "missing and corrupt records rejected");
        var invalid = Quality.Create(701, ""); invalid.rolls[0] = float.NaN; Check(!Quality.Valid(invalid), "NaN rejected");
        invalid.rolls[0] = 51; Check(!Quality.Valid(invalid), "out of range rejected");
        using var plugin = AssemblyDefinition.ReadAssembly(args[0]);
        Check(plugin.MainModule.Types.Count(t=>t.CustomAttributes.Any(a=>a.AttributeType.Name=="BepInPlugin"))==1,"single plugin entry point after merge");
        Check(!plugin.MainModule.Types.SelectMany(t=>t.Methods).Any(m=>m.Name=="ZeroExistingGunDurability"||m.Name=="EnrichLegacy"),"forced reroll and gun reset entry points absent from plugin");
        Check(!plugin.MainModule.Types.SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is string s&&s.Contains("authorized-reroll")),"plugin does not consume private cleanup receipts");
        using var core = AssemblyDefinition.ReadAssembly("D:/game/online/Steam/steamapps/common/SULFUR/Sulfur_Data/Managed/PerfectRandom.Sulfur.Core.dll");
        var runtime = plugin.MainModule.Types.Single(t=>t.Name=="EquipmentQualityTest");
        foreach(string name in new[]{"BeforeSpawnPickup","BeforePickupSetup","BeforeSetup"})
        {
            var method=runtime.Methods.Single(m=>m.Name==name);
            Check(method.Body.Instructions.Any(i=>i.Operand is MethodReference called && called.Name=="CanonicalDefinition"),name+" resolves a database definition before use");
            Check(method.Parameters.Single(p=>p.Name=="item").ParameterType.IsByReference,name+" replaces the original argument");
        }
        var resolver=runtime.Methods.Single(m=>m.Name=="CanonicalDefinition");
        Check(resolver.Body.Instructions.Any(i=>i.Operand is MethodReference called && called.Name=="GetAsset"),"world definitions come from the persistent item database");
        var supported=runtime.Methods.Single(m=>m.Name=="Supported");
        Check(!supported.Body.Instructions.Any(i=>i.Operand is MethodReference called && called.Name=="Attributes"),"equipment eligibility no longer uses the two-item whitelist");
        Check(runtime.Methods.Single(m=>m.Name=="Migrate").Body.Instructions.Any(i=>i.Operand is MethodReference called && called.Name=="Ensure"),"load hook invokes one-time equipment migration");
        var migration=plugin.MainModule.Types.Single(t=>t.Name=="EquipmentMigration").Methods.Single(m=>m.Name=="Ensure");
        var code=migration.Body.Instructions;
        int journal=code.ToList().FindIndex(i=>i.Operand is MethodReference called && called.Name=="WritePlan");
        int mutate=code.ToList().FindIndex(i=>i.Operand is FieldReference field && field.Name=="identifier" && i.OpCode.Code==Mono.Cecil.Cil.Code.Stfld);
        Check(journal>=0 && mutate>journal,"exact migration rolls journaled before save identifiers mutate");
        foreach (var method in plugin.MainModule.Types.SelectMany(t => t.Methods))
        {
            var patch = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "HarmonyPatch");
            if(patch == null) continue;
            var type=(TypeReference)patch.ConstructorArguments[0].Value; string name=(string)patch.ConstructorArguments[1].Value;
            Check(core.MainModule.GetType(type.FullName).Methods.Any(m=>m.Name==name), "patch target " + name);
        }
        if(args.Length==3&&args[1]=="--audit-current")
        {
            var actual=JToken.Parse(Read(args[2]));int count=0;
            foreach(var marker in actual.SelectTokens("$..identifier").Where(t=>t.Type==JTokenType.String&&((string)t).StartsWith(Quality.Prefix)))
            {
                Check(Quality.Decode((string)marker,out var stored),"current equipment record accepted");
                Check(Quality.Encode(stored)==(string)marker,"current equipment record byte exact");
                Check(!Quality.CanInitialize((string)marker),"current equipment excluded from new quality roll");count++;
            }
            Console.WriteLine("PASS: "+checks+" checks including read-only audit of "+count+" current equipment records; no save writes.");return;
        }
        if(args.Length<2){Console.WriteLine("PASS: "+checks+" offline checks; legacy two-item save fixture skipped.");return;}
        string path=args[1]; byte[] bytes=File.ReadAllBytes(path); var save=JToken.Parse(Read(path)); var copy=save.DeepClone();
        var targets=copy["INVENTORY"].Children().Where(i => (int)i["id"]["value"] == 701 && (bool)i["equipped"] && (int)i["inventorySlot"] == 3 || (int)i["id"]["value"] == 678 && (bool)i["equipped"] && (int)i["inventorySlot"] == 12).ToArray();
        Check(targets.Length == 2 && targets.All(i=>(int)i["quantity"]==1 && ((string)i["identifier"]=="" || Quality.Decode((string)i["identifier"],out var existing) && existing.itemId==(int)i["id"]["value"])), "exactly two valid single equipped items in slot 5");
        Check((float)targets.Single(i=>(int)i["id"]["value"]==701)["attributes"]["itemAttributes"].Single(i=>(int)i["id"]==11)["value"]["BaseValue"]==119, "boot durability matches screenshot");
        var previous=targets.Select(i=>i["identifier"].DeepClone()).ToArray();
        foreach(var marker in previous.Select(v=>(string)v).Where(v=>!string.IsNullOrEmpty(v)))
        {
            Check(Quality.Decode(marker,out var legacyRecord) && Quality.Encode(legacyRecord)==marker,"existing saved marker remains byte-for-byte unchanged");
        }
        for(int i=0;i<targets.Length;i++) targets[i]["identifier"]=Quality.Encode((int)targets[i]["id"]["value"]==701 ? boot : badge);
        string markerSnapshot=copy.ToString(Formatting.None);
        for(int pass=0;pass<3;pass++)
        {
            copy=JToken.Parse(copy.ToString(Formatting.None));
            var marked=copy["INVENTORY"].Children().Where(i=>((string)i["identifier"] ?? "").StartsWith(Quality.Prefix)).ToArray();
            Check(marked.Length==2 && marked.All(i=>Quality.Decode((string)i["identifier"],out var r) && Quality.Encode(r)==(string)i["identifier"]), "save reload " + pass);
        }
        Check(copy.ToString(Formatting.None)==markerSnapshot,"three reloads do not reroll or modify any data");
        var restoredTargets=copy["INVENTORY"].Children().Where(i=>((string)i["identifier"] ?? "").StartsWith(Quality.Prefix)).ToArray();
        for(int i=0;i<restoredTargets.Length;i++) restoredTargets[i]["identifier"]=previous[i];
        Check(JToken.DeepEquals(save,copy),"only two identifiers changed; all weapons, inventory, stash and durability untouched");
        Console.WriteLine("PASS: " + checks + " checks. This is offline validation, not a live gameplay test.");
        if(args.Length>3&&args[2]=="--audit-save")
        {
            var actualSave=JToken.Parse(Read(args[3]));int count=0;
            foreach(var token in actualSave.SelectTokens("$..identifier").Where(t=>t.Type==JTokenType.String&&((string)t).StartsWith(Quality.Prefix)))
            {
                string marker=(string)token;Check(Quality.Decode(marker,out var actual),"actual saved equipment decodes");
                Check(Quality.Encode(actual)==marker,"actual marker remains byte-exact before migration");
                string id=actual.guid;var oldRoll=actual.rolls.ToArray();int version=actual.version;
                Check(!Quality.CanInitialize(marker),"actual existing quality record is excluded from initialization");
                Check(actual.guid==id&&actual.version==version&&actual.rolls.SequenceEqual(oldRoll),"actual saved original values retained during enrichment");
                var enriched=Quality.Encode(actual);Check(Quality.Decode(enriched,out var roundtrip)&&Quality.Encode(roundtrip)==enriched,"actual enriched record reload stable");count++;
            }
            Console.WriteLine("PASS: preservation audit of "+count+" actual equipment records; source save was not written.");
        }
        if(args.Contains("--install-records"))
        {
            if(targets.Any(i=>(string)previous[Array.IndexOf(targets,i)]!="")) throw new Exception("Already initialized; refusing to replace existing IDs and rolls");
            if(System.Diagnostics.Process.GetProcessesByName("Sulfur").Length>0) throw new Exception("Game running");
            if(!File.ReadAllBytes(path).SequenceEqual(bytes)) throw new Exception("Save changed during test");
            string output=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(args[0]),"../test-records.json"));
            File.WriteAllText(output,JsonConvert.SerializeObject(new[]{boot,badge},Formatting.Indented));
            string temp=path+".quality-test.tmp";
            using(var f=File.Create(temp)) using(var g=new GZipStream(f,CompressionMode.Compress)) using(var w=new StreamWriter(g)) w.Write(markerSnapshot);
            Check(JToken.DeepEquals(JToken.Parse(Read(temp)),JToken.Parse(markerSnapshot)),"compressed save verified before replacement");
            File.Replace(temp,path,null);
            Console.WriteLine("Initialized two test records. Receipt: " + output);
            Console.WriteLine(JsonConvert.SerializeObject(new[]{boot,badge},Formatting.Indented));
        }
    }
}
