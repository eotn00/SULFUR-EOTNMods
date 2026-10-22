using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace SulfurRandomWeapons
{
    internal static class WeaponLanguage
    {
        private static Type registry;
        private static object category;
        internal static object Category { get { return category; } }
        internal static void Initialize()
        {
            var manager = AccessTools.TypeByName("I2.Loc.LocalizationManager");
            var language = manager == null ? null : AccessTools.Property(manager, "CurrentLanguage");
            WeaponText.Language = delegate { return language == null ? "English" : (string)language.GetValue(null, null); };
        }
        private static object Call(string name, params object[] args)
        {
            return registry.GetMethods().Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(null, args);
        }
        internal static void Register(ConfigEntry<bool> enabled, Action refresh, ConfigEntry<float> initial, ConfigEntry<float> min, ConfigEntry<float> max)
        {
            registry = AccessTools.TypeByName("SulfurOptionsScreenSettings.ModOptionsRegistry");
            if (registry == null) return;
            // Use a private subdirectory; never replace other mods' translation files.
            string folder = Path.Combine(Paths.ConfigPath, "OptionsScreenSettings", "Localization", "MoreRPGSulfur");
            Directory.CreateDirectory(folder);
            string[] names = { "zh", "en" };
            for (int i = 0; i < names.Length; i++)
                File.WriteAllLines(Path.Combine(folder, names[i] + ".lang"), WeaponText.Settings.Select(pair => WeaponText.Key(pair.Key) + "=" + pair.Value[i]).ToArray(), Encoding.UTF8);
            var localization = AccessTools.TypeByName("SulfurOptionsScreenSettings.ModLocalization");
            if (localization != null) localization.GetMethod("Reload").Invoke(null, null);
            category = Call("RegisterCategoryLoc", RandomWeapons.Id, WeaponText.Key("Category"), "sulfur-RPG", 9050);
            Call("AddInfoLoc",category,"RandomWeapons_Section",WeaponText.Key("Section"),0,null,"Firearm Stats",null);
            Call("AddBoolLoc", category, "RandomWeapons_Enabled", WeaponText.Key("Enabled"), enabled.Value,
                new Action<bool>(value => { enabled.Value = value; refresh(); }), 10, WeaponText.Key("EnabledTip"), WeaponText.Settings["Enabled"][1], WeaponText.Settings["EnabledTip"][1]);
            Slider("InitialRange", initial, 20);
            Slider("UpgradeMin", min, 30);
            Slider("UpgradeMax", max, 40);
        }
        private static void Slider(string name, ConfigEntry<float> entry, int order)
        {
            Call("AddSliderLoc", category, "RandomWeapons_" + name, WeaponText.Key(name), entry.Value, 0f, 20f, 0.5f, true,
                new Action<float>(value => { entry.Value = value; }), order, WeaponText.Key("NewWeaponTip"), WeaponText.Settings[name][1], WeaponText.Settings["NewWeaponTip"][1]);
        }
    }
}
