using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using LocalSulfur.UnifiedInventory;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.DataStorage;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;
using PerfectRandom.Sulfur.Core.UI;
using PerfectRandom.Sulfur.Core.Units;
using PerfectRandom.Sulfur.Core.Weapons;
using PerfectRandom.Sulfur.Core.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace LocalSulfur.TraderFramework
{
    [BepInPlugin(Id,"Trader Framework","0.1.4")]
    [BepInDependency("local.sulfur.unifiedinventory","0.1.5")]
    [BepInIncompatibility("com.hemito.sulfur.improvedtraders")]
    public sealed class TraderPlugin:BaseUnityPlugin
    {
        public const string Id="local.sulfur.traderframework";
        private static TraderPlugin instance;
        private Harmony harmony;
        private static bool busy,stopped;
        private static readonly MethodInfo allowBuy=AccessTools.Method(typeof(InventoryItem),"UnitAllowedToBuy");
        private static readonly MethodInfo nativeBuy=AccessTools.Method(typeof(InventoryItem),"UseBuy");
        private static readonly MethodInfo setBought=AccessTools.PropertySetter(typeof(InventoryItem),"BoughtForPrice");
        private static readonly MethodInfo setSold=AccessTools.PropertySetter(typeof(InventoryItem),"SoldForPrice");
        private void Awake()
        {
            instance=this;
            try
            {
                if(!MerchantBridge.Ready)throw new InvalidOperationException("Inventory Redux is not ready");
                harmony=new Harmony(Id);
                TradePricing.Install(harmony);
                var generate=AccessTools.Method(typeof(ShopKeeper),"AddItems",new[]{typeof(List<ItemDefinition>)});
                if(generate==null||allowBuy==null||nativeBuy==null||setBought==null||setSold==null)throw new MissingMethodException("Merchant API contract changed");
                harmony.Patch(generate,prefix:new HarmonyMethod(typeof(TraderPlugin),"BeforeGenerate"),postfix:new HarmonyMethod(typeof(TraderPlugin),"AfterGenerate"),finalizer:new HarmonyMethod(typeof(TraderPlugin),"GenerationError"));
                harmony.Patch(AccessTools.Method(typeof(SulfurSaveState),"WriteToDisk"),prefix:new HarmonyMethod(typeof(SaveBoundary),"BeforeWrite"));
                MerchantBridge.PurchaseClick=Click;
                Logger.LogInfo("Trader Framework 0.1.4 ready: rollback failures warn without blocking saves. Ordinary stock x2 on NEW restock; equipment, attachments, enchantments and caliber tools excluded. Right=all, Ctrl+right=one, Alt+right=half.");
            }
            catch(Exception error){Stop(error);}
        }
        private void OnDestroy()
        {if(MerchantBridge.PurchaseClick==Click)MerchantBridge.PurchaseClick=null;if(harmony!=null)harmony.UnpatchSelf();}
        private static void Stop(Exception error)
        {stopped=true;MerchantBridge.SafetyStop(error);if(instance!=null)instance.Logger.LogError(error);}
        private void OnGUI()
        {if(stopped)GUI.Box(new Rect(10,55,880,45),"Trader Framework safety stop. Saving blocked; exit without saving and restore the backup.");}

        private sealed class Generation
        {
            internal readonly Dictionary<int,Queue<int>> Quantities=new Dictionary<int,Queue<int>>();
        }
        private static bool Equipment(ItemDefinition item)
        {return item is WeaponSO||(item.slotType!=SlotType.None&&item.slotType!=SlotType.Gadget)||(item.useType==UseType.Equippable&&item.slotType!=SlotType.Gadget);}
        private static bool ExcludedFromMultiplier(ItemDefinition item)
        {return item.IsAttachment||item.useType==UseType.Enchantment||item.appliesEnchantment.IsValid||item.IsCaliberModifier;}
        private static void BeforeGenerate(ref List<ItemDefinition> staticList,out Generation __state)
        {
            __state=null;if(stopped||!MerchantBridge.Ready)return;
            var state=new Generation();var list=new List<ItemDefinition>();
            // Selection stays native. Only pack the already-selected stock, before grid placement.
            foreach(var group in staticList.Where(i=>i!=null&&i.AllowedInGame&&(!i.unlocksCheckpointWhenBroughtToChurch||!PlayerProgress.GetCheckpointReached("ItemBroughtToChurch_"+i.name))).GroupBy(i=>i))
            {
                var item=group.Key;
                Queue<int> queue;
                if(!state.Quantities.TryGetValue(item.id.value,out queue))
                {queue=new Queue<int>();state.Quantities.Add(item.id.value,queue);}
                foreach(int n in StockRules.PackStock(group.Count(),Equipment(item),MerchantBridge.CanStack(item)&&!item.unlocksCheckpointWhenBroughtToChurch,MerchantBridge.StackLimit,ExcludedFromMultiplier(item)))
                {list.Add(item);queue.Enqueue(n);}
            }
            staticList=list;__state=state;
        }
        private static void AfterGenerate(ShopKeeper __instance,Generation __state)
        {
            if(__state==null)return;
            foreach(var item in __instance.Grid.AllItemsInGridOnly())
            {
                // Quality mods clone definition objects; stable IDs survive those runtime copies.
                item.quantity=StockRules.TakeStock(__state.Quantities,item.itemDefinition.id.value);MerchantBridge.Refresh(item);
            }
            if(__state.Quantities.Values.Any(q=>q.Count!=0))throw new InvalidOperationException("Merchant grid could not fit generated stock; partial stock must not be saved");
            instance.Logger.LogInfo("Stock generated: "+__instance.Npc.UnitIdentifier+", offers="+__instance.Grid.AllItemsInGridOnly().Count()+", units="+__instance.Grid.AllItemsInGridOnly().Sum(i=>(long)i.quantity));
        }
        private static Exception GenerationError(Exception __exception)
        {if(__exception!=null)Stop(__exception);return __exception;}

        private static bool Click(InventoryItem item,PointerEventData e)
        {
            if(e.button!=PointerEventData.InputButton.Right)return false;
            if(busy||stopped||!MerchantBridge.Ready)return true;
            var ui=StaticInstance<UIManager>.Instance.InventoryUI;
            var game=StaticInstance<GameManager>.Instance;
            if(!game.InputActions.Inventory.Get().enabled||ui.draggingItem!=null)return true;
            var key=Keyboard.current;if(key==null)return true;
            bool control=key.leftCtrlKey.isPressed||key.rightCtrlKey.isPressed,alt=key.leftAltKey.isPressed||key.rightAltKey.isPressed;
            if(!MerchantBridge.CanStack(item.itemDefinition)||item.itemDefinition.unlocksCheckpointWhenBroughtToChurch)
            {
                // Equipment and automatic consumption keep the original buy callbacks.
                busy=true;try{nativeBuy.Invoke(item,null);}catch(Exception error){instance.Logger.LogError(error);}finally{busy=false;}
                return true;
            }
            busy=true;
            try{Buy(item,StockRules.BuyCount(item.quantity,control,alt));}
            catch(Exception error){instance.Logger.LogError("Purchase rejected before transaction: "+error);}
            finally{busy=false;}
            return true;
        }
        private static void Buy(InventoryItem source,int count)
        {
            var game=StaticInstance<GameManager>.Instance;var ui=StaticInstance<UIManager>.Instance.InventoryUI;
            var player=game.PlayerUnit;var owner=source.Owner;var grid=source.ContainingGrid;var shop=ui.CurrentShopKeeper.Shop;
            int original=source.quantity,unit=MerchantBridge.UnitBuyPrice(source),price;
            if(count<1||count>original||!StockRules.TryPrice(unit,count,out price)||(long)player.Stats.GetCoins(game.InSafeZone)<price||!(bool)allowBuy.Invoke(source,new object[]{player}))return;
            var data=source.GetSerialized();data.quantity=count;
            var receipt=MerchantBridge.Receipt.Plan(ui.PlayerBackpackGrid,source.itemDefinition,data,count);
            if(receipt==null){instance.Logger.LogInfo("Purchase rejected: insufficient compatible backpack space");return;}
            var save=SulfurSaveState.active;
            ShopKeeper.ShopKeeperInventoryData stock;
            if(!save.SHOPKEEPER_INVENTORY.TryGetValue(shop.Npc.UnitIdentifier,out stock))throw new InvalidOperationException("Missing saved merchant stock");
            bool hidden=(bool)AccessTools.Field(typeof(ShopKeeper),"hiddenActivated").GetValue(shop);
            var oldItems=stock.items;var oldHidden=stock.itemsHidden;var oldInventory=save.INVENTORY;var oldResources=save.WORLD_RESOURCES;var oldStash=save.WORLD_RESOURCES_STASH;
            var playerMoney=new Money(player.Stats);var merchantMoney=new Money(owner.Stats);
            long coins=(long)player.Stats.GetCoins()+player.Stats.GetCoinsStash();
            int bought=source.BoughtForPrice,sold=source.SoldForPrice;var position=source.gridPosition;
            bool removed=false,committed=false;
            var boundary=new SaveBoundary(save);
            try
            {
                source.quantity=count;
                if(source.PriceBuy!=price)throw new InvalidOperationException("Merchant quote changed");
                source.TransferOwnership(player);
                source.SetOwner(owner);setBought.Invoke(source,new object[]{bought});setSold.Invoke(source,new object[]{sold});source.quantity=original-count;
                if(source.quantity==0){removed=true;grid.UnregisterItemAt(position.x,position.y);source.gameObject.SetActive(false);}
                receipt.Apply();receipt.Refresh();
                if(source.quantity>0)MerchantBridge.Refresh(source);
                receipt.Verify();
                if(source.Owner!=owner||source.quantity!=original-count||(source.quantity>0&&grid.GetSlotAt(position).ItemInSlot!=source)||coins-((long)player.Stats.GetCoins()+player.Stats.GetCoinsStash())!=price)throw new InvalidOperationException("Purchase conservation check failed");
                if(hidden)stock.itemsHidden=grid.GetSerializedContents();else stock.items=grid.GetSerializedContents();
                game.SaveInventoryData();
                committed=true;
            }
            catch(Exception error)
            {
                try
                {
                    receipt.Rollback();
                    if(source==null)throw new InvalidOperationException("Merchant source destroyed during transaction");
                    source.SetOwner(owner);source.quantity=original;setBought.Invoke(source,new object[]{bought});setSold.Invoke(source,new object[]{sold});
                    if(removed){source.gameObject.SetActive(true);if(!grid.RegisterItemAt(source,position.x,position.y))throw new InvalidOperationException("Cannot restore merchant stock");}
                    playerMoney.Restore();merchantMoney.Restore();
                    stock.items=oldItems;stock.itemsHidden=oldHidden;save.INVENTORY=oldInventory;save.WORLD_RESOURCES=oldResources;save.WORLD_RESOURCES_STASH=oldStash;
                    MerchantBridge.Refresh(source);
                    ui.UpdateResourceDisplays();
                    instance.Logger.LogWarning("Purchase rolled back: "+error);
                }
                catch(Exception rollback){MerchantBridge.ReportTransactionFailure(new AggregateException("Purchase rollback failed",error,rollback));}
            }
            finally{boundary.End(committed);}
            if(!committed)return;
            // Destruction is deferred until the complete inventory and currency state is committed.
            if(removed){try{AccessTools.Method(typeof(InventoryItem),"HideDescription").Invoke(source,null);}finally{UnityEngine.Object.Destroy(source.gameObject);}}
            instance.Logger.LogInfo("Purchased "+count+" x "+data.id+" for "+price+"; merchant remainder="+(original-count));
        }
        private sealed class Money
        {
            private readonly EntityStats stats;private readonly int[] carried,stash;
            private readonly ActiveStatsList<int> active=new ActiveStatsList<int>(),activeStash=new ActiveStatsList<int>();
            internal Money(EntityStats stats)
            {this.stats=stats;carried=(int[])stats.worldResources.Clone();stash=(int[])stats.worldResourcesStash.Clone();active.Copy(stats.activeWorldResources);activeStash.Copy(stats.activeWorldResourcesStash);}
            internal void Restore()
            {Array.Copy(carried,stats.worldResources,carried.Length);Array.Copy(stash,stats.worldResourcesStash,stash.Length);stats.activeWorldResources.Copy(active);stats.activeWorldResourcesStash.Copy(activeStash);}
        }
        private sealed class SaveBoundary
        {
            private static SaveBoundary current;
            private readonly SulfurSaveState save;
            private readonly List<Action<SaveResults>> callbacks=new List<Action<SaveResults>>();
            internal SaveBoundary(SulfurSaveState save)
            {if(current!=null)throw new InvalidOperationException("Nested purchase save boundary");this.save=save;current=this;}
            private static bool BeforeWrite(int slot,SulfurSaveState data,Action<SaveResults> onCompleted)
            {
                if(stopped){if(onCompleted!=null)onCompleted(SaveResults.UNKNOWN_ERROR);return false;}
                if(current==null||!ReferenceEquals(data,current.save)||slot!=current.save.slotIndex)return true;
                if(onCompleted!=null)current.callbacks.Add(onCompleted);return false;
            }
            internal void End(bool commit)
            {
                current=null;
                Action<SaveResults> complete=result=>{foreach(var callback in callbacks)try{callback(result);}catch(Exception error){instance.Logger.LogError(error);}};
                if(!commit){complete(SaveResults.UNKNOWN_ERROR);return;}
                try{SulfurSaveState.WriteToDisk(save.slotIndex,save,result=>{if(result!=SaveResults.OK)instance.Logger.LogError("Purchase is valid in memory but save failed: "+result);complete(result);});}
                catch(Exception error){instance.Logger.LogError("Purchase completed; disk write failed: "+error);complete(SaveResults.UNKNOWN_ERROR);}
            }
        }
    }
}
