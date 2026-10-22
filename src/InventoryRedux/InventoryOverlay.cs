using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;

namespace LocalSulfur.UnifiedInventory
{
    internal static class InventoryOverlay
    {
        private static Viewport scrolling;
        private static float grab;
        private static bool moving;
        private static Vector2 dragStart,toolbarStart,toolbarPosition;
        private static Font font;
        private static GUIStyle button,label;
        private static string status="";
        private static float statusUntil;
        private static ItemGrid Stash()
        {
            var manager=StaticInstance<UIManager>.Instance;
            if(manager==null||manager.InventoryUI==null||!manager.InventoryUI.IsOpened)return null;
            var grid=manager.InventoryUI.ServiceStationGrid;
            return grid!=null&&grid.isActiveAndEnabled&&Plugin.Stashes.ContainsKey(grid)?grid:null;
        }
        private static bool Active()
        {var m=StaticInstance<UIManager>.Instance;var g=StaticInstance<GameManager>.Instance;return !Menu.Open&&m!=null&&m.InventoryUI!=null&&m.InventoryUI.IsOpened&&g!=null&&g.InputActions.Inventory.Get().enabled;}
        private static List<Rect> TooltipBounds()
        {
            var result=new List<Rect>();var ui=StaticInstance<UIManager>.Instance.InventoryUI;
            foreach(var tooltip in new[]{ui.itemDescription,ui.secondaryItemDescription})
            {
                if(tooltip==null||!tooltip.gameObject.activeInHierarchy)continue;
                var rect=tooltip.transform as RectTransform;if(rect==null)continue;
                var c=new Vector3[4];rect.GetWorldCorners(c);
                result.Add(Rect.MinMaxRect(c[0].x,Screen.height-c[2].y,c[2].x,Screen.height-c[0].y));
            }
            return result;
        }
        private static bool TooltipAt(Vector2 point)
        {foreach(var rect in TooltipBounds())if(rect.Contains(point))return true;return false;}
        private static bool Covered(Rect area)
        {foreach(var rect in TooltipBounds())if(rect.Overlaps(area))return true;return false;}
        private static void FillBelowTooltip(Rect area,Color color)
        {
            var parts=new List<Rect>{area};
            foreach(var cover in TooltipBounds())
            {
                var next=new List<Rect>();
                foreach(var r in parts)
                {
                    if(!r.Overlaps(cover)){next.Add(r);continue;}
                    float top=Mathf.Max(r.yMin,cover.yMin),bottom=Mathf.Min(r.yMax,cover.yMax);
                    if(top>r.yMin)next.Add(new Rect(r.x,r.y,r.width,top-r.y));
                    if(bottom<r.yMax)next.Add(new Rect(r.x,bottom,r.width,r.yMax-bottom));
                    if(cover.xMin>r.xMin)next.Add(new Rect(r.x,top,cover.xMin-r.xMin,bottom-top));
                    if(cover.xMax<r.xMax)next.Add(new Rect(cover.xMax,top,r.xMax-cover.xMax,bottom-top));
                }
                parts=next;
            }
            foreach(var r in parts)Fill(r,color);
        }
        private static Vector2 GuiPoint(Vector2 p){return new Vector2(p.x,Screen.height-p.y);}
        private static Rect Toolbar()
        {
            var s=Plugin.Instance.Settings;
            var p=moving?toolbarPosition:new Vector2(s.ActionX.Value*Screen.width,s.ActionY.Value*Screen.height);
            return new Rect(Mathf.Clamp(p.x,0,Mathf.Max(0,Screen.width-360)),Mathf.Clamp(p.y,0,Mathf.Max(0,Screen.height-38)),360,38);
        }
        private static Rect Track(Viewport view)
        {var r=view.ScreenRect;return new Rect(Mathf.Min(Screen.width-18,r.xMax+3),r.y,15,r.height);}
        private static Rect Pager(Viewport view)
        {var r=view.ScreenRect;return new Rect(Mathf.Clamp(r.xMax-150,0,Screen.width-150),Mathf.Max(0,r.y-28),150,26);}
        internal static bool PageInput()
        {
            if(!Active()||Mouse.current==null||Keyboard.current==null)return false;
            var screen=Mouse.current.position.ReadValue();var point=GuiPoint(screen);var k=Keyboard.current;
            if(TooltipAt(point))return false;
            foreach(var view in Viewport.Live)
            {
                if(view==null||view.Grid==null||!view.Grid.isActiveAndEnabled)continue;
                int direction=0;
                if(view.Contains(screen)||Pager(view).Contains(point))
                {if(k.pageUpKey.wasPressedThisFrame)direction=-1;else if(k.pageDownKey.wasPressedThisFrame)direction=1;}
                if(Pager(view).Contains(point)&&Mouse.current.leftButton.wasPressedThisFrame)
                {
                    var ui=StaticInstance<UIManager>.Instance.InventoryUI;
                    if(ui.draggingItem==null&&!Menu.GroupDragging&&!Menu.Selecting)
                    {var r=Pager(view);if(point.x<r.x+30)direction=-1;else if(point.x>=r.xMax-30)direction=1;}
                }
                if(direction!=0){view.TurnPage(direction);return true;}
            }
            return false;
        }
        private static Rect Thumb(Viewport view)
        {
            var track=Track(view);float h=Mathf.Min(track.height,Mathf.Max(24,track.height*view.Visible/view.PageRows));
            int max=view.PageRows-view.Visible;
            float fraction=max==0?0:1-(float)(view.First-view.PageBottom)/max;
            return new Rect(track.x,track.y+(track.height-h)*fraction,track.width,h);
        }
        internal static bool Captures(Vector2 screen)
        {
            if(!Active())return false;
            var p=GuiPoint(screen);
            if(TooltipAt(p))return false;
            if(moving||scrolling!=null||HudLayout.Captures(screen))return true;
            if(Stash()!=null&&Toolbar().Contains(p))return true;
            foreach(var view in Viewport.Live)if(view!=null&&view.Grid!=null&&view.Grid.isActiveAndEnabled&&(Track(view).Contains(p)||Pager(view).Contains(p)))return true;
            return false;
        }
        internal static void Cancel()
        {
            if(moving)
            {var r=Toolbar();var s=Plugin.Instance.Settings;s.ActionX.Value=r.x/Screen.width;s.ActionY.Value=r.y/Screen.height;}
            moving=false;scrolling=null;HudLayout.EndDrag();
        }
        internal static bool Input()
        {
            if(!Active()){Cancel();return false;}
            var mouse=Mouse.current;if(mouse==null)return false;
            var ui=StaticInstance<UIManager>.Instance.InventoryUI;
            if(ui.draggingItem!=null||Menu.GroupDragging||Menu.Selecting)return false;
            var screen=mouse.position.ReadValue();var p=GuiPoint(screen);
            if(!moving&&scrolling==null&&TooltipAt(p))return false;
            if(moving)
            {
                toolbarPosition=toolbarStart+p-dragStart;
                if(!mouse.leftButton.isPressed||!Controls.Held(Modifier.Alt))Cancel();
                return true;
            }
            if(scrolling!=null)
            {
                if(!mouse.leftButton.isPressed||!scrolling.Grid.isActiveAndEnabled){scrolling=null;return true;}
                var track=Track(scrolling);var thumb=Thumb(scrolling);
                float fraction=Mathf.Clamp01((p.y-grab-track.y)/Mathf.Max(1,track.height-thumb.height));
                scrolling.ScrollTo(scrolling.PageBottom+Mathf.RoundToInt((1-fraction)*(scrolling.PageRows-scrolling.Visible)));return true;
            }
            var stash=Stash();var toolbar=Toolbar();
            if(stash!=null&&toolbar.Contains(p))
            {
                if(mouse.leftButton.wasPressedThisFrame)
                {
                    if(Controls.Held(Modifier.Alt)){moving=true;dragStart=p;toolbarStart=toolbar.position;toolbarPosition=toolbarStart;}
                    else {status=Management.Deposit(ui.PlayerBackpackGrid,stash,p.x>=toolbar.center.x);statusUntil=Time.unscaledTime+5;}
                }
                return true;
            }
            if(HudLayout.DragInput())return true;
            foreach(var view in Viewport.Live)
                if(view!=null&&view.Grid!=null&&view.Grid.isActiveAndEnabled&&Track(view).Contains(p))
                {
                    if(mouse.leftButton.wasPressedThisFrame&&view.PageRows>view.Visible)
                    {scrolling=view;var thumb=Thumb(view);grab=thumb.Contains(p)?p.y-thumb.y:thumb.height*.5f;}
                    return true;
                }
            return false;
        }
        internal static void Draw()
        {
            if(!Active())return;
            if(font==null)font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Arial"},15);
            if(button==null){button=new GUIStyle(GUI.skin.box){font=font,fontSize=15,alignment=TextAnchor.MiddleCenter};label=new GUIStyle(GUI.skin.label){font=font,fontSize=14,wordWrap=true};}
            foreach(var view in Viewport.Live)
                if(view!=null&&view.Grid!=null&&view.Grid.isActiveAndEnabled)
                {
                    FillBelowTooltip(Track(view),new Color(.07f,.08f,.08f,.92f));FillBelowTooltip(Thumb(view),new Color(1f,.67f,.23f,.95f));
                    var page=Pager(view);if(Covered(page))continue;Fill(page,new Color(.07f,.08f,.08f,.96f));
                    GUI.Box(new Rect(page.x,page.y,30,page.height),"<",button);
                    GUI.Box(new Rect(page.x+30,page.y,90,page.height),(view.CurrentPage+1)+" / "+view.PageCount,button);
                    GUI.Box(new Rect(page.xMax-30,page.y,30,page.height),">",button);
                }
            if(Stash()==null)return;
            var r=Toolbar();if(Covered(r))return;Fill(r,new Color(.08f,.09f,.08f,.96f));
            GUI.Box(new Rect(r.x,r.y,178,r.height),"全部存入 / Deposit all",button);
            GUI.Box(new Rect(r.x+182,r.y,178,r.height),"补充堆叠 / Top up",button);
            if(Time.unscaledTime<statusUntil)GUI.Label(new Rect(r.x,r.yMax+3,r.width,55),status,label);
        }
        private static void Fill(Rect r,Color color)
        {var prior=GUI.color;GUI.color=color;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=prior;}
    }
}
