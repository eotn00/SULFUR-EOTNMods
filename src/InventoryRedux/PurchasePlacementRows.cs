using System;
using System.Collections.Generic;

namespace LocalSulfur.UnifiedInventory
{
    internal static class PurchasePlacementRows
    {
        internal static IEnumerable<int> Ordered(int total,int height,int first,int visible,int pageBottom,int pageRows)
        {
            if(total<1||height<1||height>total)yield break;
            var seen=new HashSet<int>();
            int bottom=Math.Max(0,pageBottom),top=Math.Min(total,bottom+pageRows);
            int visibleBottom=Math.Max(bottom,first),visibleTop=Math.Min(top,first+visible);
            // Grid Y grows upward: search complete footprints from the visible top downward.
            for(int y=visibleTop-height;y>=visibleBottom;y--)if(seen.Add(y))yield return y;
            for(int y=top-height;y>=bottom;y--)if(seen.Add(y))yield return y;
            for(int y=total-height;y>=0;y--)if(seen.Add(y))yield return y;
        }
    }
}
