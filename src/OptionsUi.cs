using System;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace SulfurCustomMods
{
    internal sealed class OptionsUi
    {
        private readonly Type registry;
        private readonly object category;

        internal OptionsUi(string id, string label)
        {
            registry = AccessTools.TypeByName("SulfurOptionsScreenSettings.ModOptionsRegistry");
            if (registry != null)
                category = registry.GetMethod("RegisterCategory").Invoke(null, new object[] { id, label, 9050 });
        }

        private void Invoke(string name, params object[] args)
        {
            if (registry == null) return;
            MethodInfo method = registry.GetMethods().Single(m => m.Name == name && m.GetParameters().Length == args.Length);
            method.Invoke(null, args);
        }

        internal void Toggle(string id, string label, ConfigEntry<bool> entry, Action changed, string tooltip)
        {
            Invoke("AddBool", category, id, label, entry.Value, new Action<bool>(value =>
            {
                entry.Value = value;
                if (changed != null) changed();
            }), 10, tooltip);
        }

        internal void Slider(string id, string label, ConfigEntry<float> entry, Action changed, int order)
        {
            Invoke("AddSlider", category, id, label, entry.Value, 0f, 20f, 0.5f, true, new Action<float>(value =>
            {
                entry.Value = value;
                if (changed != null) changed();
            }), order, "Percent per weapon rank; applied to the current rank without repeated stacking.");
        }
    }
}
