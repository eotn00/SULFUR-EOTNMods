using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.UI;
using PerfectRandom.Sulfur.Core.UI.Inventory;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace LocalSulfur.UnifiedInventory
{
    public sealed class HudLayout:MonoBehaviour
    {
        private static readonly List<HudLayout> live=new List<HudLayout>();
        private static HudLayout dragging;
        private static Vector2 dragPoint;
        private Vector3 dragOffset;
        private int kind;
        private Vector3 applied;
        private Vector3 basePosition;
        private bool positioned;
        private UnityEngine.UI.Graphic[] graphics;
        private RectTransform holder;
        private CanvasGroup visibility;
        internal static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(ResourceDisplay),"SetupDisplays"),postfix:new HarmonyMethod(typeof(HudLayout),"Resources"));
            h.Patch(AccessTools.Method(typeof(InventoryUI),"SetupBagSpaceGrid"),postfix:new HarmonyMethod(typeof(HudLayout),"Hints"));
            h.Patch(AccessTools.Method(typeof(InventoryUI),"SetState"),postfix:new HarmonyMethod(typeof(HudLayout),"SyncVisibility"));
            h.Patch(AccessTools.Method(typeof(ServiceStation),"SetDividerPosition"),postfix:new HarmonyMethod(typeof(HudLayout),"HideStashDivider"));
        }
        private static void HideStashDivider(ServiceStation __instance,RectTransform ___divider)
        {
            if(__instance is PlayerStash&&__instance.Grid!=null&&__instance.Grid.GetComponent<Viewport>()!=null&&___divider!=null)
                ___divider.gameObject.SetActive(false);
        }
        private static void Attach(Transform target,int kind)
        {
            if(target==null||!(target is RectTransform))return;
            var component=target.GetComponent<HudLayout>()??target.gameObject.AddComponent<HudLayout>();component.kind=kind;
            component.graphics=target.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            if(component.holder==null)
            {
                var ui=StaticInstance<UIManager>.Instance.InventoryUI;
                var obj=new GameObject("UnifiedHudPosition"+kind,typeof(RectTransform),typeof(UnityEngine.UI.LayoutElement),typeof(CanvasGroup));
                component.visibility=obj.GetComponent<CanvasGroup>();
                component.visibility.alpha=0;component.visibility.interactable=false;component.visibility.blocksRaycasts=false;
                component.holder=obj.GetComponent<RectTransform>();component.holder.SetParent(ui.transform,false);
                obj.GetComponent<UnityEngine.UI.LayoutElement>().ignoreLayout=true;
                component.holder.anchorMin=Vector2.zero;component.holder.anchorMax=Vector2.one;
                component.holder.offsetMin=component.holder.offsetMax=Vector2.zero;
                target.SetParent(component.holder,true);
                component.basePosition=target.localPosition;component.positioned=true;
            }
            if(!live.Contains(component))live.Add(component);
            SyncVisibility();
        }
        internal static void SyncVisibility()
        {
            var manager=StaticInstance<UIManager>.Instance;
            bool shown=manager!=null&&manager.InventoryUI!=null&&manager.InventoryUI.IsOpened;
            foreach(var entry in live)
            {
                if(entry==null||entry.visibility==null)continue;
                // Keep the component running so closing the inventory cannot prevent reopening it.
                entry.visibility.alpha=shown?1:0;
                entry.visibility.interactable=shown;entry.visibility.blocksRaycasts=shown;
            }
            if(!shown)EndDrag();
        }
        internal static bool Captures(Vector2 screen)
        {return dragging!=null||(Mouse.current!=null&&Mouse.current.leftButton.isPressed&&Controls.Held(Modifier.Alt)&&Find(screen)!=null);}
        private static HudLayout Find(Vector2 screen)
        {
            var manager=StaticInstance<UIManager>.Instance;
            if(manager==null||manager.InventoryUI==null||!manager.InventoryUI.IsOpened)return null;
            for(int i=live.Count-1;i>=0;i--)
            {
                var entry=live[i];if(entry==null||!entry.isActiveAndEnabled)continue;
                if(entry.graphics==null)continue;
                foreach(var graphic in entry.graphics)
                    if(graphic!=null&&graphic.isActiveAndEnabled&&RectTransformUtility.RectangleContainsScreenPoint(graphic.rectTransform,screen,null))return entry;
            }
            return null;
        }
        internal static bool DragInput()
        {
            var mouse=Mouse.current;if(mouse==null)return false;Vector2 screen=mouse.position.ReadValue();
            if(dragging==null&&Controls.Held(Modifier.Alt)&&mouse.leftButton.wasPressedThisFrame)
            {
                dragging=Find(screen);
                if(dragging!=null){dragging.dragOffset=Vector3.zero;RectTransformUtility.ScreenPointToLocalPointInRectangle(dragging.transform.parent as RectTransform,screen,null,out dragPoint);}
            }
            if(dragging==null)return false;
            if(!mouse.leftButton.isPressed||!Controls.Held(Modifier.Alt)){EndDrag();return true;}
            Vector2 point;RectTransformUtility.ScreenPointToLocalPointInRectangle(dragging.transform.parent as RectTransform,screen,null,out point);
            dragging.dragOffset=point-dragPoint;return true;
        }
        internal static void EndDrag()
        {
            if(dragging==null)return;var s=Plugin.Instance.Settings;
            s.HudX[dragging.kind].Value+=dragging.dragOffset.x;s.HudY[dragging.kind].Value+=dragging.dragOffset.y;
            dragging.dragOffset=Vector3.zero;dragging=null;
        }
        private void OnDestroy(){if(dragging==this)EndDrag();live.Remove(this);}
        private static void Resources(ResourceDisplay __instance)
        {
            if((StoragePlaceType)AccessTools.Field(typeof(ResourceDisplay),"storagePlace").GetValue(__instance)!=StoragePlaceType.Player)return;
            Attach((Transform)AccessTools.Field(typeof(ResourceDisplay),"AmmoTransform").GetValue(__instance),1);
            Attach((Transform)AccessTools.Field(typeof(ResourceDisplay),"CurrencyTransform").GetValue(__instance),2);
        }
        private static void Hints(InventoryUI __instance)
        {
            foreach(string field in new[]{"LegendKeyboard","LegendGamepad"})
            {var obj=(GameObject)AccessTools.Field(typeof(InventoryUI),field).GetValue(__instance);if(obj!=null)Attach(obj.transform,0);}
        }
        private void LateUpdate()
        {
            var manager=StaticInstance<UIManager>.Instance;
            if(manager==null||manager.InventoryUI==null||!manager.InventoryUI.IsOpened)return;
            var s=Plugin.Instance.Settings;
            var next=new Vector3(s.HudX[kind].Value,s.HudY[kind].Value,0)+dragOffset;
            if(!positioned){basePosition=transform.localPosition;positioned=true;}
            transform.localPosition=basePosition+next;applied=next;
            KeepVisible();
        }
        private void KeepVisible()
        {
            if(graphics==null)return;
            bool found=false;Bounds bounds=new Bounds();var corners=new Vector3[4];
            foreach(var graphic in graphics)
            {
                if(graphic==null||!graphic.isActiveAndEnabled)continue;
                graphic.rectTransform.GetWorldCorners(corners);
                if(!found){bounds=new Bounds(corners[0],Vector3.zero);found=true;}
                foreach(var corner in corners)bounds.Encapsulate(corner);
            }
            if(!found)return;
            float dx=bounds.size.x>Screen.width-16?0:bounds.min.x<8?8-bounds.min.x:bounds.max.x>Screen.width-8?Screen.width-8-bounds.max.x:0;
            float dy=bounds.size.y>Screen.height-16?0:bounds.min.y<8?8-bounds.min.y:bounds.max.y>Screen.height-8?Screen.height-8-bounds.max.y:0;
            if(dx==0&&dy==0)return;
            var delta=transform.parent.InverseTransformVector(new Vector3(dx,dy,0));
            basePosition+=delta;transform.localPosition+=delta;
        }
    }
}
