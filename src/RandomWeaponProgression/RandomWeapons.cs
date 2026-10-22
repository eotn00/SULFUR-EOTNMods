using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;
using UnityEngine;
using SulfurCustomMods;

namespace SulfurRandomWeapons
{
    [BepInPlugin(Id, Name, "0.3.3")]
    [BepInDependency("com.sulfur.optionsscreen.settings", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("AetharNet.Mods.EWeapon", BepInDependency.DependencyFlags.SoftDependency)]
    [HarmonyPatch]
    public sealed class RandomWeapons : BaseUnityPlugin
    {
        public const string Id = "local.sulfur.randomweaponprogression";
        public const string Name = "更RPG的火湖 / sulfur-RPG";
        internal static RandomWeapons Instance;
        internal static ConfigEntry<bool> Enabled;
        private static ConfigEntry<float> initialRange, upgradeMin, upgradeMax;
        private Harmony harmony;
        private static readonly ConditionalWeakTable<InventoryItem, WeaponRecord> pending = new ConditionalWeakTable<InventoryItem, WeaponRecord>();
        private static readonly ConditionalWeakTable<Pickup, WeaponRecord> pickups = new ConditionalWeakTable<Pickup, WeaponRecord>();
        [ThreadStatic] private static WeaponRecord pickupContext;
        [ThreadStatic] private static bool selfTesting;

        public static bool Eligible(ItemDefinition item)
        {
            var weapon = item as WeaponSO;
            return weapon != null && !weapon.IsMelee && !weapon.IsThrowable && item.HasRank;
        }
        public static RollRules Rules()
        {
            float low = Math.Max(0.1f, (float)Math.Round(upgradeMin.Value, 1)), high = Math.Max(low, (float)Math.Round(upgradeMax.Value, 1));
            return new RollRules { initialRange = initialRange.Value, upgradeMin = low, upgradeMax = high };
        }
        internal static void LogInfo(string text) { if (Instance != null) Instance.Logger.LogInfo(text); }
        internal static void LogWarning(string text) { if (Instance != null) Instance.Logger.LogWarning(text); }

        private void Awake()
        {
            Instance = this;
            WeaponLanguage.Initialize();
            Enabled = Config.Bind("General", "Enabled", true, "Apply stored random firearm stats. Turning this off retains IDs and history.");
            initialRange = Config.Bind("NewWeapons", "InitialRangePercent", 15f, new ConfigDescription("Initial independent +/- range for all six stats. Applies only to newly created records.", new AcceptableValueRange<float>(0, 50)));
            upgradeMin = Config.Bind("NewWeapons", "UpgradeMinPercent", 2f, new ConfigDescription("Minimum per-rank gain, captured when each weapon record is born.", new AcceptableValueRange<float>(0.1f, 20)));
            upgradeMax = Config.Bind("NewWeapons", "UpgradeMaxPercent", 6f, new ConfigDescription("Maximum per-rank gain. Actual upper bound is at least the minimum.", new AcceptableValueRange<float>(0.1f, 20)));
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("local.sulfur.weaponrankbonuses") || BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("local.sulfur.singleweaponsavetest") || BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("local.sulfur.equipmentqualitytest"))
            {
                Logger.LogError("An old standalone weapon/equipment plugin is active. Remove the old DLL before using the unified RPG plugin; activation stopped to prevent duplicate modifiers.");
                return;
            }
            harmony = new Harmony(Id);
            SulfurEquipmentQualityTest.EquipmentQualityTest.Initialize(Config,Logger);
            try
            {
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                SelfTest();
            }
            catch (Exception ex) { harmony.UnpatchSelf(); Logger.LogError("Random Weapon Progression activation failed: " + ex); return; }
            try
            {
                WeaponLanguage.Register(Enabled, Refresh, initialRange, upgradeMin, upgradeMax);
                SulfurEquipmentQualityTest.QualitySettings.Register(WeaponLanguage.Category);
            }
            catch (Exception ex) { Logger.LogWarning("Options UI registration failed: " + ex.Message); }
            Logger.LogInfo(Name + " 0.3.3 loaded. Unreal equipment quality: independent 1/10000 roll; existing records retained.");
        }
        private static void Refresh()
        {
            foreach (var item in Resources.FindObjectsOfTypeAll<InventoryItem>())
            {
                WeaponRecord record;
                if (item == null || !WeaponState.TryGet(item.stats, out record)) continue;
                Progression.Apply(item.stats, record, Enabled.Value);
                if (item.equipped && item.Owner != null) item.SyncWithInstancedVersion();
            }
        }
        private static void UpdateItem(InventoryItem item)
        {
            WeaponRecord record;
            if (item == null || !WeaponState.TryGet(item.stats, out record)) return;
            bool grew = Progression.Advance(record, item.GetRankLevel());
            Progression.Apply(item.stats, record, Enabled.Value);
            if (grew) LogInfo("Recorded ranks through " + record.upgrades.Count + " for " + record.guid + ".");
        }
        private void SelfTest()
        {
            selfTesting = true;
            try
            {
                var record = Progression.Create(919, Rules(), "self-test", "");
                Progression.Advance(record, 5);
                string original = Progression.Encode(record);
                var stats = new ItemStats(); stats.SetAttributeBaseValue(ItemAttributes.Damage, 100); stats.SetAttributeBaseValue(ItemAttributes.Experience, 50);
                WeaponState.Register(stats, record); Progression.Apply(stats, record, true);
                var data = new InventoryData(new ItemId(919), 0, 0, 1, 1, CaliberTypes._9mm, stats.SerializedAttributeData(), new ItemId[0], new ItemId[0], 0, 4, 2);
                if (data.identifier != original) throw new InvalidOperationException("Constructor metadata hook failed.");
                for (int i = 0; i < 3; i++)
                {
                    data = JsonUtility.FromJson<InventoryData>(JsonUtility.ToJson(data));
                    var loaded = new ItemStats(); loaded.SetAttributeBaseValue(ItemAttributes.Damage, 100); loaded.LoadAttributesFromData(data.attributes);
                    if (!WeaponState.Import(loaded, data)) throw new InvalidOperationException("Unity metadata roundtrip failed.");
                    WeaponRecord restored; WeaponState.TryGet(loaded, out restored);
                    Progression.Advance(restored, 5); Progression.Apply(loaded, restored, true); Progression.Apply(loaded, restored, true);
                    if (Progression.Encode(restored) != original || Math.Abs(loaded.GetAttribute(ItemAttributes.Damage) - 100 * Progression.Factor(record, 0)) > 0.001f)
                        throw new InvalidOperationException("Random record rerolled or stacked.");
                    data = new InventoryData(new ItemId(919), 0, 0, 1, 1, CaliberTypes._9mm, loaded.SerializedAttributeData(), new ItemId[0], new ItemId[0], 0, 4, 2);
                }
                LogInfo("PASS: 3 Unity JSON roundtrips preserved UUID, initial rolls, level history and exact random state.");
            }
            finally { selfTesting = false; }
        }

        [HarmonyPatch(typeof(GameManager), "LoadEquipmentData")]
        [HarmonyPrefix]
        private static void MigrateSave()
        {
            try { Migration.Ensure(SulfurSaveState.active); }
            catch (Exception ex) { LogWarning("Legacy migration stopped without rerolling saved records: " + ex); }
        }

        [HarmonyPatch(typeof(InventoryItem), "Setup", new Type[] { typeof(Vector2), typeof(ItemDefinition), typeof(ItemGrid), typeof(int), typeof(int), typeof(InventoryData), typeof(bool) })]
        [HarmonyPrefix]
        private static void BeforeRealSetup(InventoryItem __instance, ItemDefinition item, InventoryData attachedData)
        {
            pending.Remove(__instance);
            if (!Eligible(item)) return;
            WeaponRecord record;
            if (attachedData == null)
            {
                record = pickupContext != null && pickupContext.weaponId == item.id.value ? pickupContext : Progression.Create(item.id.value, Rules(), "generated", "");
                pending.Add(__instance, record);
            }
            else if (Progression.TryDecode(attachedData.identifier, out record) && record.weaponId == item.id.value) pending.Add(__instance, record);
            else LogWarning("Saved firearm lacks a valid record: id=" + item.id.value + ". No random ID or roll was generated during load.");
        }

        [HarmonyPatch(typeof(InventoryItem), "SetupStats")]
        [HarmonyPostfix]
        private static void AfterStats(InventoryItem __instance)
        {
            WeaponRecord record;
            if (!pending.TryGetValue(__instance, out record)) return;
            WeaponState.Register(__instance.stats, record);
            UpdateItem(__instance);
        }

        [HarmonyPatch(typeof(InventoryItem), "Setup", new Type[] { typeof(Vector2), typeof(ItemDefinition), typeof(ItemGrid), typeof(int), typeof(int), typeof(InventoryData), typeof(bool) })]
        [HarmonyPostfix]
        private static void AfterRealSetup(InventoryItem __instance)
        {
            UpdateItem(__instance); pending.Remove(__instance);
        }

        [HarmonyPatch(typeof(InventoryItem), "OnRankUp")]
        [HarmonyPrefix]
        private static void OnRankUp(InventoryItem __instance)
        {
            UpdateItem(__instance);
            if (__instance.equipped && __instance.Owner != null) __instance.SyncWithInstancedVersion();
        }

        [HarmonyPatch(typeof(ItemStats), "SerializedAttributeData")]
        [HarmonyPostfix]
        private static void SerializeStats(ItemStats __instance, ItemAttributeCollectionData __result) { WeaponState.Attach(__instance, __result); }

        [HarmonyPatch(typeof(InventoryItem), "GetSerialized")]
        [HarmonyPostfix]
        private static void SerializeItem(InventoryItem __instance, InventoryData __result) { WeaponState.Export(__instance.stats, __result); }

        [HarmonyPatch(typeof(InventoryItem), "OverrideStats")]
        [HarmonyPrefix]
        private static void BeforeOverride(InventoryItem __instance, out WeaponRecord __state) { WeaponState.TryGet(__instance.stats, out __state); }
        [HarmonyPatch(typeof(InventoryItem), "OverrideStats")]
        [HarmonyPostfix]
        private static void AfterOverride(InventoryItem __instance, WeaponRecord __state)
        {
            if (__state != null && __instance.stats != null) WeaponState.Register(__instance.stats, __state);
            UpdateItem(__instance);
        }

        [HarmonyPatch(typeof(Pickup), "SetupAndSpawn")]
        [HarmonyPostfix]
        private static void OnPickupSpawn(Pickup __instance, ItemDefinition item, InventoryData _inventoryData)
        {
            pickups.Remove(__instance);
            if (!Eligible(item)) return;
            WeaponRecord record;
            if (_inventoryData == null) record = Progression.Create(item.id.value, Rules(), "world-generated", "");
            else if (!Progression.TryDecode(_inventoryData.identifier, out record) || record.weaponId != item.id.value) return;
            pickups.Add(__instance, record);
        }

        [HarmonyPatch(typeof(InteractionManager), "ExecutePickup")]
        [HarmonyPrefix]
        private static void BeforePickup(Pickup pickup, out WeaponRecord __state)
        {
            __state = pickupContext;
            WeaponRecord record;
            pickupContext = pickup != null && pickups.TryGetValue(pickup, out record) ? record : null;
        }
        [HarmonyPatch(typeof(InteractionManager), "ExecutePickup")]
        [HarmonyFinalizer]
        private static void AfterPickup(WeaponRecord __state) { pickupContext = __state; }

        [HarmonyPatch(typeof(FileAccess_PC), "WriteToDisk", new Type[] { typeof(string), typeof(SulfurSaveState), typeof(Action<SaveResults>) })]
        [HarmonyPostfix]
        private static void ObserveSave(string filePath)
        {
            if (selfTesting) return;
            try
            {
                var markers = Migration.DiskMarkers(filePath);
                int count = markers.Count(m => m != null && m.StartsWith(Progression.Prefix, StringComparison.Ordinal));
                string status = "UTC=" + DateTime.UtcNow.ToString("o") + "\nFile=" + Path.GetFileName(filePath) + "\nPersisted random weapon records=" + count + "\n";
                File.WriteAllText(Path.Combine(Paths.ConfigPath, "RandomWeaponProgression.status.txt"), status);
            }
            catch (Exception ex) { LogWarning("Save audit unavailable: " + ex.Message); }
        }
        private void OnDestroy() { if (harmony != null) harmony.UnpatchSelf(); }
    }

    [HarmonyPatch]
    internal static class InventoryConstructors
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> Targets() { return typeof(InventoryData).GetConstructors(); }
        [HarmonyPostfix]
        private static void AfterConstruct(InventoryData __instance) { WeaponState.ExportConstructor(__instance); }
    }
}
