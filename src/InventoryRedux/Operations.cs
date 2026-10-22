using System;
using System.Collections.Generic;
using System.Linq;

namespace LocalSulfur.UnifiedInventory
{
    public sealed class Placement
    {
        public int Key, X, Y, Width, Height;
        public bool Locked;
        public Placement Copy(){return (Placement)MemberwiseClone();}
    }

    public static class Operations
    {
        public static bool SelectionLockValue(IEnumerable<bool> locks)
        {return locks.Any(locked=>!locked);}
        public static int PageBottom(int rowsPerPage,int pages,int page)
        {return (pages-1-Math.Max(0,Math.Min(page,pages-1)))*rowsPerPage;}
        public static bool CanStack(bool equipment,bool automaticConsume,bool stateful)
        {return !equipment&&!automaticConsume&&!stateful;}

        public static int MergeAmount(int source,int destination,int maximum)
        {
            if(source<1||destination<1||maximum<1)throw new ArgumentOutOfRangeException();
            return Math.Min(source,Math.Max(0,maximum-destination));
        }

        public static int SplitAmount(int quantity,bool half)
        {return quantity<2?0:half?quantity/2:1;}
        public static int PourAmount(int quantity,bool half)
        {return quantity<1?0:half?Math.Max(1,quantity/2):1;}
        public static int[] QuestRemainders(int[] ids,int[] quantities,IDictionary<int,int> required)
        {
            if(ids.Length!=quantities.Length)throw new ArgumentException("Mismatched quest items");
            var needs=new Dictionary<int,int>(required);var result=new int[ids.Length];
            for(int i=0;i<ids.Length;i++)
            {
                if(quantities[i]<1)throw new ArgumentException("Invalid quest quantity");
                int need;needs.TryGetValue(ids[i],out need);
                int take=Math.Min(quantities[i],Math.Max(0,need));needs[ids[i]]=need-take;result[i]=quantities[i]-take;
            }
            return needs.Values.Any(n=>n>0)?null:result;
        }

        public static bool WithinPage(int y,int height,int pageRows)
        {return pageRows<=0||(y>=0&&height>0&&y/pageRows==(y+height-1)/pageRows);}
        public static bool Valid(int width,int height,IEnumerable<Placement> items,int pageRows=0)
        {
            if(width<1||height<1||width>100||height>500)return false;
            var used=new bool[width,height];var keys=new HashSet<int>();
            foreach(var item in items)
            {
                if(!keys.Add(item.Key)||!Fits(used,item.X,item.Y,item.Width,item.Height,pageRows))return false;
                Mark(used,item,true);
            }
            return true;
        }

        private static bool Fits(bool[,] occupied,int x,int y,int width,int height,int pageRows=0)
        {
            if(!WithinPage(y,height,pageRows))return false;
            if(x<0||y<0||width<1||height<1||x>occupied.GetLength(0)-width||y>occupied.GetLength(1)-height)return false;
            for(int row=y;row<y+height;row++)for(int col=x;col<x+width;col++)if(occupied[col,row])return false;
            return true;
        }

        private static void Mark(bool[,] occupied,Placement item,bool value)
        {for(int y=item.Y;y<item.Y+item.Height;y++)for(int x=item.X;x<item.X+item.Width;x++)occupied[x,y]=value;}

        // Plan entirely off-grid: failure leaves the caller's items untouched.
        public static List<Placement> Sort(int width,int height,IList<Placement> items,int pageRows=0)
        {
            if(!Valid(width,height,items))return null;
            var occupied=new bool[width,height];var result=new List<Placement>();
            foreach(var item in items.Where(p=>p.Locked)) {var copy=item.Copy();result.Add(copy);Mark(occupied,copy,true);}
            foreach(var item in items.Where(p=>!p.Locked).OrderByDescending(p=>p.Width*p.Height).ThenByDescending(p=>p.Height).ThenBy(p=>p.Key))
            {
                Placement placed=null;
                for(int y=height-item.Height;y>=0&&placed==null;y--)
                    for(int x=0;x<=width-item.Width;x++)
                        if(Fits(occupied,x,y,item.Width,item.Height,pageRows))
                        {placed=item.Copy();placed.X=x;placed.Y=y;break;}
                if(placed==null)return null;
                result.Add(placed);Mark(occupied,placed,true);
            }
            return result;
        }

        public static List<Placement> MoveGroup(int width,int height,IList<Placement> all,ISet<int> selected,int dx,int dy)
        {
            if(selected.Count==0||!Valid(width,height,all)||selected.Any(k=>!all.Any(p=>p.Key==k)))return null;
            var result=new List<Placement>();
            foreach(var item in all)
            {
                var copy=item.Copy();
                if(selected.Contains(copy.Key))
                {long x=(long)copy.X+dx,y=(long)copy.Y+dy;if(x<0||y<0||x>width||y>height)return null;copy.X=(int)x;copy.Y=(int)y;}
                result.Add(copy);
            }
            return Valid(width,height,result)?result:null;
        }

        public static List<Placement> PackGroup(int width,int height,IList<Placement> occupied,IList<Placement> group,int x,int y,int pageRows=0)
        {
            if(x<0||y<0||x>=width||y>=height||group.Count==0||!Valid(width,height,occupied))return null;
            var used=new bool[width,height];foreach(var p in occupied)Mark(used,p,true);
            var result=new List<Placement>();
            foreach(var p in group.OrderByDescending(p=>p.Width*p.Height).ThenBy(p=>p.Key))
            {
                Placement found=null;
                // Fill complete rows through the target area before extending elsewhere.
                for(int pass=0;pass<2&&found==null;pass++)
                    for(int row=pass==0?y:height-1;row>=(pass==0?0:y+1)&&found==null;row--)
                        for(int col=0;col<width;col++)
                            if(Fits(used,col,row,p.Width,p.Height,pageRows)){found=p.Copy();found.X=col;found.Y=row;break;}
                if(found==null)return null;
                result.Add(found);Mark(used,found,true);
            }
            return result;
        }

        public static List<Placement> Resize(int width,int height,IList<Placement> items,int pageRows=0)
        {
            if(Valid(width,height,items,pageRows))return items.Select(p=>p.Copy()).ToList();
            var locked=items.Where(p=>p.Locked).ToList();
            if(!Valid(width,height,locked,pageRows))return null;
            var movable=items.Where(p=>!p.Locked).ToList();if(movable.Count==0)return locked;
            var packed=PackGroup(width,height,locked,movable,0,height-1,pageRows);
            return packed==null?null:locked.Concat(packed).ToList();
        }
    }
}
