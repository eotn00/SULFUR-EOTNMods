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
    [BepInPlugin(Id,"库存重构 / Inventory Redux","0.1.5")]
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
        private static bool transactionWarning;
        private static SulfurSaveState transactionWarningSave;
        private static SulfurSaveState auditSave;
        private static readonly Dictionary<string,LoadAuditResult> auditWarnings=new Dictionary<string,LoadAuditResult>();
        private static string auditChinese="",auditEnglish="";
        private static readonly PropertyInfo currentLanguage=AccessTools.Property(AccessTools.TypeByName("I2.Loc.LocalizationManager"),"CurrentLanguage");
        private Font auditFont;
        private GUIStyle auditStyle;
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
                Logger.LogInfo("Inventory Redux 0.1.5 loaded. Load differences and failed rollback warn without blocking saves. Test build.");
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

        internal static void TransactionFailed(Exception e)
        {
            transactionWarning=true;transactionWarningSave=SulfurSaveState.active;
            if(Instance!=null)Instance.Logger.LogWarning("Inventory operation failed and recovery was incomplete. Saving remains allowed; check items and currency before deciding whether to save. "+e);
        }

        internal void Report(string message){Logger.LogWarning(message);}

        private static bool AllowSave(int slot,SulfurSaveState data,Action<SaveResults> onCompleted)
        {if(!failed&&!Stacks.PlacementPending)return true;if(onCompleted!=null)onCompleted(SaveResults.UNKNOWN_ERROR);return false;}

        private void OnGUI()
        {
            if(failed)GUI.Box(new Rect(10,10,720,40),"Inventory Redux error: saving blocked. Exit and restore previous plugins.");
            bool showAudit=auditWarnings.Count>0&&ReferenceEquals(auditSave,SulfurSaveState.active);
            bool showTransaction=transactionWarning&&ReferenceEquals(transactionWarningSave,SulfurSaveState.active);
            if(!showAudit&&!showTransaction)return;
            if(auditFont==null)auditFont=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Arial"},16);
            if(auditStyle==null)auditStyle=new GUIStyle(GUI.skin.box){font=auditFont,fontSize=16,wordWrap=true,alignment=TextAnchor.UpperLeft,padding=new RectOffset(12,12,10,10)};
            string language=currentLanguage==null?"":(string)currentLanguage.GetValue(null,null)??"";
            bool chinese=language.IndexOf("chinese",StringComparison.OrdinalIgnoreCase)>=0||language.StartsWith("zh",StringComparison.OrdinalIgnoreCase);
            string text="";
            if(showTransaction)
            {
                text=chinese?"Inventory Redux：交易或物品操作失败，未能完整恢复。物品或金币状态可能不完整。\n仍允许保存，请自行检查并决定是否保存退出。":
                    "Inventory Redux: a trade or inventory operation failed and recovery was incomplete. Items or currency may be inconsistent.\nSaving remains allowed. Check your inventory and decide whether to save and exit.";
                if(!showAudit)text+=chinese?"\n你也可以不保存退出，避免覆盖旧存档。Alt+F4 仍可能保存；可在任务管理器的“详细信息”中结束游戏进程。此前自动保存无法撤销。":
                    "\nYou may exit without saving to avoid overwriting the previous save. Alt+F4 may save; ending the game process in Task Manager > Details skips exit callbacks. Earlier autosaves cannot be undone.";
            }
            if(showAudit)text+=(showTransaction?"\n\n":"")+(chinese?auditChinese:auditEnglish);
            float width=Mathf.Min(1000,Mathf.Max(1,Screen.width-20));
            GUI.Box(new Rect(10,failed?60:10,width,auditStyle.CalcHeight(new GUIContent(text),width)),text,auditStyle);
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
        private static LoadAuditEntry AuditEntry(InventoryData d)
        {return new LoadAuditEntry(d.id.value,d.quantity,d.id+"|"+d.quantity+"|"+d.equipped+"|"+d.inventorySlot+"|"+(d.position==null?"":string.Join(",",d.position))+"|"+d.xSize+"|"+d.ySize+"|"+d.rotated);}

        private static void BeforeLoadBag(out LoadAuditEntry[] __state)
        {__state=SulfurSaveState.active.INVENTORY.Select(AuditEntry).ToArray();}

        private static void AfterLoadBag(LoadAuditEntry[] __state)
        {
            var ui=StaticInstance<UIManager>.Instance;
            Audit(__state,ui.PlayerBackpackGrid.GetSerializedContents().Concat(ui.Paperdoll.GetSerializedContents()),"backpack/equipment");
            ui.PlayerBackpackGrid.GetComponent<Viewport>().RestorePosition();
        }

        private static void BeforeLoadStash(string stashIdentifier,out LoadAuditEntry[] __state)
        {__state=Records("stash/"+(stashIdentifier??"")).Select(AuditEntry).ToArray();}

        private static void AfterLoadStash(string stashIdentifier,LoadAuditEntry[] __state)
        {Audit(__state,StaticInstance<UIManager>.Instance.InventoryUI.GetStashItemGrid(stashIdentifier??"").GetSerializedContents(),"stash/"+stashIdentifier);}

        private static void Audit(LoadAuditEntry[] expected,IEnumerable<InventoryData> actual,string name)
        {
            if(!ReferenceEquals(auditSave,SulfurSaveState.active))
            {auditSave=SulfurSaveState.active;auditWarnings.Clear();}
            var current=actual.Select(AuditEntry).ToArray();
            var result=LoadAudit.Compare(expected,current);
            if(result.Matches)
            {auditWarnings.Remove(name);Instance.Logger.LogInfo("Load audit OK: "+name+", "+current.Length+" items; quantities, dimensions and positions match.");}
            else
            {
                auditWarnings[name]=result;
                Instance.Logger.LogWarning("Load audit difference "+name+": expected "+expected.Length+", actual "+current.Length+
                    "; fewer items="+result.MissingKinds+" types / "+result.MissingUnits+" units; saving remains allowed. Missing: "+
                    string.Join(", ",result.Missing.OrderBy(p=>p.Key).Select(p=>"ItemId_"+p.Key+" x"+p.Value)));
                Instance.Logger.LogWarning("Unmatched saved records (first 20): "+string.Join("; ",expected.Select(e=>e.Signature).Except(current.Select(e=>e.Signature)).Take(20))+
                    "\nUnmatched loaded records (first 20): "+string.Join("; ",current.Select(e=>e.Signature).Except(expected.Select(e=>e.Signature)).Take(20)));
            }
            int kinds=auditWarnings.Values.SelectMany(r=>r.Missing.Keys).Distinct().Count();
            long units=auditWarnings.Values.Sum(r=>r.MissingUnits);
            auditChinese="Inventory Redux：读档物品变化提醒（允许保存）\n"+
                (units>0?"检测到 "+kinds+" 种物品数量减少，共 "+units+" 个。":"未发现物品数量减少，但物品记录、位置或装备状态有变化。")+
                "升级消耗或其他 Mod 也可能造成这种变化。\n你可以自行选择正常保存退出，或不保存退出，避免用当前状态覆盖旧存档。\n"+
                "Alt+F4 仍可能保存。要跳过退出保存，可在任务管理器的“详细信息”中结束游戏进程；此前自动保存无法撤销。";
            auditEnglish="Inventory Redux: inventory changed during loading (saving allowed)\n"+
                (units>0?"Fewer items: "+kinds+" types, "+units+" units. ":"No quantity loss detected, but item records, positions or equipment changed. ")+
                "Upgrades or other mods may cause this.\nYou can save and exit, or exit without saving to avoid overwriting the previous inventory state.\n"+
                "Alt+F4 may still save. To skip the exit save, end the game process in Task Manager > Details. Earlier autosaves cannot be undone.";
        }
    }
}
