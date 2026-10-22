using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using Description=PerfectRandom.Sulfur.Core.UI.ItemDescription.ItemDescription;

namespace LocalSulfur.RecipeBrowser
{
    [BepInPlugin("local.sulfur.recipebrowser","what's this / 我食纪事","0.2.2")]
    [HarmonyPatch]
    public sealed class RecipeBrowserPlugin:BaseUnityPlugin
    {
        internal static RecipeBrowserPlugin Instance;
        private new ConfigEntry<bool> enabled;
        private ConfigEntry<bool> showAllRecipes;
        private bool indexedAllRecipes;
        private ConfigEntry<Key> key;
        private Harmony harmony;
        private InventoryItem hovered;
        private RecipeIndex index;
        private RecipeDatabase database;
        private SulfurSaveState save;
        private HashSet<string> learned;
        private RecipeWindow window;
        private InputActionMap inventoryMap;
        private bool restoreInput;
        private static readonly MethodInfo addText=AccessTools.Method(typeof(Description),"AddDescriptionText");
        internal static bool Chinese {get{string s=I2.Loc.LocalizationManager.CurrentLanguage??"";return s.IndexOf("chinese",StringComparison.OrdinalIgnoreCase)>=0||s.StartsWith("zh",StringComparison.OrdinalIgnoreCase);}}
        internal static string Pick(string cn,string en){return Chinese?cn:en;}
        internal static bool AllRecipes {get{return Instance!=null&&Instance.showAllRecipes.Value;}}
        internal static bool Open {get{return Instance!=null&&Instance.window!=null&&Instance.window.IsOpen;}}
        private void Awake()
        {
            Instance=this;enabled=Config.Bind("General","Enabled",true,"Enable read-only learned recipe references.");
            showAllRecipes=Config.Bind("General","ShowAllRecipes",false,"显示所有配方（包括未解锁）；关闭时仅显示已解锁配方。不解锁配方。 / Show all recipes, including unlearned recipes. Off: learned recipes only. Does not unlock recipes.");
            showAllRecipes.SettingChanged+=(sender,args)=>{index=null;if(Open){var data=Index();if(data!=null)window.Refresh(data);}};
            key=Config.Bind("Controls","OpenKey",Key.F8,"Open the hovered item's recipe reference. Escape closes it.");
            harmony=new Harmony("local.sulfur.recipebrowser");
            try{harmony.PatchAll(Assembly.GetExecutingAssembly());Logger.LogInfo("what's this / 我食纪事 0.2.2 ready; default F8. No save writes.");}
            catch(Exception e){harmony.UnpatchSelf();Logger.LogError(e);enabled.Value=false;}
        }
        internal static void SetAllRecipes(bool value){if(Instance!=null)Instance.showAllRecipes.Value=value;}
        private RecipeIndex Index()
        {
            var current=SulfurSaveState.active;var loader=StaticInstance<AsyncAssetLoading>.Instance;
            var db=loader==null?null:loader.recipeDatabase;
            if(current==null||db==null||current.KNOWN_RECIPES==null)return null;
            bool all=showAllRecipes.Value;
            if(index!=null&&save==current&&database==db&&indexedAllRecipes==all&&(all||learned.SetEquals(current.KNOWN_RECIPES.Values)))return index;
            var entries=new List<RecipeEntry>();var raw=db.GetRawList();if(raw==null)return null;
            foreach(var r in raw)
            {
                if(!r.canBeCrafted||(!all&&!r.IsLearned)||r.itemsNeeded==null)continue;
                var entry=new RecipeEntry{Id=r.id.value,Product=r.createsItem.value,Count=r.quantityCreated};
                foreach(var m in r.itemsNeeded)entry.Ingredients.Add(new MaterialEntry{Id=m.item.value,Count=m.quantity});
                entries.Add(entry);
            }
            index=new RecipeIndex(entries);save=current;database=db;indexedAllRecipes=all;learned=new HashSet<string>(current.KNOWN_RECIPES.Values);
            return index;
        }
        internal static string Name(int id)
        {var item=new ItemId((ushort)id).GetAsset();return item==null?"#"+id:item.LocalizedDisplayName;}
        private void Update()
        {
            var ui=StaticInstance<UIManager>.Instance;
            if(Open)
            {
                if(!enabled.Value||ui==null||ui.InventoryUI==null||!ui.InventoryUI.IsOpened||save!=SulfurSaveState.active||Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame)Close();
                return;
            }
            if(!enabled.Value||Keyboard.current==null||key.Value==Key.None||!Keyboard.current[key.Value].wasPressedThisFrame||hovered==null||ui==null||ui.InventoryUI==null||!ui.InventoryUI.IsOpened||ui.InventoryUI.draggingItem!=null)return;
            try
            {
                var data=Index();if(data==null)return;
                int id=hovered.itemDefinition.id.value;
                if(window==null)window=new RecipeWindow();
                window.Show(hovered.itemDefinition,data,Close);
                var game=StaticInstance<GameManager>.Instance;
                inventoryMap=game.InputActions.Inventory.Get();restoreInput=inventoryMap.enabled;inventoryMap.Disable();
                hovered=null;
            }
            catch(Exception e){Close();Logger.LogError("Recipe window: "+e);}
        }
        private void Close()
        {
            if(window!=null)window.Hide();
            var ui=StaticInstance<UIManager>.Instance;
            if(restoreInput&&inventoryMap!=null&&ui!=null&&ui.InventoryUI!=null&&ui.InventoryUI.IsOpened&&save==SulfurSaveState.active)inventoryMap.Enable();
            inventoryMap=null;restoreInput=false;hovered=null;
        }
        private void OnDisable(){Close();}
        private void OnDestroy(){Close();if(window!=null)window.Dispose();if(harmony!=null)harmony.UnpatchSelf();if(Instance==this)Instance=null;}
        [HarmonyPatch(typeof(InventoryItem),"OnPointerEnter")][HarmonyPostfix]
        private static void Enter(InventoryItem __instance){if(Instance!=null&&!Open)Instance.hovered=__instance;}
        [HarmonyPatch(typeof(InventoryItem),"OnPointerExit")][HarmonyPostfix]
        private static void Exit(InventoryItem __instance){if(Instance!=null&&Instance.hovered==__instance)Instance.hovered=null;}
        [HarmonyPatch(typeof(Description),"AddPriceInfo")][HarmonyPrefix]
        private static void Tooltip(Description __instance,InventoryItem item,ref int childIndex)
        {
            if(Instance==null||!Instance.enabled.Value||item==null||item.itemDefinition==null)return;
            try
            {
                var data=Instance.Index();if(data==null)return;int id=item.itemDefinition.id.value;
                int uses=data.ProductCount(id);List<RecipeEntry> sources;int count=data.Sources.TryGetValue(id,out sources)?sources.Count:0;
                if(uses==0&&count==0)return;
                var text=new StringBuilder();
                if(uses>0)text.Append(Pick("可用于制作：","Used to craft: ")).Append(uses).Append(Pick(" 种物品"," items"));
                if(count>0){if(text.Length>0)text.Append('\n');text.Append(Pick("制作来源：","Crafting sources: ")).Append(count).Append(Pick(" 个配方"," recipes"));}
                if(Instance.key.Value!=Key.None)text.Append('\n').Append(Pick("按 ","Press ")).Append(Instance.key.Value).Append(Pick(" 查看详情"," for details"));
                object[] args={text.ToString(),childIndex};addText.Invoke(__instance,args);childIndex=(int)args[1];
            }
            catch(Exception e){Instance.Logger.LogWarning("Recipe tooltip: "+e.Message);}
        }
    }
    [HarmonyPatch]
    internal static class ModalGuard
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> Targets()
        {
            foreach(string name in new[]{"Use","DropFromPlayer","OnBeginDrag","OnPointerEnter"})yield return AccessTools.Method(typeof(InventoryItem),name);
            yield return AccessTools.Method(typeof(PerfectRandom.Sulfur.Core.UI.Inventory.InventoryUI),"Update");
        }
        [HarmonyPrefix][HarmonyPriority(Priority.First)]
        private static bool Guard(){return !RecipeBrowserPlugin.Open;}
    }
}
