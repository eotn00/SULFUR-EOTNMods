using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;
using PerfectRandom.Sulfur.Core.UI.Inventory;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LocalSulfur.RummageInventoryCompat
{
    // All Rummage references are optional and resolved once. No item or save hooks belong here.
    internal static class RummageCompatibility
    {
        internal const string Id="ryuka.sulfur.rummage";
        internal static readonly Harmony Patches=new Harmony(Plugin.Id);
        private static FieldInfo barRoot,resultsField,resultsRoot,displayField;
        private static PropertyInfo placementProperty;
        private static MethodInfo layoutMethod,placeResultsMethod,unfocusMethod;
        private static Func<bool> typing;
        private static bool failed;
        internal static RummagePanel Current;

        internal static void Install()
        {
            BepInEx.PluginInfo info;
            if(!Chainloader.PluginInfos.TryGetValue(Id,out info)||info.Instance==null)return;
            try
            {
                if(info.Metadata.Version!=new Version(1,0,0))
                    throw new NotSupportedException("Adapter supports Rummage 1.0.0; detected "+info.Metadata.Version);
                if(!ReduxBridge.Initialize())return;
                var assembly=info.Instance.GetType().Assembly;
                var bar=assembly.GetType("Ryuka.Sulfur.Rummage.UserInterface.SearchBar",true);
                var results=assembly.GetType("Ryuka.Sulfur.Rummage.UserInterface.SearchResultsPanel",true);
                var display=assembly.GetType("Ryuka.Sulfur.Rummage.Config.DisplayConfig",true);
                var guard=assembly.GetType("Ryuka.Sulfur.Rummage.InputGuard.SearchInputGuard",true);
                var position=assembly.GetType("Ryuka.Sulfur.Rummage.Config.ResultsPosition",true);
                barRoot=Field(bar,"rootRect");resultsField=Field(bar,"results");resultsRoot=Field(results,"rootRect");
                displayField=Field(bar,"display");placementProperty=AccessTools.Property(display,"ResultsPlacement");
                if(placementProperty==null)throw new MissingMemberException(display.FullName,"ResultsPlacement");
                layoutMethod=Method(bar,"Layout",typeof(float));
                placeResultsMethod=Method(results,"Place",typeof(float),typeof(float),typeof(float),typeof(float),position);
                unfocusMethod=Method(bar,"Unfocus");
                typing=(Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>),Method(guard,"Typing"));
                // Validate every target before installing any hook.
                var build=Method(bar,"TryBuild",typeof(InventoryUI),typeof(RectTransform));
                var place=Method(bar,"Place",typeof(RectTransform));
                var dispose=Method(bar,"Dispose");var focus=Method(bar,"FocusInput");
                Patches.Patch(build,prefix:Hook("BeforeBuild"),postfix:Hook("AfterBuild"));
                Patches.Patch(place,prefix:Hook("Place"));
                Patches.Patch(dispose,postfix:Hook("Dispose"));
                Patches.Patch(focus,prefix:Hook("AllowFocus"));
                ReduxBridge.Install(Patches);
                Plugin.Instance.LogInfo("Rummage 1.0.0 compatibility enabled: visible search panel, shared layout dragging and input isolation.");
            }
            catch(Exception error){Fail(error);Patches.UnpatchSelf();}
        }
        private static FieldInfo Field(Type type,string name)
        {var value=AccessTools.Field(type,name);if(value==null)throw new MissingFieldException(type.FullName,name);return value;}
        private static MethodInfo Method(Type type,string name,params Type[] args)
        {var value=AccessTools.Method(type,name,args);if(value==null)throw new MissingMethodException(type.FullName,name);return value;}
        private static HarmonyMethod Hook(string name){return new HarmonyMethod(typeof(RummageCompatibility),name);}
        internal static void Fail(Exception error)
        {
            if(!failed)Plugin.Instance.Report("Rummage compatibility disabled (inventory/save operations unchanged): "+error);
            failed=true;
            if(Current!=null){Current.Hide();Current.enabled=false;}
        }
        private static void BeforeBuild(object __instance,InventoryUI ui,ref RectTransform gridRect)
        {
            if(failed||ui==null||ui.PlayerBackpackGrid==null)return;
            var view=ReduxBridge.View(ui.PlayerBackpackGrid);
            var frame=ReduxBridge.Frame(view);
            if(view==null||frame==null)return;
            try
            {
                if(Current!=null)UnityEngine.Object.Destroy(Current.gameObject);
                var obj=new GameObject("InventoryRedux_Rummage",typeof(RectTransform),typeof(CanvasGroup),typeof(LayoutElement));
                obj.GetComponent<LayoutElement>().ignoreLayout=true;
                obj.transform.SetParent(ui.transform,false);
                var panel=obj.AddComponent<RummagePanel>();Current=panel;
                panel.Initialize(__instance,ui,view);
                // Rummage builds both roots beside this anchor, outside the grid's RectMask2D.
                gridRect=panel.Anchor;
            }
            catch(Exception error){Fail(error);}
        }
        private static void AfterBuild(object __instance,bool __result)
        {
            if(failed||Current==null||!ReferenceEquals(Current.Owner,__instance))return;
            if(!__result){UnityEngine.Object.Destroy(Current.gameObject);Current=null;return;}
            try
            {
                var results=resultsField.GetValue(__instance);
                var placement=(ConfigEntryBase)placementProperty.GetValue(displayField.GetValue(__instance),null);
                Current.Bind((RectTransform)barRoot.GetValue(__instance),(RectTransform)resultsRoot.GetValue(results),
                    (Action<float>)Delegate.CreateDelegate(typeof(Action<float>),__instance,layoutMethod),
                    results,placeResultsMethod,placement,(Action)Delegate.CreateDelegate(typeof(Action),__instance,unfocusMethod));
            }
            catch(Exception error){Fail(error);}
        }
        private static bool Place(object __instance)
        {
            if(failed||Current==null||!ReferenceEquals(Current.Owner,__instance))return true;
            try{Current.Place();return false;}
            catch(Exception error){Fail(error);return false;}
        }
        private static bool AllowFocus()
        {return Current==null||(!failed&&Current.CanInteract&&Current.CanStartDrag);}
        private static void Dispose(object __instance)
        {
            if(Current==null||!ReferenceEquals(Current.Owner,__instance))return;
            UnityEngine.Object.Destroy(Current.gameObject);Current=null;
        }
        internal static bool Typing
        {
            get
            {
                if(failed||Current==null||typing==null)return false;
                try{return Current.CanInteract&&typing();}catch(Exception error){Fail(error);return false;}
            }
        }
        internal static bool PointerOver(Vector2 point)
        {return !failed&&Current!=null&&Current.Contains(point);}
        internal static bool Owns(GameObject obj)
        {return !failed&&Current!=null&&obj!=null&&obj.transform.IsChildOf(Current.transform);}
        internal static bool BlocksItemInput
        {get{return Typing||(!failed&&Current!=null&&Current.Dragging)||Mouse.current!=null&&PointerOver(Mouse.current.position.ReadValue());}}
        internal static bool UpdateInput()
        {
            if(failed||Current==null)return false;
            try
            {
                if(Current.UpdateDrag())return true;
                return Typing||Mouse.current!=null&&Current.Contains(Mouse.current.position.ReadValue());
            }
            catch(Exception error){Fail(error);return false;}
        }
        internal static bool RedirectDrag(PointerEventData data,List<RaycastResult> hits)
        {
            if(failed||Current==null||!Current.CanInteract||hits.Count==0||!Owns(hits[0].gameObject))return false;
            if(!Current.Dragging&&!(Current.CanStartDrag&&Controls.LayoutHeld))return false;
            // Redirect to a non-selectable surface so dragging cannot click Clear or focus the text field.
            var first=hits[0];first.gameObject=Current.gameObject;hits.Clear();hits.Add(first);return true;
        }
        internal static bool ProtectSearchHits(PointerEventData data,List<RaycastResult> hits)
        {
            if(failed||Current==null||data==null||!Current.Contains(data.position))return false;
            for(int i=0;i<hits.Count;i++)
            {
                var obj=hits[i].gameObject;
                if(obj==null)continue;
                if(Owns(obj))
                {
                    // Only pass native inventory surfaces, never another window or a button.
                    if(i>0){var hit=hits[i];hits.RemoveAt(i);hits.Insert(0,hit);}
                    return true;
                }
                if(!Current.IsInventorySurface(obj))return false;
            }
            return false;
        }
        internal static void CoverBounds(List<Rect> bounds)
        {if(!failed&&Current!=null)Current.CoverBounds(bounds);}
        internal static Rect Pager(object view,Rect fallback)
        {return !failed&&Current!=null&&ReferenceEquals(Current.View,view)&&Current.CanInteract?Current.Pager(fallback):fallback;}
    }

    public sealed class RummagePanel:MonoBehaviour
    {
        internal object Owner;
        internal Component View;
        internal RectTransform Anchor;
        private InventoryUI ui;
        private RectTransform mount,bar,results;
        private CanvasGroup visibility;
        private Action<float> layout;
        private Action unfocus;
        private object resultsObject,placement;
        private MethodInfo resultsPlace;
        private ConfigEntryBase placementConfig;
        private EventInfo placementEvent;
        private EventHandler placementChanged;
        private ConfigEntry<float> offsetX,offsetY;
        private readonly Vector3[] viewCorners=new Vector3[4],canvasCorners=new Vector3[4];
        private readonly Vector3[] boundsCorners=new Vector3[4];
        private SearchPanelPlacement applied;
        private bool placed,dirty=true,above,dragging;
        private int dragFrame=-1;
        private Vector2 dragStart,offsetStart,dragOffset;
        internal bool Dragging {get{return dragging||dragFrame==Time.frameCount;}}
        internal bool CanInteract
        {
            get
            {
                var game=StaticInstance<GameManager>.Instance;
                var manager=StaticInstance<UIManager>.Instance;
                return ui!=null&&ui.IsOpened&&View!=null&&ReduxBridge.Frame(View)!=null&&ui.PlayerBackpackGrid!=null&&ui.PlayerBackpackGrid.isActiveAndEnabled&&
                    bar!=null&&bar.gameObject.activeInHierarchy&&!ReduxBridge.MenuOpen&&game!=null&&game.InputActions!=null&&game.InputActions.Inventory.Get().enabled&&
                    manager!=null&&manager.informationDialogsActive.Count==0;
            }
        }
        internal bool CanStartDrag
        {get{return ui!=null&&ui.draggingItem==null&&!ReduxBridge.GestureActive;}}
        internal void Initialize(object owner,InventoryUI inventory,Component view)
        {
            Owner=owner;ui=inventory;View=view;mount=(RectTransform)transform;
            mount.anchorMin=mount.anchorMax=new Vector2(.5f,.5f);mount.pivot=Vector2.zero;mount.sizeDelta=Vector2.zero;
            visibility=GetComponent<CanvasGroup>();visibility.alpha=0;visibility.blocksRaycasts=false;
            Anchor=new GameObject("BuildAnchor",typeof(RectTransform)).GetComponent<RectTransform>();Anchor.SetParent(mount,false);
            offsetX=Plugin.Instance.OffsetX;offsetY=Plugin.Instance.OffsetY;
        }
        internal void Bind(RectTransform search,RectTransform matches,Action<float> relayout,object resultOwner,
            MethodInfo resultLayout,ConfigEntryBase setting,Action stopTyping)
        {
            bar=search;results=matches;layout=relayout;resultsObject=resultOwner;resultsPlace=resultLayout;unfocus=stopTyping;
            if(bar==null||results==null)throw new InvalidOperationException("Rummage UI roots are missing");
            bar.SetParent(mount,false);results.SetParent(mount,false);
            Surface(bar);Surface(results);
            placementConfig=setting;
            placementChanged=delegate{placement=setting.BoxedValue;above=placement.ToString()=="AboveSearchBar";dirty=true;};
            placementEvent=setting.GetType().GetEvent("SettingChanged");
            if(placementEvent==null)throw new MissingMemberException("Rummage results setting has no change notification");
            placementEvent.AddEventHandler(setting,placementChanged);placementChanged(null,EventArgs.Empty);
            Place();
            Plugin.Instance.LogInfo("Search panel attached outside clipped viewport; screen bounds="+Bounds(bar)+". Shared layout drag enabled.");
        }
        private static void Surface(RectTransform rect)
        {
            var surface=rect.GetComponent<Image>()??rect.gameObject.AddComponent<Image>();
            surface.color=Color.clear;surface.raycastTarget=true;
        }
        internal void Place()
        {
            var frame=ReduxBridge.Frame(View);
            if(bar==null||results==null||View==null||frame==null)return;
            SyncVisibility();
            // A sibling of the backpack alone can remain underneath the separate stash branch.
            var parent=ui.transform as RectTransform;if(parent==null)return;
            if(mount.parent!=parent){mount.SetParent(parent,false);dirty=true;}
            if(mount.GetSiblingIndex()!=parent.childCount-1)mount.SetAsLastSibling();
            var canvas=mount.GetComponentInParent<Canvas>();
            if(canvas==null)return;
            frame.GetWorldCorners(viewCorners);
            ((RectTransform)canvas.rootCanvas.transform).GetWorldCorners(canvasCorners);
            var a=parent.InverseTransformPoint(viewCorners[1]);var b=parent.InverseTransformPoint(viewCorners[2]);
            var lo=parent.InverseTransformPoint(canvasCorners[0]);var hi=parent.InverseTransformPoint(canvasCorners[2]);
            var offset=dragging?dragOffset:new Vector2(offsetX.Value,offsetY.Value);
            var next=RummageLayout.Place(a.x,a.y,b.x-a.x,lo.x,lo.y,hi.x-lo.x,hi.y-lo.y,offset.x,offset.y,above);
            if(!dirty&&placed&&next.Left==applied.Left&&next.Bottom==applied.Bottom&&next.Width==applied.Width&&next.Scale==applied.Scale)return;
            applied=next;placed=true;dirty=false;
            mount.localScale=Vector3.one*next.Scale;mount.localPosition=new Vector3(next.Left,next.Bottom,0);
            bar.sizeDelta=new Vector2(next.Width,62);bar.localPosition=Vector3.zero;
            layout(next.Width);
            // Leave the pager between the bar and results when results are configured above it.
            resultsPlace.Invoke(resultsObject,new[]{(object)0f,0f,above?92f:62f,next.Width,placement});
        }
        internal bool IsInventorySurface(GameObject obj)
        {
            if(obj==null||obj.GetComponentInParent<Selectable>()!=null)return false;
            var frame=ReduxBridge.Frame(View);
            if(frame!=null&&frame.parent!=null&&obj.transform.IsChildOf(frame.parent))return true;
            var station=ui.CurrentServiceStation;
            return station!=null&&station.ContainerTransform!=null&&obj.transform.IsChildOf(station.ContainerTransform);
        }
        private void SyncVisibility()
        {
            bool shown=CanInteract;
            visibility.alpha=shown?1:0;visibility.interactable=shown;visibility.blocksRaycasts=shown;
            if(!shown){EndDrag();if(unfocus!=null)unfocus();}
        }
        internal void Hide()
        {
            dragging=false;
            if(visibility!=null){visibility.alpha=0;visibility.interactable=false;visibility.blocksRaycasts=false;}
            if(unfocus!=null){try{unfocus();}catch{}}
        }
        private Rect Bounds(RectTransform rect)
        {
            rect.GetWorldCorners(boundsCorners);
            return Rect.MinMaxRect(boundsCorners[0].x,Screen.height-boundsCorners[2].y,boundsCorners[2].x,Screen.height-boundsCorners[0].y);
        }
        internal bool Contains(Vector2 screen)
        {
            if(!CanInteract)return false;
            var p=new Vector2(screen.x,Screen.height-screen.y);
            return Bounds(bar).Contains(p)||(results.gameObject.activeInHierarchy&&Bounds(results).Contains(p));
        }
        internal void CoverBounds(List<Rect> bounds)
        {
            if(!CanInteract)return;
            bounds.Add(Bounds(bar));if(results.gameObject.activeInHierarchy)bounds.Add(Bounds(results));
        }
        internal Rect Pager(Rect fallback)
        {
            var r=Bounds(bar);
            float scale=r.height/62;
            return new Rect(Mathf.Clamp(r.xMax-fallback.width,0,Mathf.Max(0,Screen.width-fallback.width)),Mathf.Max(0,r.y-30*scale),fallback.width,26*scale);
        }
        internal bool UpdateDrag()
        {
            if(!CanInteract){EndDrag();return false;}
            var mouse=Mouse.current;if(mouse==null){EndDrag();return false;}
            if(!dragging&&CanStartDrag&&Controls.LayoutHeld&&Controls.DragStarted&&Contains(mouse.position.ReadValue()))
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(mount.parent as RectTransform,mouse.position.ReadValue(),null,out dragStart);
                offsetStart=new Vector2(applied.OffsetX,applied.OffsetY);dragOffset=offsetStart;dragging=true;
                if(unfocus!=null)unfocus();
            }
            if(!dragging)return Dragging;
            dragFrame=Time.frameCount;
            if(!Controls.DragHeld||!Controls.LayoutHeld){EndDrag();return true;}
            Vector2 point;RectTransformUtility.ScreenPointToLocalPointInRectangle(mount.parent as RectTransform,mouse.position.ReadValue(),null,out point);
            dragOffset=offsetStart+point-dragStart;Place();return true;
        }
        private void EndDrag()
        {
            if(!dragging)return;
            dragging=false;dragFrame=Time.frameCount;
            offsetX.Value=applied.OffsetX;offsetY.Value=applied.OffsetY;
        }
        private void LateUpdate()
        {
            if(View==null||ReduxBridge.Frame(View)==null){Destroy(gameObject);return;}
            if(RummageCompatibility.Current!=this)return;
            try{Place();}catch(Exception error){RummageCompatibility.Fail(error);}
        }
        private void OnDestroy()
        {
            EndDrag();
            if(placementConfig!=null&&placementChanged!=null&&placementEvent!=null)placementEvent.RemoveEventHandler(placementConfig,placementChanged);
            if(RummageCompatibility.Current==this)RummageCompatibility.Current=null;
        }
    }
}
