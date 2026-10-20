using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
class SaveCompatibilityChecks
{
    static Type type;
    static object Call(string name, params object[] args) { return type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args); }
    static void Check(bool value) { if (!value) throw new Exception("Check failed"); }
    static int Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Pass the game directory and built plugin path");
        string game = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e) {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string dir in new[] { game + @"\BepInEx\core", game + @"\Sulfur_Data\Managed" })
                if (File.Exists(Path.Combine(dir, name))) return Assembly.LoadFrom(Path.Combine(dir, name));
            return null;
        };
        type = Assembly.LoadFrom(args[1]).GetType("SaveCompatibilityFix");
        object[] result = { true };
        Check((bool)Call("AllowRestore", result));
        Call("BeginRestore"); Call("BeginRestore");
        result[0] = true;
        Check(!(bool)Call("AllowRestore", result) && !(bool)result[0]);
        var error = new Exception("sentinel");
        Check(Object.ReferenceEquals(Call("EndRestore", error), error));
        Check(!(bool)Call("AllowRestore", result));
        Call("EndRestore", new object[] { null });
        Check((bool)Call("AllowRestore", result));
        Call("EndRestore", new object[] { null });
        Check((bool)Call("AllowRestore", result));
        string path = Path.Combine(Path.GetTempPath(), "sulfur-baseline-test-" + Guid.NewGuid() + ".txt");
        try {
            Call("AtomicSave", path, new List<string> { "guid\tName\t1.0" });
            Check(File.ReadAllLines(path)[0] == "guid\tName\t1.0");
            Call("AtomicSave", path, new List<string> { "guid\tName\t2.0" });
            Check(File.ReadAllLines(path)[0] == "guid\tName\t2.0");
            Check(File.ReadAllLines(path + ".bak")[0] == "guid\tName\t1.0");
        } finally { File.Delete(path); File.Delete(path + ".bak"); }
        Console.WriteLine("9 checks passed: restore scope, nesting, exception preservation, reset, atomic baseline roundtrip and backup.");
        return 0;
    }
}
