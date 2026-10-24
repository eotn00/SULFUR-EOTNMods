using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Mono.Cecil;
using LocalSulfur.TraderFramework;
using LocalSulfur.UnifiedInventory;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;
using PerfectRandom.Sulfur.Core.CharacterStats;

class Program
{
    static int checks;
    static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
    static IEnumerable<TypeDefinition> Types(TypeDefinition t)
    {yield return t;foreach(var n in t.NestedTypes)foreach(var v in Types(n))yield return v;}
    static void Main(string[] args)
    {
        CheckInventoryCopy();
        CheckTradePrices();
        var stockPlan=new Dictionary<int,Queue<int>>{{701,new Queue<int>(new[]{1,1})},{35,new Queue<int>(new[]{2})}};
        var originalDefinition=new {Id=701,Name="original"};var qualityClone=new {Id=701,Name="quality clone"};
        Check(!ReferenceEquals(originalDefinition,qualityClone)&&StockRules.TakeStock(stockPlan,qualityClone.Id)==1,"quality definition clone matched by stable ID");
        Check(StockRules.TakeStock(stockPlan,originalDefinition.Id)==1&&stockPlan[701].Count==0,"each equipment offer accounted once without merging attributes");
        bool unknownRejected=false,extraRejected=false;
        try{StockRules.TakeStock(stockPlan,999);}catch(InvalidOperationException){unknownRejected=true;}
        try{StockRules.TakeStock(stockPlan,701);}catch(InvalidOperationException){extraRejected=true;}
        Check(unknownRejected&&extraRejected,"unexpected IDs and excess offers still rejected");
        Check(PurchasePlacementRows.Ordered(210,1,180,30,140,70).First()==209,"visible top first, not inventory bottom");
        Check(PurchasePlacementRows.Ordered(210,3,160,30,140,70).Take(28).SequenceEqual(Enumerable.Range(160,28).Reverse()),"entire footprint visible");
        Check(PurchasePlacementRows.Ordered(210,1,90,30,70,70).Skip(30).First()==139,"current page before other pages");
        for(int page=0;page<3;page++)for(int scroll=0;scroll<=40;scroll++)for(int height=1;height<=35;height++)
        {
            int bottom=page*70,first=bottom+scroll;
            var rows=PurchasePlacementRows.Ordered(210,height,first,30,bottom,70).ToArray();
            Check(rows.Length==210-height+1&&rows.Distinct().Count()==rows.Length,"all valid origin rows exactly once");
            int visibleCount=Math.Max(0,30-height+1);
            Check(rows.Take(visibleCount).All(y=>y>=first&&y+height<=first+30),"visible region before hidden rows");
            Check(rows.Take(70-height+1).All(y=>y>=bottom&&y+height<=bottom+70),"current page before other pages");
        }
        for(int stock=0;stock<=10000;stock++)
        {
            Check(StockRules.BuyCount(stock,false,false)==stock,"whole stack");
            Check(StockRules.BuyCount(stock,true,false)==(stock==0?0:1),"single");
            Check(StockRules.BuyCount(stock,false,true)==(stock==0?0:Math.Max(1,stock/2)),"floor half");
            Check(StockRules.BuyCount(stock,true,true)==(stock==0?0:1),"control wins");
        }
        foreach(int limit in new[]{1,2,3,1024,99999})for(int n=0;n<300;n++)foreach(bool equipment in new[]{false,true})foreach(bool stack in new[]{false,true})
        {
            var packed=StockRules.PackStock(n,equipment,stack,limit);
            Check(packed.Sum()==n*(equipment?1:2),"generation conservation");
            Check(packed.All(q=>q>0&&q<=(equipment||!stack?1:limit)),"stack cap/equipment exclusion");
        }
        foreach(int limit in new[]{1,2,1024})for(int n=0;n<300;n++)foreach(bool stack in new[]{false,true})
        {
            var packed=StockRules.PackStock(n,false,stack,limit,true);
            Check(packed.Sum()==n,"excluded goods not doubled");
            Check(packed.All(q=>q>0&&q<=(stack?limit:1)),"excluded goods still honor compatible grouping");
        }
        var random=new Random(21023);
        for(int i=0;i<100000;i++)
        {
            int unit=random.Next(int.MaxValue),count=random.Next(1,100000),price;
            bool ok=StockRules.TryPrice(unit,count,out price);
            Check(ok==((long)unit*count<=int.MaxValue),"price overflow rejection");
            if(ok)Check(price==(long)unit*count,"exact price");
        }
        int result;
        Check(!StockRules.TryPrice(-1,2,out result)&&!StockRules.TryPrice(10,0,out result),"invalid quotes");
        Check(StockRules.TryPrice(0,99999,out result)&&result==0,"free item");
        Check(StockRules.TryPrice(int.MaxValue,1,out result)&&result==int.MaxValue,"max price");
        Check(!StockRules.TryPrice(int.MaxValue,2,out result),"no saturated price exploit");
        string root=args[0],game=args[1];
        var resolver=new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(root);resolver.AddSearchDirectory(Path.Combine(game,"Sulfur_Data","Managed"));resolver.AddSearchDirectory(Path.Combine(game,"BepInEx","core"));
        int resolved=0;
        foreach(string file in new[]{"InventoryRedux.dll","TraderFramework.dll"})
        {
            using(var assembly=AssemblyDefinition.ReadAssembly(Path.Combine(root,file),new ReaderParameters{AssemblyResolver=resolver}))
            {
                if(file=="TraderFramework.dll")
                {
                    var allowed=new[]{"mscorlib","System","System.Core","InventoryRedux","PerfectRandom.Sulfur.Core","BepInEx","0Harmony","UnityEngine.CoreModule","UnityEngine.UI","UnityEngine.IMGUIModule","Unity.InputSystem"};
                    foreach(var reference in assembly.MainModule.AssemblyReferences)Check(allowed.Contains(reference.Name),"explicit dependency: "+reference.Name);
                    var plugin=assembly.MainModule.Types.Single(t=>t.Name=="TraderPlugin");
                    var dependency=plugin.CustomAttributes.Single(a=>a.AttributeType.Name=="BepInDependency");
                    Check((string)dependency.ConstructorArguments[0].Value=="local.sulfur.unifiedinventory"&&(string)dependency.ConstructorArguments[1].Value=="0.1.5","declared bridge minimum version");
                    var purchaseCalls=plugin.Methods.Single(m=>m.Name=="Buy").Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().ToArray();
                    Check(purchaseCalls.Any(m=>m.Name=="ReportTransactionFailure")&&!purchaseCalls.Any(m=>m.Name=="Stop"),"purchase rollback failure warns without latching merchant save blocking");
                    var exclusions=plugin.Methods.Single(m=>m.Name=="ExcludedFromMultiplier").Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().Select(m=>m.Name).ToArray();
                    Check(exclusions.Contains("get_IsAttachment")&&exclusions.Contains("get_IsCaliberModifier")&&exclusions.Contains("get_IsValid"),"native attachment, caliber and enchantment classification");
                    Check(plugin.CustomAttributes.Any(a=>a.AttributeType.Name=="BepInIncompatibility"&&(string)a.ConstructorArguments[0].Value=="com.hemito.sulfur.improvedtraders"),"incompatible overlay blocked");
                    Check(!Types(plugin).SelectMany(t=>t.Methods).Any(m=>new[]{"Update","LateUpdate","FixedUpdate"}.Contains(m.Name)),"no frame inventory scanning");
                    Check(plugin.Methods.Single(m=>m.Name=="BeforeGenerate").Parameters[0].ParameterType.IsByReference,"generation changes only new selected list");
                    var pricing=assembly.MainModule.Types.Single(t=>t.Name=="TradePricing");
                    foreach(string name in new[]{"StoreBought","StoreSold"})
                    {
                        var calls=pricing.Methods.Single(m=>m.Name==name).Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().ToArray();
                        Check(calls.Any(m=>m.Name=="SetValue"&&m.DeclaringType.FullName=="System.Reflection.FieldInfo"),"direct history assignment: "+name);
                        Check(!calls.Any(m=>m.Name=="set_BoughtForPrice"||m.Name=="set_SoldForPrice"),"no double normalization through setter prefix: "+name);
                    }
                    var stores=pricing.Methods.SelectMany(m=>m.HasBody?m.Body.Instructions:Enumerable.Empty<Mono.Cecil.Cil.Instruction>())
                        .Where(i=>i.OpCode==Mono.Cecil.Cil.OpCodes.Stfld).Select(i=>(FieldReference)i.Operand);
                    Check(!stores.Any(f=>f.DeclaringType.FullName=="PerfectRandom.Sulfur.Core.Items.InventoryData"&&f.Name!="boughtFor"),"price policy does not mutate quantity, position or item attributes");
                }
                foreach(var type in assembly.MainModule.Types.SelectMany(Types))foreach(var method in type.Methods.Where(m=>m.HasBody))foreach(var instruction in method.Body.Instructions)
                {
                    if(instruction.Operand is MethodReference mr&&!mr.DeclaringType.IsArray){Check(mr.Resolve()!=null,"method contract: "+mr.FullName);resolved++;}
                    else if(instruction.Operand is FieldReference fr){Check(fr.Resolve()!=null,"field contract: "+fr.FullName);resolved++;}
                }
            }
        }
        using(var native=AssemblyDefinition.ReadAssembly(Path.Combine(game,"Sulfur_Data","Managed","PerfectRandom.Sulfur.Core.dll")))
        {
            var item=native.MainModule.GetType("PerfectRandom.Sulfur.Core.Items.InventoryItem");
            foreach(string method in new[]{"UnitAllowedToBuy","UseBuy","HideDescription","set_BoughtForPrice","set_SoldForPrice"})Check(item.Methods.Count(m=>m.Name==method)==1,"reflection contract: "+method);
            var transferCalls=item.Methods.Single(m=>m.Name=="TransferOwnership").Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().ToArray();
            foreach(string name in new[]{"BoughtForPrice","SoldForPrice"})
            {
                Check(item.Fields.Single(f=>f.Name=="<"+name+">k__BackingField").FieldType.FullName=="System.Int32","native history field: "+name);
                Check(transferCalls.Count(m=>m.DeclaringType.FullName==item.FullName&&m.Name=="set_"+name)==1,"exactly one native assignment: "+name);
            }
            var shop=native.MainModule.GetType("PerfectRandom.Sulfur.Core.Units.ShopKeeper");
            Check(shop.Fields.Single(f=>f.Name=="hiddenActivated").FieldType.FullName=="System.Boolean","hidden inventory channel");
            Check(shop.Methods.Count(m=>m.Name=="AddItems"&&m.Parameters.Count==1&&m.Parameters[0].ParameterType.FullName=="System.Collections.Generic.List`1<PerfectRandom.Sulfur.Core.Items.ItemDefinition>")==1,"unambiguous generation boundary");
        }
        Console.WriteLine("PASS "+checks+" checks, including "+resolved+" binary member resolutions. Offline only; Unity UI, callbacks, and disk-write behavior still require a live test.");
    }
    static void CheckTradePrices()
    {
        foreach(int count in new[]{1,2,3,10,99999})foreach(int unit in new[]{0,1,7,100,21000})
        {
            int total;
            if(!StockRules.TryPrice(unit,count,out total))continue;
            int stored=StockRules.UnitHistory(total,count,true),buyback;
            Check(stored==unit,"sold history is a single unit, not the transaction total");
            Check(StockRules.TryPrice(stored,count,out buyback)&&buyback==total,"sell then buyback multiplies quantity only once");
            foreach(int n in new[]{StockRules.BuyCount(count,true,false),StockRules.BuyCount(count,false,true),count})
            {
                int partial;
                Check(StockRules.TryPrice(stored,n,out partial)&&partial==unit*n,"partial buyback has correct payment");
                Check((long)partial+(long)stored*(count-n)==total,"purchase and remainder conserve quote");
            }
        }
        Check(StockRules.UnitHistory(1000,10,true)==100,"regression: ten 100-coin items retain a 100-coin unit price");
        Check(StockRules.UnitHistory(1000,10,false)==1000,"nonstackable equipment history unchanged");
        Check(StockRules.PurchaseHistory(675,false)==675,"equipment purchase history retained");
        Check(StockRules.PurchaseHistory(675,true)==0&&StockRules.PurchaseHistory(0,true)==0,"purchased and picked-up goods use the same native history");
        var ordinary=new InventoryData(new ItemId{value=35},0,0,10,0,CaliberTypes.None,null,null,null,0,1,1);
        var purchased=InventoryDataCopy.Create(ordinary);purchased.boughtFor=675;
        Check(Newtonsoft.Json.JsonConvert.SerializeObject(ordinary)!=Newtonsoft.Json.JsonConvert.SerializeObject(purchased),"reproduce boughtFor stack mismatch");
        purchased.boughtFor=StockRules.PurchaseHistory(purchased.boughtFor,true);
        Check(Newtonsoft.Json.JsonConvert.SerializeObject(ordinary)==Newtonsoft.Json.JsonConvert.SerializeObject(purchased),"old purchased data matches ordinary stack after normalization");
        purchased.identifier="unique-item";
        Check(Newtonsoft.Json.JsonConvert.SerializeObject(ordinary)!=Newtonsoft.Json.JsonConvert.SerializeObject(purchased),"unique identities still remain distinct");
    }
    static void CheckInventoryCopy()
    {
        // Execute the real game data constructors and actual copier, without a Unity scene.
        var stat=new CharacterStat(12.5f);stat.AddModifier(new StatModifier(3,StatModType.Flat,37,123));
        var source=new InventoryData(new ItemId{value=35},4,7,9,11,CaliberTypes._556mm,
            new ItemAttributeCollectionData{itemAttributes=new[]{new ItemAttributeData((ItemAttributes)0,stat)}},
            new[]{new ItemId{value=8}},new[]{new ItemId{value=45}},67,2,3,true,true);
        source.boughtFor=67;source.equipped=true;source.selected=true;source.inventorySlot=6;
        source.identifier="legacy";source.attachments=new[]{"attachment"};source.enchantments=new[]{"enchant"};source.caliber="legacy-caliber";
        bool reproduced=false;
        try{Newtonsoft.Json.JsonConvert.DeserializeObject<InventoryData>(Newtonsoft.Json.JsonConvert.SerializeObject(source));}
        catch(Newtonsoft.Json.JsonSerializationException){reproduced=true;}
        Check(reproduced,"reproduce runtime JSON constructor failure with the actual native type");
        var copy=InventoryDataCopy.Create(source);
        var fields=typeof(InventoryData).GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance);
        var expected=new[]{"id","attachmentIds","enchantmentIds","caliberId","boughtFor","inventorySlot","position","quantity","xSize","ySize","currentAmmo","equipped","selected","rotated","attributes","identifier","attachments","enchantments","caliber"};
        Check(fields.Select(f=>f.Name).OrderBy(n=>n).SequenceEqual(expected.OrderBy(n=>n)),"copier schema covers every native InventoryData field");
        foreach(var field in fields)
        {
            if(field.Name=="attributes")continue;
            var a=field.GetValue(source);var b=field.GetValue(copy);
            if(a is Array array)
            {Check(array.Cast<object>().SequenceEqual(((Array)b).Cast<object>()),"array copied: "+field.Name);Check(!ReferenceEquals(a,b),"array detached: "+field.Name);}
            else Check(Equals(a,b),"field copied: "+field.Name);
        }
        Check(!ReferenceEquals(copy.attributes,source.attributes)&&!ReferenceEquals(copy.attributes.itemAttributes,source.attributes.itemAttributes),"attribute collection detached");
        var copiedStat=copy.attributes.itemAttributes[0].value;
        Check(!ReferenceEquals(copiedStat,stat)&&copiedStat.BaseValue==12.5f&&copiedStat.statModifiers[0].Order==37&&copiedStat.statModifiers[0].SourceId==123,"full attribute state copied");
        copy.position[0]=99;copy.attachmentIds[0]=new ItemId{value=99};copiedStat.BaseValue=99;copiedStat.statModifiers.Clear();
        Check(source.position[0]==4&&source.attachmentIds[0].value==8&&stat.BaseValue==12.5f&&stat.statModifiers.Count==1,"mutating purchase copy does not change source");
        source.attributes=null;source.position=null;source.attachmentIds=null;source.enchantmentIds=null;source.attachments=null;source.enchantments=null;
        copy=InventoryDataCopy.Create(source);
        Check(copy.attributes==null&&copy.position==null&&copy.attachments==null&&copy.attachmentIds==null,"null data preserved");
    }
}
