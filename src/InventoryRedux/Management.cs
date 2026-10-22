using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;
using UnityEngine;

namespace LocalSulfur.UnifiedInventory
{
    internal static class Management
    {
        internal static readonly HashSet<InventoryItem> Selection=new HashSet<InventoryItem>();
        internal static bool Resize(ItemGrid grid,int width,int pageRows,int pages,out string message)
        {
            message="无法安全调整 / Cannot resize safely";
            if(pageRows<1||pages<1||pages>20||pageRows>500/pages){message="总行数不能超过500 / Total rows cannot exceed 500";return false;}
            int height=pageRows*pages;
            if(grid==null||Plugin.Failed||Menu.GroupDragging||Menu.Selecting||StaticInstance<UIManager>.Instance.InventoryUI.draggingItem!=null)return false;
            var view=grid.GetComponent<Viewport>();if(view==null)return false;
            var items=grid.AllItemsInGridOnly();
            var original=items.Select((item,i)=>new Placement{Key=i,X=item.gridPosition.x,Y=item.gridPosition.y,Width=item.InventorySize.x,Height=item.InventorySize.y,Locked=ItemLocks.Locked(item)}).ToList();
            var plan=Operations.Resize(width,height,original,pageRows);
            if(plan==null){message="空间不足或锁定物品超出边界，未修改 / No room or locked item outside bounds; unchanged";return false;}
            var oldSize=grid.gridSize;var oldSlots=grid.slots;
            int oldColumns=view.Options.Columns.Value,oldRows=view.Options.Rows.Value,oldPages=view.Options.Pages.Value;
            var replacement=new GridSlot[width,height];
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)replacement[x,y]=new GridSlot(x,y,grid,grid.tileSize);
            view.Release();grid.SetHoveredSlot(null);Selection.Clear();
            view.Resizing=true;
            try
            {
                foreach(var p in original)grid.UnregisterItemAt(items[p.Key],p.X,p.Y,p.Width,p.Height);
                AccessTools.PropertySetter(typeof(ItemGrid),"gridSize").Invoke(grid,new object[]{new Vector2Int(width,height)});
                grid.slots=replacement;
                foreach(var p in plan)
                {
                    if(!grid.HasRoomFor(new Vector2Int(p.X,p.Y),items[p.Key].InventorySize))throw new InvalidOperationException("Resize placement rejected");
                    items[p.Key].SetToNewGridPosition(p.X,p.Y);
                    if(grid.GetSlotAt(p.X,p.Y).ItemInSlot!=items[p.Key])throw new InvalidOperationException("Resize registration failed");
                }
                if(grid.AllItemsInGridOnly().Count!=items.Count)throw new InvalidOperationException("Resize count mismatch");
                grid.RectTransform.sizeDelta=new Vector2(width*grid.tileSize.x,height*grid.tileSize.y);
                view.Options.Columns.Value=width;view.Options.Rows.Value=pageRows;view.Options.Pages.Value=pages;view.FocusItems();
                message="容量已立即应用 / Capacity applied";return true;
            }
            catch(Exception error)
            {
                try
                {
                    view.Options.Columns.Value=oldColumns;view.Options.Rows.Value=oldRows;view.Options.Pages.Value=oldPages;
                    foreach(var item in grid.AllItemsInGridOnly().ToList())grid.UnregisterItemAt(item,item.gridPosition.x,item.gridPosition.y,item.InventorySize.x,item.InventorySize.y);
                    view.Release();AccessTools.PropertySetter(typeof(ItemGrid),"gridSize").Invoke(grid,new object[]{oldSize});grid.slots=oldSlots;
                    foreach(var p in original)items[p.Key].SetToNewGridPosition(p.X,p.Y);
                    grid.RectTransform.sizeDelta=new Vector2(oldSize.x*grid.tileSize.x,oldSize.y*grid.tileSize.y);view.Layout();
                }
                catch(Exception rollback){Plugin.Fail(rollback);}
                Plugin.Instance.Report("Resize rolled back: "+error);return false;
            }
            finally {view.Resizing=false;}
        }
        internal static string Deposit(ItemGrid source,ItemGrid destination,bool existingOnly)
        {
            if(source==null||destination==null||source==destination||!Plugin.Stashes.ContainsKey(destination))return "请先打开仓库 / Open a stash first";
            int moved=0,skipped=0;Selection.Clear();
            foreach(var item in source.AllItemsInGridOnly().ToList())
            {
                if(item==null||ItemLocks.Locked(item)||item.isBeingDragged||item.paperdollSlot!=null){skipped++;continue;}
                moved+=Stacks.DepositExisting(item,destination);
                if(item==null||item.quantity<=0)continue;
                if(existingOnly){skipped++;continue;}
                var pos=destination.GetPossibleSpace(item.InventorySize);
                if(pos.x<0){skipped++;continue;}
                int quantity=item.quantity;
                if(Commit(new List<InventoryItem>{item},destination,new List<Vector2Int>{pos}))moved+=quantity;
                else {skipped++;if(Plugin.Failed)break;}
            }
            destination.GetComponent<Viewport>().FocusItems();
            return "已移动 / Moved: "+moved+"; 留在背包的堆 / Stacks remaining: "+skipped;
        }
        internal static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(ItemGrid),"TrySortInventory"),prefix:new HarmonyMethod(typeof(Management),"Sort"));
            h.Patch(AccessTools.Method(typeof(ItemGrid),"HasRoomFor",new[]{typeof(Vector2Int),typeof(Vector2Int),typeof(InventoryItem)}),prefix:new HarmonyMethod(typeof(Management),"PageRoom"));
        }
        private static bool PageRoom(ItemGrid __instance,Vector2Int origin,Vector2Int size,ref bool __result)
        {
            var view=__instance.GetComponent<Viewport>();
            if(view==null||view.Resizing||Operations.WithinPage(origin.y,size.y,view.PageRows))return true;
            __result=false;return false;
        }
        private static bool Sort(ItemGrid __instance,ref bool __result)
        {
            if(__instance.GetComponent<Viewport>()==null)return true;
            __result=false;
            var ui=StaticInstance<UIManager>.Instance.InventoryUI;
            if(ui.draggingItem!=null)return false;
            Stacks.MergeForSort(__instance);
            var items=__instance.AllItemsInGridOnly();
            var entries=items.Select((item,i)=>new Placement{Key=i,X=item.gridPosition.x,Y=item.gridPosition.y,Width=item.InventorySize.x,Height=item.InventorySize.y,Locked=ItemLocks.Locked(item)}).ToList();
            var plan=Operations.Sort(__instance.gridSize.x,__instance.gridSize.y,entries,__instance.GetComponent<Viewport>().PageRows);if(plan==null)return false;
            var changed=plan.Where(p=>!p.Locked&&(items[p.Key].gridPosition!=new Vector2Int(p.X,p.Y))).ToList();
            __result=Commit(changed.Select(p=>items[p.Key]).ToList(),__instance,changed.Select(p=>new Vector2Int(p.X,p.Y)).ToList());return false;
        }
        internal static bool MoveSelected(ItemGrid destination,Vector2Int origin)
        {
            List<InventoryItem> items;List<Vector2Int> targets;
            return PlanSelected(destination,origin,out items,out targets)&&Commit(items,destination,targets);
        }
        internal static bool PlanSelected(ItemGrid destination,Vector2Int origin,out List<InventoryItem> items,out List<Vector2Int> targets)
        {
            items=null;targets=null;
            Selection.RemoveWhere(i=>i==null);
            if(Selection.Count==0||destination==null||destination.GetComponent<Viewport>()==null)return false;
            items=Selection.OrderBy(i=>i.GetInstanceID()).ToList();var source=items[0].ContainingGrid;
            if(items.Any(i=>i.ContainingGrid!=source||i.isBeingDragged||i.paperdollSlot!=null))return false;
            int minX=items.Min(i=>i.gridPosition.x),minY=items.Min(i=>i.gridPosition.y);
            targets=items.Select(i=>origin+i.gridPosition-new Vector2Int(minX,minY)).ToList();
            var placements=new List<Placement>();int key=0;
            foreach(var existing in destination.AllItemsInGridOnly().Where(i=>!Selection.Contains(i)))
                placements.Add(new Placement{Key=key++,X=existing.gridPosition.x,Y=existing.gridPosition.y,Width=existing.InventorySize.x,Height=existing.InventorySize.y});
            var group=new List<Placement>();
            for(int i=0;i<items.Count;i++)group.Add(new Placement{Key=key+i,X=targets[i].x,Y=targets[i].y,Width=items[i].InventorySize.x,Height=items[i].InventorySize.y});
            int pageRows=destination.GetComponent<Viewport>().PageRows;
            if(Operations.Valid(destination.gridSize.x,destination.gridSize.y,placements.Concat(group),pageRows))return true;
            var packed=Operations.PackGroup(destination.gridSize.x,destination.gridSize.y,placements,group,Mathf.Clamp(origin.x,0,destination.gridSize.x-1),Mathf.Clamp(origin.y,0,destination.gridSize.y-1),pageRows);
            if(packed==null)return false;
            targets=packed.OrderBy(p=>p.Key).Select(p=>new Vector2Int(p.X,p.Y)).ToList();return true;
        }
        internal static bool Commit(List<InventoryItem> items,ItemGrid destination,List<Vector2Int> targets)
        {
            if(items.Count==0)return true;
            var origins=items.Select(i=>i.gridPosition).ToList();var sources=items.Select(i=>i.ContainingGrid).ToList();
            for(int n=0;n<items.Count;n++)
                if(sources[n]==null||sources[n].GetSlotAt(origins[n])==null||sources[n].GetSlotAt(origins[n]).ItemInSlot!=items[n])return false;
            try
            {
                for(int n=0;n<items.Count;n++)sources[n].UnregisterItemAt(items[n],origins[n].x,origins[n].y,items[n].InventorySize.x,items[n].InventorySize.y);
                for(int n=0;n<items.Count;n++)
                {
                    if(!destination.HasRoomFor(targets[n],items[n].InventorySize))throw new InvalidOperationException("Placement changed during transaction");
                    items[n].SetContainingGrid(destination);items[n].SetToNewGridPosition(targets[n]);
                    if(destination.GetSlotAt(targets[n]).ItemInSlot!=items[n])throw new InvalidOperationException("Placement rejected");
                }
                return true;
            }
            catch(Exception e)
            {
                try
                {
                    foreach(var item in items)if(item.ContainingGrid!=null)item.ContainingGrid.UnregisterItemAt(item,item.gridPosition.x,item.gridPosition.y,item.InventorySize.x,item.InventorySize.y);
                    for(int n=0;n<items.Count;n++){items[n].SetContainingGrid(sources[n]);items[n].SetToNewGridPosition(origins[n]);}
                    for(int n=0;n<items.Count;n++)
                        if(sources[n].GetSlotAt(origins[n]).ItemInSlot!=items[n])throw new InvalidOperationException("Rollback placement failed");
                }
                catch(Exception rollback){Plugin.Fail(rollback);}
                Plugin.Instance.Report("Inventory operation rolled back: "+e.Message);return false;
            }
        }
    }
}
