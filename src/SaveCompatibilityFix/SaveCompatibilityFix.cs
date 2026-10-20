using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using PerfectRandom.Sulfur.Core.Items;
using UnityEngine;

[BepInPlugin("local.sulfur.savecompatibility", "Save Compatibility Fix", "1.0.0")]
public sealed class SaveCompatibilityFix : BaseUnityPlugin
{
    [ThreadStatic] private static int restoring;
    private static Type guard;
    private static string directory;

    private void Start()
    {
        try
        {
            var harmony = new Harmony("local.sulfur.savecompatibility");
            var runtime = AccessTools.TypeByName("InventoryLockMod.LockRuntime");
            if (runtime == null) throw new InvalidOperationException("Lock runtime not found");
            harmony.Patch(AccessTools.Method(typeof(ItemGrid), "AddItem", new[] { typeof(InventoryData) }),
                prefix: new HarmonyMethod(typeof(SaveCompatibilityFix), "BeginRestore"),
                finalizer: new HarmonyMethod(typeof(SaveCompatibilityFix), "EndRestore"));
            foreach (string name in new[] { "IsItemLocked", "HasLockedCellBlocking" })
                harmony.Patch(AccessTools.Method(runtime, name), prefix: new HarmonyMethod(typeof(SaveCompatibilityFix), "AllowRestore"));
            guard = AccessTools.TypeByName("SulfurOptionsScreenSettings.ModSaveLoadGuard");
            if (guard == null) throw new InvalidOperationException("Save guard not found");
            directory = Path.Combine(Application.persistentDataPath, "BepInExPluginBaselines");
            Directory.CreateDirectory(directory);
            // A one-time migration for existing save slots, explicitly requested by the player.
            string marker = Path.Combine(directory, "initial-migration-complete.txt");
            if (!File.Exists(marker))
            {
                var records = CurrentRecords();
                foreach (string path in Directory.GetFiles(Application.persistentDataPath, "Sulfur_SaveSlot_*.sss"))
                {
                    string slot = Path.GetFileNameWithoutExtension(path).Substring("Sulfur_SaveSlot_".Length);
                    int number;
                    if (int.TryParse(slot, out number))
                    {
                        string target = Path.Combine(directory, "Profile_" + number + ".txt");
                        if (!File.Exists(target)) AtomicSave(target, records);
                    }
                }
                File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
            }
            harmony.Patch(AccessTools.Method(guard, "LoadSavedPlugins"), prefix: new HarmonyMethod(typeof(SaveCompatibilityFix), "LoadBaseline"));
            harmony.Patch(AccessTools.Method(guard, "SaveCurrentPlugins"), prefix: new HarmonyMethod(typeof(SaveCompatibilityFix), "SaveBaseline"));
            Logger.LogInfo("Save restoration guards and persistent per-slot plugin baselines installed.");
        }
        catch (Exception e) { Logger.LogError(e); }
    }

    private static void BeginRestore() { restoring++; }
    private static Exception EndRestore(Exception __exception) { restoring = Math.Max(0, restoring - 1); return __exception; }
    private static bool AllowRestore(ref bool __result)
    {
        if (restoring == 0) return true;
        __result = false;
        return false;
    }
    private static List<string> Records(IEnumerable records)
    {
        var result = new List<string>();
        foreach (object record in records)
            result.Add((string)AccessTools.Method(record.GetType(), "ToStorageString").Invoke(record, null));
        return result;
    }
    private static List<string> CurrentRecords()
    {
        return Records((IEnumerable)AccessTools.Method(guard, "GetCurrentPlugins").Invoke(null, null));
    }
    private static string BaselinePath()
    {
        string slot = (string)AccessTools.Field(guard, "activeSaveIdentifier").GetValue(null);
        if (string.IsNullOrEmpty(slot)) slot = (string)AccessTools.Method(guard, "GetActiveSaveIdentifierFromState").Invoke(null, null);
        if (string.IsNullOrEmpty(slot)) throw new InvalidOperationException("Cannot store a plugin baseline without a save slot");
        if (slot.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidOperationException("Invalid save slot");
        return Path.Combine(directory, slot + ".txt");
    }
    private static bool LoadBaseline(ref List<string> __result)
    {
        string path = BaselinePath();
        if (!File.Exists(path)) return true;
        __result = new List<string>(File.ReadAllLines(path));
        return false;
    }
    private static bool SaveBaseline(object[] __args)
    {
        AtomicSave(BaselinePath(), Records((IEnumerable)__args[0]));
        return false;
    }
    private static void AtomicSave(string path, List<string> records)
    {
        string temporary = path + ".tmp";
        File.WriteAllLines(temporary, records.ToArray());
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
        else File.Move(temporary, path);
    }
}
