using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;
using PerfectRandom.Sulfur.Core.UI.Inventory;
using UnityEngine;

namespace LocalSulfur.UnifiedInventory
{
    [BepInPlugin(Id,"库存重构 / Inventory Redux","0.1.0")]
    [BepInIncompatibility("AetharNet.Mods.BigBackpack")]
    [BepInIncompatibility("AetharNet.Mods.BigStash")]
    [BepInIncompatibility("com.dsh.sulfur.scrollbags")]
    [BepInIncompatibility("com.sulfur.inventorylock")]
    [BepInIncompatibility("com.sulfur.stackingmod")]
    [BepInIncompatibility("local.sulfur.lockscopes")]
    [BepInIncompatibility("AetharNet.Mods.InventoryBoxSelect")]
    [BepInIncompatibility("AetharNet.Mods.Autoback")]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Id="local.sulfur.unifiedinventory";
        internal static Plugin Instance;
        internal Settings Settings;
        internal static readonly Dictionary<ItemGrid,string> Stashes=new Dictionary<ItemGrid,string>();
        private readonly Harmony harmony=new Harmony(Id);
        private static bool failed;
        internal static bool Failed {get{return failed;}}

        private void Awake()
        {
            Instance=this;
            try
            {
                Patch(typeof(SulfurSaveState),"WriteToDisk","AllowSave",null);
                Settings=new Settings(Config);
                Patch(typeof(InventoryUI),"RegisterStashItemGrid",null,"RegisterStash");
                Patch(typeof(ItemGrid),"SetupGrid","BeforeSetup","AfterSetup");
                Patch(typeof(ItemGrid),"AcquireVisuals","Acquire",null);
                Patch(typeof(ItemGrid),"ReleaseVisuals","Release",null);
                Patch(typeof(ItemGrid),"ReleaseVisualsForDestroy","ReleaseForDestroy",null);
                Patch(typeof(ItemGrid),"GetGridPosition","HitTest",null);
                Patch(typeof(ServiceStation),"PositionUI",null,"AfterPosition");
                Patch(typeof(GameManager),"LoadEquipmentData","BeforeLoadBag","AfterLoadBag");
                Patch(typeof(GameManager),"LoadStashData","BeforeLoadStash","AfterLoadStash");
                ItemLocks.Install(harmony);
                Management.Install(harmony);
                Stacks.Install(harmony);
                QuestPlacement.Install(harmony);
                Menu.Install(harmony);
                HudLayout.Install(harmony);
                InventoryRaycasts.Install(harmony);
                gameObject.AddComponent<Menu>();
                Logger.LogInfo("Inventory Redux 0.1.0 loaded. Native storage; visible-row pooling. Test build.");
            }
            catch(Exception e){Fail(e);}
        }

        private void Patch(Type type,string name,string before,string after)
        {
            var method=AccessTools.Method(type,name);
            if(method==null)throw new MissingMethodException(type.FullName,name);
            harmony.Patch(method,before==null?null:new HarmonyMethod(typeof(Plugin),before),after==null?null:new HarmonyMethod(typeof(Plugin),after));
        }

        internal static void Fail(Exception e)
        {failed=true;if(Instance!=null)Instance.Logger.LogError("Inventory Redux safety stop: "+e);}

        internal void Report(string message){Logger.LogWarning(message);}

        private static bool AllowSave(int slot,SulfurSaveState data,Action<SaveResults> onCompleted)
        {if(!failed&&!Stacks.PlacementPending)return true;if(onCompleted!=null)onCompleted(SaveResults.UNKNOWN_ERROR);return false;}

        private void OnGUI()
        {
            if(failed)GUI.Box(new Rect(10,10,720,40),"Inventory Redux error: saving blocked. Exit and restore previous plugins.");
        }

        private static void RegisterStash(string identifier,ItemGrid itemGrid)
        {if(itemGrid!=null)Stashes[itemGrid]=identifier??"";}

        internal static IEnumerable<InventoryData> Records(string id)
        {
            var save=SulfurSaveState.active;
            if(save==null)return Enumerable.Empty<InventoryData>();
            if(id=="backpack")return save.INVENTORY.Where(d=>d!=null&&!d.equipped);
            string stash=id.Substring(6); string key="Stash"+(stash.Length==0?"":"_"+stash);
            return save.STASH.ContainsKey(key)?save.STASH[key].list:Enumerable.Empty<InventoryData>();
        }

