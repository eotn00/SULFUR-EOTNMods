using System;
using System.Linq;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;
using PerfectRandom.Sulfur.Core.CharacterStats;

namespace LocalSulfur.UnifiedInventory
{
    internal static class InventoryDataCopy
    {
        private static T[] Copy<T>(T[] array){return array==null?null:(T[])array.Clone();}
        internal static InventoryData Create(InventoryData data)
        {
            if(data==null)throw new ArgumentNullException("data");
            // InventoryData has two constructors and cannot be round-tripped by default JSON.NET.
            var result=new InventoryData(data.id,0,0,data.quantity,data.currentAmmo,data.caliberId,
                Attributes(data.attributes),Copy(data.attachmentIds),Copy(data.enchantmentIds),data.boughtFor,data.xSize,data.ySize,data.rotated,data.selected);
            result.boughtFor=data.boughtFor;result.inventorySlot=data.inventorySlot;result.position=Copy(data.position);
            result.equipped=data.equipped;result.selected=data.selected;result.identifier=data.identifier;
            result.attachments=Copy(data.attachments);result.enchantments=Copy(data.enchantments);result.caliber=data.caliber;
            return result;
        }
        private static ItemAttributeCollectionData Attributes(ItemAttributeCollectionData data)
        {
            if(data==null)return null;
            return new ItemAttributeCollectionData{itemAttributes=data.itemAttributes==null?null:data.itemAttributes.Select(a=>a==null?null:new ItemAttributeData(a.id,Stat(a.value))).ToArray()};
        }
        private static CharacterStat Stat(CharacterStat data)
        {
            if(data==null)return null;
            var result=new CharacterStat(data.BaseValue);
            if(data.statModifiers!=null)foreach(var mod in data.statModifiers)
                result.statModifiers.Add(mod==null?null:new StatModifier(mod.Value,mod.Type,mod.Order,mod.SourceId));
            else result.statModifiers=null;
            return result;
        }
    }
}
