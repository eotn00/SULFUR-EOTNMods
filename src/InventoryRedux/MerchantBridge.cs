using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LocalSulfur.UnifiedInventory
{
    // Optional integration: Inventory Redux has no dependency on the merchant plugin.
    public static class MerchantBridge
    {
        public static Func<InventoryItem,PointerEventData,bool> PurchaseClick;
        [ThreadStatic] internal static bool Quoting;
        [ThreadStatic] private static Receipt creating;
        public static bool Ready {get{return Plugin.Instance!=null&&Plugin.Instance.Settings!=null&&!Plugin.Failed;}}
        public static int StackLimit {get{return Plugin.Instance.Settings.MaxStack.Value;}}
        public static bool CanStack(ItemDefinition definition){return Stacks.CanStack(definition);}
        public static bool Compatible(InventoryItem item,InventoryData data,ItemDefinition definition)
        {return Stacks.Compatible(item,data,definition,true);}
        public static void Refresh(InventoryItem item){Stacks.RefreshBadge(item);}
        public static void SafetyStop(Exception error){Plugin.Fail(error);}
        public static void ReportTransactionFailure(Exception error){Plugin.TransactionFailed(error);}
        public static int UnitBuyPrice(InventoryItem item)
        {bool old=Quoting;Quoting=true;try{return item.PriceBuy;}finally{Quoting=old;}}
        public static bool IsMerchantItem(InventoryItem item)
        {
            var manager=StaticInstance<UIManager>.Instance;
            var ui=manager==null?null:manager.InventoryUI;
            return PurchaseClick!=null&&item!=null&&!item.OwnedByPlayer&&ui!=null&&ui.IsOpened&&ui.draggingItem==null&&
                ui.CurrentShopKeeper!=null&&ui.CurrentShopKeeper.Shop!=null&&item.Owner==ui.CurrentShopKeeper&&item.ContainingGrid==ui.CurrentShopKeeper.Shop.Grid;
        }
        internal static bool HandleClick(InventoryItem item,PointerEventData data)
        {return IsMerchantItem(item)&&PurchaseClick(item,data);}
        internal static void Capture(InventoryItem item,ItemGrid grid,InventoryData data)
        {
            if(creating!=null&&creating.Grid==grid&&ReferenceEquals(creating.PendingData,data))creating.Created.Add(item);
        }
        public sealed class Receipt
        {
            internal ItemGrid Grid;
            internal InventoryData PendingData;
            internal readonly List<InventoryItem> Created=new List<InventoryItem>();
            private readonly List<InventoryItem> targets=new List<InventoryItem>();
            private readonly List<int> before=new List<int>(),amounts=new List<int>();
            private readonly List<Vector2Int> positions=new List<Vector2Int>();
            private readonly List<int> chunks=new List<int>();
            private readonly List<InventoryData> copies=new List<InventoryData>();
            private ItemDefinition definition;
            private InventoryData data;
            private bool applied;
            private int touched;
            public static Receipt Plan(ItemGrid grid,ItemDefinition definition,InventoryData data,int count)
            {
                if(!Ready||grid==null||definition==null||data==null||count<1||!CanStack(definition))return null;
                var r=new Receipt{Grid=grid,definition=definition,data=data};
                int remaining=count,max=StackLimit;
                foreach(var target in grid.AllItemsInGridOnly().Where(i=>Compatible(i,data,definition)).OrderByDescending(i=>ItemLocks.Locked(i)))
                {
                    int n=Math.Min(remaining,Math.Max(0,max-target.quantity));if(n==0)continue;
                    r.targets.Add(target);r.before.Add(target.quantity);r.amounts.Add(n);remaining-=n;if(remaining==0)break;
                }
                var size=new Vector2Int(data.xSize,data.ySize);
                if(size.x<1||size.y<1)return null;
                var reserved=new HashSet<Vector2Int>();
                var view=grid.GetComponent<Viewport>();
                var rowOrder=PurchasePlacementRows.Ordered(grid.gridSize.y,size.y,
                    view==null?0:view.First,view==null?grid.gridSize.y:view.Visible,
                    view==null?0:view.PageBottom,view==null?grid.gridSize.y:view.PageRows).ToArray();
                while(remaining>0)
                {
                    bool found=false;
                    foreach(int y in rowOrder)
                    {
                        if(found)break;
                        for(int x=0;x<grid.gridSize.x&&!found;x++)
                        {
                        var p=new Vector2Int(x,y);if(!grid.HasRoomFor(p,size))continue;
                        bool free=true;
                        for(int dy=0;dy<size.y;dy++)for(int dx=0;dx<size.x;dx++)if(reserved.Contains(p+new Vector2Int(dx,dy)))free=false;
                        if(!free)continue;
                        for(int dy=0;dy<size.y;dy++)for(int dx=0;dx<size.x;dx++)reserved.Add(p+new Vector2Int(dx,dy));
                        int n=Math.Min(max,remaining);r.positions.Add(p);r.chunks.Add(n);remaining-=n;found=true;
                        }
                    }
                    if(!found)return null;
                }
                // Complete data copying before any native payment callbacks can run.
                for(int i=0;i<r.positions.Count;i++)
                {
                    var copy=InventoryDataCopy.Create(data);copy.quantity=r.chunks[i];copy.equipped=false;copy.selected=false;
                    copy.position=new[]{r.positions[i].x,r.positions[i].y};r.copies.Add(copy);
                }
                return r;
            }
            public void Apply()
            {
                if(applied||creating!=null)throw new InvalidOperationException("Nested or reused merchant receipt");
                applied=true;
                for(int i=0;i<targets.Count;i++)
                {
                    if(targets[i]==null||targets[i].quantity!=before[i]||!Compatible(targets[i],data,definition))throw new InvalidOperationException("Receipt target changed");
                    targets[i].quantity=checked(before[i]+amounts[i]);
                    touched++;
                }
                creating=this;
                try
                {
                    for(int i=0;i<positions.Count;i++)
                    {
                        PendingData=copies[i];
                        int n=Created.Count;
                        Grid.AddItemToPosition(definition,positions[i].x,positions[i].y,PendingData,PendingData.rotated,true);
                        if(Created.Count!=n+1||Grid.GetSlotAt(positions[i]).ItemInSlot!=Created[n]||Created[n].quantity!=chunks[i])throw new InvalidOperationException("Receipt creation failed");
                        var mark=Created[n].GetComponent<ItemMark>();if(mark!=null)mark.Locked=false;
                    }
                }
                finally{PendingData=null;creating=null;}
                Verify();
            }
            public void Verify()
            {
                if(Created.Count!=positions.Count)throw new InvalidOperationException("Receipt item count changed");
                for(int i=0;i<targets.Count;i++)if(targets[i]==null||targets[i].quantity!=before[i]+amounts[i]||targets[i].ContainingGrid!=Grid||Grid.GetSlotAt(targets[i].gridPosition).ItemInSlot!=targets[i])throw new InvalidOperationException("Receipt quantity changed");
                for(int i=0;i<Created.Count;i++)if(Created[i]==null||Created[i].quantity!=chunks[i]||Grid.GetSlotAt(positions[i]).ItemInSlot!=Created[i])throw new InvalidOperationException("Receipt item changed");
            }
            public void Rollback()
            {
                if(!applied)return;
                var slots=(GridSlot[,])AccessTools.Field(typeof(ItemGrid),"slots").GetValue(Grid);
                foreach(var item in Created)
                {
                    if(item==null)throw new InvalidOperationException("Cannot restore destroyed receipt item");
                    foreach(var slot in slots)if(slot!=null&&slot.ItemInSlot==item)slot.ClearItem();
                    item.quantity=0;item.gameObject.SetActive(false);UnityEngine.Object.Destroy(item.gameObject);
                }
                for(int i=0;i<touched;i++)
                {if(targets[i]==null||targets[i].ContainingGrid!=Grid||Grid.GetSlotAt(targets[i].gridPosition).ItemInSlot!=targets[i])throw new InvalidOperationException("Cannot restore receipt target");targets[i].quantity=before[i];}
            }
            public void Refresh()
            {foreach(var item in targets)Stacks.RefreshBadge(item);foreach(var item in Created)if(item!=null&&item.quantity>0)Stacks.RefreshBadge(item);}
        }
    }
}
