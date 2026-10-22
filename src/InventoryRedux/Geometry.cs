using System;
using System.Collections.Generic;
using System.IO;

namespace LocalSulfur.UnifiedInventory
{
    public sealed class Footprint
    {
        public int X, Y, Width, Height;
    }

    public static class Geometry
    {
        public static int Clamp(int value, int low, int high)
        { return Math.Max(low, Math.Min(high, value)); }

        public static void Fit(ref int columns, ref int rows, IEnumerable<Footprint> items)
        {
            if(columns<1 || columns>100 || rows<1 || rows>500)
                throw new InvalidDataException("Grid capacity outside supported bounds");
            var occupied=new HashSet<int>();
            foreach(var item in items)
            {
                if(item.X<0 || item.Y<0 || item.Width<1 || item.Height<1 ||
                   item.X>100-item.Width || item.Y>500-item.Height)
                    throw new InvalidDataException("Invalid saved item footprint");
                columns=Math.Max(columns,item.X+item.Width);
                rows=Math.Max(rows,item.Y+item.Height);
                for(int y=item.Y;y<item.Y+item.Height;y++)
                    for(int x=item.X;x<item.X+item.Width;x++)
                        if(!occupied.Add(y*100+x))
                            throw new InvalidDataException("Overlapping saved items");
            }
        }

        public static int FirstRow(float offset, float tileHeight, int rows, int visible)
        {
            if(float.IsNaN(offset)||float.IsInfinity(offset)||tileHeight<=0)
                throw new ArgumentOutOfRangeException("offset");
            return Clamp((int)Math.Floor(offset/tileHeight),0,Math.Max(0,rows-visible));
        }

        public static bool Intersects(int itemRow,int height,int first,int visible)
        { return height>0 && itemRow<first+visible && itemRow+height>first; }
    }
}
