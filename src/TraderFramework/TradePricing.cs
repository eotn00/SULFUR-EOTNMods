using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LocalSulfur.UnifiedInventory;
using PerfectRandom.Sulfur.Core.Items;
using UnityEngine;

namespace LocalSulfur.TraderFramework
{
    internal static class TradePricing
    {
        private static readonly FieldInfo bought=AccessTools.Field(typeof(InventoryItem),"<BoughtForPrice>k__BackingField");
        private static readonly FieldInfo sold=AccessTools.Field(typeof(InventoryItem),"<SoldForPrice>k__BackingField");

        internal static void Install(Harmony harmony)
        {
            if(bought==null||sold==null||bought.FieldType!=typeof(int)||sold.FieldType!=typeof(int))
                throw new MissingFieldException("Native trade history fields changed");
            harmony.Patch(AccessTools.Method(typeof(InventoryItem),"TransferOwnership"),
                transpiler:new HarmonyMethod(typeof(TradePricing),"Transfer"),
                postfix:new HarmonyMethod(typeof(TradePricing),"AfterTransfer"));
            harmony.Patch(AccessTools.Method(typeof(InventoryItem),"Setup",new[]{typeof(Vector2),typeof(ItemDefinition),typeof(ItemGrid),typeof(int),typeof(int),typeof(InventoryData),typeof(bool)}),
                postfix:new HarmonyMethod(typeof(TradePricing),"AfterSetup"));
            harmony.Patch(AccessTools.Method(typeof(InventoryItem),"GetSerialized"),
                postfix:new HarmonyMethod(typeof(TradePricing),"Serialize"));
        }

        private static IEnumerable<CodeInstruction> Transfer(IEnumerable<CodeInstruction> instructions)
        {
            var result=new List<CodeInstruction>(instructions);
            var setBought=AccessTools.PropertySetter(typeof(InventoryItem),"BoughtForPrice");
            var setSold=AccessTools.PropertySetter(typeof(InventoryItem),"SoldForPrice");
            int buySites=0,sellSites=0;
            foreach(var code in result)
            {
                if(code.Calls(setBought))
                {code.opcode=OpCodes.Call;code.operand=AccessTools.Method(typeof(TradePricing),"StoreBought");buySites++;}
                else if(code.Calls(setSold))
                {code.opcode=OpCodes.Call;code.operand=AccessTools.Method(typeof(TradePricing),"StoreSold");sellSites++;}
            }
            if(buySites!=1||sellSites!=1)throw new InvalidOperationException("Native trade history assignment changed");
            return result;
        }

        // Patch the assignments inside the transaction: tiny setters can be inlined by Mono.
        // Direct fields also avoid dividing twice through Redux's existing setter prefixes.
        private static void StoreBought(InventoryItem item,int total)
        {bought.SetValue(item,StockRules.PurchaseHistory(total,MerchantBridge.CanStack(item.itemDefinition)));}
        private static void StoreSold(InventoryItem item,int total)
        {sold.SetValue(item,StockRules.UnitHistory(total,item.quantity,MerchantBridge.CanStack(item.itemDefinition)));}

        private static void AfterTransfer(InventoryItem __instance,bool isTransaction)
        {
            if(isTransaction&&__instance.OwnedByPlayer&&MerchantBridge.CanStack(__instance.itemDefinition))
            {bought.SetValue(__instance,0);sold.SetValue(__instance,0);}
        }
        private static void AfterSetup(InventoryItem __instance)
        {
            if(MerchantBridge.CanStack(__instance.itemDefinition))bought.SetValue(__instance,0);
        }
        private static void Serialize(InventoryItem __instance,InventoryData __result)
        {
            // Native InventoryData already omits purchase history. Keep that rule for fungible
            // goods so pickups, purchases, split portions and old saved stacks can all merge.
            if(__result!=null)__result.boughtFor=StockRules.PurchaseHistory(__instance.BoughtForPrice,MerchantBridge.CanStack(__instance.itemDefinition));
        }
    }
}
