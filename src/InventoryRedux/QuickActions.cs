using System.Collections.Generic;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalSulfur.UnifiedInventory
{
    internal static class QuickActions
    {
        private static readonly HashSet<int> visited=new HashSet<int>();
        private static int sweep,blockedFrame=-1;
        private static ItemGrid source;
        private static float nextPlace;
        private static bool halfGesture;
        private static bool placing;
        internal static bool NativeAction;
        internal static bool BlocksClick {get{return sweep!=0||blockedFrame==Time.frameCount;}}
        internal static void Cancel(){sweep=0;source=null;visited.Clear();}
        internal static bool Update()
        {
            var mouse=Mouse.current;var game=StaticInstance<GameManager>.Instance;var manager=StaticInstance<UIManager>.Instance;
            if(mouse==null||game==null||manager==null||manager.InventoryUI==null)return false;
            var ui=manager.InventoryUI;
            if(Menu.Open||!ui.IsOpened||!game.InputActions.Inventory.Get().enabled||Plugin.Failed){Cancel();return false;}
            if(placing)
            {
                blockedFrame=Time.frameCount;
                if(!mouse.rightButton.isPressed)placing=false;
                else if(ui.draggingItem==null)return true;
            }
            if(ui.draggingItem!=null)
            {
                Cancel();
                // Only the ordinary left-button drag participates, not right-button split dragging.
                if(!mouse.leftButton.isPressed)return false;
                if(mouse.rightButton.wasPressedThisFrame){placing=true;halfGesture=Controls.Held(Modifier.Alt);nextPlace=0;}
                if(!mouse.rightButton.isPressed)return false;
                blockedFrame=Time.frameCount;
                if(halfGesture&&!mouse.rightButton.wasPressedThisFrame)return false;
                if(!halfGesture&&Time.unscaledTime<nextPlace)return false;
                var grid=Menu.Hovered()??QuestPlacement.Hovered(mouse.position.ReadValue());
                if(grid!=null)Stacks.PlaceHeld(ui.draggingItem,grid,grid.GetGridPosition(mouse.position.ReadValue()),halfGesture);
                nextPlace=Time.unscaledTime+(mouse.rightButton.wasPressedThisFrame?.35f:.12f);
                return ui.draggingItem==null;
            }
            if(Menu.GroupDragging||Menu.Selecting){Cancel();return false;}
            if(sweep!=0)
            {
                blockedFrame=Time.frameCount;
                if((sweep==1&&!mouse.rightButton.isPressed)||(sweep==2&&(!mouse.leftButton.isPressed||!Controls.Held(Modifier.Control)))){Cancel();return true;}
            }
            var hovered=Menu.Hovered();
            if(sweep==0&&hovered!=null)
            {
                if(mouse.rightButton.wasPressedThisFrame&&!Controls.SplitHeld&&ui.InStashUI)sweep=1;
                else if(mouse.leftButton.wasPressedThisFrame&&Controls.Held(Modifier.Control)&&!Controls.LayoutHeld)sweep=2;
                if(sweep!=0){source=hovered;visited.Clear();blockedFrame=Time.frameCount;Management.Selection.Clear();}
            }
            if(sweep==0)return false;
            // A gesture stays in its starting inventory so deposited items cannot bounce back.
            if(hovered!=source)return true;
            var slot=source.GetSlotAt(source.GetGridPosition(mouse.position.ReadValue()));var item=slot==null?null:slot.ItemInSlot;
            if(item==null||ItemLocks.Locked(item)||item.paperdollSlot!=null||item.isBeingDragged||!visited.Add(item.GetInstanceID()))return true;
            NativeAction=true;
            try
            {
                if(sweep==1&&ui.InStashUI)item.Use();
                else if(sweep==2&&item.OwnedByPlayer)item.DropFromPlayer();
            }
            finally {NativeAction=false;}
            return true;
        }
    }
}
