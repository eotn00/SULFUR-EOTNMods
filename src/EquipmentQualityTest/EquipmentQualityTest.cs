using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;
using UnityEngine;
using Description = PerfectRandom.Sulfur.Core.UI.ItemDescription.ItemDescription;

namespace SulfurEquipmentQualityTest
{
    [HarmonyPatch]
    public static class EquipmentQualityTest
    {
        private sealed class State { internal QualityRecord record; internal ItemDefinition clone; }
        private static readonly ConditionalWeakTable<InventoryItem, State> items = new ConditionalWeakTable<InventoryItem, State>();
        private static readonly ConditionalWeakTable<ItemStats, QualityRecord> stats = new ConditionalWeakTable<ItemStats, QualityRecord>();
        private static readonly ConditionalWeakTable<ItemAttributeCollectionData, QualityRecord> collections = new ConditionalWeakTable<ItemAttributeCollectionData, QualityRecord>();
        private static ManualLogSource log;
        internal static ConfigEntry<bool> Enabled;
        private static PropertyInfo language;
        private static TMPro.TMP_FontAsset chineseFallback;
        private static readonly ConditionalWeakTable<Pickup, QualityRecord> pickupRecords = new ConditionalWeakTable<Pickup, QualityRecord>();
        [ThreadStatic] private static QualityRecord pickupContext;
        private sealed class WearCache { internal object save; internal int revision; internal float reduction; }
        private static readonly ConditionalWeakTable<EquipmentManager,WearCache> wearCaches=new ConditionalWeakTable<EquipmentManager,WearCache>();
        private static int wearRevision;
        private static void InvalidateWear() { unchecked { wearRevision++; } }
        [HarmonyPatch(typeof(EquipmentManager),"OnEquipmentChange")]
        [HarmonyPostfix]
        private static void EquipmentChanged() { InvalidateWear(); }
        [HarmonyPatch(typeof(InventoryItem),"OnDurabilityChange")]
        [HarmonyPrefix]
        private static void BeforeDurabilityChanged(float newValue,float oldValue)
        { if((newValue<=0)!=(oldValue<=0))InvalidateWear(); }
        [HarmonyPatch(typeof(InventoryItem),"OnDurabilityChange")]
        [HarmonyFinalizer]
        private static void AfterDurabilityChanged(float newValue,float oldValue)
        { if((newValue<=0)!=(oldValue<=0))InvalidateWear(); }
        private static float WearReduction(EquipmentManager manager)
        {
            var cache=wearCaches.GetOrCreateValue(manager);
            if(cache.save==SulfurSaveState.active&&cache.revision==wearRevision&&cache.save!=null)return cache.reduction;
            float reduction=0;
            foreach(var pair in manager.EquippedItems)
            {
                State state;var gear=pair.Value;
                if(gear==null||gear.IsBroken||!items.TryGetValue(gear,out state)||state.record.extras==null)continue;
                foreach(var extra in state.record.extras)if(extra.key=="care")reduction+=extra.value;
            }
            cache.save=SulfurSaveState.active;cache.revision=wearRevision;cache.reduction=reduction;
            return reduction;
        }
        internal static bool Eligible(ItemDefinition item)
        {return item!=null&&!(item is WeaponSO)&&item.useType==UseType.Equippable&&Affixes.EligibleSlot(item.slotType);}
        internal static EntityAttributes[] Supported(ItemDefinition item)
        {
            if (item == null || item is WeaponSO || item.useType != UseType.Equippable || item.modifiersOnEquipNew == null) return new EntityAttributes[0];
            if (item.slotType != SlotType.Head && item.slotType != SlotType.Torso && item.slotType != SlotType.Legs && item.slotType != SlotType.Feet && item.slotType != SlotType.Hands && item.slotType != SlotType.PassiveEnhancements) return new EntityAttributes[0];
            return item.modifiersOnEquipNew.Where(m => m != null && Quality.Direction(m.attribute) != 0 && m.value != 0 && !float.IsNaN(m.value) && !float.IsInfinity(m.value) && !m.applyItemDefinitionValue &&
                item.modifiersOnEquipNew.Count(n => n != null && n.attribute == m.attribute) == 1 && m.attribute.GetAsset() != null && !m.attribute.GetAsset().isBooleanAttribute).Select(m => m.attribute).ToArray();
        }
        internal static QualityRecord NewRecord(ItemDefinition item, string original)
        {
            var record=new QualityRecord{version=4,guid=Guid.NewGuid().ToString("N"),itemId=item.id.value,originalIdentifier=original??"",attributes=Supported(item)};
            record.rolls=new float[record.attributes.Length];
            Affixes.Enrich(record,item.slotType,QualitySettings.Rules(),new System.Random(BitConverter.ToInt32(Guid.NewGuid().ToByteArray(),0)));
            EnsureDurability(record,item);
            if(!Quality.Valid(record))throw new InvalidOperationException("Generated invalid equipment record");
            return record;
        }
        private static bool Chinese { get { string value = language == null ? "" : ((string)language.GetValue(null, null) ?? "").ToLowerInvariant(); return value.Contains("chinese") || value.StartsWith("zh"); } }
        internal static void Initialize(ConfigFile config,ManualLogSource logger)
        {
            log = logger;
            Enabled=config.Bind("General","EquipmentEnabled",true,"Apply stored equipment quality, affixes and preservation. Disabling retains records.");
            QualitySettings.Initialize(config);
            var manager = AccessTools.TypeByName("I2.Loc.LocalizationManager");
            if (manager != null) language = AccessTools.Property(manager, "CurrentLanguage");
            try {EnsureFont();}catch(Exception ex){log.LogWarning("Quality font integration: "+ex.Message);}
        }
        [HarmonyPatch(typeof(InventoryItem), "Setup", new Type[] { typeof(Vector2), typeof(ItemDefinition), typeof(ItemGrid), typeof(int), typeof(int), typeof(InventoryData), typeof(bool) })]
        [HarmonyPrefix]
        private static void BeforeSetup(InventoryItem __instance, ref ItemDefinition item, InventoryData attachedData)
        {
            item = CanonicalDefinition(item, attachedData);
            State old;
            if (items.TryGetValue(__instance, out old)) { if (old.clone != null) UnityEngine.Object.Destroy(old.clone); items.Remove(__instance); }
            QualityRecord record;
            if (SulfurSaveState.active == null || item == null) return;
            if (attachedData == null)
            {
                if (!Eligible(item)) return;
                try { record = pickupContext != null && pickupContext.itemId == item.id.value ? pickupContext : NewRecord(item, ""); }
                catch (Exception ex) { log.LogError("New equipment roll skipped: " + ex.Message); return; }
            }
            else if (!Quality.Decode(attachedData.identifier, out record) || record.itemId != item.id.value) return;
            EnsureCoverage(record,item);
            EnsureDurability(record,item);
            var state = new State { record = record };
            items.Add(__instance, state);
            try
            {
                var modifiers = Modifiers(item,record);
                state.clone = UnityEngine.Object.Instantiate(item);
                state.clone.name = item.name;
                state.clone.stackSize = 1;
                state.clone.modifiersOnEquipNew = modifiers;
                item = state.clone;
                log.LogInfo("Restored equipment " + record.itemId + " UUID=" + record.guid + " rolls=" + string.Join(",", record.rolls.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray()));
            }
            catch (Exception ex) { log.LogError("Quality effect not applied; record retained: " + ex); }
        }
        private static ItemDefinition CanonicalDefinition(ItemDefinition fallback, InventoryData data)
        {
            QualityRecord record;
            if (data == null || !Quality.Decode(data.identifier, out record) || data.id.value != record.itemId) return fallback;
            var original = data.id.GetAsset();
            return original != null ? original : fallback;
        }
        // World pickups must not retain the short-lived inventory definition clone.
        [HarmonyPatch(typeof(InteractionManager), "SpawnPickup")]
        [HarmonyPrefix]
        private static void BeforeSpawnPickup(ref ItemDefinition item, InventoryData inventoryData)
        {
            item = CanonicalDefinition(item, inventoryData);
        }
        [HarmonyPatch(typeof(Pickup), "SetupAndSpawn")]
        [HarmonyPrefix]
        private static void BeforePickupSetup(ref ItemDefinition item, InventoryData _inventoryData)
        {
            item = CanonicalDefinition(item, _inventoryData);
        }
        [HarmonyPatch(typeof(Pickup), "SetupAndSpawn")]
        [HarmonyPostfix]
        private static void AfterPickupSetup(Pickup __instance, ItemDefinition item, InventoryData _inventoryData)
        {
            pickupRecords.Remove(__instance);
            if (SulfurSaveState.active == null || item == null) return;
            QualityRecord record;
            try
            {
                if (_inventoryData == null) { if (!Eligible(item)) return; record = NewRecord(item, ""); }
                else if (!Quality.Decode(_inventoryData.identifier, out record) || record.itemId != item.id.value)
                {
                    if(!Eligible(item)||_inventoryData.quantity!=1||!string.IsNullOrEmpty(_inventoryData.identifier))return;
                    record=NewRecord(item,"");_inventoryData.identifier=Quality.Encode(record);
                }
                pickupRecords.Add(__instance, record);
            }
            catch (Exception ex) { log.LogError("World equipment roll skipped: " + ex.Message); }
        }
        [HarmonyPatch(typeof(InteractionManager), "ExecutePickup")]
        [HarmonyPrefix]
        private static void BeforeTake(Pickup pickup, out QualityRecord __state)
        {
            __state = pickupContext;
            QualityRecord record;
            pickupContext = pickup != null && pickupRecords.TryGetValue(pickup, out record) ? record : null;
        }
        [HarmonyPatch(typeof(InteractionManager), "ExecutePickup")]
        [HarmonyFinalizer]
        private static void AfterTake(QualityRecord __state) { pickupContext = __state; }
        [HarmonyPatch(typeof(InventoryItem), "SetupStats")]
        [HarmonyPostfix]
        private static void AfterStats(InventoryItem __instance)
        {
            InvalidateWear();
            State state;
            if (!items.TryGetValue(__instance, out state) || __instance.stats == null) return;
            stats.Remove(__instance.stats); stats.Add(__instance.stats, state.record);
            Quality.ApplyDurability(__instance.stats,state.record,Enabled.Value);
        }
        [HarmonyPatch(typeof(InventoryItem), "OverrideStats")]
        [HarmonyPostfix]
        private static void AfterOverride(InventoryItem __instance) { AfterStats(__instance); }
        [HarmonyPatch(typeof(EquipmentManager), "EquipOnSlot")]
        [HarmonyPostfix]
        private static void AuditEquip(EquipmentManager __instance, InventorySlot inventorySlot, InventoryItem inventoryItem)
        {
            Audit(inventoryItem, inventorySlot, __instance.GetItemAtSlot(inventorySlot) == inventoryItem && !inventoryItem.IsBroken);
        }
        [HarmonyPatch(typeof(EquipmentManager), "UnEquipFromSlot")]
        [HarmonyPrefix]
        private static void BeforeUnequip(EquipmentManager __instance, InventorySlot inventorySlot, out InventoryItem __state) { __state = __instance.GetItemAtSlot(inventorySlot); }
        [HarmonyPatch(typeof(EquipmentManager), "UnEquipFromSlot")]
        [HarmonyPostfix]
        private static void AuditUnequip(InventorySlot inventorySlot, InventoryItem __state) { Audit(__state, inventorySlot, false); }
        private static void Audit(InventoryItem item, InventorySlot slot, bool equipped)
        {
            State state;
            if (item == null || !items.TryGetValue(item, out state)) return;
            try
            {
                uint source = item.itemDefinition.id.AsGlobalId((byte)slot);
                var playerStats = StaticInstance<GameManager>.Instance.PlayerUnit.Stats;
                bool pass = state.clone != null;
                foreach (var group in item.itemDefinition.modifiersOnEquipNew.GroupBy(m=>m.attribute))
                {
                    var stat = playerStats.entityAttributes[(int)group.Key];
                    var own = stat == null ? new PerfectRandom.Sulfur.Core.CharacterStats.StatModifier[0] : stat.statModifiers.Where(v => v.SourceId == source).ToArray();
                    var expected=group.OrderBy(m=>m.modType).ThenBy(m=>m.value).ToArray();var actual=own.OrderBy(m=>m.Type).ThenBy(m=>m.Value).ToArray();
                    pass &= equipped ? actual.Length==expected.Length&&actual.Select((v,i)=>v.Type==expected[i].modType&&Math.Abs(v.Value-expected[i].value)<.00001f).All(v=>v) : own.Length == 0;
                }
                log.LogInfo((pass ? "PASS" : "FAIL") + ": quality " + (equipped ? "equip" : "unequip/broken") + " modifier audit UUID=" + state.record.guid);
            }
            catch (Exception ex) { log.LogWarning("Quality audit unavailable: " + ex.Message); }
        }
        [HarmonyPatch(typeof(ItemStats), "SerializedAttributeData")]
        [HarmonyPostfix]
        private static void SerializeStats(ItemStats __instance, ItemAttributeCollectionData __result)
        {
            QualityRecord record;
            if (__result == null || !stats.TryGetValue(__instance, out record)) return;
            collections.Remove(__result); collections.Add(__result, record);
        }
        internal static void Attach(InventoryData data)
        {
            QualityRecord record;
            if (data != null && data.attributes != null && collections.TryGetValue(data.attributes, out record) && data.id.value == record.itemId) data.identifier = Quality.Encode(record);
        }
        [HarmonyPatch(typeof(InventoryItem), "GetSerialized")]
        [HarmonyPostfix]
        private static void SerializeItem(InventoryItem __instance, InventoryData __result)
        {
            State state;
            if (__result != null && items.TryGetValue(__instance, out state)) __result.identifier = Quality.Encode(state.record);
        }
        [HarmonyPatch(typeof(InventoryItem), "OnDestroy")]
        [HarmonyPostfix]
        private static void DestroyItem(InventoryItem __instance)
        {
            InvalidateWear();
            State state;
            if (items.TryGetValue(__instance, out state) && state.clone != null) UnityEngine.Object.Destroy(state.clone);
            items.Remove(__instance);
        }
        [HarmonyPatch(typeof(Description), "AddPriceInfo")]
        [HarmonyPrefix]
        private static void AddInfo(Description __instance, InventoryItem item, ref int childIndex)
        {
            State state;
            if (item == null || !items.TryGetValue(item, out state)) return;
            if(chineseFallback!=null)
            {
                var source=((ItemDescriptionText)AccessTools.Field(typeof(Description),"flavorText").GetValue(__instance)).textComp.font;
                if(source!=null&&!source.fallbackFontAssetTable.Contains(chineseFallback))source.fallbackFontAssetTable.Add(chineseFallback);
            }
            var r = state.record;
            var attrs = r.version == 1 ? Quality.Attributes(r.itemId) : r.attributes;
            string[] labels = attrs.Select(AttributeLabel).ToArray();
            string grade=r.quality.HasValue?Affixes.GradeLabel(r.quality.Value,Chinese):Quality.Rating(r,Chinese);
            string title = "<b><color=#F5BD58>" + grade + (Chinese ? " · 装备品质" : " · Equipment Quality") + "</color></b>\n<size=75%>" + r.guid.Substring(0, 8) + "</size>";
            var lines = r.rolls.Select((v, i) => labels[i] + " <color=" + (v >= 0 ? "#93D7A0" : "#F09D94") + ">" + (v > 0 ? "+" : "") + v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%</color>");
            string text = title + "\n" + string.Join("\n", lines.ToArray()) + "\n<size=75%>" + (Chinese ? "相对原装备效果浮动, 读档保留" : "Relative to original effects; saved per item") + "</size>";
            if(r.durabilityRoll.HasValue)text+="\n"+(Chinese?"最大耐久：":"Max Durability: ")+(r.durabilityRoll.Value>0?"+":"")+r.durabilityRoll.Value.ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+"%";
            if(!Enabled.Value)text+="\n"+(Chinese?"装备属性效果已暂停":"Equipment stat effects paused");
            if(r.legacyPreserved)text+="\n<size=75%>"+(Chinese?"旧版原属性保留":"Legacy original rolls retained")+"</size>";
            if(r.extras!=null&&r.extras.Count>0)text+="\n<b>"+(Chinese?"额外词条":"Extra Affixes")+"</b>\n"+string.Join("\n",r.extras.Select(ExtraLabel).ToArray());
            if (state.clone == null) text += "\n" + (Chinese ? "效果未应用，请检查日志" : "Effects inactive; check log");
            object[] args = { text, childIndex };
            AccessTools.Method(typeof(Description), "AddDescriptionText").Invoke(__instance, args); childIndex = (int)args[1];
        }
        private static string AttributeLabel(EntityAttributes attribute)
        {
            if(attribute==EntityAttributes.ExtraDamage_LMG)return Chinese?"轻机枪伤害":"LMG Damage";
            string translated = I2.Loc.LocalizationManager.GetTranslation("EntityAttributes/" + attribute + "_itemDescription");
            if (!string.IsNullOrEmpty(translated)) return translated;
            var asset = attribute.GetAsset();
            return asset != null && !string.IsNullOrEmpty(asset.itemDescriptionName) ? asset.itemDescriptionName : attribute.ToString();
        }
        [HarmonyPatch(typeof(Description),"AddAttribute")]
        [HarmonyPrefix]
        private static void LocalizeMachineGun(ref string key)
        {
            if(Chinese&&string.Equals(key,"LMG Damage",StringComparison.OrdinalIgnoreCase))key="轻机枪伤害";
        }
        private static string ExtraLabel(ExtraAffix e)
        {
            return Affixes.Describe(e,Chinese);
        }
        private static void EnsureFont()
        {
            string name=Font.GetOSInstalledFontNames().FirstOrDefault(n=>n.IndexOf("YaHei",StringComparison.OrdinalIgnoreCase)>=0||n.Contains("微软雅黑"));if(name==null)return;
            var font=Font.CreateDynamicFontFromOSFont(name,24);var asset=TMPro.TMP_FontAsset.CreateFontAsset(font);chineseFallback=asset;
            if(asset!=null){asset.name="MoreRPGSulfur_ChineseFallback";UnityEngine.Object.DontDestroyOnLoad(asset);if(TMPro.TMP_Settings.fallbackFontAssets!=null)TMPro.TMP_Settings.fallbackFontAssets.Add(asset);}
        }
        [HarmonyPatch(typeof(InventoryItem),"ModifyDurability")]
        [HarmonyPrefix]
        private static void ReduceWear(InventoryItem __instance,ref float durabilityChange)
        {
            if(!Enabled.Value||durabilityChange>=0||__instance.Owner==null||!__instance.Owner.isPlayer)return;
            var weapon=__instance.itemDefinition as WeaponSO;
            bool firearm=weapon!=null&&!weapon.IsMelee&&!weapon.IsThrowable;
            bool armor=weapon==null&&Eligible(__instance.itemDefinition)&&__instance.itemDefinition.slotType!=SlotType.PassiveEnhancements&&__instance.itemDefinition.HasDurability;
            if(!firearm&&!armor)return;
            var manager=StaticInstance<GameManager>.Instance.EquipmentManager;if(manager==null)return;
            durabilityChange*=Quality.WearFactor(WearReduction(manager),QualitySettings.CareCap.Value);
        }
        [HarmonyPatch(typeof(GameManager), "LoadEquipmentData")]
        [HarmonyPrefix]
        private static void Migrate()
        {
            InvalidateWear();
            try { EquipmentMigration.Ensure(SulfurSaveState.active); }
            catch (Exception ex) { log.LogError("Equipment migration stopped without rerolling: " + ex); }
        }
        internal static bool EnsureDurability(QualityRecord record,ItemDefinition item)
        {
            if(record.durabilityRoll.HasValue||!record.quality.HasValue||item==null||!item.HasDurability||item.slotType==SlotType.PassiveEnhancements)return false;
            Quality.AssignDurability(record,QualitySettings.Rules());return true;
        }
        internal static bool EnsureCoverage(QualityRecord record,ItemDefinition item)
        {return Quality.ExtendAttributes(record,Supported(item),QualitySettings.Rules());}
        private static List<ModifierContainer> Modifiers(ItemDefinition original,QualityRecord record)
        {
            var source=original.modifiersOnEquipNew??new List<ModifierContainer>();
            return Enabled.Value?Affixes.AddExtras(Quality.Transform(source,record),record):new List<ModifierContainer>(source);
        }
        internal static void Refresh()
        {
            InvalidateWear();
            var game=StaticInstance<GameManager>.Instance;
            foreach(var item in Resources.FindObjectsOfTypeAll<InventoryItem>())
            {
                State state;if(item==null||!items.TryGetValue(item,out state)||state.clone==null)continue;
                var original=item.itemDefinition.id.GetAsset();if(original==null)continue;
                bool active=item.equipped&&!item.IsBroken&&item.Owner!=null;
                var unit=active?item.Owner.Stats:null;
                float healthRatio=unit!=null&&unit.GetAttribute(EntityAttributes.Stat_MaxHealth)>0?unit.GetStatus(EntityAttributes.Status_CurrentHealth)/unit.GetAttribute(EntityAttributes.Stat_MaxHealth):0;
                uint source=0;
                if(active&&game!=null)
                {
                    var slots=game.EquipmentManager.EquippedItems.Where(p=>p.Value==item).ToArray();active=slots.Length==1;
                    if(active){source=original.id.AsGlobalId((byte)slots[0].Key);unit.RemoveModifiersFromList(state.clone.modifiersOnEquipNew,source);}
                }
                else active=false;
                state.clone.modifiersOnEquipNew=Modifiers(original,state.record);
                Quality.ApplyDurability(item.stats,state.record,Enabled.Value);
                if(active){unit.AddModifiersFromList(state.clone.modifiersOnEquipNew,source);unit.SetStatus(EntityAttributes.Status_CurrentHealth,Math.Max(0,Math.Min(1,healthRatio))*unit.GetAttribute(EntityAttributes.Stat_MaxHealth));}
            }
        }
    }
    [HarmonyPatch]
    internal static class Constructors
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> Targets() { return typeof(InventoryData).GetConstructors(); }
        [HarmonyPostfix]
        private static void After(InventoryData __instance) { EquipmentQualityTest.Attach(__instance); }
    }
}
