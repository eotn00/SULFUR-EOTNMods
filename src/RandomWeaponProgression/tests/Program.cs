using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Mono.Cecil;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;
using PerfectRandom.Sulfur.Core.CharacterStats;
using SulfurRandomWeapons;

class Program
{
    static int checks;
    static void Check(bool pass, string label) { if (!pass) throw new Exception("FAIL: " + label); checks++; }
    static void Near(float a, float b, string label) { Check(Math.Abs(a - b) < .001, label + " actual=" + a + " expected=" + b); }
    static WeaponRecord Record(uint seed = 123) { return Progression.Create(919, new RollRules(), "test", "", "12345678-1234-1234-1234-123456789abc", seed); }
    static InventoryData Data(ItemStats stats)
    {
        var attributes = stats.SerializedAttributeData(); WeaponState.Attach(stats, attributes);
        var data = new InventoryData(new ItemId(919), InventorySlot.Weapon0, 1, 1, CaliberTypes._9mm, attributes, new ItemId[0], new ItemId[0], 0, 4, 2);
        WeaponState.ExportConstructor(data); return data;
    }
    static string ReadGzip(string file)
    {
        using var input = File.OpenRead(file); using var gzip = new GZipStream(input, CompressionMode.Decompress); using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }
    static void Main(string[] args)
    {
        var record = Record();
        Check(Progression.Valid(record), "birth record valid");
        Check(record.initial.Length == 6 && record.initial.All(v => v >= -15 && v <= 15), "six signed initial rolls in range");
        var old=Record();old.version=2;old.initial=old.initial.Take(5).ToArray();Progression.Advance(old,0);
        // Model an actual old five-stat save and retain its seed and prior history.
        old.version=2;old.initial=old.initial.Take(5).ToArray();
        string oldId=old.guid;uint oldSeed=old.randomState;var oldRolls=old.initial.ToArray();
        Check(Progression.TryDecode(Progression.Encode(old),out var oldSaved),"five-stat save accepted");
        Progression.Advance(oldSaved,0);
        Check(oldSaved.initial.Length==6 && oldSaved.initial[5]==0 && oldSaved.initial.Take(5).SequenceEqual(oldRolls) && oldSaved.randomState==oldSeed && oldSaved.guid==oldId,"legacy sixth stat appended as zero without reroll or seed consumption");
        var durability=Record();durability.initial[5]=20;
        var durabilityStats=new ItemStats();durabilityStats.SetAttributeBaseValue(ItemAttributes.MaxDurability,1000);durabilityStats.SetAttributeBaseValue(ItemAttributes.Durability,900);
        Progression.Apply(durabilityStats,durability,true);
        Near(durabilityStats.GetAttribute(ItemAttributes.MaxDurability),1200,"positive durability scales upward, not inverse");
        Near(durabilityStats.GetAttribute(ItemAttributes.Durability),900,"increasing maximum does not repair current durability");
        durability.initial[5]=-20;Progression.Apply(durabilityStats,durability,true);
        Near(durabilityStats.GetAttribute(ItemAttributes.Durability),800,"decreasing maximum clamps current durability");
        var one = Progression.Create(919, new RollRules(), "new", ""); var two = Progression.Create(919, new RollRules(), "new", "");
        Check(one.guid != two.guid, "identical gun models get distinct UUIDs");
        string birth = Progression.Encode(record);
        Check(Progression.TryDecode(birth, out var decoded) && Progression.Encode(decoded) == birth, "birth record roundtrip exact");
        var gradual = Record(); for (int rank = 1; rank <= 20; rank++) Progression.Advance(gradual, rank);
        Progression.Advance(record, 20);
        Check(Progression.Encode(gradual) == Progression.Encode(record), "multi-rank jump matches gradual leveling exactly");
        Check(record.upgrades.All(level => level.gains.Count >= 1 && level.gains.Count <= 2 && level.gains.All(g => g.percent >= 2 && g.percent <= 6) && level.gains.Select(g => g.stat).Distinct().Count() == level.gains.Count), "unique positive 1-2 stat gains per level in range");
        string advanced = Progression.Encode(record);
        for (int i = 0; i < 100; i++) Progression.Advance(record, 20);
        Check(Progression.Encode(record) == advanced, "repeated load or same rank does not reroll");
        Progression.Advance(record, 0); Progression.Advance(record, 10);
        Check(Progression.Encode(record) == advanced, "rank reduction and regain does not reroll earned gains");
        Check(Progression.TryDecode(advanced, out decoded), "leveled record decodes");
        Progression.Advance(decoded, 21); Progression.Advance(record, 21);
        Check(Progression.Encode(decoded) == Progression.Encode(record), "next upgrade matches after process-independent restoration");
        var rules = new RollRules(); var frozen = Progression.Create(919, rules, "new", ""); rules.upgradeMax = 20;
        Check(frozen.rules.upgradeMax == 6, "birth rules are captured, config edits do not change existing guns");
        bool validSweep = true, negativeSeen = false, positiveSeen = false, bothPickCounts = false;
        var pickCounts = new System.Collections.Generic.HashSet<int>();
        for (uint seed = 1; seed <= 300; seed++)
        {
            var sample = Record(seed); Progression.Advance(sample, 15);
            validSweep &= Progression.Valid(sample) && Enumerable.Range(0, 6).All(stat => Progression.Factor(sample, stat) > 0 && float.IsFinite(Progression.Factor(sample, stat)));
            negativeSeen |= sample.initial.Any(v => v < 0); positiveSeen |= sample.initial.Any(v => v > 0);
            foreach (var level in sample.upgrades) pickCounts.Add(level.gains.Count);
        }
        bothPickCounts = pickCounts.SetEquals(new[] { 1, 2 });
        Check(validSweep, "300 seeds retain valid positive stat factors");
        Check(negativeSeen && positiveSeen && bothPickCounts, "both birth signs and both upgrade pick counts occur");
        var stats = new ItemStats();
        foreach (var attr in Progression.Attributes) stats.SetAttributeBaseValue(attr, 100);
        stats.SetAttributeBaseValue(ItemAttributes.MaxDurability,5000);
        stats.SetAttributeBaseValue(ItemAttributes.Durability, 2331); stats.SetAttributeBaseValue(ItemAttributes.Experience, 3268.6084f);
        stats.AddModifier(ItemAttributes.Damage, new StatModifier(.2f, StatModType.PercentMult, 123));
        WeaponState.Register(stats, record);
        for (int i = 0; i < 50; i++) Progression.Apply(stats, record, true);
        Near(stats.GetAttribute(ItemAttributes.Damage), 120 * Progression.Factor(record, 0), "bonus coexists with enchantments without stacking");
        Near(stats.GetBaseAttribute(ItemAttributes.Damage), 100, "base damage unchanged");
        Check(stats.itemAttributes[(int)ItemAttributes.Damage].statModifiers.Count == 2, "one own modifier and one external modifier");
        Progression.Apply(stats, record, false); Near(stats.GetAttribute(ItemAttributes.Damage), 120, "disable preserves external modifiers");
        Check(Data(stats).identifier == Progression.Encode(record), "disabled mode still preserves UUID and history");
        Progression.Apply(stats, record, true);
        var data = Data(stats); var loaded = new ItemStats(); loaded.SetAttributeBaseValue(ItemAttributes.Damage, 100); loaded.LoadAttributesFromData(data.attributes);
        Check(WeaponState.Import(loaded, data), "metadata restored after original attribute loader");
        WeaponState.TryGet(loaded, out var loadedRecord); Progression.Apply(loaded, loadedRecord, true);
        Near(loaded.GetAttribute(ItemAttributes.Damage), 100 * Progression.Factor(record, 0), "saved random damage rebuilt");
        Near(loaded.GetAttribute(ItemAttributes.Durability), 2331, "durability unchanged");
        Near(loaded.GetAttribute(ItemAttributes.Experience), 3268.6084f, "XP unchanged");
        Check(Data(loaded).identifier == data.identifier, "second save preserves exact record");
        var unmarked = Data(new ItemStats()); Check(string.IsNullOrEmpty(unmarked.identifier), "plain constructors do not silently generate identities");
        Check(!WeaponState.Import(new ItemStats(), unmarked), "missing saved record is not repaired");
        data.id = new ItemId(988); Check(!WeaponState.Import(new ItemStats(), data), "mismatched weapon ID rejected"); data.id = new ItemId(919);
        string corrupt = Progression.Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"));
        Check(!Progression.TryDecode(corrupt, out _), "invalid schema rejected");
        Check(!Progression.TryDecode(Progression.Prefix + "!", out _), "invalid encoding rejected");
        var invalid = Record(); invalid.initial[0] = float.NaN; Check(!Progression.Valid(invalid), "NaN rejected");
        invalid = Record(); Progression.Advance(invalid, 1); invalid.upgrades[0].rank = 2; Check(!Progression.Valid(invalid), "gapped level history rejected");
        var legacy = new JObject { ["version"] = 1, ["weaponGuid"] = "c8d7bf69-3ec8-49e1-b08c-46d8ac4091fa", ["weaponId"] = 919, ["damagePercent"] = 10, ["originalIdentifier"] = "" };
        string oldMarker = Progression.LegacyPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(legacy.ToString(Formatting.None)));
        var converted = Progression.ConvertTest(oldMarker, 919, new RollRules());
        Check(converted != null && converted.guid == (string)legacy["weaponGuid"] && converted.inheritedDamage == 10, "successful trial ID and 10 percent bonus preserved");
        Check(Progression.ConvertTest(oldMarker, 988, new RollRules()) == null, "trial migration rejects wrong weapon");
        string beforeLanguageSwitch = Progression.Encode(record);
        WeaponText.Language = () => "Chinese (Simplified)";
        Check(Progression.Summary(record.initial).Contains("伤害") && Progression.History(record).Contains("Lv.21"), "Chinese UI shows birth stats and every recorded level");
        WeaponText.Language = () => "English";
        Check(Progression.Summary(record.initial).Contains("Accuracy") && !Progression.Summary(record.initial).Contains("伤害"), "English weapon summary uses English labels");
        Check(Progression.History(record).Contains("Lv.21") && !Progression.History(record).Contains("精准"), "English history keeps every recorded rank");
        Check(WeaponText.Pick("中文", "English") == "English", "English headings selected");
        Check(WeaponText.IsChinese("zh-CN") && WeaponText.IsChinese("Chinese (Traditional)") && !WeaponText.IsChinese("German"), "Chinese variants detected, other languages fall back to English");
        Check(WeaponText.Settings.Count == 8 && WeaponText.Settings.Values.All(v => v.Length == 2 && v.All(s => !string.IsNullOrEmpty(s))), "all settings have Chinese and English translations");
        WeaponText.Language = () => "zh-TW";
        Check(WeaponText.Pick("中文", "English") == "中文", "language changes are reflected without restart");
        Check(Progression.Encode(record) == beforeLanguageSwitch, "language switching does not alter weapon identity, rolls, random state or save bytes");
        var cap = Record(); Progression.Advance(cap, int.MaxValue); Check(cap.upgrades.Count == Progression.MaxRank && Progression.Valid(cap), "bounded history handles large rank safely");
        Check(Progression.TryDecode(Progression.Encode(cap), out _), "maximum history fits serialization bounds");
        using var game = AssemblyDefinition.ReadAssembly(Path.Combine(args[1], "Sulfur_Data/Managed/PerfectRandom.Sulfur.Core.dll"));
        using var plugin = AssemblyDefinition.ReadAssembly(args[0]);
        using var options = AssemblyDefinition.ReadAssembly(Path.Combine(args[1], "BepInEx/plugins/OptionsScreen设置项.dll"));
        var registry = options.MainModule.Types.Single(t => t.Name == "ModOptionsRegistry");
        Check(registry.Methods.Count(m => m.Name == "RegisterCategoryLoc" && m.Parameters.Count == 4) == 1, "installed options plugin supports localized category registration");
        Check(registry.Methods.Count(m => m.Name == "AddBoolLoc" && m.Parameters.Count == 9) == 1, "installed options plugin supports localized toggles");
        Check(registry.Methods.Count(m => m.Name == "AddSliderLoc" && m.Parameters.Count == 13) == 1, "installed options plugin supports localized sliders");
        Check(registry.Methods.Count(m => m.Name == "AddInfoLoc" && m.Parameters.Count == 7) == 1, "installed options plugin supports both section headers");
        var patches = plugin.MainModule.Types.Where(type => type.CustomAttributes.Any(attr => attr.AttributeType.Name == "HarmonyPatch")).ToArray();
        Check(patches.Length == 5, "unified weapon, equipment, constructor and description patch containers annotated");
        Check(plugin.MainModule.Types.Count(t=>t.CustomAttributes.Any(a=>a.AttributeType.Name=="BepInPlugin"))==1,"unified assembly exposes exactly one BepInEx plugin");
        foreach (var method in patches.SelectMany(type => type.Methods))
        {
            var patch = method.CustomAttributes.FirstOrDefault(attr => attr.AttributeType.Name == "HarmonyPatch");
            if (patch == null) continue;
            var type = (TypeReference)patch.ConstructorArguments[0].Value; string name = (string)patch.ConstructorArguments[1].Value;
            Check(game.MainModule.GetType(type.FullName).Methods.Any(target => target.Name == name), "game patch target " + type.Name + "." + name);
        }
        Check(plugin.MainModule.Types.Single(t => t.Name == "WeaponDescription").Methods.Any(m => m.Name == "AddWeaponInfo"), "information panel integration compiled");
        if (args.Length > 3 && args[2]=="--audit-save")
        {
            var actual=JToken.Parse(ReadGzip(args[3]));int count=0;
            foreach(var marker in actual.SelectTokens("$..identifier").Where(t=>t.Type==JTokenType.String&&((string)t).StartsWith(Progression.Prefix)))
            {
                Check(Progression.TryDecode((string)marker,out var saved),"actual firearm marker decodes");
                var previous=saved.initial.ToArray();uint seed=saved.randomState;string guid=saved.guid;string history=JsonConvert.SerializeObject(saved.upgrades);
                Progression.Upgrade(saved);
                Check(saved.initial.Take(previous.Length).SequenceEqual(previous)&&saved.randomState==seed&&saved.guid==guid&&JsonConvert.SerializeObject(saved.upgrades)==history,"actual old firearm rolls, seed, UUID and all history retained");
                Check(Progression.TryDecode(Progression.Encode(saved),out _),"actual six-stat marker roundtrip valid");count++;
            }
            Console.WriteLine("PASS: dry-run six-stat migration of "+count+" actual firearm records; source save was not written.");
        }
        else if (args.Length > 2)
        {
            var original = JToken.Parse(ReadGzip(args[2])); var copy = original.DeepClone();
            var target = copy["INVENTORY"].Children().First(item => (int)item["id"]["value"] == 919 && (bool)item["equipped"]);
            var originalMarker = target["identifier"].DeepClone(); target["identifier"] = Progression.Encode(record);
            using var stream = new MemoryStream();
            using (var gzip = new GZipStream(stream, CompressionMode.Compress, true)) using (var writer = new StreamWriter(gzip)) writer.Write(copy.ToString(Formatting.None));
            stream.Position = 0; using var zipReader = new StreamReader(new GZipStream(stream, CompressionMode.Decompress, true));
            var restored = JToken.Parse(zipReader.ReadToEnd());
            var restoredTarget = restored["INVENTORY"].Children().First(item => (int)item["id"]["value"] == 919 && (bool)item["equipped"]);
            Check(Progression.TryDecode((string)restoredTarget["identifier"], out var diskRecord) && Progression.Encode(diskRecord) == Progression.Encode(record), "full save JSON and gzip preserve record");
            restoredTarget["identifier"] = originalMarker;
            Check(JToken.DeepEquals(original, restored), "all unrelated values preserved in full save roundtrip");
        }
        Console.WriteLine("PASS: " + checks + " checks; random generation, per-rank progression, stable restoration, independent IDs, modifier composition, UI text, patch targets, and full-save serialization.");
    }
}
