using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using LocalSulfur.RummageInventoryCompat;

class Program
{
    static int checks;
    static void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;}
    static void Fields(TypeDefinition type,params string[] names)
    {foreach(var name in names)Check(type.Fields.Any(f=>f.Name==name),type.Name+" field "+name);}
    static MethodDefinition Method(TypeDefinition type,string name,params string[] parameters)
    {
        var matches=type.Methods.Where(m=>m.Name==name&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(parameters)).ToArray();
        Check(matches.Length==1,type.Name+" method "+name);return matches[0];
    }
    static void CheckNativeResolution(string patchPath,string game)
    {
        AssemblyLoadContext.Default.Resolving+=(context,name)=>
        {
            foreach(var folder in new[]{game+@"\BepInEx\core",game+@"\Sulfur_Data\Managed"})
            {
                string path=Path.Combine(folder,name.Name+".dll");
                if(File.Exists(path))return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(patchPath));
        var bridge=assembly.GetType("LocalSulfur.RummageInventoryCompat.ReduxBridge",true);
        var resolve=bridge.GetMethod("ResolveItemHooks",BindingFlags.Static|BindingFlags.NonPublic);
        Check(resolve!=null,"native hook resolver exists");
        var targets=(MethodInfo[])resolve.Invoke(null,null);
        Check(targets.Select(m=>m.Name).SequenceEqual(new[]{"OnPointerClick","OnBeginDrag","Use"}),"actual runtime resolver selects all three native actions");
        foreach(var target in targets)
        {
            Check(!target.IsStatic&&target.ReturnType==typeof(void),"native action is an instance void method: "+target.Name);
            var expected=target.Name=="Use"?Array.Empty<string>():new[]{"UnityEngine.EventSystems.PointerEventData"};
            Check(target.GetParameters().Select(p=>p.ParameterType.FullName).SequenceEqual(expected),"actual runtime native signature: "+target.Name);
        }
        Console.WriteLine("Validated actual reflection resolution against game assemblies.");
    }
    static void Main(string[] args)
    {
        foreach(float width in new[]{280f,640f,1280f,1920f,3840f})
        foreach(float height in new[]{240f,720f,1080f,2160f})
        foreach(bool above in new[]{false,true})
        foreach(float x in new[]{-100000f,0f,50f,100000f,float.NaN,float.PositiveInfinity})
        foreach(float y in new[]{-100000f,0f,100000f,float.NaN,float.NegativeInfinity})
        {
            var p=RummageLayout.Place(width*.25f,height*.9f,width*.26f,0,0,width,height,x,y,above);
            Check(p.Left>=8&&p.Left+p.Width*p.Scale<=width-7.9f,"search horizontally bounded");
            Check(p.Bottom>=8&&p.Bottom+(62+32)*p.Scale<=height-7.9f,"bar and pager vertically bounded");
            Check(p.Width>=359.9f&&p.Scale>0&&p.Scale<=1,"controls retain usable logical width");
            var restored=RummageLayout.Place(width*.25f,height*.9f,width*.26f,0,0,width,height,p.OffsetX,p.OffsetY,above);
            Check(Math.Abs(restored.Left-p.Left)<.01f&&Math.Abs(restored.Bottom-p.Bottom)<.01f,"clamped offsets restore exactly");
        }
        var expected=RummageLayout.Place(427,902,500,0,0,1920,1080,0,0,false);
        Check(expected.Left==427&&expected.Bottom==908&&expected.Width==500,"layout uses visible frame instead of full 4800-high grid");
        var shifted=RummageLayout.Place(427-960,902-540,500,-960,-540,1920,1080,0,0,false);
        Check(shifted.Left==expected.Left-960&&shifted.Bottom==expected.Bottom-540,"nonzero canvas origin supported");
        string game=Environment.GetEnvironmentVariable("SULFUR_TEST_GAME_ROOT")??throw new Exception("Set SULFUR_TEST_GAME_ROOT");
        using var resolver=new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(game+@"\BepInEx\core");resolver.AddSearchDirectory(game+@"\Sulfur_Data\Managed");
        using var patch=AssemblyDefinition.ReadAssembly(args[0],new ReaderParameters{AssemblyResolver=resolver});
        Check(!patch.MainModule.AssemblyReferences.Any(a=>a.Name=="InventoryRedux"||a.Name=="Rummage"||a.Name=="小地图Mod"),"original mods are not hard binary dependencies");
        var plugin=patch.MainModule.GetType("LocalSulfur.RummageInventoryCompat.Plugin");
        foreach(var removed in new[]{"InputTrace","MiniMapCompatibility","ShortcutCompatibility"})
            Check(patch.MainModule.GetType("LocalSulfur.RummageInventoryCompat."+removed)==null,"experimental component removed: "+removed);
        Check(plugin.CustomAttributes.Count(a=>a.AttributeType.Name=="BepInDependency")==2,"only Redux and Rummage dependencies remain");
        foreach(var id in new[]{"local.sulfur.unifiedinventory","ryuka.sulfur.rummage"})
            Check(plugin.CustomAttributes.Any(a=>a.AttributeType.Name=="BepInDependency"&&(string)a.ConstructorArguments[0].Value==id&&Convert.ToInt32(a.ConstructorArguments[1].Value)==2),"optional dependency: "+id);
        foreach(var type in patch.MainModule.Types)
        {
            Check(!type.Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference m&&
                new[]{"WriteToDisk","AddItemToPosition","Consume","DropFromPlayer","MoveSelected","SafetyStop"}.Contains(m.Name)),"UI patch does not mutate item or save data: "+type.Name);
            Check(!type.Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference m&&
                (m.Name=="Enable"||m.Name=="Disable")&&m.DeclaringType.FullName.StartsWith("UnityEngine.InputSystem.InputAction")),"UI patch never force-toggles action maps: "+type.Name);
        }
        using var rummage=AssemblyDefinition.ReadAssembly(game+@"\BepInEx\plugins\Rummage.dll");
        using var native=AssemblyDefinition.ReadAssembly(game+@"\Sulfur_Data\Managed\PerfectRandom.Sulfur.Core.dll");
        var item=native.MainModule.GetType("PerfectRandom.Sulfur.Core.Items.InventoryItem");
        Method(item,"OnPointerClick","UnityEngine.EventSystems.PointerEventData");
        Method(item,"OnBeginDrag","UnityEngine.EventSystems.PointerEventData");Method(item,"Use");
        var bridge=patch.MainModule.GetType("LocalSulfur.RummageInventoryCompat.ReduxBridge");
        Check(Method(bridge,"Initialize").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="ResolveItemHooks"),"native targets validated before patch installation");
        Check(!bridge.Methods.Single(m=>m.Name=="Install").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==bridge.FullName&&m.Name=="Method"),"installation uses prevalidated native targets");
        var bar=rummage.MainModule.GetType("Ryuka.Sulfur.Rummage.UserInterface.SearchBar");
        var results=rummage.MainModule.GetType("Ryuka.Sulfur.Rummage.UserInterface.SearchResultsPanel");
        Fields(bar,"rootRect","results","display");Fields(results,"rootRect");
        var build=Method(bar,"TryBuild","PerfectRandom.Sulfur.Core.UI.Inventory.InventoryUI","UnityEngine.RectTransform");
        Check(build.Parameters.Select(p=>p.Name).SequenceEqual(new[]{"ui","gridRect"}),"Rummage build Harmony parameters match");
        Method(bar,"Place","UnityEngine.RectTransform");Method(bar,"Layout","System.Single");
        foreach(var name in new[]{"Dispose","FocusInput","Unfocus"})Method(bar,name);
        Method(results,"Place","System.Single","System.Single","System.Single","System.Single","Ryuka.Sulfur.Rummage.Config.ResultsPosition");
        Check(rummage.MainModule.GetType("Ryuka.Sulfur.Rummage.Config.DisplayConfig").Properties.Any(p=>p.Name=="ResultsPlacement"),"results placement is a property");
        Method(rummage.MainModule.GetType("Ryuka.Sulfur.Rummage.InputGuard.SearchInputGuard"),"Typing");
        foreach(var path in new[]{game+@"\BepInEx\plugins\InventoryRedux.dll",Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])),"InventoryRedux.dll")}.Distinct().Where(File.Exists))
        {
            using var redux=AssemblyDefinition.ReadAssembly(path);
            TypeDefinition Type(string name)=>redux.MainModule.GetType("LocalSulfur.UnifiedInventory."+name);
            Fields(Type("Viewport"),"frame","IsLayoutDragging");Fields(Type("Menu"),"Open","GroupDragging","Selecting");
            Fields(Type("Stacks"),"NativeSplitInput");Fields(Type("QuickActions"),"NativeAction","placing","halfGesture","nextPlace","blockedFrame");
            Fields(Type("Plugin"),"Settings");Fields(Type("Settings"),"LayoutModifier","DragButton");
            Method(Type("QuickActions"),"Cancel");Method(Type("Stacks"),"UpdateSplitDrag");
            Method(Type("InventoryOverlay"),"Cancel");Method(Type("InventoryOverlay"),"TooltipBounds");
            Check(Method(Type("InventoryOverlay"),"Pager","LocalSulfur.UnifiedInventory.Viewport").Parameters[0].Name=="view","pager Harmony parameter name");
            Method(Type("Menu"),"Update");
            Check(Method(Type("Viewport"),"Topmost","UnityEngine.Vector2").Parameters[0].Name=="screen","viewport Harmony parameter name");
            var filter=Type("InventoryRaycasts").Methods.Single(m=>m.Name=="Filter");
            Check(filter.Parameters.Select(p=>p.Name).SequenceEqual(new[]{"eventData","raycastResults"}),"raycast Harmony parameter names");
            Console.WriteLine("Validated Inventory Redux contracts: "+path);
        }
        CheckNativeResolution(args[0],game);
        Console.WriteLine("PASS "+checks+" offline layout and binary-contract checks. Unity interaction testing still required.");
    }
}
