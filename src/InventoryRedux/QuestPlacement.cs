using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LocalSulfur.UnifiedInventory
{
    internal static class QuestPlacement
    {
        private static readonly Type questType=AccessTools.TypeByName("PerfectRandom.Sulfur.Gameplay.QuestGiver");
        internal static bool Accepts(ItemGrid grid)
        {
            var manager=StaticInstance<UIManager>.Instance;
            var station=manager==null||manager.InventoryUI==null?null:manager.InventoryUI.CurrentServiceStation;
            return grid!=null&&grid.isActiveAndEnabled&&station!=null&&questType!=null&&questType.IsInstanceOfType(station)&&station.Grid==grid&&station.PlayerCanFreelyMoveItems;
        }
        internal static ItemGrid Hovered(Vector2 point)
        {
            var manager=StaticInstance<UIManager>.Instance;if(manager==null||manager.InventoryUI==null||EventSystem.current==null)return null;
            var station=manager.InventoryUI.CurrentServiceStation;var grid=station==null?null:station.Grid;
            if(!Accepts(grid))return null;
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
            foreach(var hit in hits)
            {
                if(hit.gameObject==null)continue;
                var item=hit.gameObject.GetComponentInParent<InventoryItem>();if(item!=null&&item.isBeingDragged)continue;
                return hit.gameObject.GetComponentInParent<ItemGrid>()==grid?grid:null;
            }
            return null;
        }
        internal static void Install(Harmony harmony)
        {
            if(questType==null)throw new TypeLoadException("QuestGiver");
            harmony.Patch(AccessTools.Method(questType,"UpdateCombination"),prefix:new HarmonyMethod(typeof(QuestPlacement),"UpdateEnough"));
            harmony.Patch(AccessTools.Method(questType,"CompleteQuest"),prefix:new HarmonyMethod(typeof(QuestPlacement),"ReturnExcess"));
        }
        private static Dictionary<ushort,int> Requirements(object quest)
        {
            if(quest==null)return null;
            var requirements=AccessTools.Field(quest.GetType(),"requirements").GetValue(quest) as IEnumerable<QuestRequirement>;
            if(requirements==null)return null;
            var result=new Dictionary<ushort,int>();
            foreach(var r in requirements)
            {
                if(r==null||r.quantity<=0)return null;
                int n;result.TryGetValue(r.ItemId.value,out n);result[r.ItemId.value]=checked(n+r.quantity);
            }
            return result.Count==0?null:result;
        }
        private static bool Enough(ItemGrid grid,Dictionary<ushort,int> needs)
        {
            if(grid==null||needs==null||Plugin.Failed)return false;
            var counts=new Dictionary<ushort,long>();
            foreach(var item in grid.AllItemsInGridOnly()){long n;counts.TryGetValue(item.itemDefinition.id.value,out n);counts[item.itemDefinition.id.value]=n+Math.Max(1,item.quantity);}
            foreach(var pair in needs){long n;if(!counts.TryGetValue(pair.Key,out n)||n<pair.Value)return false;}
            return true;
        }
        private static bool UpdateEnough(ServiceStation __instance,object ___currentQuest,ref bool ___canTurnIn,UnityEngine.UI.Button ___turnInConfirmButton)
        {
            ___canTurnIn=Enough(__instance.Grid,Requirements(___currentQuest));
            if(___turnInConfirmButton!=null)___turnInConfirmButton.interactable=___canTurnIn;
            return false;
        }
        private static bool ReturnExcess(ServiceStation __instance,object ___currentQuest)
        {
            var needs=Requirements(___currentQuest);var grid=__instance.Grid;
            if(!Enough(grid,needs))return false;
            var ui=StaticInstance<UIManager>.Instance.InventoryUI;
            if(ui.draggingItem!=null)return false;
            var bag=ui.PlayerBackpackGrid;var view=bag==null?null:bag.GetComponent<Viewport>();if(view==null)return false;
            var returns=new List<InventoryItem>();var quantities=new List<int>();
            var submitted=grid.AllItemsInGridOnly();
            var remainders=Operations.QuestRemainders(submitted.Select(i=>(int)i.itemDefinition.id.value).ToArray(),submitted.Select(i=>Math.Max(1,i.quantity)).ToArray(),needs.ToDictionary(p=>(int)p.Key,p=>p.Value));
            if(remainders==null)return false;
            for(int k=0;k<submitted.Count;k++)
            {
                if(remainders[k]>0){returns.Add(submitted[k]);quantities.Add(remainders[k]);}
            }
            if(returns.Count==0)return true;
            var occupied=bag.AllItemsInGridOnly().Select((i,k)=>new Placement{Key=k,X=i.gridPosition.x,Y=i.gridPosition.y,Width=i.InventorySize.x,Height=i.InventorySize.y}).ToList();
            var group=returns.Select((i,k)=>new Placement{Key=occupied.Count+k,Width=i.InventorySize.x,Height=i.InventorySize.y}).ToList();
            var plan=Operations.PackGroup(bag.gridSize.x,bag.gridSize.y,occupied,group,0,bag.gridSize.y-1,view.PageRows);
            if(plan==null)
            {
                Plugin.Instance.Report("Quest not submitted: no backpack space to return excess items.");
                return false;
            }
            var original=returns.Select(i=>i.quantity).ToList();
            for(int i=0;i<returns.Count;i++)returns[i].quantity=quantities[i];
            if(!Management.Commit(returns,bag,plan.OrderBy(p=>p.Key).Select(p=>new Vector2Int(p.X,p.Y)).ToList()))
            {for(int i=0;i<returns.Count;i++){returns[i].quantity=original[i];Stacks.RefreshBadge(returns[i]);}return false;}
            foreach(var item in returns)Stacks.RefreshBadge(item);
            // Native completion consumes only the full required stacks still in the quest grid.
            // Required portions of returned stacks were deducted above; rewards stay native.
            return true;
        }
    }
}
