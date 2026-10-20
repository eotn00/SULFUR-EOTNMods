using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using LocalSulfur.ChurchCrafting;

class ChurchCraftingTests
{
    static int checks;
    static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    static void Main(string[] args)
    {
        List<Spend> spend;
        var materials = new List<Material> {
            new Material { Token=0, Id=5, Count=3 }, new Material { Token=1, Id=5, Count=9 },
            new Material { Token=2, Id=6, Count=4 } };
        Check(CraftPlan.Ingredients(materials, new Dictionary<int,int>{{5,8},{6,2}}, out spend), "split sources");
        Check(spend.Count==3 && spend[0].Token==0 && spend[0].Count==3 && spend[1].Count==5, "priority and partial stacks");
        Check(materials[0].Count==3 && materials[1].Count==9, "planner is read only");
        Check(!CraftPlan.Ingredients(materials, new Dictionary<int,int>{{5,13}}, out spend) && spend.Count==0, "insufficient no partial plan");
        Check(!CraftPlan.Ingredients(materials, new Dictionary<int,int>{{0,1}}, out spend), "invalid id");
        Check(!CraftPlan.Ingredients(materials, new Dictionary<int,int>{{5,0}}, out spend), "zero requirement");
        Check(!CraftPlan.Ingredients(materials, new Dictionary<int,int>(), out spend), "empty recipe");
        List<Destination> output;
        var empty = new List<Material>();
        var board = new bool[2,2];
        board[0,0]=true; board[1,0]=true; board[0,1]=true; board[1,1]=true;
        Check(!CraftPlan.Outputs(board,2,1,1,10,1,empty,out output) && output.Count==0, "full backpack blocks");
        Check(CraftPlan.Outputs(board,2,1,1,10,3,new List<Material>{new Material{Token=4,Count=3}},out output), "full backpack can merge");
        Check(output.Count==1 && output[0].Token==4 && output[0].Count==3, "stack target");
        board[1,1]=false;
        Check(CraftPlan.Outputs(board,2,1,1,10,13,new List<Material>{new Material{Token=4,Count=3}},out output), "merge plus new stack");
        Check(output.Count==2 && output[1].X==1 && output[1].Y==1 && output[1].Count==10, "new stack limit");
        Check(!CraftPlan.Outputs(board,2,1,1,10,14,new List<Material>{new Material{Token=4,Count=3}},out output), "batch capacity preflight");
        Check(CraftPlan.Outputs(new bool[2,1],1,1,2,1,1,empty,out output) && output[0].Rotated, "rotated placement");
        var pages = new bool[1,4]; pages[0,0]=true; pages[0,3]=true;
        Check(!CraftPlan.Outputs(pages,2,1,2,1,1,empty,out output), "no cross page placement");
        pages[0,3]=false;
        Check(CraftPlan.Outputs(pages,2,1,2,1,1,empty,out output) && output[0].Y==2, "search other page");
        Check(!pages[0,2] && !pages[0,3], "output planner read only");
        Check(!CraftPlan.Outputs(new bool[1,1],1,0,1,1,1,empty,out output), "bad dimensions");
        var random = new Random(7419);
        for (int test=0;test<300;test++)
        {
            int w=random.Next(1,15), h=random.Next(1,15), iw=random.Next(1,5), ih=random.Next(1,5), max=random.Next(1,21), quantity=random.Next(1,61);
            var cells=new bool[w,h];
            for(int x=0;x<w;x++) for(int y=0;y<h;y++) cells[x,y]=random.Next(4)==0;
            var original=(bool[,])cells.Clone();
            bool success=CraftPlan.Outputs(cells,h,iw,ih,max,quantity,empty,out output);
            int total=0;
            foreach(var place in output)
            {
                Check(place.Count>0 && place.Count<=max,"random stack cap");
                total+=place.Count;
                int pw=place.Rotated?ih:iw, ph=place.Rotated?iw:ih;
                Check(place.X>=0 && place.Y>=0 && place.X+pw<=w && place.Y+ph<=h,"random bounds");
                for(int x=place.X;x<place.X+pw;x++) for(int y=place.Y;y<place.Y+ph;y++)
                { Check(!original[x,y],"random no overlap"); original[x,y]=true; }
            }
            Check(success ? total==quantity : output.Count==0,"random all or nothing");
        }
        if (args.Length != 1) throw new ArgumentException("Pass the SULFUR game directory");
        Contracts(args[0]);
        Console.WriteLine(checks + " checks passed, including 300 randomized output plans and installed-mod API contracts.");
    }
    static void Contracts(string game)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e) {
            string name=new AssemblyName(e.Name).Name+".dll";
            foreach(string dir in new[]{game+@"\Sulfur_Data\Managed",game+@"\BepInEx\core",game+@"\BepInEx\plugins"})
                if(File.Exists(Path.Combine(dir,name))) return Assembly.LoadFrom(Path.Combine(dir,name));
            return null;
        };
        string[] dlls={"堆叠mod.dll","锁定格子.dll","大背包.dll","大仓库.dll"};
        string[] types={"StackingMod.StackRuntime","InventoryLockMod.LockRuntime","AetharNet.Mods.bigbk.BackpackPaging","AetharNet.Mods.bigbk.StashSaveGuard"};
        string[][] methods={new[]{"CanStack","MaxFor","RefreshBadge"},new[]{"IsItemLocked","IsSlotLocked"},new[]{"GetVisibleRows"},new[]{"Prefix"}};
        for(int i=0;i<dlls.Length;i++)
        {
            Type type=Assembly.LoadFrom(game+@"\BepInEx\plugins\"+dlls[i]).GetType(types[i]);
            Check(type!=null,"installed mod type "+types[i]);
            foreach(string name in methods[i]) Check(type.GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!=null,"installed API "+name);
        }
        var gameplay=Assembly.LoadFrom(game+@"\Sulfur_Data\Managed\PerfectRandom.Sulfur.Gameplay.dll");
        Type variant=gameplay.GetType("PerfectRandom.Sulfur.Gameplay.RecipeListItemVariant");
        Check(variant.GetProperty("HasIngredients",BindingFlags.Instance|BindingFlags.NonPublic)!=null,"ingredient hook");
        Check(variant.GetMethod("CookItem")!=null,"craft hook");
        Check(variant.GetProperty("CraftingRecipe")!=null,"recipe access");
    }
}
