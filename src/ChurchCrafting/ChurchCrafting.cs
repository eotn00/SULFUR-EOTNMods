using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;
using PerfectRandom.Sulfur.Core.UI.Inventory;
using PerfectRandom.Sulfur.Gameplay;
using UnityEngine;

namespace LocalSulfur.ChurchCrafting
{
    [BepInPlugin("local.sulfur.churchstashcrafting", "Church Stash Crafting", "1.0.0")]
    [BepInDependency("com.sulfur.stackingmod")]
    [BepInDependency("com.sulfur.inventorylock")]
    public sealed class ChurchCraftingPlugin : BaseUnityPlugin
    {
        private static ManualLogSource log;
        private static MethodInfo isLocked, slotLocked, canStack, maxFor, refresh, destroy, report;
        private static MethodInfo pageRows;
        private static bool busy, faulted;
        private static int previewFrame = -1;
        private static ServiceStation previewStation;
        private static List<Source> previewSources;
        private static Harmony harmony;
        private static InventoryUI UI { get { return StaticInstance<UIManager>.Instance.InventoryUI; } }
        private static GameManager Game { get { return StaticInstance<GameManager>.Instance; } }

        private void Start()
        {
            log = Logger;
            try
            {
                Type locking = AccessTools.TypeByName("InventoryLockMod.LockRuntime");
                Type stacking = AccessTools.TypeByName("StackingMod.StackRuntime");
                isLocked = Require(locking, "IsItemLocked");
                slotLocked = Require(locking, "IsSlotLocked");
                canStack = Require(stacking, "CanStack");
                maxFor = Require(stacking, "MaxFor");
                refresh = Require(stacking, "RefreshBadge");
                destroy = Require(typeof(InventoryItem), "DestroyFromInventory");
                report = Require(typeof(ItemGrid), "ReportContentsChanged");
                // Both size plugins share type names; resolve paging from the backpack assembly explicitly.
                foreach (var plugin in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
                    if (plugin.Metadata.GUID == "AetharNet.Mods.BigBackpack")
                        pageRows = Require(plugin.Instance.GetType().Assembly.GetType("AetharNet.Mods.bigbk.BackpackPaging"), "GetVisibleRows");
                harmony = new Harmony("local.sulfur.churchstashcrafting");
                harmony.Patch(Require(typeof(RecipeData), "CanCraft"), postfix: new HarmonyMethod(typeof(ChurchCraftingPlugin), "CanCraftPostfix"));
                harmony.Patch(AccessTools.PropertyGetter(typeof(RecipeListItemVariant), "HasIngredients"),
                    prefix: new HarmonyMethod(typeof(ChurchCraftingPlugin), "HasIngredientsPrefix"));
                harmony.Patch(Require(typeof(RecipeListItemVariant), "CookItem"),
                    prefix: new HarmonyMethod(typeof(ChurchCraftingPlugin), "CookPrefix") { priority = Priority.First });
                Logger.LogInfo("Church stash crafting ready; stack counts, logical backpack pages and slot locks supported.");
            }
            catch (Exception e)
            {
                faulted = true;
                if (harmony != null) harmony.UnpatchSelf();
                Logger.LogError("Church stash crafting disabled: " + e);
            }
        }

        private static MethodInfo Require(Type type, string name)
        {
            MethodInfo method = type == null ? null : AccessTools.Method(type, name);
            if (method == null) throw new MissingMethodException(type == null ? "Missing mod type" : type.FullName, name);
            return method;
        }
        private static bool AtChurch()
        {
            return Game != null && Game.currentEnvironment != null && Game.currentEnvironment.id == WorldEnvironmentIds.ChurchHub;
        }
        private static bool Active()
        {
            return !faulted && AtChurch() && UI != null && UI.CurrentServiceStation is CraftingStation;
        }
        private static bool Locked(InventoryItem item) { return (bool)isLocked.Invoke(null, new object[] { item }); }
        private static bool SlotLocked(ItemGrid grid, int x, int y) { return (bool)slotLocked.Invoke(null, new object[] { grid, x, y }); }
        private static int StackMax(ItemDefinition item)
        {
            return (bool)canStack.Invoke(null, new object[] { item }) ? Math.Max(1, (int)maxFor.Invoke(null, new object[] { item })) : 1;
        }
        private static void Refresh(InventoryItem item) { refresh.Invoke(null, new object[] { item }); }
        private static void Report(ItemGrid grid) { report.Invoke(grid, null); }
        private static void Alert(string message) { StaticInstance<UIManager>.Instance.ShowSmallAlert(message, 2.5f); }

        private sealed class Source
        {
            internal InventoryItem Item;
            internal ItemGrid Grid;
            internal string Stash;
        }
        private sealed class InputState
        {
            internal Source Source;
            internal int Quantity;
            internal Vector2Int Position;
            internal bool Active, Detached;
        }
        private static List<Source> Sources()
        {
            var result = new List<Source>();
            var seen = new HashSet<InventoryItem>();
            AddSources(result, seen, UI.PlayerBackpackGrid, null);
            AddSources(result, seen, UI.CurrentServiceStation.Grid, null);
            var keys = new List<string>(UI.Stashes.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string identifier in keys)
            {
                var station = UI.Stashes[identifier] as PlayerStash;
                if (station == null || !station.gameObject.scene.isLoaded) continue;
                AddSources(result, seen, UI.GetStashItemGrid(identifier), identifier);
            }
            return result;
        }
        private static void AddSources(List<Source> list, HashSet<InventoryItem> seen, ItemGrid grid, string stash)
        {
            if (grid == null) return;
            foreach (var item in grid.AllItemsInGridOnly())
                if (item != null && item.itemDefinition != null && item.paperdollSlot == null &&
                    !item.isBeingDragged && !Locked(item) && seen.Add(item))
                    list.Add(new Source { Item = item, Grid = grid, Stash = stash });
        }
        private static bool PlanIngredients(RecipeData recipe, List<Source> sources, out List<Spend> spend)
        {
            spend = new List<Spend>();
            if (!recipe.canBeCrafted || recipe.itemsNeeded == null || recipe.quantityCreated <= 0) return false;
            var required = new Dictionary<int, int>();
            foreach (var ingredient in recipe.itemsNeeded)
            {
                int id = ingredient.item.value;
                if (id <= 0 || ingredient.quantity <= 0) return false;
                int previous;
                required.TryGetValue(id, out previous);
                required[id] = checked(previous + ingredient.quantity);
            }
            var materials = new List<Material>();
            for (int i = 0; i < sources.Count; i++)
                materials.Add(new Material { Token = i, Id = sources[i].Item.itemDefinition.id.value, Count = Math.Max(1, sources[i].Item.quantity) });
            return CraftPlan.Ingredients(materials, required, out spend);
        }
        private static bool CanUse(RecipeData recipe)
        {
            if (busy || faulted) return false;
            if (previewFrame != Time.frameCount || previewStation != UI.CurrentServiceStation || previewSources == null)
            {
                previewSources = Sources();
                previewFrame = Time.frameCount;
                previewStation = UI.CurrentServiceStation;
            }
            List<Spend> spend;
            return PlanIngredients(recipe, previewSources, out spend);
        }
        private static void CanCraftPostfix(RecipeData __instance, bool fromBagSpace, ref bool __result)
        {
            if (!Active() || !fromBagSpace) return;
            try { __result = CanUse(__instance); }
            catch (Exception e) { __result = false; log.LogError(e); }
        }
        private static bool HasIngredientsPrefix(RecipeListItemVariant __instance, ref bool __result)
        {
            if (!Active()) return true;
            try { __result = CanUse(__instance.CraftingRecipe); }
            catch (Exception e) { __result = false; log.LogError(e); }
            return false;
        }
        private static bool CookPrefix(RecipeListItemVariant __instance)
        {
            if (!AtChurch()) return true;
            if (faulted || busy) return false;
            if (!(UI.CurrentServiceStation is CraftingStation)) return true;
            busy = true;
            try { Craft(__instance); }
            catch (Exception e) { log.LogError(e); Alert("\u5236\u4f5c\u672a\u5b8c\u6210\uff0c\u8bf7\u67e5\u770b Mod \u65e5\u5fd7"); }
            finally { busy = false; previewSources = null; }
            if (!faulted)
            {
                try { ((CraftingStation)UI.CurrentServiceStation).UpdateVariantsCookableStatus(true); __instance.UpdateCanCook(); }
                catch (Exception e) { log.LogError(e); }
            }
            return false;
        }

        private static bool PlanOutputs(ItemDefinition product, int quantity, List<Source> sources,
            List<Spend> spend, out List<Destination> output, out List<InventoryItem> bagItems)
        {
            ItemGrid bag = UI.PlayerBackpackGrid;
            bagItems = bag.AllItemsInGridOnly();
            var spent = new Dictionary<InventoryItem, int>();
            foreach (var take in spend) spent[sources[take.Token].Item] = take.Count;
            var occupied = new bool[bag.gridSize.x, bag.gridSize.y];
            for (int x = 0; x < bag.gridSize.x; x++)
                for (int y = 0; y < bag.gridSize.y; y++)
                {
                    GridSlot slot = bag.GetSlotAt(x, y);
                    var item = slot == null ? null : slot.ItemInSlot;
                    int used;
                    bool removed = item != null && spent.TryGetValue(item, out used) && used == Math.Max(1, item.quantity);
                    occupied[x, y] = slot == null || SlotLocked(bag, x, y) || (item != null && !removed);
                }
            int maximum = StackMax(product);
            var capacity = new List<Material>();
            for (int i = 0; i < bagItems.Count && maximum > 1; i++)
            {
                var item = bagItems[i];
                if (item.itemDefinition.id != product.id || item.isBeingDragged || Locked(item)) continue;
                int used;
                spent.TryGetValue(item, out used);
                int remaining = Math.Max(1, item.quantity) - used;
                if (remaining > 0 && remaining < maximum)
                    capacity.Add(new Material { Token = i, Count = maximum - remaining });
            }
            int rows = pageRows == null ? bag.gridSize.y : (int)pageRows.Invoke(null, null);
            return CraftPlan.Outputs(occupied, rows, product.inventorySize.x, product.inventorySize.y,
                maximum, quantity, capacity, out output);
        }

        private static void Craft(RecipeListItemVariant variant)
        {
            RecipeData recipe = variant.CraftingRecipe;
            var sources = Sources();
            List<Spend> spend;
            if (!PlanIngredients(recipe, sources, out spend))
            { Alert("\u6750\u6599\u4e0d\u8db3\uff08\u9501\u5b9a\u7269\u54c1\u4e0d\u53c2\u4e0e\u5236\u4f5c\uff09"); return; }
            ItemDefinition product = recipe.createsItem.GetAsset();
            if (product == null || product.automaticConsume) throw new InvalidOperationException("Unsupported crafting product");
            List<Destination> output;
            List<InventoryItem> bagItems;
            if (!PlanOutputs(product, recipe.quantityCreated, sources, spend, out output, out bagItems))
            { Alert("\u80cc\u5305\u7a7a\u95f4\u4e0d\u8db3\uff0c\u672a\u6d88\u8017\u6750\u6599"); return; }
            var inputs = new List<InputState>();
            var changedStacks = new Dictionary<InventoryItem, int>();
            var created = new List<InventoryItem>();
            var changedGrids = new HashSet<ItemGrid>();
            var changedStashes = new Dictionary<string, ItemGrid>();
            ItemGrid bag = UI.PlayerBackpackGrid;
            bool committed = false;
            try
            {
                // Keep fully spent objects alive and detached until every output has been placed.
                foreach (var take in spend)
                {
                    Source source = sources[take.Token];
                    var state = new InputState { Source = source, Quantity = source.Item.quantity,
                        Position = source.Item.gridPosition, Active = source.Item.gameObject.activeSelf };
                    inputs.Add(state);
                    if (Locked(source.Item)) throw new InvalidOperationException("Ingredient became locked");
                    int remaining = Math.Max(1, source.Item.quantity) - take.Count;
                    if (remaining == 0)
                    {
                        state.Detached = true;
                        source.Item.UnregisterAtItemGrid();
                        source.Item.gameObject.SetActive(false);
                    }
                    else { source.Item.quantity = remaining; Refresh(source.Item); }
                    changedGrids.Add(source.Grid);
                    if (source.Stash != null) changedStashes[source.Stash] = source.Grid;
                }
                foreach (var place in output)
                {
                    if (place.Token >= 0)
                    {
                        var target = bagItems[place.Token];
                        changedStacks[target] = target.quantity;
                        target.quantity = checked(target.quantity + place.Count);
                        Refresh(target);
                        continue;
                    }
                    if (bag.GetSlotAt(place.X, place.Y).ItemInSlot != null) throw new InvalidOperationException("Output slot occupied");
                    try
                    {
                        bag.AddItemToPosition(product, place.X, place.Y, null, place.Rotated, true);
                    }
                    finally
                    {
                        var spawned = bag.GetSlotAt(place.X, place.Y).ItemInSlot;
                        if (spawned != null && !bagItems.Contains(spawned) && !created.Contains(spawned)) created.Add(spawned);
                    }
                    var item = bag.GetSlotAt(place.X, place.Y).ItemInSlot;
                    if (item == null || bagItems.Contains(item) || item.itemDefinition.id != product.id)
                        throw new InvalidOperationException("Output placement failed");
                    item.quantity = place.Count;
                    Refresh(item);
                }
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    try
                    {
                        foreach (var item in created) if (item != null) destroy.Invoke(item, null);
                        foreach (var entry in changedStacks) { entry.Key.quantity = entry.Value; Refresh(entry.Key); }
                        foreach (var state in inputs)
                        {
                            state.Source.Item.quantity = state.Quantity;
                            if (state.Detached && !state.Source.Grid.RegisterItemAt(state.Source.Item, state.Position.x, state.Position.y))
                                throw new InvalidOperationException("Could not restore ingredient position");
                            state.Source.Item.gameObject.SetActive(state.Active);
                            Refresh(state.Source.Item);
                        }
                        log.LogWarning("Craft cancelled; ingredient objects and stack counts restored.");
                    }
                    catch (Exception e) { faulted = true; log.LogError("Craft rollback failed; crafting disabled: " + e); }
                }
            }
            foreach (var state in inputs) if (state.Detached) UnityEngine.Object.Destroy(state.Source.Item.gameObject);
            changedGrids.Add(bag);
            foreach (var grid in changedGrids) Report(grid);
            // Update the saved list first so Big Stash's empty-grid guard cannot resurrect spent ingredients.
            foreach (var stash in changedStashes)
            {
                var data = new SerializableList<InventoryData>();
                data.list = stash.Value.GetSerializedContents();
                string key = "Stash" + (stash.Key.Length == 0 ? "" : "_" + stash.Key);
                SulfurSaveState.active.STASH[key] = data;
                Game.SaveStashData(stash.Key);
            }
            Game.SaveInventoryData();
            var station = (CraftingStation)UI.CurrentServiceStation;
            station.TrackItemCrafted(product, recipe, recipe.quantityCreated);
            station.OnServicePerformedEffects();
            station.PlayCraftingSound();
            log.LogInfo("Crafted " + product.name + " x" + recipe.quantityCreated + "; used " + spend.Count + " stacks across " + changedStashes.Count + " stashes.");
        }
    }
}
