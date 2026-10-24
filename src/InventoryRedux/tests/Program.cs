using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using LocalSulfur.UnifiedInventory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Mono.Cecil;

class Program
{
    static int checks;
    static void Check(bool test,string label){if(!test)throw new Exception(label);checks++;}
    static string Snapshot(object value){return JsonConvert.SerializeObject(value);}
    static void Main(string[] args)
    {
        CheckLoadAudit();
        Check(Operations.SelectionLockValue(new[]{false,false}),"unlocked selection locks all");
        Check(Operations.SelectionLockValue(new[]{true,false,true}),"mixed selection locks all");
        Check(!Operations.SelectionLockValue(new[]{true,true}),"locked selection unlocks all");
        Check(!Operations.SelectionLockValue(new bool[0]),"empty selection no lock operation");
        Check(Operations.SelectionLockValue(new[]{false})&&!Operations.SelectionLockValue(new[]{true}),"single selection toggles");
        for(int fault=0;fault<5;fault++)
        {
            int source=10,target=0;bool stopped=false,restored=false,finished=false;
            int stage=fault;
            bool ok=QuantityTransaction.Run(()=>{target=5;if(stage==1)throw new Exception("create");source=5;if(stage==2)throw new Exception("deduct");},
                ()=>{if(stage==3)throw new Exception("callback");},
                ()=>{source=10;target=0;},()=>{finished=true;if(stage==4)throw new Exception("cleanup");},e=>restored=true,e=>stopped=true);
            Check(stage==0?ok&&source==5&&target==5:stage==4?!ok&&stopped&&source==5&&target==5:!ok&&restored&&source==10&&target==0,"transaction fault recovery");
            Check(finished==(stage==0||stage==4),"no destruction before verified commit");
        }
        bool rollbackStopped=false;
        QuantityTransaction.Run(()=>{throw new Exception("change");},()=>{},()=>{throw new Exception("rollback");},()=>{},e=>{},e=>rollbackStopped=true);
        Check(rollbackStopped,"failed rollback reports the recovery failure");
        foreach(int initial in new[]{1,2,9,10})foreach(int existing in new[]{0,7})foreach(bool half in new[]{false,true})foreach(bool fault in new[]{false,true})
        {
            int source=initial,target=existing,amount=Operations.PourAmount(initial,half);bool destroyed=false;
            bool ok=QuantityTransaction.Run(()=>{target+=amount;source-=amount;if(fault)throw new Exception("refresh");},
                ()=>{Check(source+target==initial+existing,"transaction conserves total");},
                ()=>{source=initial;target=existing;},()=>{destroyed=source==0;},e=>{},e=>{throw e;});
            Check(ok!=fault&&source==(fault?initial:initial-amount)&&target==(fault?existing:existing+amount),"single/half new/existing stack transaction");
            Check(destroyed==(!fault&&initial==amount),"last unit destroyed only on commit");
        }
        for(int q=2;q<=4096;q++)
        {
            int half=Operations.SplitAmount(q,true);Check(half>0&&q-half>0&&half+q-half==q,"split conserves quantity");
            Check(Operations.SplitAmount(q,false)==1,"single split");
        }
        Check(!Operations.CanStack(true,false,false),"equipment cannot stack");
        Check(!Operations.CanStack(false,true,false)&&!Operations.CanStack(false,false,true),"special states excluded");
        var random=new Random(1941);
        for(int n=0;n<2000;n++)
        {
            int width=random.Next(5,31),height=random.Next(5,51);var items=new List<Placement>();
            for(int k=0;k<50;k++)
            {
                var item=new Placement{Key=k,X=random.Next(width),Y=random.Next(height),Width=random.Next(1,5),Height=random.Next(1,5),Locked=random.Next(4)==0};
                var trial=items.Concat(new[]{item}).ToList();if(Operations.Valid(width,height,trial))items.Add(item);
            }
            string before=Snapshot(items);var plan=Operations.Sort(width,height,items);
            Check(Snapshot(items)==before,"sort plan does not mutate items");
            if(plan!=null)
            {
                Check(Operations.Valid(width,height,plan),"no sorted overlap/out-of-bounds");
                Check(plan.Count==items.Count&&plan.Select(p=>p.Key).OrderBy(k=>k).SequenceEqual(items.Select(p=>p.Key).OrderBy(k=>k)),"sort preserves identities");
                foreach(var locked in items.Where(p=>p.Locked))
                {var after=plan.Single(p=>p.Key==locked.Key);Check(after.X==locked.X&&after.Y==locked.Y,"locked position preserved");}
            }
            var selected=new HashSet<int>(items.Where(p=>random.Next(2)==0).Select(p=>p.Key));
            var moved=Operations.MoveGroup(width,height,items,selected,random.Next(-6,7),random.Next(-6,7));
            Check(Snapshot(items)==before,"group failure leaves original unchanged");
            if(moved!=null)Check(Operations.Valid(width,height,moved)&&moved.Count==items.Count,"group validity and preservation");
            int source=random.Next(1,5000),target=random.Next(1,5000),max=random.Next(2,1025),add=Operations.MergeAmount(source,target,max);
            Check(add>=0&&add<=source&&source-add+target+add==source+target,"merge conserves count");
            Check(target>=max?add==0:target+add<=max,"merge limit and legacy overfull preservation");
        }
        var single=new List<Placement>{new Placement{Key=1,X=1,Y=1,Width=2,Height=2,Locked=true}};
        var blocked=new List<Placement>{new Placement{Key=20,X=2,Y=2,Width=2,Height=2}};
        var group=new List<Placement>{new Placement{Key=1,Width=2,Height=2},new Placement{Key=2,Width=1,Height=3}};
        var packed=Operations.PackGroup(8,8,blocked,group,2,2);
        Check(packed!=null&&Operations.Valid(8,8,blocked.Concat(packed)),"group fallback avoids obstacles");
        Check(packed.Count==2&&packed.Select(p=>p.Key).OrderBy(k=>k).SequenceEqual(new[]{1,2}),"group fallback preserves all identities");
        Check(Operations.PackGroup(2,2,new List<Placement>(),group,0,0)==null,"insufficient group space rejected atomically");
        Check(Operations.PackGroup(8,8,blocked,group,-1,0)==null,"outside cursor rejected");
        var shrinkInput=new List<Placement>{new Placement{Key=0,X=7,Y=5,Width=2,Height=2},new Placement{Key=1,X=0,Y=0,Width=1,Height=1,Locked=true}};
        var shrink=Operations.Resize(4,4,shrinkInput);
        Check(shrink!=null&&Operations.Valid(4,4,shrink),"live resize repacks out-of-bounds items");
        Check(shrink.Single(p=>p.Key==1).X==0&&shrink.Single(p=>p.Key==1).Y==0,"resize preserves lock position");
        Check(Operations.Resize(1,1,shrinkInput)==null,"unsafe resize rejected");
        Check(shrinkInput[0].X==7&&shrinkInput[0].Y==5,"resize planner never mutates original");
        var filling=Operations.PackGroup(4,4,new List<Placement>(),Enumerable.Range(0,6).Select(i=>new Placement{Key=i,Width=1,Height=1}).ToList(),2,3);
        Check(filling.Count(p=>p.Y==3)==4&&filling.Count(p=>p.Y==2)==2,"fallback fills row before extending");
        var manual=Operations.MoveGroup(10,10,single,new HashSet<int>{1},2,3);
        Check(manual!=null&&manual[0].X==3&&manual[0].Y==4&&manual[0].Locked,"manual movement of locked item allowed");
        Check(Operations.MoveGroup(10,10,single,new HashSet<int>{1},100,0)==null,"invalid move rejected");
        for(int rows=1;rows<=500;rows++)for(int vis=1;vis<=Math.Min(40,rows);vis++)
        {
            int first=Geometry.FirstRow(999999,42,rows,vis);Check(first+vis<=rows&&first>=0,"viewport bounded");
            Check(Geometry.Intersects(Math.Max(0,first-1),2,first,vis),"cross-boundary large item visible");
        }
        string game=Environment.GetEnvironmentVariable("SULFUR_TEST_GAME_ROOT")??throw new Exception("Set SULFUR_TEST_GAME_ROOT");
        using var core=AssemblyDefinition.ReadAssembly(game+@"\Sulfur_Data\Managed\PerfectRandom.Sulfur.Core.dll");
        using var gameplay=AssemblyDefinition.ReadAssembly(game+@"\Sulfur_Data\Managed\PerfectRandom.Sulfur.Gameplay.dll");
        var quest=gameplay.MainModule.GetType("PerfectRandom.Sulfur.Gameplay.QuestGiver");
        var combination=quest.Methods.Single(m=>m.Name=="UpdateCombination");
        Check(combination.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.Name=="get_Count"&&m.DeclaringType is GenericInstanceType g&&g.GenericArguments.Any(a=>a.Name=="InventoryItem"))==1,"quest total object count has one patch target");
        Check(combination.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="ItemCount"),"quest per-type quantity validation preserved");
        Check(quest.Methods.Single(m=>m.Name=="CompleteQuest").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="RemoveAllItems"),"native quest completion removes submitted contents");
        TypeDefinition Type(string n)=>core.MainModule.Types.Single(t=>t.Name==n);
        foreach(var entry in new[]{("ItemGrid","GetGridPosition"),("ItemGrid","AcquireVisuals"),("ItemGrid","ReleaseVisuals"),("InventoryItem","Consume"),("InventoryItem","GetConsumedByExternalFactor"),("InventoryItem","DropOnOccupiedItemGridSlot"),("InventoryItem","DropOnOccupiedPaperDollSlot"),("ThrowableWeapon","AfterThrow"),("ServiceStation","PositionUI")})
            Check(Type(entry.Item1).Methods.Count(m=>m.Name==entry.Item2)==1,"unambiguous hook "+entry);
        var write=Type("FileAccess_PC").Methods.Single(m=>m.Name=="WriteToDisk"&&m.Parameters[0].ParameterType.FullName=="System.String");
        Check(write.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.DeclaringType.Name=="JsonUtility"&&m.Name=="ToJson")==1,"PC writer serializer replacement");
        var read=Type("FileAccess_PC").NestedTypes.Single(t=>t.Name.Contains("ReadFromDisk")).Methods.Single(m=>m.Name=="MoveNext");
        Check(read.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.DeclaringType.Name=="JsonUtility"&&m.Name=="FromJson")==1,"PC reader serializer replacement");
        using var plugin=AssemblyDefinition.ReadAssembly(args[0]);
        var stacks=plugin.MainModule.GetType("LocalSulfur.UnifiedInventory.Stacks");
        var pluginType=plugin.MainModule.GetType("LocalSulfur.UnifiedInventory.Plugin");
        var audit=pluginType.Methods.Single(m=>m.Name=="Audit");
        Check(!audit.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="Fail"),"load differences do not activate save blocking");
        Check(audit.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="LogWarning"),"load differences are logged as warnings");
        var transactionWarning=pluginType.Methods.Single(m=>m.Name=="TransactionFailed");
        Check(!transactionWarning.Body.Instructions.Any(i=>i.Operand is MethodReference m&&new[]{"Fail","WriteToDisk","WriteActiveToDisk"}.Contains(m.Name)),"transaction warning neither safety-stops nor initiates saving");
        Check(!transactionWarning.Body.Instructions.Any(i=>i.OpCode==Mono.Cecil.Cil.OpCodes.Stsfld&&i.Operand is FieldReference f&&f.Name=="failed"),"failed recovery does not latch the save-blocking state");
        Check(transactionWarning.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="LogWarning"),"failed recovery is reported");
        Check(stacks.Methods.Single(m=>m.Name=="PlaceHeld").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="TransactionFailed"),"held-placement failure uses warning callback");
        var management=plugin.MainModule.GetType("LocalSulfur.UnifiedInventory.Management");
        foreach(string name in new[]{"Resize","Commit"})
        {
            var calls=management.Methods.Single(m=>m.Name==name).Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().ToArray();
            Check(calls.Any(m=>m.Name=="TransactionFailed")&&!calls.Any(m=>m.Name=="Fail"),"inventory rollback failure warns: "+name);
        }
        Check(!plugin.MainModule.Types.SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions)
            .Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="UnityEngine.Application"&&m.Name=="Quit"),"no custom exit implementation");
        Check(!stacks.Methods.Single(m=>m.Name=="PlaceHeld").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="Locked"),"manual held placement accepts locked source");
        Check(stacks.Methods.Single(m=>m.Name=="Split").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="UnlockNewPortion"),"split portion starts unlocked");
        foreach(string methodName in new[]{"Add","MergeForSort","DepositExisting"})
            Check(stacks.Methods.Single(m=>m.Name==methodName).Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="OrderByDescending"),"locked receiving stacks prioritized: "+methodName);
        Check(stacks.Methods.Single(m=>m.Name=="MergeForSort").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="Locked"),"sort checks donor lock before merging");
        Check(stacks.Methods.Single(m=>m.Name=="DepositExisting").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="Locked"),"top up skips locked source items");
        Check(Operations.QuestRemainders(new[]{1},new[]{10},new Dictionary<int,int>{{1,5}}).SequenceEqual(new[]{5}),"quest ten supplied five returned");
        Check(Operations.QuestRemainders(new[]{1,1,2},new[]{3,7,4},new Dictionary<int,int>{{1,5}}).SequenceEqual(new[]{0,5,4}),"quest consumes across stacks and returns unrelated items");
        Check(Operations.QuestRemainders(new[]{1},new[]{4},new Dictionary<int,int>{{1,5}})==null,"insufficient quest cannot submit");
        for(int supplied=1;supplied<=200;supplied++)for(int needed=1;needed<=supplied;needed++)
            Check(Operations.QuestRemainders(new[]{1},new[]{supplied},new Dictionary<int,int>{{1,needed}})[0]==supplied-needed,"quest remainder conservation");
        for(int quantity=1;quantity<=10000;quantity++)
        {
            int one=Operations.PourAmount(quantity,false),half=Operations.PourAmount(quantity,true);
            Check(one==1&&half>=1&&half<=quantity,"held stack placement count is valid");
            Check(half==Math.Max(1,quantity/2),"half placement rounds down, retains last single");
            Check(quantity-half+half==quantity,"placement conserves quantity");
        }
        var quick=plugin.MainModule.GetType("LocalSulfur.UnifiedInventory.QuickActions");
        var quickUpdate=quick.Methods.Single(m=>m.Name=="Update");
        foreach(string action in new[]{"Use","DropFromPlayer","PlaceHeld"})
            Check(quickUpdate.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name==action),"quick action dispatch: "+action);
        Check(quickUpdate.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="halfGesture"),"half placement gesture latch exists");
        Check(quickUpdate.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="Locked"),"sweep skips locked items");
        Check(stacks.Methods.Single(m=>m.Name=="PlaceHeld").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="Compatible"),"held deposit uses full stack compatibility");
        for(int rows=1;rows<=100;rows++)for(int pages=1;pages<=5;pages++)
        {
            var seen=new HashSet<int>();
            for(int page=0;page<pages;page++)
            {
                int bottom=Operations.PageBottom(rows,pages,page);
                for(int y=bottom;y<bottom+rows;y++)Check(seen.Add(y),"independent pages do not overlap");
                Check(Operations.WithinPage(bottom,rows,rows),"entire page is valid");
                Check(!Operations.WithinPage(bottom+rows-1,2,rows),"item cannot cross page boundary");
            }
            Check(seen.Count==rows*pages,"all pages have full equal capacity");
        }
        var crossing=new List<Placement>{new Placement{Key=1,X=0,Y=69,Width=2,Height=2}};
        var migrated=Operations.Resize(20,210,crossing,70);
        Check(migrated!=null&&Operations.Valid(20,210,migrated,70),"repack crossing item into a complete page");
        crossing[0].Locked=true;
        Check(Operations.Resize(20,210,crossing,70)==null,"reject resize that splits locked item");
        Check(Operations.PageBottom(70,3,0)==140&&Operations.PageBottom(70,3,1)==70&&Operations.PageBottom(70,3,2)==0,"three distinct seventy-row pages");
        Check(Operations.MergeAmount(20,1,1)==0,"limit one disables additional merging");
        Check(Operations.MergeAmount(99998,1,99999)==99998,"expanded stack limit");
        var sort=plugin.MainModule.GetType("LocalSulfur.UnifiedInventory.Management").Methods.Single(m=>m.Name=="Sort");
        Check(sort.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="MergeForSort"),"sort merges compatible stacks");
        var splitHeld=plugin.MainModule.GetType("LocalSulfur.UnifiedInventory.Controls").Methods.Single(m=>m.Name=="get_SplitHeld");
        Check(splitHeld.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="SingleModifier"),"single modifier independently triggers split");
        var split=stacks.Methods.Single(m=>m.Name=="Split");
        Check(!split.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="DeserializeObject"),"split does not require a JSON constructor");
        Check(split.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="GetSerialized"),"split uses native item serialization");
        Check(Type("InventoryItem").Methods.Single(m=>m.Name=="GetSerialized").Body.Instructions.Count(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Newobj&&i.Operand is MethodReference m&&m.DeclaringType.Name=="InventoryData")==2,"native serializer creates fresh data in both branches");
        var viewport=plugin.MainModule.GetType("LocalSulfur.UnifiedInventory.Viewport");
        foreach(var name in new[]{"Release","Acquire","OnDisable","OnEnable"})
            Check(!viewport.Methods.Single(m=>m.Name==name).Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="FlushPendingReparents"),"no synchronous reparent during lifecycle: "+name);
        Check(viewport.Methods.Any(m=>m.Name=="FocusItems")&&viewport.Methods.Any(m=>m.Name=="Reveal"),"occupied-row navigation exists");
        Check(plugin.MainModule.GetType("LocalSulfur.UnifiedInventory.LockGlow")!=null,"lock highlight component exists");
        var overlay=plugin.MainModule.GetType("LocalSulfur.UnifiedInventory.InventoryOverlay");
        Check(overlay.Methods.Single(m=>m.Name=="Active").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="get_enabled"),"overlay respects modal inventory input blocking");
        Check(overlay.Methods.Single(m=>m.Name=="Draw").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="FillBelowTooltip"),"scrollbars clipped beneath native descriptions");
        Check(overlay!=null&&overlay.Methods.Any(m=>m.Name=="Captures")&&overlay.Methods.Any(m=>m.Name=="Input"),"toolbar and scrollbar input capture exists");
        Check(viewport.Methods.Any(m=>m.Name=="ScrollTo"),"scrollbar uses shared scroll path");
        var hud=plugin.MainModule.GetType("LocalSulfur.UnifiedInventory.HudLayout");
        Check(!hud.Methods.Single(m=>m.Name=="SyncVisibility").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="get_DividerRectTransform"),"unrelated inventory divider is no longer modified");
        var stashDivider=hud.Methods.Single(m=>m.Name=="HideStashDivider");
        Check(stashDivider.Parameters.Any(p=>p.Name=="___divider"&&p.ParameterType.Name=="RectTransform"),"stash divider uses native field injection");
        Check(stashDivider.Body.Instructions.Any(i=>i.Operand is TypeReference t&&t.Name=="PlayerStash"),"divider suppression scoped to stash");
        Check(Type("ServiceStation").Fields.Any(f=>f.Name=="divider"&&f.FieldType.Name=="RectTransform"),"native stash divider field exists");
        Check(Type("ServiceStation").Methods.Count(m=>m.Name=="SetDividerPosition")==1,"stash divider hook unambiguous");
        var visibility=hud.Methods.Single(m=>m.Name=="SyncVisibility");
        foreach(string method in new[]{"get_IsOpened","set_alpha","set_interactable","set_blocksRaycasts"})
            Check(visibility.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name==method),"HUD visibility contract: "+method);
        Check(hud.Methods.Any(m=>m.Name=="DragInput")&&hud.Methods.Any(m=>m.Name=="EndDrag"),"HUD drag and release persistence exist");
        Check(hud.Methods.Single(m=>m.Name=="Captures").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="get_leftButton"),"HUD capture requires left button, not Alt alone");
        Check(hud.Methods.Any(m=>m.Name=="KeepVisible"),"HUD off-screen recovery exists");
        foreach(string name in new[]{"Setup","Add","Consume","Count","Contains","Find","Remove","Throw","Price"})Check(stacks.Methods.Count(m=>m.Name==name)==1,"stack hook exists "+name);
        using var json=AssemblyDefinition.ReadAssembly(game+@"\Sulfur_Data\Managed\UnityEngine.JSONSerializeModule.dll");
        var utility=json.MainModule.GetType("UnityEngine.JsonUtility");
        foreach(var name in new[]{"ToJson","FromJson"})
        {
            var wrapper=utility.Methods.Single(m=>m.Name==name&&!m.HasGenericParameters&&m.Parameters.Count==2);
            Check(wrapper.HasBody&&!wrapper.Body.HasExceptionHandlers,"small managed JSON wrapper can be patched: "+name);
        }
        var itemType=Type("InventoryItem");
        foreach(var name in new[]{"BoughtForPrice","SoldForPrice"})
        {
            var setter=itemType.Methods.Single(m=>m.Name=="set_"+name);
            Check(setter.Parameters.Count==1&&setter.Parameters[0].Name=="value"&&setter.Parameters[0].ParameterType.FullName=="System.Int32","per-unit price setter contract");
        }
        Check(plugin.MainModule.GetType("LocalSulfur.UnifiedInventory.ItemLocks").Methods.Any(m=>m.Name=="Deserialize"),"lock metadata read hook exists");
        foreach(var path in args.Skip(1))AuditSave(path);
        Console.WriteLine("PASS "+checks+" checks. Offline algorithms and binary contracts only; Unity play test still required.");
    }
    static void CheckLoadAudit()
    {
        LoadAuditEntry Entry(int id,int quantity,string position="0,0")=>new LoadAuditEntry(id,quantity,id+"|"+quantity+"|"+position);
        var expected=Enumerable.Range(1,49).Select(id=>Entry(id,1)).ToArray();
        var before=expected.Select(e=>e.Id+"|"+e.Quantity+"|"+e.Signature).ToArray();
        var result=LoadAudit.Compare(expected,expected.Take(48).ToArray());
        Check(!result.Matches&&result.ExpectedRecords==49&&result.ActualRecords==48,"49/48 report remains visible");
        Check(result.MissingKinds==1&&result.MissingUnits==1&&result.Missing[49]==1,"missing record identified by actual item ID and quantity");
        Check(expected.Select(e=>e.Id+"|"+e.Quantity+"|"+e.Signature).SequenceEqual(before),"audit never changes saved records");
        Check(LoadAudit.Compare(expected,expected.Reverse().ToArray()).Matches,"record enumeration order does not matter");
        result=LoadAudit.Compare(new[]{Entry(1,5)},new[]{Entry(1,4)});
        Check(result.MissingKinds==1&&result.MissingUnits==1,"partial stack loss counted with equal record counts");
        result=LoadAudit.Compare(new[]{Entry(1,5),Entry(1,5,"1,0")},new[]{Entry(1,10)});
        Check(!result.Matches&&result.MissingKinds==0&&result.MissingUnits==0,"merged stacks are not reported as lost items");
        result=LoadAudit.Compare(new[]{Entry(1,10)},new[]{Entry(1,5),Entry(1,5,"1,0")});
        Check(result.MissingUnits==0,"split stack is not reported as loss");
        result=LoadAudit.Compare(new[]{Entry(1,10)},new[]{Entry(1,10,"9,9")});
        Check(!result.Matches&&result.MissingUnits==0,"layout change is distinguished from quantity loss");
        result=LoadAudit.Compare(new[]{Entry(1,10)},new[]{Entry(2,100)});
        Check(result.MissingKinds==1&&result.MissingUnits==10,"new unrelated items cannot conceal missing items");
        result=LoadAudit.Compare(new[]{Entry(1,int.MaxValue),Entry(1,int.MaxValue,"1,0")},new LoadAuditEntry[0]);
        Check(result.MissingKinds==1&&result.MissingUnits==2L*int.MaxValue,"large stack totals cannot overflow int");
        Check(LoadAudit.Compare(new LoadAuditEntry[0],new[]{Entry(1,1)}).MissingUnits==0,"native starter item additions are not loss");
        Check(LoadAudit.Compare(new[]{Entry(1,1),Entry(1,1)},new[]{Entry(1,1)}).MissingUnits==1,"duplicate records are accounted by quantity");
    }
    static void AuditSave(string path)
    {
        using var file=File.OpenRead(path);using var gzip=new GZipStream(file,CompressionMode.Decompress);using var reader=new StreamReader(gzip);
        var save=JObject.Parse(reader.ReadToEnd());var groups=new Dictionary<string,JArray>();
        groups["backpack"]=new JArray(((JArray)save["INVENTORY"]).Where(d=>!(bool)d["equipped"]));
        var stash=(JObject)save["STASH"];
        for(int i=0;i<((JArray)stash["_keys"]).Count;i++)groups[(string)stash["_keys"][i]]=(JArray)stash["_values"][i]["list"];
        foreach(var pair in groups)
        {
            int width=1,height=1;var footprints=pair.Value.Select(d=>new Footprint{X=(int)d["position"][0],Y=(int)d["position"][1],Width=(int)d["xSize"],Height=(int)d["ySize"]}).ToList();
            Geometry.Fit(ref width,ref height,footprints);
            Check(width<=100&&height<=500,"saved capacity supported");
            long quantity=pair.Value.Sum(d=>(long)d["quantity"]);
            Console.WriteLine(pair.Key+": "+pair.Value.Count+" objects, "+quantity+" quantity, occupied bounds "+width+"x"+height);
        }
    }
}
