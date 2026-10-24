using System;

namespace LocalSulfur.RummageInventoryCompat
{
    internal struct SearchPanelPlacement
    {
        internal float Left, Bottom, Width, Scale, OffsetX, OffsetY;
    }

    internal static class RummageLayout
    {
        internal static SearchPanelPlacement Place(float viewLeft,float viewTop,float viewWidth,
            float screenLeft,float screenBottom,float screenWidth,float screenHeight,float offsetX,float offsetY,bool resultsAbove)
        {
            float available=Math.Max(1,screenWidth-16);
            float scale=Math.Min(1,available/360);
            float width=Math.Min(Math.Max(360,viewWidth/scale),available/scale);
            float left=screenLeft+8,right=Math.Max(left,screenLeft+screenWidth-8-width*scale);
            float bottom=screenBottom+8;
            // Reserve a separate row for the page controls, even at the top of the screen.
            float top=Math.Max(bottom,screenBottom+screenHeight-8-(62+(resultsAbove?148:32))*scale);
            float baseX=Clamp(viewLeft,left,right),baseY=Clamp(viewTop+6,bottom,top);
            float x=Clamp(baseX+Finite(offsetX),left,right),y=Clamp(baseY+Finite(offsetY),bottom,top);
            return new SearchPanelPlacement{Left=x,Bottom=y,Width=width,Scale=scale,OffsetX=x-baseX,OffsetY=y-baseY};
        }
        private static float Clamp(float value,float min,float max){return Math.Max(min,Math.Min(max,value));}
        private static float Finite(float value){return float.IsNaN(value)||float.IsInfinity(value)?0:value;}
    }
}
