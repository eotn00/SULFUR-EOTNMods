using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;
using UnityEngine;

namespace LocalSulfur.UnifiedInventory
{
    internal sealed class LockRecord
    {
        public string Context;
        public int X,Y,Id,Width,Height;
    }
    internal sealed class LockState
    {
        public int Version=1;
        public List<LockRecord> Items=new List<LockRecord>();
        public List<string> ImportedContexts=new List<string>();
    }
    public sealed class ItemMark:MonoBehaviour
    {
        public bool Locked;
    }
    internal static class ItemLocks
    {
        private const string Field="_UnifiedInventoryLocks";
        private static readonly ConditionalWeakTable<SulfurSaveState,LockState> states=new ConditionalWeakTable<SulfurSaveState,LockState>();
        internal static void Install(Harmony h)
        {
            // Patch the small managed JSON wrappers, not the file writer's exception-filter IL.
            h.Patch(AccessTools.Method(typeof(JsonUtility),"ToJson",new[]{typeof(object),typeof(bool)}),postfix:new HarmonyMethod(typeof(ItemLocks),"Serialize"));
            h.Patch(AccessTools.Method(typeof(JsonUtility),"FromJson",new[]{typeof(string),typeof(Type)}),postfix:new HarmonyMethod(typeof(ItemLocks),"Deserialize"));
            foreach(var name in new[]{"SaveInventoryData","SaveStashData","LoadEquipmentData","LoadStashData"})
                h.Patch(AccessTools.Method(typeof(GameManager),name),postfix:new HarmonyMethod(typeof(ItemLocks),name));
        }
        private static void Serialize(object __0,ref string __result)
        {
            var save=__0 as SulfurSaveState;if(save==null)return;
            if(Plugin.Failed)throw new InvalidOperationException("Inventory Redux safety stop prevents save serialization");
            LockState state;if(!states.TryGetValue(save,out state))return;
            var root=JObject.Parse(__result);root[Field]=JToken.FromObject(state);__result=root.ToString(Formatting.None);
        }
        private static void Deserialize(string __0,object __result)
        {
            var save=__result as SulfurSaveState;if(save==null)return;
            var token=JObject.Parse(__0)[Field];
            if(token!=null)
            {
                var state=token.ToObject<LockState>();
                if(state==null||state.Version!=1||state.Items==null||state.ImportedContexts==null||state.ImportedContexts.Count>1000||state.Items.Count>10000||state.Items.Any(r=>r==null||r.Context==null||r.Context.Length>512||r.X<0||r.Y<0||r.Width<1||r.Height<1||r.X>100-r.Width||r.Y>500-r.Height))
                    throw new InvalidOperationException("Invalid locked item metadata");
                states.Add(save,state);
            }
        }
        internal static bool Locked(InventoryItem item)
        {var mark=item==null?null:item.GetComponent<ItemMark>();return mark!=null&&mark.Locked;}
        internal static void Toggle(InventoryItem item)
        {
            if(item==null||!item.OwnedByPlayer||item.isBeingDragged||item.paperdollSlot!=null)return;
            if(Management.Selection.Contains(item))
            {
                var selected=Management.Selection.Where(i=>i!=null&&i.OwnedByPlayer&&!i.isBeingDragged&&i.paperdollSlot==null&&i.ContainingGrid==item.ContainingGrid).ToList();
                bool value=Operations.SelectionLockValue(selected.Select(Locked));
                foreach(var selectedItem in selected)
                    (selectedItem.GetComponent<ItemMark>()??selectedItem.gameObject.AddComponent<ItemMark>()).Locked=value;
                return;
            }
            var mark=item.GetComponent<ItemMark>()??item.gameObject.AddComponent<ItemMark>();mark.Locked=!mark.Locked;
        }
        private static void Capture(string context,ItemGrid grid)
        {
            var save=SulfurSaveState.active;if(save==null||grid==null||grid.GetComponent<Viewport>()==null)return;
            var state=states.GetValue(save,s=>new LockState());state.Items.RemoveAll(r=>r.Context==context);
            foreach(var item in grid.AllItemsInGridOnly())if(Locked(item))
                state.Items.Add(new LockRecord{Context=context,X=item.gridPosition.x,Y=item.gridPosition.y,Width=item.InventorySize.x,Height=item.InventorySize.y,Id=item.itemDefinition.id.value});
            if(context=="backpack")
            {
                state.Items.RemoveAll(r=>r.Context=="equipment");
                foreach(var item in grid.AllItems().Where(i=>i.paperdollSlot!=null&&Locked(i)))
                    state.Items.Add(new LockRecord{Context="equipment",X=(int)item.GetSerialized().inventorySlot,Y=0,Width=1,Height=1,Id=item.itemDefinition.id.value});
            }
        }
        private static void Restore(string context,ItemGrid grid)
        {
            var save=SulfurSaveState.active;if(save==null||grid==null)return;
            var state=states.GetValue(save,s=>new LockState());
            if(!state.ImportedContexts.Contains(context))
            {
                if(!state.Items.Any(r=>r.Context==context))LegacyLocks.Import(save.slotIndex,context,grid);
                state.ImportedContexts.Add(context);
            }
            foreach(var record in state.Items.Where(r=>r.Context==context))
            {
                var slot=grid.GetSlotAt(record.X,record.Y);var item=slot==null?null:slot.ItemInSlot;
                if(item!=null&&item.gridPosition==new Vector2Int(record.X,record.Y)&&item.itemDefinition.id.value==record.Id&&item.InventorySize==new Vector2Int(record.Width,record.Height))
                    (item.GetComponent<ItemMark>()??item.gameObject.AddComponent<ItemMark>()).Locked=true;
            }
            if(context=="backpack")foreach(var record in state.Items.Where(r=>r.Context=="equipment"))
            {
                var item=grid.AllItems().FirstOrDefault(i=>i.paperdollSlot!=null&&(int)i.GetSerialized().inventorySlot==record.X&&i.itemDefinition.id.value==record.Id);
                if(item!=null)(item.GetComponent<ItemMark>()??item.gameObject.AddComponent<ItemMark>()).Locked=true;
            }
        }
        private static void SaveInventoryData(){Capture("backpack",StaticInstance<UIManager>.Instance.PlayerBackpackGrid);}
        private static void SaveStashData(string stashIdentifier){Capture("stash/"+(stashIdentifier??""),StaticInstance<UIManager>.Instance.InventoryUI.GetStashItemGrid(stashIdentifier??""));}
        private static void LoadEquipmentData(){Restore("backpack",StaticInstance<UIManager>.Instance.PlayerBackpackGrid);}
        private static void LoadStashData(string stashIdentifier){Restore("stash/"+(stashIdentifier??""),StaticInstance<UIManager>.Instance.InventoryUI.GetStashItemGrid(stashIdentifier??""));}
    }
}
