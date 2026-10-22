using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;
using PerfectRandom.Sulfur.Core.Units;
using PerfectRandom.Sulfur.Core.Weapons;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LocalSulfur.UnifiedInventory
{
    internal static class Stacks
    {
        internal static bool NativeSplitInput;
        private static InventoryItem splitDrag;
        private static Vector2 splitPointer;
        private static int splitReleaseFrame=-1;
        internal static bool UpdateSplitDrag()
        {
            if(splitDrag==null)return false;
            if(!splitDrag.isBeingDragged){splitDrag=null;return false;}
            var mouse=Mouse.current;var ui=StaticInstance<UIManager>.Instance.InventoryUI;
            if(mouse==null)return true;
            var point=mouse.position.ReadValue();
            var e=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=point,delta=point-splitPointer};splitPointer=point;
            NativeSplitInput=true;
            try
            {
                if(!ui.IsOpened||(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame))
                {splitDrag.DroppedOnInvalid();splitDrag=null;}
                else
                {
                    splitDrag.OnDrag(e);
                    if(mouse.rightButton.wasReleasedThisFrame){splitReleaseFrame=Time.frameCount;splitDrag.OnEndDrag(e);splitDrag=null;}
                }
            }
            finally{NativeSplitInput=false;}
            return true;
        }
        private static readonly MethodInfo destroy=AccessTools.Method(typeof(InventoryItem),"DestroyFromInventory");
        private static readonly MethodInfo report=AccessTools.Method(typeof(ItemGrid),"ReportContentsChanged");
        private static readonly MethodInfo prevented=AccessTools.PropertyGetter(typeof(InventoryItem),"ConsumePrevented");
        [ThreadStatic] private static InventoryItem applying;
        [ThreadStatic] private static int wholeConsumption;
        [ThreadStatic] private static int transaction;
        private sealed class PlacementAttempt
        {
            internal ItemGrid Grid;
            internal InventoryData Data;
            internal InventoryItem Created;
        }
        private static PlacementAttempt placement;
        internal static bool PlacementPending {get{return placement!=null;}}
        private static void CapturePlacement(InventoryItem __instance,ItemGrid containingGrid,InventoryData attachedData)
        {
            if(placement!=null&&placement.Grid==containingGrid&&ReferenceEquals(placement.Data,attachedData))
            {
                if(placement.Created!=null)throw new InvalidOperationException("Nested placement creation");
                placement.Created=__instance;
            }
        }
        internal static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(InventoryItem),"Setup",new[]{typeof(Vector2),typeof(ItemDefinition),typeof(ItemGrid),typeof(int),typeof(int),typeof(InventoryData),typeof(bool)}),prefix:new HarmonyMethod(typeof(Stacks),"CapturePlacement"),postfix:new HarmonyMethod(typeof(Stacks),"Setup"));
            h.Patch(AccessTools.Method(typeof(ItemGrid),"AddItem",new[]{typeof(ItemDefinition),typeof(bool),typeof(bool),typeof(InventoryData)}),prefix:new HarmonyMethod(typeof(Stacks),"Add"));
            foreach(string name in new[]{"Consume","GetConsumedByExternalFactor","OnPointerClick","DropOnOccupiedItemGridSlot"})
                h.Patch(AccessTools.Method(typeof(InventoryItem),name),prefix:new HarmonyMethod(typeof(Stacks),name));
            foreach(string name in new[]{"DropOnOccupiedItemGridSlot","DropOnOccupiedPaperDollSlot"})
                h.Patch(AccessTools.Method(typeof(InventoryItem),name),prefix:new HarmonyMethod(typeof(Stacks),"BeginApply"),finalizer:new HarmonyMethod(typeof(Stacks),"EndApply"));
            h.Patch(destroy,prefix:new HarmonyMethod(typeof(Stacks),"DestroyOneApplied"));
            h.Patch(AccessTools.Method(typeof(ThrowableWeapon),"AfterThrow"),prefix:new HarmonyMethod(typeof(Stacks),"Throw"));
            foreach(string name in new[]{"PriceBuy","PriceSell"})h.Patch(AccessTools.PropertyGetter(typeof(InventoryItem),name),postfix:new HarmonyMethod(typeof(Stacks),"Price"));
            h.Patch(AccessTools.Method(typeof(InventoryItem),"TransferOwnership"),prefix:new HarmonyMethod(typeof(Stacks),"BeginTransaction"),finalizer:new HarmonyMethod(typeof(Stacks),"EndTransaction"));
            foreach(string name in new[]{"BoughtForPrice","SoldForPrice"})h.Patch(AccessTools.PropertySetter(typeof(InventoryItem),name),prefix:new HarmonyMethod(typeof(Stacks),"StoreUnitPrice"));
            h.Patch(AccessTools.Method(typeof(ItemGrid),"ItemCount"),prefix:new HarmonyMethod(typeof(Stacks),"Count"));
            h.Patch(AccessTools.Method(typeof(ItemGrid),"ContainsItem"),prefix:new HarmonyMethod(typeof(Stacks),"Contains"));
            h.Patch(AccessTools.Method(typeof(ItemGrid),"GetItemOfType"),prefix:new HarmonyMethod(typeof(Stacks),"Find"));
            h.Patch(AccessTools.Method(typeof(ItemGrid),"RemoveItem"),prefix:new HarmonyMethod(typeof(Stacks),"Remove"));
            var craft=AccessTools.TypeByName("PerfectRandom.Sulfur.Gameplay.CraftingStation");
            if(craft==null)throw new TypeLoadException("CraftingStation");
            h.Patch(AccessTools.Method(craft,"DoCraft"),prefix:new HarmonyMethod(typeof(Stacks),"BeginCraft"),finalizer:new HarmonyMethod(typeof(Stacks),"EndCraft"));
        }
        internal static bool CanStack(ItemDefinition definition)
        {
            if(definition==null)return false;
            bool equipment=definition.slotType!=SlotType.None&&definition.slotType!=SlotType.Gadget;
            equipment|=definition.useType==UseType.Equippable&&definition.slotType!=SlotType.Gadget;
            var weapon=definition as WeaponSO;
            if(weapon!=null&&definition.slotType!=SlotType.Gadget)equipment=true;
            return Operations.CanStack(equipment,definition.automaticConsume,definition.HasDurability);
        }
        internal static void RefreshBadge(InventoryItem item)
        {
            if(item==null)return;
            (item.GetComponent<Badge>()??item.gameObject.AddComponent<Badge>()).Item=item;
            if(item.ContainingGrid!=null)report.Invoke(item.ContainingGrid,null);
        }
        private static void Setup(InventoryItem __instance,InventoryData attachedData)
        {if(attachedData!=null)__instance.quantity=Math.Max(1,attachedData.quantity);RefreshBadge(__instance);}
        private static void UnlockNewPortion(InventoryItem item)
        {
            var mark=item.GetComponent<ItemMark>();if(mark!=null)mark.Locked=false;
        }
        private static bool Compatible(InventoryItem left,InventoryData right,ItemDefinition definition,bool manual=false)
        {
            if(left==null||left.itemDefinition!=definition||!CanStack(definition)||left.isBeingDragged||(!manual&&ItemLocks.Locked(left))||left.SoldForPrice!=0)return false;
            var data=left.GetSerialized();
            if(!string.IsNullOrEmpty(data.identifier)||(right!=null&&!string.IsNullOrEmpty(right.identifier)))return false;
            if(right==null)return data.boughtFor==0&&data.currentAmmo==0&&(data.attachmentIds==null||data.attachmentIds.Length==0)&&(data.enchantmentIds==null||data.enchantmentIds.Length==0);
            return State(data)==State(right);
        }
        private static string State(InventoryData data)
        {
            var token=JObject.FromObject(data);
            foreach(string key in new[]{"position","quantity","xSize","ySize","rotated","equipped","selected","inventorySlot"})token.Remove(key);
            return token.ToString(Formatting.None);
        }
        private static bool Add(ItemGrid __instance,ItemDefinition itemSO,bool isPickup,bool announce,InventoryData attachedData,ref bool __result)
        {
            if(__instance.GetComponent<Viewport>()==null||!CanStack(itemSO)||!itemSO.AllowedInGame)return true;
            int quantity=attachedData==null?1:Math.Max(1,attachedData.quantity),max=Plugin.Instance.Settings.MaxStack.Value;
            var targets=__instance.AllItemsInGridOnly().Where(i=>Compatible(i,attachedData,itemSO,true)&&i.quantity<max).OrderByDescending(i=>ItemLocks.Locked(i)).ToList();
            long capacity=targets.Sum(i=>(long)Math.Max(0,max-i.quantity));
            if(capacity<quantity)return true;
            int remaining=quantity;
            foreach(var target in targets)
            {
                int n=Math.Min(remaining,max-target.quantity);target.quantity+=n;remaining-=n;RefreshBadge(target);if(remaining==0)break;
            }
            if(announce&&!itemSO.doNotAnnounce)__instance.PlayPickupNote(itemSO);
            if(__instance.IsOwnedByPlayer){StaticInstance<AchievementManager>.Instance.ItemPickedUp(itemSO);if(__instance.onItemPickup!=null)__instance.onItemPickup(itemSO,announce);}
            __result=true;return false;
        }
        private static bool Consume(InventoryItem __instance,Unit ___owner,Unit ___playerUnit)
        {
            if(!CanStack(__instance.itemDefinition)||__instance.quantity<=1)return true;
            __instance.itemDefinition.PlayUseSound();if((bool)prevented.Invoke(__instance,null))return false;
            ___owner.ConsumeItem(__instance.itemDefinition);__instance.quantity--;RefreshBadge(__instance);
            if(___playerUnit!=null&&___playerUnit.PlayerScript!=null)___playerUnit.PlayerScript.SetHoveredConsumable(null);return false;
        }
        private static bool GetConsumedByExternalFactor(InventoryItem __instance)
        {if(wholeConsumption>0||!CanStack(__instance.itemDefinition)||__instance.quantity<=1)return true;__instance.quantity--;RefreshBadge(__instance);return false;}
        private static void BeginCraft(){wholeConsumption++;}
        private static Exception EndCraft(Exception __exception){wholeConsumption--;return __exception;}
        private static bool Throw(ThrowableWeapon __instance)
        {
            var item=__instance.inventoryItem;
            if(item==null||!CanStack(item.itemDefinition)||item.quantity<=1||__instance.iAmmoCurrent>0)return true;
            item.quantity--;__instance.iAmmoCurrent=1;RefreshBadge(item);return false;
        }
        private static void Price(InventoryItem __instance,ref int __result)
        {if(CanStack(__instance.itemDefinition)&&__instance.quantity>1)__result=(int)Math.Min(int.MaxValue,(long)__result*__instance.quantity);}
        private static void BeginTransaction(){transaction++;}
        private static Exception EndTransaction(Exception __exception){transaction--;return __exception;}
        // Native transactions store their total. Keep per-unit history for later splitting and resale.
        private static void StoreUnitPrice(InventoryItem __instance,ref int value)
        {if(transaction>0&&CanStack(__instance.itemDefinition)&&__instance.quantity>1)value/=__instance.quantity;}
        private static void BeginApply(InventoryItem __instance,out InventoryItem __state){__state=applying;applying=__instance;}
        private static Exception EndApply(InventoryItem __state,Exception __exception){applying=__state;return __exception;}
        private static bool DestroyOneApplied(InventoryItem __instance)
        {
            if(applying!=__instance||!CanStack(__instance.itemDefinition)||__instance.quantity<=1)return true;
            __instance.quantity--;__instance.DroppedOnInvalid();RefreshBadge(__instance);return false;
        }
        private static bool DropOnOccupiedItemGridSlot(InventoryItem __instance,GridSlot slot)
        {
            var target=slot==null?null:slot.ItemInSlot;
            if(target==__instance||!__instance.OwnedByPlayer||target==null||!target.OwnedByPlayer||__instance.SoldForPrice!=0||!Compatible(target,__instance.GetSerialized(),__instance.itemDefinition,true))return true;
            int count=Operations.MergeAmount(__instance.quantity,target.quantity,Plugin.Instance.Settings.MaxStack.Value);
            if(count==0){__instance.DroppedOnInvalid();return false;}
            if(ItemLocks.Locked(__instance))(target.GetComponent<ItemMark>()??target.gameObject.AddComponent<ItemMark>()).Locked=true;
            target.quantity+=count;__instance.quantity-=count;RefreshBadge(target);
            if(__instance.quantity==0)destroy.Invoke(__instance,null);else{__instance.DroppedOnInvalid();RefreshBadge(__instance);}return false;
        }
        private static bool OnPointerClick(InventoryItem __instance,PointerEventData pointerEventData)
        {
            if(splitDrag!=null||splitReleaseFrame==Time.frameCount)return false;
            var k=Keyboard.current;
            if(Menu.Open||Menu.Selecting||Viewport.IsLayoutDragging)return false;
            if(pointerEventData.button!=PointerEventData.InputButton.Right||k==null||!Controls.SplitHeld)return true;
            var settings=Plugin.Instance.Settings;
            // Raw mouse-down is handled by Menu; swallow the later click to avoid a second split/use.
            return false;
        }
        internal static bool Split(InventoryItem source,bool half)
        {
            if(source==null){Plugin.Instance.Report("Split rejected: no item under cursor");return false;}
            if(source.isBeingDragged||source.paperdollSlot!=null||!CanStack(source.itemDefinition)){Plugin.Instance.Report("Split rejected: item cannot stack or is equipped/dragged: "+source.itemDefinition.id);return false;}
            int quantity=Operations.SplitAmount(source.quantity,half);if(quantity==0){Plugin.Instance.Report("Split rejected: quantity is less than two");return false;}
            var grid=source.ContainingGrid;if(grid==null||grid.GetComponent<Viewport>()==null)return false;
            var position=grid.GetPossibleSpace(source.InventorySize);if(position.x<0){Plugin.Instance.Report("Split rejected: no safe return slot available");return false;}
            // The native serializer returns a fresh InventoryData; only replace quantity and position.
            var data=source.GetSerialized();
            data.quantity=quantity;data.position=new[]{position.x,position.y};
            grid.AddItemToPosition(source.itemDefinition,position.x,position.y,data,data.rotated,true);
            var created=grid.GetSlotAt(position).ItemInSlot;
            if(created==null||created==source){Plugin.Fail(new InvalidOperationException("Split placement failed"));return false;}
            UnlockNewPortion(created);
            created.quantity=quantity;source.quantity-=quantity;RefreshBadge(source);RefreshBadge(created);
            Management.Selection.Clear();
            if(Mouse.current!=null&&EventSystem.current!=null)
            {
                splitPointer=Mouse.current.position.ReadValue();NativeSplitInput=true;
                try
                {
                    created.OnBeginDrag(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=splitPointer});
                    if(created.isBeingDragged){splitDrag=created;Plugin.Instance.Report("Split drag started: "+quantity+" units; remainder "+source.quantity);}
                    else Plugin.Instance.Report("Split created but native drag did not start");
                }
                finally{NativeSplitInput=false;}
            }
            return true;
        }
        internal static bool PlaceHeld(InventoryItem source,ItemGrid grid,Vector2Int position,bool half)
        {
            if(PlacementPending||Plugin.Failed)return false;
            if(source==null||!source.isBeingDragged||!source.OwnedByPlayer||!CanStack(source.itemDefinition)||grid==null)return false;
            bool managed=grid.GetComponent<Viewport>()!=null&&(grid.IsPlayerInventory||grid.IsPlayerStash);
            if(!managed&&!QuestPlacement.Accepts(grid))return false;
            var slot=grid.GetSlotAt(position);if(slot==null)return false;
            var target=slot.ItemInSlot;if(target==source)return false;
            int count=Operations.PourAmount(source.quantity,half);if(count==0)return false;
            if(target!=null)
            {
                if(!Compatible(target,source.GetSerialized(),source.itemDefinition,true))return false;
                count=Operations.MergeAmount(count,target.quantity,Plugin.Instance.Settings.MaxStack.Value);if(count==0)return false;
            }
            else
            {
                if(!grid.HasRoomFor(position,source.InventorySize))return false;
                count=Math.Min(count,Plugin.Instance.Settings.MaxStack.Value);
            }
            int beforeSource=source.quantity,beforeTarget=target==null?0:target.quantity;
            bool creating=target==null;
            var attempt=new PlacementAttempt{Grid=grid};
            if(creating)
            {
                attempt.Data=source.GetSerialized();attempt.Data.quantity=count;attempt.Data.position=new[]{position.x,position.y};
            }
            placement=attempt;
            try
            {
                return QuantityTransaction.Run(()=>
                {
                    if(creating)
                    {
                        grid.AddItemToPosition(source.itemDefinition,position.x,position.y,attempt.Data,attempt.Data.rotated,true);
                        target=attempt.Created;
                        if(target==null||grid.GetSlotAt(position).ItemInSlot!=target)throw new InvalidOperationException("Held placement registration failed");
                        UnlockNewPortion(target);
                    }
                    else target.quantity=beforeTarget+count;
                    source.quantity=beforeSource-count;
                    RefreshBadge(target);RefreshBadge(source);
                },()=>
                {
                    if(source==null||target==null||source.quantity!=beforeSource-count||target.quantity!=beforeTarget+count||grid.GetSlotAt(position).ItemInSlot!=target||!source.isBeingDragged)
                        throw new InvalidOperationException("Held placement state changed during callback");
                },()=>
                {
                    if(source==null||!source.isBeingDragged)throw new InvalidOperationException("Cannot restore held source");
                    source.quantity=beforeSource;
                    if(creating)
                    {
                        var created=attempt.Created;
                        if(created==null)throw new InvalidOperationException("Creation failed before its instance could be tracked; saving blocked");
                        if(created!=null)
                        {
                            // Setup may throw before registration. Remove only references to this exact new object.
                            var slots=(GridSlot[,])AccessTools.Field(typeof(ItemGrid),"slots").GetValue(grid);
                            foreach(var cell in slots)if(cell!=null&&cell.ItemInSlot==created)cell.ClearItem();
                            foreach(var cell in slots)if(cell!=null&&cell.ItemInSlot==created)throw new InvalidOperationException("Created item still registered");
                            created.quantity=0;created.gameObject.SetActive(false);UnityEngine.Object.Destroy(created.gameObject);
                        }
                        if(grid.GetSlotAt(position).ItemInSlot!=null)throw new InvalidOperationException("Placement slot changed during rollback");
                    }
                    else
                    {
                        if(target==null||grid.GetSlotAt(position).ItemInSlot!=target)throw new InvalidOperationException("Cannot restore target stack");
                        target.quantity=beforeTarget;
                    }
                    RefreshBadge(source);if(!creating)RefreshBadge(target);else report.Invoke(grid,null);
                    if(source.quantity!=beforeSource||(!creating&&target.quantity!=beforeTarget))throw new InvalidOperationException("Rollback quantity verification failed");
                },()=>
                {
                    if(source.quantity==0)
                    {
                        AccessTools.Method(typeof(InventoryItem),"GenericDragEnd").Invoke(source,null);
                        destroy.Invoke(source,null);
                    }
                },error=>Plugin.Instance.Report("Held placement cancelled and restored: "+error),Plugin.Fail);
            }
            finally { placement=null; }
        }
        internal static int DepositExisting(InventoryItem source,ItemGrid destination)
        {
            if(source==null||ItemLocks.Locked(source)||!CanStack(source.itemDefinition))return 0;
            int moved=0;var data=source.GetSerialized();
            foreach(var target in destination.AllItemsInGridOnly().OrderByDescending(i=>i!=null&&ItemLocks.Locked(i)).ToList())
            {
                if(target==source||!Compatible(target,data,source.itemDefinition,true))continue;
                int count=Operations.MergeAmount(source.quantity,target.quantity,Plugin.Instance.Settings.MaxStack.Value);
                if(count==0)continue;
                target.quantity+=count;source.quantity-=count;moved+=count;RefreshBadge(target);
                if(source.quantity==0){destroy.Invoke(source,null);return moved;}
            }
            if(moved>0)RefreshBadge(source);return moved;
        }
        internal static void MergeForSort(ItemGrid grid)
        {
            int max=Plugin.Instance.Settings.MaxStack.Value;if(max<=1)return;
            var items=grid.AllItemsInGridOnly().Where(i=>i!=null&&!i.isBeingDragged&&i.paperdollSlot==null&&CanStack(i.itemDefinition)&&i.SoldForPrice==0).OrderByDescending(i=>ItemLocks.Locked(i)).ToList();
            // Locked stacks receive first, but can never donate or lose their position.
            for(int i=0;i<items.Count;i++)
            {
                var target=items[i];if(target==null||target.quantity>=max)continue;
                for(int j=i+1;j<items.Count&&target.quantity<max;j++)
                {
                    var source=items[j];if(source==null||ItemLocks.Locked(source)||!Compatible(target,source.GetSerialized(),source.itemDefinition,true))continue;
                    int count=Operations.MergeAmount(source.quantity,target.quantity,max);if(count==0)continue;
                    target.quantity+=count;source.quantity-=count;
                    Management.Selection.Remove(source);
                    if(source.quantity==0){items[j]=null;destroy.Invoke(source,null);}else RefreshBadge(source);
                    RefreshBadge(target);
                }
            }
        }
        private static int Total(ItemGrid grid,ItemId id,bool equipment)
        {return (equipment?grid.AllItems():grid.AllItemsInGridOnly()).Where(i=>i!=null&&i.itemDefinition.id==id).Sum(i=>Math.Max(1,i.quantity));}
        private static bool Count(ItemGrid __instance,ItemId item,ref int __result){__result=Total(__instance,item,__instance.IsPlayerInventory);return false;}
        private static bool Contains(ItemGrid __instance,ItemId item,int amount,bool includePaperdoll,ref bool __result){__result=Total(__instance,item,includePaperdoll)>=amount;return false;}
        private static bool Find(ItemGrid __instance,ItemId itemId,bool includePaperdoll,List<InventoryItem> itemsToNotCount,ref InventoryItem __result)
        {
            __result=(includePaperdoll?__instance.AllItems():__instance.AllItemsInGridOnly()).FirstOrDefault(i=>i.itemDefinition.id==itemId&&Math.Max(1,i.quantity)>(itemsToNotCount==null?0:itemsToNotCount.Count(e=>e==i)));return false;
        }
        private static bool Remove(ItemGrid __instance,ItemDefinition itemToRemove,int amount)
        {
            if(amount<=0)return false;
            foreach(var item in __instance.AllItems().Where(i=>i.itemDefinition.id==itemToRemove.id).ToList())
            {
                int take=Math.Min(amount,Math.Max(1,item.quantity));amount-=take;
                if(take>=item.quantity)destroy.Invoke(item,null);else{item.quantity-=take;RefreshBadge(item);}if(amount==0)break;
            }
            return false;
        }
    }
    public sealed class Badge:MonoBehaviour
    {
        internal InventoryItem Item;
        private Text text;
        private LockGlow glow;
        private int lastQuantity=-1;
        private bool lastLocked,lastSelected;
        private static Font font;
        private void LateUpdate()
        {
            if(Item==null)return;var mark=GetComponent<ItemMark>();
            bool locked=mark!=null&&mark.Locked,selected=Management.Selection.Contains(Item);
            if(Item.quantity==lastQuantity&&locked==lastLocked&&selected==lastSelected)return;
            lastQuantity=Item.quantity;lastLocked=locked;lastSelected=selected;
            string value=Item.quantity>1?Item.quantity.ToString():"";
            if(locked&&glow==null)
            {
                var overlay=new GameObject("UnifiedLockGlow",typeof(RectTransform),typeof(CanvasRenderer),typeof(LockGlow));
                overlay.transform.SetParent(transform,false);
                glow=overlay.GetComponent<LockGlow>();glow.raycastTarget=false;
                glow.rectTransform.anchorMin=Vector2.zero;glow.rectTransform.anchorMax=Vector2.one;
                glow.rectTransform.offsetMin=glow.rectTransform.offsetMax=Vector2.zero;
                if(text!=null)text.transform.SetAsLastSibling();
            }
            if(glow!=null)glow.enabled=locked;
            if(text==null)
            {
                if(font==null)font=Font.CreateDynamicFontFromOSFont("Arial",16);
                var obj=new GameObject("UnifiedItemBadge",typeof(RectTransform),typeof(CanvasRenderer),typeof(Text));obj.transform.SetParent(transform,false);
                text=obj.GetComponent<Text>();text.font=font;text.fontSize=16;text.alignment=TextAnchor.LowerRight;text.raycastTarget=false;text.color=Color.white;
                text.rectTransform.anchorMin=Vector2.zero;text.rectTransform.anchorMax=Vector2.one;text.rectTransform.offsetMin=Vector2.zero;text.rectTransform.offsetMax=Vector2.zero;
            }
            text.text=value;
        }
    }
}
