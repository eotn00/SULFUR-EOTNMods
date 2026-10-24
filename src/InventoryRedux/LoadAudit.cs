using System;
using System.Collections.Generic;
using System.Linq;

namespace LocalSulfur.UnifiedInventory
{
    internal sealed class LoadAuditEntry
    {
        internal readonly int Id,Quantity;
        internal readonly string Signature;
        internal LoadAuditEntry(int id,int quantity,string signature)
        {Id=id;Quantity=quantity;Signature=signature;}
    }

    internal sealed class LoadAuditResult
    {
        internal bool Matches;
        internal int ExpectedRecords,ActualRecords;
        internal readonly Dictionary<int,long> Missing=new Dictionary<int,long>();
        internal int MissingKinds {get{return Missing.Count;}}
        internal long MissingUnits {get{return Missing.Values.Sum();}}
    }

    internal static class LoadAudit
    {
        internal static LoadAuditResult Compare(LoadAuditEntry[] expected,LoadAuditEntry[] actual)
        {
            var result=new LoadAuditResult{ExpectedRecords=expected.Length,ActualRecords=actual.Length,
                Matches=expected.Select(e=>e.Signature).OrderBy(s=>s,StringComparer.Ordinal)
                    .SequenceEqual(actual.Select(e=>e.Signature).OrderBy(s=>s,StringComparer.Ordinal))};
            // Count units by item ID: a merged stack or moved item is not a lost item.
            var current=actual.GroupBy(e=>e.Id).ToDictionary(g=>g.Key,g=>g.Sum(e=>(long)e.Quantity));
            foreach(var group in expected.GroupBy(e=>e.Id))
            {
                long remaining;current.TryGetValue(group.Key,out remaining);
                long missing=group.Sum(e=>(long)e.Quantity)-remaining;
                if(missing>0)result.Missing.Add(group.Key,missing);
            }
            return result;
        }
    }
}
