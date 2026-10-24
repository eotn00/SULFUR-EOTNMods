using System;
using System.Collections.Generic;

namespace LocalSulfur.TraderFramework
{
    internal static class StockRules
    {
        internal static int PurchaseHistory(int price,bool stackable)
        {return stackable?0:price;}
        internal static int UnitHistory(int total,int quantity,bool stackable)
        {return stackable&&quantity>1?total/quantity:total;}
        internal static int TakeStock(Dictionary<int,Queue<int>> plan,int id)
        {
            Queue<int> counts;
            if(!plan.TryGetValue(id,out counts)||counts.Count==0)throw new InvalidOperationException("Unexpected generated merchant ItemId="+id);
            return counts.Dequeue();
        }
        internal static List<int> PackStock(int selected,bool equipment,bool stackable,int limit,bool excluded=false)
        {
            if(selected<0||limit<1)throw new ArgumentOutOfRangeException();
            int count=checked(selected*(equipment||excluded?1:2)),max=!equipment&&stackable?limit:1;
            var result=new List<int>();
            while(count>0){int n=Math.Min(count,max);result.Add(n);count-=n;}
            return result;
        }
        internal static int BuyCount(int stock,bool control,bool alt)
        {if(stock<1)return 0;return control?1:alt?Math.Max(1,stock/2):stock;}
        internal static bool TryPrice(int unit,int count,out int price)
        {long value=(long)unit*count;price=0;if(unit<0||count<1||value>int.MaxValue)return false;price=(int)value;return true;}
    }
}
