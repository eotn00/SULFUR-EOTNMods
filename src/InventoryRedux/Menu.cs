using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalSulfur.UnifiedInventory
{
    public sealed class Menu:MonoBehaviour
    {
        internal static bool Open;
        internal static bool Selecting;
        internal static bool GroupDragging;
        private Vector2Int grabOffset,previewOrigin;
        private ItemGrid previewGrid;
        private List<InventoryItem> previewItems;
        private List<Vector2Int> previewTargets;
        private bool previewValid;
        private string resizeStatus="";
        private bool restoreInput;
        private ItemGrid editing,selectionGrid;
        private Vector2 start;
        private Rect window=new Rect(40,60,530,690);
        private ContainerSettings settings;
        private string columns,rows,visible,pages;
        private GUIStyle label;
        private Font font;
        private Vector2 scroll;
        internal static void Install(Harmony h)
        {
            foreach(string name in new[]{"OnPointerClick","OnBeginDrag","OnDrag","OnEndDrag","Use"})
                h.Patch(AccessTools.Method(typeof(InventoryItem),name),prefix:new HarmonyMethod(typeof(Menu),"AllowItemInput"));
        }
        private static bool AllowItemInput(InventoryItem __instance,System.Reflection.MethodBase __originalMethod)
        {
            if(Stacks.NativeSplitInput||QuickActions.NativeAction)return true;
            if(QuickActions.BlocksClick&&(__originalMethod.Name=="OnPointerClick"||__originalMethod.Name=="OnBeginDrag"))return false;
            var k=Keyboard.current;
            if(Mouse.current!=null&&InventoryOverlay.Captures(Mouse.current.position.ReadValue()))return false;
            if(GroupDragging)return false;
            bool managed=__instance.ContainingGrid!=null&&__instance.ContainingGrid.GetComponent<Viewport>()!=null;
            if(managed&&__originalMethod.Name=="OnBeginDrag"&&Controls.Held(Modifier.Control)&&!Controls.LayoutHeld)return false;
            if(managed&&(__originalMethod.Name=="OnBeginDrag"||__originalMethod.Name=="OnDrag"||__originalMethod.Name=="OnEndDrag")&&(GroupDragging||Management.Selection.Contains(__instance)))return false;
            if(managed&&(__originalMethod.Name=="OnBeginDrag"||__originalMethod.Name=="OnDrag")&&(Viewport.IsLayoutDragging||Controls.LayoutHeld))return false;
            if(managed&&__originalMethod.Name=="OnPointerClick"&&Controls.SplitHeld)return !Open&&!Selecting;
            return !Open&&!Selecting&&!(managed&&k!=null&&(k.leftShiftKey.isPressed||k.rightShiftKey.isPressed));
        }
        internal static ItemGrid Hovered()
        {
            var ui=StaticInstance<UIManager>.Instance;
            if(ui==null||ui.InventoryUI==null||!ui.InventoryUI.IsOpened||Mouse.current==null)return null;
            foreach(var view in Viewport.Live)
                if(view!=null&&view.Topmost(Mouse.current.position.ReadValue()))return view.Grid;
            return null;
        }
        private void LateUpdate(){HudLayout.SyncVisibility();}
        private void Update()
        {
            var k=Keyboard.current;var mouse=Mouse.current;var game=StaticInstance<GameManager>.Instance;var manager=StaticInstance<UIManager>.Instance;
            if(k==null||mouse==null||game==null||manager==null||manager.InventoryUI==null)return;
            var ui=manager.InventoryUI;
            InventoryOverlay.PageInput();
            if(QuickActions.Update())return;
            bool splitPress=mouse.rightButton.wasPressedThisFrame&&Controls.SplitHeld;
            if(ui.IsOpened&&splitPress)
                Plugin.Instance.Report("Split raw input: menu="+Open+", input="+game.InputActions.Inventory.Get().enabled+", dragging="+(ui.draggingItem!=null)+", group="+GroupDragging+", overlay="+InventoryOverlay.Captures(mouse.position.ReadValue()));
            if(Stacks.UpdateSplitDrag())return;
            if(!ui.IsOpened){InventoryOverlay.Cancel();Close();Selecting=false;GroupDragging=false;Management.Selection.Clear();return;}
            if(k.f6Key.wasPressedThisFrame)
            {
                if(Open)Close();else if(ui.draggingItem==null&&game.InputActions.Inventory.Get().enabled)
                {
                    if(!Edit(CurrentStash()??ui.PlayerBackpackGrid))return;
                    restoreInput=game.InputActions.Inventory.Get().enabled;game.InputActions.Inventory.Disable();Open=true;
                }
            }
            if(Open){InventoryOverlay.Cancel();if(k.escapeKey.wasPressedThisFrame)Close();return;}
            if(!game.InputActions.Inventory.Get().enabled||ui.draggingItem!=null)return;
            if(!splitPress&&InventoryOverlay.Input())return;
            var hovered=Hovered();bool shift=k.leftShiftKey.isPressed||k.rightShiftKey.isPressed;
            if(splitPress&&!GroupDragging)
            {
                if(MerchantBridge.IsMerchantItem(ui.hoveredOrNavigatedInventoryItem))return;
                if(hovered==null){Plugin.Instance.Report("Split input detected, but no managed grid was hit");return;}
                var slot=hovered.GetSlotAt(hovered.GetGridPosition(mouse.position.ReadValue()));
                Stacks.Split(slot==null?null:slot.ItemInSlot,!Controls.Held(Plugin.Instance.Settings.SingleModifier.Value));
                return;
            }
            if(!GroupDragging&&!shift&&!Controls.LayoutHeld&&mouse.leftButton.wasPressedThisFrame&&hovered!=null)
            {
                var cell=hovered.GetGridPosition(mouse.position.ReadValue());var slot=hovered.GetSlotAt(cell);
                if(slot!=null&&Management.Selection.Contains(slot.ItemInSlot))
                {
                    grabOffset=cell-new Vector2Int(Management.Selection.Min(i=>i.gridPosition.x),Management.Selection.Min(i=>i.gridPosition.y));
                    GroupDragging=true;previewGrid=null;
                }
                else Management.Selection.Clear();
            }
            if(GroupDragging)
            {
                var origin=hovered==null?new Vector2Int(-1,-1):hovered.GetGridPosition(mouse.position.ReadValue())-grabOffset;
                if(hovered!=previewGrid||origin!=previewOrigin)
                {
                    previewGrid=hovered;previewOrigin=origin;
                    previewValid=Management.PlanSelected(hovered,origin,out previewItems,out previewTargets);
                }
                if(k.escapeKey.wasPressedThisFrame){GroupDragging=false;return;}
                if(mouse.leftButton.wasReleasedThisFrame)
                {if(previewValid)Management.MoveSelected(previewGrid,previewOrigin);GroupDragging=false;}
                return;
            }
            if(hovered!=null&&Controls.LockPressed&&!Controls.LayoutHeld)
            {var slot=hovered.GetSlotAt(hovered.GetGridPosition(mouse.position.ReadValue()));if(slot!=null)ItemLocks.Toggle(slot.ItemInSlot);}
            if(shift&&!Controls.LayoutHeld&&mouse.leftButton.wasPressedThisFrame&&hovered!=null)
            {Selecting=true;selectionGrid=hovered;start=mouse.position.ReadValue();Management.Selection.Clear();}
            if(Selecting&&mouse.leftButton.wasReleasedThisFrame)
            {
                Selecting=false;
                if(selectionGrid!=null)
                {
                    Vector2 end=mouse.position.ReadValue();var area=Rect.MinMaxRect(Mathf.Min(start.x,end.x),Mathf.Min(start.y,end.y),Mathf.Max(start.x,end.x),Mathf.Max(start.y,end.y));
                    foreach(var item in selectionGrid.AllItemsInGridOnly())
                    {
                        Vector3[] corners=new Vector3[4];((RectTransform)item.transform).GetWorldCorners(corners);
                        var center=(corners[0]+corners[2])*.5f;
                        if(area.Contains(center)&&selectionGrid.GetComponent<Viewport>().Contains(center))Management.Selection.Add(item);
                    }
                }
            }
            if(shift&&!Controls.Held(Plugin.Instance.Settings.SplitModifier.Value)&&mouse.rightButton.wasPressedThisFrame&&hovered!=null)
                Management.MoveSelected(hovered,hovered.GetGridPosition(mouse.position.ReadValue()));
            if(k.escapeKey.wasPressedThisFrame)Management.Selection.Clear();
        }
        private void Close()
        {
            if(!Open)return;Open=false;
            var game=StaticInstance<GameManager>.Instance;if(restoreInput&&game!=null)game.InputActions.Inventory.Enable();restoreInput=false;
        }
        private void OnDisable(){InventoryOverlay.Cancel();Close();Selecting=false;GroupDragging=false;}
        private ItemGrid CurrentStash()
        {
            var manager=StaticInstance<UIManager>.Instance;
            var ui=manager==null?null:manager.InventoryUI;
            var grid=ui==null?null:ui.ServiceStationGrid;
            return grid!=null&&grid.isActiveAndEnabled&&grid.GetComponent<Viewport>()!=null?grid:null;
        }
        private bool Edit(ItemGrid grid)
        {
            var view=grid==null?null:grid.GetComponent<Viewport>();
            if(view==null)return false;
            editing=grid;settings=view.Options;
            columns=settings.Columns.Value.ToString();rows=settings.Rows.Value.ToString();visible=settings.VisibleRows.Value.ToString();pages=settings.Pages.Value.ToString();
            scroll=Vector2.zero;
            view.ReportLayout();
            return true;
        }
        private void OnGUI()
        {
            InventoryOverlay.Draw();
            var manager=StaticInstance<UIManager>.Instance;
            if(manager!=null&&manager.InventoryUI!=null&&manager.InventoryUI.IsOpened&&!Open)
            {
                foreach(var item in Management.Selection)
                    if(item!=null&&item.gameObject.activeInHierarchy)
                    {
                        var rect=ItemRect(item);Outline(rect,new Color(1f,.85f,.1f,1f));
                    }
                if(GroupDragging&&previewGrid!=null&&previewItems!=null&&previewTargets!=null)
                    for(int n=0;n<previewItems.Count;n++)
                    {
                        var pos=previewTargets[n];var size=previewItems[n].InventorySize;
                        var a=previewGrid.transform.TransformPoint(new Vector3(pos.x*previewGrid.tileSize.x,pos.y*previewGrid.tileSize.y,0));
                        var b=previewGrid.transform.TransformPoint(new Vector3((pos.x+size.x)*previewGrid.tileSize.x,(pos.y+size.y)*previewGrid.tileSize.y,0));
                        Outline(Rect.MinMaxRect(a.x,Screen.height-b.y,b.x,Screen.height-a.y),previewValid?Color.green:Color.red);
                    }
            }
            if(Open)
            {
                if(font==null)font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Arial"},16);
                if(label==null)label=new GUIStyle(GUI.skin.label){font=font,fontSize=16,wordWrap=true};
                var prior=GUI.skin.font;GUI.skin.font=font;
                window.height=Mathf.Min(690,Screen.height-20);window.width=Mathf.Min(530,Screen.width-20);
                window.x=Mathf.Clamp(window.x,0,Mathf.Max(0,Screen.width-window.width));
                window.y=Mathf.Clamp(window.y,0,Mathf.Max(0,Screen.height-window.height));
                window=GUI.Window(814230,window,Draw,"库存重构 / Inventory Redux");GUI.skin.font=prior;
            }
            if(Selecting&&Mouse.current!=null)
            {
                var end=Mouse.current.position.ReadValue();GUI.Box(Rect.MinMaxRect(Math.Min(start.x,end.x),Screen.height-Math.Max(start.y,end.y),Math.Max(start.x,end.x),Screen.height-Math.Min(start.y,end.y)),"");
            }
        }
        private static Rect ItemRect(InventoryItem item)
        {var c=new Vector3[4];((RectTransform)item.transform).GetWorldCorners(c);return Rect.MinMaxRect(c[0].x,Screen.height-c[2].y,c[2].x,Screen.height-c[0].y);}
        private static void Outline(Rect r,Color color)
        {
            var previous=GUI.color;GUI.color=color;
            GUI.DrawTexture(new Rect(r.x,r.y,r.width,2),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(r.x,r.yMax-2,r.width,2),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x,r.y,2,r.height),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(r.xMax-2,r.y,2,r.height),Texture2D.whiteTexture);GUI.color=previous;
        }
        private void Draw(int id)
        {
            var ui=StaticInstance<UIManager>.Instance.InventoryUI;
            var stash=CurrentStash();
            if(editing==null||!editing.isActiveAndEnabled){if(!Edit(stash??ui.PlayerBackpackGrid)){Close();return;}}
            GUILayout.BeginHorizontal();
            GUI.enabled=editing!=ui.PlayerBackpackGrid;
            if(GUILayout.Button("背包 / Backpack"))Edit(ui.PlayerBackpackGrid);
            GUI.enabled=stash!=null&&editing!=stash;
            if(GUILayout.Button("当前仓库 / Current stash"))Edit(stash);
            GUI.enabled=true;
            GUILayout.EndHorizontal();
            GUILayout.Label(editing==ui.PlayerBackpackGrid?"正在调整：背包 / Editing: Backpack":"正在调整：当前仓库 / Editing: Current stash",label);
            scroll=GUILayout.BeginScrollView(scroll,GUILayout.Height(Mathf.Max(100,window.height-165)));
            Row("每页列数 / Columns per page",ref columns);Row("每页行数 / Rows per page",ref rows);Row("页数 / Pages",ref pages);Row("可见行数 / Visible rows",ref visible);
            GUILayout.Label("应用后立即调整；空间不足则不修改 / Apply now; reject if no room",label);
            if(resizeStatus.Length>0)GUILayout.Label(resizeStatus,label);
            var view=editing.GetComponent<Viewport>();
            GUILayout.Label("当前容量 / Actual: "+editing.gridSize.x+" x "+editing.gridSize.y+"; 行 / Rows: "+(view.First+1)+" - "+(view.First+view.Visible),label);
            GUILayout.Label("缩小容量会重排未锁定物品 / Shrinking repacks unlocked items",label);
            if(GUILayout.Button("定位物品 / Find items"))view.FocusItems();
            var global=Plugin.Instance.Settings;
            global.WheelPages.Value=GUILayout.Toggle(global.WheelPages.Value,"整页滚动 / Scroll by page");
            GUILayout.Label("滚动行数 / Scroll rows: "+global.WheelRows.Value,label);
            global.WheelRows.Value=(int)GUILayout.HorizontalSlider(global.WheelRows.Value,1,20);
            GUILayout.Label("缩放 / Scale: "+settings.Scale.Value.ToString("0.00"),label);
            settings.Scale.Value=GUILayout.HorizontalSlider(settings.Scale.Value,.25f,2f);
            GUILayout.Label("堆叠上限 / Stack limit: "+global.MaxStack.Value,label);
            global.MaxStack.Value=(int)GUILayout.HorizontalSlider(global.MaxStack.Value,1,99999);
            int stackLimit;
            if(int.TryParse(GUILayout.TextField(global.MaxStack.Value.ToString()),out stackLimit))global.MaxStack.Value=Mathf.Clamp(stackLimit,1,99999);
            GUILayout.Label("1：关闭额外堆叠，保留已有数量 / 1: disable merging; preserve existing stacks",label);
            GUILayout.Label("装备不堆叠 / Equipment never stacks",label);
            GUILayout.Space(12);
            GUILayout.Label("操作键 / Controls",label);
            global.LayoutModifier.Value=(Modifier)GUILayout.Toolbar((int)global.LayoutModifier.Value,new[]{"Alt","Ctrl","Shift"});
            GUILayout.Label("位置拖动 / Position drag",label);
            global.DragButton.Value=(LayoutButton)GUILayout.Toolbar((int)global.DragButton.Value,new[]{"左键 / Left","中键 / Middle"});
            GUILayout.Label("拆分修饰键 + 右键 / Split + right-click",label);
            var split=(Modifier)GUILayout.Toolbar((int)global.SplitModifier.Value,new[]{"Alt","Ctrl","Shift"});
            if(split!=global.SplitModifier.Value){global.SplitModifier.Value=split;if(global.SingleModifier.Value==split)global.SingleModifier.Value=(Modifier)(((int)split+1)%3);}
            GUILayout.Label("只取一个修饰键 + 右键 / Split one + right-click",label);
            var single=(Modifier)GUILayout.Toolbar((int)global.SingleModifier.Value,new[]{"Alt","Ctrl","Shift"});
            if(single!=global.SplitModifier.Value)global.SingleModifier.Value=single;
            GUILayout.Label("锁定键 / Lock key: "+global.LockKey.Value,label);
            var keys=new[]{Key.L,Key.K,Key.F7,Key.F9};
            int selected=Array.IndexOf(keys,global.LockKey.Value);
            int chosen=GUILayout.Toolbar(selected,new[]{"L","K","F7","F9"});
            if(chosen>=0&&chosen!=selected)global.LockKey.Value=keys[chosen];
            GUILayout.Space(10);
            GUILayout.Label(global.LayoutModifier.Value+" + "+global.DragButton.Value+" 拖动 / drag: 移动窗口 / reposition",label);
            GUILayout.Label(global.LayoutModifier.Value+" + 滚轮 / wheel: 缩放 / scale",label);
            GUILayout.Label(global.SplitModifier.Value+" + 右键 / right-click: 拆一半 / split half",label);
            GUILayout.Label(global.SingleModifier.Value+" + 右键 / right-click: 拆一个 / split one",label);
            GUILayout.Label(global.LockKey.Value+": 悬停物品锁定 / lock hovered item",label);
            GUILayout.Label("Shift + 左键框选；松开 Shift，拖动选中物品移动整组\nShift + left-drag selects; drag a selected item to move the group",label);
            GUILayout.Label("仓库：按住右键扫过转移 / Stash: hold right-click and sweep to transfer",label);
            GUILayout.Label("Ctrl + 按住左键扫过丢弃 / Ctrl + left-drag: sweep to discard stacks",label);
            GUILayout.Label("手持堆叠：右键连放一个；Alt + 右键只放一半一次\nHeld stack: hold right-click to place singles; Alt + right-click places half once",label);
            GUILayout.EndScrollView();
            if(GUILayout.Button("应用 / Apply"))
            {
                int w,h,value;
                int pageCount;
                if(int.TryParse(columns,out w)&&int.TryParse(rows,out h)&&int.TryParse(pages,out pageCount))Management.Resize(editing,Geometry.Clamp(w,1,100),Geometry.Clamp(h,1,500),Geometry.Clamp(pageCount,1,20),out resizeStatus);
                if(int.TryParse(visible,out value))settings.VisibleRows.Value=Geometry.Clamp(value,4,40);
                if(editing!=null)editing.GetComponent<Viewport>().Layout();
            }
            if(GUILayout.Button("重置位置 / Reset position")){settings.X.Value=0;settings.Y.Value=0;if(editing!=null)editing.GetComponent<Viewport>().Layout();}
            if(GUILayout.Button("关闭 / Close"))Close();
            GUI.DragWindow(new Rect(0,0,window.width,25));
        }
        private void Row(string text,ref string value){GUILayout.BeginHorizontal();GUILayout.Label(text,label,GUILayout.Width(300));value=GUILayout.TextField(value,5,GUILayout.Width(90));GUILayout.EndHorizontal();}
    }
}
