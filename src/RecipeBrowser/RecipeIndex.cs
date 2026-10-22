using System.Collections.Generic;

namespace LocalSulfur.RecipeBrowser
{
    public sealed class MaterialEntry { public int Id, Count; }
    public sealed class RecipeEntry
    {
        public int Id, Product, Count;
        public List<MaterialEntry> Ingredients=new List<MaterialEntry>();
    }
    public sealed class RecipeIndex
    {
        public readonly Dictionary<int,List<RecipeEntry>> Uses=new Dictionary<int,List<RecipeEntry>>();
        public readonly Dictionary<int,List<RecipeEntry>> Sources=new Dictionary<int,List<RecipeEntry>>();
        public RecipeIndex(IEnumerable<RecipeEntry> recipes)
        {
            var seen=new HashSet<int>();
            foreach(var r in recipes)
            {
                if(r.Id<=0||r.Product<=0||r.Count<=0||r.Ingredients==null||r.Ingredients.Count==0||!seen.Add(r.Id))continue;
                bool valid=true;foreach(var m in r.Ingredients)if(m==null||m.Id<=0||m.Count<=0)valid=false;
                if(!valid)continue;
                Add(Sources,r.Product,r);
                var inputs=new HashSet<int>();
                foreach(var m in r.Ingredients)if(inputs.Add(m.Id))Add(Uses,m.Id,r);
            }
        }
        private static void Add(Dictionary<int,List<RecipeEntry>> index,int id,RecipeEntry recipe)
        {List<RecipeEntry> list;if(!index.TryGetValue(id,out list))index.Add(id,list=new List<RecipeEntry>());list.Add(recipe);}
        public int ProductCount(int id)
        {List<RecipeEntry> list;if(!Uses.TryGetValue(id,out list))return 0;var ids=new HashSet<int>();foreach(var r in list)ids.Add(r.Product);return ids.Count;}
    }
}
