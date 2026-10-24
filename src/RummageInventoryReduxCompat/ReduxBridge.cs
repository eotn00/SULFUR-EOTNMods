using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using PerfectRandom.Sulfur.Core.Items;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace LocalSulfur.RummageInventoryCompat
{
    internal static class ReduxBridge
    {
        private static Type viewportType;
        private static FieldInfo frame,open,group,selecting,layoutDragging,nativeSplit,nativeAction;
        private static FieldInfo placing,halfGesture,nextPlace,blockedFrame;
        private static MethodInfo menuUpdate,raycastFilter,topmost,tooltipBounds,pager;
        private static MethodInfo[] itemActions;
        private static Action cancelQuick,cancelOverlay;
        private static Func<bool> updateSplit;
        internal static ConfigEntryBase LayoutModifier,DragButton;

        internal static bool Initialize()
        {
            PluginInfo info;
            if(!Chainloader.PluginInfos.TryGetValue("local.sulfur.unifiedinventory",out info)||info.Instance==null)
            {Plugin.Instance.LogInfo("Compatibility patch inactive: Inventory Redux is not installed.");return false;}
            var assembly=info.Instance.GetType().Assembly;
            viewportType=Type(assembly,"Viewport");
            var menu=Type(assembly,"Menu");var overlay=Type(assembly,"InventoryOverlay");
            var quick=Type(assembly,"QuickActions");var stacks=Type(assembly,"Stacks");
            frame=Field(viewportType,"frame");layoutDragging=Field(viewportType,"IsLayoutDragging");
            open=Field(menu,"Open");group=Field(menu,"GroupDragging");selecting=Field(menu,"Selecting");
            nativeSplit=Field(stacks,"NativeSplitInput");nativeAction=Field(quick,"NativeAction");
            placing=Field(quick,"placing");halfGesture=Field(quick,"halfGesture");nextPlace=Field(quick,"nextPlace");blockedFrame=Field(quick,"blockedFrame");
            var settings=Field(info.Instance.GetType(),"Settings").GetValue(info.Instance);
            if(settings==null)throw new InvalidOperationException("Inventory Redux settings are unavailable");
            LayoutModifier=(ConfigEntryBase)Field(settings.GetType(),"LayoutModifier").GetValue(settings);
            DragButton=(ConfigEntryBase)Field(settings.GetType(),"DragButton").GetValue(settings);
            cancelQuick=(Action)Delegate.CreateDelegate(typeof(Action),Method(quick,"Cancel"));
            cancelOverlay=(Action)Delegate.CreateDelegate(typeof(Action),Method(overlay,"Cancel"));
            updateSplit=(Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>),Method(stacks,"UpdateSplitDrag"));
            menuUpdate=Method(menu,"Update");topmost=Method(viewportType,"Topmost",typeof(Vector2));
            raycastFilter=Method(Type(assembly,"InventoryRaycasts"),"Filter",typeof(PointerEventData),typeof(List<RaycastResult>));
            tooltipBounds=Method(overlay,"TooltipBounds");pager=Method(overlay,"Pager",viewportType);
            itemActions=ResolveItemHooks();
            Plugin.Instance.LogInfo("Detected Inventory Redux "+info.Metadata.Version+"; optional UI contracts validated.");
            return true;
        }
        private static Type Type(Assembly assembly,string name){return assembly.GetType("LocalSulfur.UnifiedInventory."+name,true);}
        private static FieldInfo Field(Type type,string name)
        {var value=AccessTools.Field(type,name);if(value==null)throw new MissingFieldException(type.FullName,name);return value;}
        private static MethodInfo Method(Type type,string name,params Type[] args)
        {
            var value=type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static,null,args,null);
            if(value==null)throw new MissingMethodException(type.FullName,name);return value;
        }
        private static MethodInfo[] ResolveItemHooks()
        {
            // Resolve exact signatures before installing any Harmony patches.
            return new[]{
                Method(typeof(InventoryItem),"OnPointerClick",typeof(PointerEventData)),
                Method(typeof(InventoryItem),"OnBeginDrag",typeof(PointerEventData)),
                Method(typeof(InventoryItem),"Use")
            };
        }
        internal static void Install(Harmony harmony)
        {
            harmony.Patch(menuUpdate,prefix:Hook("MenuInput"));
            harmony.Patch(raycastFilter,prefix:Hook("PreserveSearchHits"));
            harmony.Patch(topmost,postfix:Hook("ExcludeSearch"));
            harmony.Patch(tooltipBounds,postfix:Hook("CoverSearch"));
            harmony.Patch(pager,postfix:Hook("MovePager"));
            foreach(var action in itemActions)
                harmony.Patch(action,prefix:new HarmonyMethod(typeof(ReduxBridge),"AllowItem"){priority=Priority.First});
        }
        private static HarmonyMethod Hook(string name){return new HarmonyMethod(typeof(ReduxBridge),name);}
        internal static Component View(ItemGrid grid){return grid==null?null:grid.GetComponent(viewportType);}
        internal static RectTransform Frame(Component view){return view==null?null:(RectTransform)frame.GetValue(view);}
        internal static bool MenuOpen {get{return (bool)open.GetValue(null);}}
        internal static bool GestureActive
        {get{return (bool)group.GetValue(null)||(bool)selecting.GetValue(null)||(bool)layoutDragging.GetValue(null);}}
        private static bool MenuInput()
        {
            if(!RummageCompatibility.UpdateInput())return true;
            try
            {
                cancelQuick();placing.SetValue(null,false);halfGesture.SetValue(null,false);
                nextPlace.SetValue(null,0f);blockedFrame.SetValue(null,Time.frameCount);cancelOverlay();
                // Do not strand a native split drag merely because it crossed the search panel.
                updateSplit();selecting.SetValue(null,false);group.SetValue(null,false);return false;
            }
            catch(Exception error){RummageCompatibility.Fail(error);return true;}
        }
        private static bool AllowItem(InventoryItem __instance)
        {
            if((bool)nativeSplit.GetValue(null)||(bool)nativeAction.GetValue(null)||__instance.isBeingDragged)return true;
            return !RummageCompatibility.BlocksItemInput;
        }
        private static bool PreserveSearchHits(PointerEventData eventData,List<RaycastResult> raycastResults)
        {
            if(!RummageCompatibility.ProtectSearchHits(eventData,raycastResults))return true;
            RummageCompatibility.RedirectDrag(eventData,raycastResults);
            return false;
        }
        private static void ExcludeSearch(Vector2 screen,ref bool __result)
        {if(__result&&(RummageCompatibility.Typing||RummageCompatibility.PointerOver(screen)))__result=false;}
        private static void CoverSearch(List<Rect> __result){RummageCompatibility.CoverBounds(__result);}
        private static void MovePager(object view,ref Rect __result){__result=RummageCompatibility.Pager(view,__result);}
    }

    internal static class Controls
    {
        internal static bool LayoutHeld
        {
            get
            {
                var k=Keyboard.current;if(k==null)return false;
                int modifier=Convert.ToInt32(ReduxBridge.LayoutModifier.BoxedValue);
                return modifier==1?k.leftCtrlKey.isPressed||k.rightCtrlKey.isPressed:
                    modifier==2?k.leftShiftKey.isPressed||k.rightShiftKey.isPressed:k.leftAltKey.isPressed||k.rightAltKey.isPressed;
            }
        }
        internal static bool DragHeld
        {get{var m=Mouse.current;return m!=null&&(Convert.ToInt32(ReduxBridge.DragButton.BoxedValue)==0?m.leftButton.isPressed:m.middleButton.isPressed);}}
        internal static bool DragStarted
        {get{var m=Mouse.current;return m!=null&&(Convert.ToInt32(ReduxBridge.DragButton.BoxedValue)==0?m.leftButton.wasPressedThisFrame:m.middleButton.wasPressedThisFrame);}}
    }
}
