using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using LocalSulfur.RecipeBrowser;
class Program
{
    static int checks;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
    static RecipeEntry Recipe(int id,int product,params int[] ingredients){return new RecipeEntry{Id=id,Product=product,Count=1,Ingredients=ingredients.Select(i=>new MaterialEntry{Id=i,Count=2}).ToList()};}
    static void Main(string[] args)
    {
        var rows=new[]{Recipe(1,10,2,2),Recipe(2,10,2,3),Recipe(3,11,2),Recipe(1,10,2),Recipe(4,12,0),Recipe(5,0,2)};
        var data=new RecipeIndex(rows);
        Check(data.ProductCount(2)==2,"distinct products not recipe variants");
        Check(data.Uses[2].Count==3,"duplicate ingredient or recipe not double indexed");
        Check(data.Sources[10].Count==2,"all source variants");Check(!data.Sources.ContainsKey(12),"invalid ingredient excluded");Check(data.ProductCount(999)==0,"unrelated item");
        var random=new Random(19);
        for(int pass=0;pass<100;pass++)
        {
            var recipes=Enumerable.Range(1,100).Select(id=>Recipe(id,random.Next(1,20),random.Next(1,20),random.Next(1,20))).ToArray();
            data=new RecipeIndex(recipes);
            for(int id=1;id<20;id++)
            {
                Check(data.ProductCount(id)==recipes.Where(r=>r.Ingredients.Any(m=>m.Id==id)).Select(r=>r.Product).Distinct().Count(),"random uses count");
                Check((data.Sources.TryGetValue(id,out var list)?list.Count:0)==recipes.Count(r=>r.Product==id),"random source count");
            }
        }
        using var dll=AssemblyDefinition.ReadAssembly(args[0]);using var core=AssemblyDefinition.ReadAssembly(args[1]);
        var plugin=dll.MainModule.Types.Single(t=>t.Name=="RecipeBrowserPlugin");
        var index=plugin.Methods.Single(m=>m.Name=="Index");
        Check(plugin.Fields.Any(f=>f.Name=="showAllRecipes"),"all recipes configuration");
        Check(index.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="indexedAllRecipes"),"mode participates in cache key");
        var awake=plugin.Methods.Single(m=>m.Name=="Awake");
        Check(awake.Body.Instructions.Any(i=>i.Operand is string s&&s=="ShowAllRecipes"),"stable all recipes configuration key");
        Check(index.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="get_IsLearned"),"native unlock filter");
        Check(index.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="GetRawList"),"complete recipe database");
        Check(index.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="SetEquals"),"unlock changes refresh index even if count unchanged");
        foreach(var method in dll.MainModule.Types.SelectMany(t=>t.Methods).Where(m=>m.HasBody))
            Check(!method.Body.Instructions.Any(i=>i.Operand is MethodReference m&&(m.Name=="WriteActiveToDisk"||m.Name=="WriteToDisk"||m.Name=="CookItem"||m.Name=="SaveInventoryData")),"read-only "+method.Name);
        foreach(var method in plugin.Methods)
        {
            var patch=method.CustomAttributes.FirstOrDefault(a=>a.AttributeType.Name=="HarmonyPatch");if(patch==null)continue;
            var type=(TypeReference)patch.ConstructorArguments[0].Value;var name=(string)patch.ConstructorArguments[1].Value;
            Check(core.MainModule.GetType(type.FullName).Methods.Any(m=>m.Name==name),"hook "+name);
        }
        foreach(string name in new[]{"Use","DropFromPlayer","OnBeginDrag","OnPointerEnter"})Check(core.MainModule.GetType("PerfectRandom.Sulfur.Core.Items.InventoryItem").Methods.Any(m=>m.Name==name),"modal target "+name);
        var update=plugin.Methods.Single(m=>m.Name=="Update");Check(!update.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="GetRawList"),"no per-frame recipe scan");
        var window=dll.MainModule.Types.Single(t=>t.Name=="RecipeWindow");
        Check(window.Fields.Any(f=>f.Name=="allRecipes"&&f.FieldType.Name=="Toggle"),"in-window recipe visibility toggle");
        Check(window.Methods.Single(m=>m.Name=="Render").Body.Instructions.Any(i=>i.Operand is string s&&s=="查看所有配方"),"Chinese toggle label");
        Check(window.Methods.Single(m=>m.Name=="Render").Body.Instructions.Any(i=>i.Operand is string s&&s=="Show all recipes"),"English toggle label");
        Check(plugin.Methods.Single(m=>m.Name=="Tooltip").Body.Instructions.Any(i=>i.Operand is string s&&s==" for details"),"localized details hint");
        Check(window.Fields.Any(f=>f.Name=="byProduct"),"product grouping");
        Check(window.Methods.Single(m=>m.Name=="AddIcon").Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="artwork"),"native item artwork");
        Check(window.Methods.Single(m=>m.Name=="ShowTooltip").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="Setup"&&m.DeclaringType.Name=="ItemDescription"),"native tooltip setup");
        Check(window.Methods.Single(m=>m.Name=="HideTooltip").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="set_overrideSorting"),"tooltip sorting restored");
        Check(window.Methods.Single(m=>m.Name=="ShowTooltip").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="SetParent"),"tooltip hosted under modal overlay");
        Check(window.Methods.Single(m=>m.Name=="HideTooltip").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="SetParent"),"tooltip returned to original parent");
        Check(window.Methods.Single(m=>m.Name=="HideTooltip").Body.ExceptionHandlers.Any(h=>h.HandlerType==Mono.Cecil.Cil.ExceptionHandlerType.Finally),"tooltip restoration protected by finally");
        Check(dll.MainModule.Types.Any(t=>t.Name=="RecipeFlowLayout"),"recipe wrap layout");
        Console.WriteLine("PASS: "+checks+" index, unlock, API, read-only and modal checks. UI rendering and interaction require live testing.");
    }
}
