using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;

namespace LocalSulfur.UnifiedInventory
{
    internal static class InventoryRaycasts
    {
        internal static void Install(Harmony h)
        {h.Patch(AccessTools.Method(typeof(EventSystem),"RaycastAll"),postfix:new HarmonyMethod(typeof(InventoryRaycasts),"Filter"));}
        private static void Filter(PointerEventData eventData,List<RaycastResult> raycastResults)
        {
            var manager=StaticInstance<UIManager>.Instance;
            var game=StaticInstance<GameManager>.Instance;
            if(Menu.Open||manager==null||manager.InventoryUI==null||!manager.InventoryUI.IsOpened||game==null||!game.InputActions.Inventory.Get().enabled)return;
            bool overGrid=false;
            foreach(var view in Viewport.Live)if(view!=null&&view.Contains(eventData.position)){overGrid=true;break;}
            if(!overGrid)
                foreach(var hit in raycastResults)
                {
                    var item=hit.gameObject==null?null:hit.gameObject.GetComponentInParent<InventoryItem>();
                    if(item!=null&&item.paperdollSlot!=null){overGrid=true;break;}
                }
            if(!overGrid)return;
            for(int i=raycastResults.Count-1;i>=0;i--)
            {
                var obj=raycastResults[i].gameObject;if(obj==null)continue;
                var equipment=obj.GetComponentInParent<InventoryItem>();
                // Equipped items do not belong to the backpack's scrolling rectangle.
                if(equipment!=null&&equipment.paperdollSlot!=null)continue;
                bool surface=false;
                foreach(var view in Viewport.Live)if(view!=null&&view.OwnsSurface(obj)){surface=true;if(!view.Contains(eventData.position))raycastResults.RemoveAt(i);break;}
                if(surface)continue;
                var grid=obj.GetComponentInParent<ItemGrid>();
                if(grid!=null)
                {
                    var view=grid.GetComponent<Viewport>();
                    if(view!=null&&!view.Contains(eventData.position))raycastResults.RemoveAt(i);
                    continue;
                }
                // Preserve buttons, paperdoll items and external modal windows. Ignore only
                // decorative inventory graphics that cover a moved grid's actual bounds.
                if(obj.transform.IsChildOf(manager.InventoryUI.transform)&&obj.GetComponentInParent<InventoryItem>()==null&&obj.GetComponentInParent<Selectable>()==null&&obj.GetComponentInParent<Viewport>()==null&&obj.GetComponent<Graphic>()!=null)
                    raycastResults.RemoveAt(i);
            }
        }
    }
}