        private static void BeforeSetup(ItemGrid __instance,ref Vector2Int gridSize)
        {
            var ui=StaticInstance<UIManager>.Instance;
            string stash,id;
            if(ui!=null&&ui.PlayerBackpackGrid==__instance)id="backpack";
            else if(Stashes.TryGetValue(__instance,out stash)&&ui!=null&&ui.InventoryUI.GetStashItemGrid(stash)==__instance)id="stash/"+stash;
            else return;
            try
            {
                var settings=Instance.Settings.Get(id);
                int width=settings.Columns.Value,height=checked(settings.Rows.Value*settings.Pages.Value);
                if(height>500)throw new InvalidOperationException("Page rows times pages exceeds 500");
                var footprints=new List<Footprint>();
                foreach(var d in Records(id))
                {
                    if(d==null||d.position==null||d.position.Length!=2)throw new InvalidOperationException("Invalid saved coordinates");
                    footprints.Add(new Footprint{X=d.position[0],Y=d.position[1],Width=d.xSize,Height=d.ySize});
                }
                Geometry.Fit(ref width,ref height,footprints);
                int pageRows=settings.Rows.Value;
                int pages=(height+pageRows-1)/pageRows;
                if(pages*pageRows>500||footprints.Any(p=>!Operations.WithinPage(p.Y,p.Height,pageRows)))
                {
                    // A changed config or restored save must never hide a boundary-spanning item.
                    settings.Rows.Value=height;settings.Pages.Value=1;
                    Instance.Report("Restored inventory requires a single safe page; configure pages in F6 to repack safely.");
                }
                else {height=pages*pageRows;settings.Pages.Value=pages;}
                gridSize=new Vector2Int(width,height);
                var view=__instance.GetComponent<Viewport>()??__instance.gameObject.AddComponent<Viewport>();
                view.Configure(__instance,settings);
                Instance.Logger.LogInfo(id+" capacity "+width+"x"+height+", "+footprints.Count+" saved items validated");
            }
            catch(Exception e){Fail(e);throw;}
        }

        private static void AfterSetup(ItemGrid __instance)
        {
            var view=__instance.GetComponent<Viewport>();
            if(view!=null){view.Initialize();view.Layout();}
        }

        private static bool Acquire(ItemGrid __instance)
        {
            var view=__instance.GetComponent<Viewport>();if(view==null)return true;
            view.Initialize();view.Acquire();return false;
        }

        private static bool Release(ItemGrid __instance)
        {
            var view=__instance.GetComponent<Viewport>();if(view==null)return true;
            view.Release();return false;
        }

        private static bool ReleaseForDestroy(ItemGrid __instance)
        {
            var view=__instance.GetComponent<Viewport>();if(view==null)return true;
            view.BeforeGridDestroy();return false;
        }

        private static bool HitTest(ItemGrid __instance,Vector2 mousePosition,ref Vector2Int __result)
        {
            var view=__instance.GetComponent<Viewport>();
            if(view==null||(!Menu.Open&&view.Contains(mousePosition)))return true;
            __result=new Vector2Int(-1,-1);return false;
        }

        private static void AfterPosition(ServiceStation __instance)
        {
            if(!(__instance is PlayerStash)||__instance.Grid==null)return;
            var view=__instance.Grid.GetComponent<Viewport>();if(view!=null)view.Layout();
        }

        // Snapshot before native loading, because other plugins may update the save in place.
        private static string Signature(InventoryData d)
        {return d.id+"|"+d.quantity+"|"+d.equipped+"|"+d.inventorySlot+"|"+(d.position==null?"":string.Join(",",d.position))+"|"+d.xSize+"|"+d.ySize+"|"+d.rotated;}

        private static void BeforeLoadBag(out string[] __state)
        {__state=SulfurSaveState.active.INVENTORY.Select(Signature).OrderBy(s=>s,StringComparer.Ordinal).ToArray();}

        private static void AfterLoadBag(string[] __state)
        {
            var ui=StaticInstance<UIManager>.Instance;
            Audit(__state,ui.PlayerBackpackGrid.GetSerializedContents().Concat(ui.Paperdoll.GetSerializedContents()),"backpack/equipment");
            ui.PlayerBackpackGrid.GetComponent<Viewport>().RestorePosition();
        }

        private static void BeforeLoadStash(string stashIdentifier,out string[] __state)
        {__state=Records("stash/"+(stashIdentifier??"")).Select(Signature).OrderBy(s=>s,StringComparer.Ordinal).ToArray();}

        private static void AfterLoadStash(string stashIdentifier,string[] __state)
        {Audit(__state,StaticInstance<UIManager>.Instance.InventoryUI.GetStashItemGrid(stashIdentifier??"").GetSerializedContents(),"stash/"+stashIdentifier);}

        private static void Audit(string[] expected,IEnumerable<InventoryData> actual,string name)
        {
            var current=actual.Select(Signature).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
            if(!expected.SequenceEqual(current))Fail(new InvalidOperationException("Load audit mismatch "+name+": expected "+expected.Length+", actual "+current.Length));
            else Instance.Logger.LogInfo("Load audit OK: "+name+", "+current.Length+" items; quantities, dimensions and positions match.");
        }
    }
}
