using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PerfectRandom.Sulfur.Core.Items;
using UnityEngine;

namespace LocalSulfur.UnifiedInventory
{
    internal static class LegacyLocks
    {
        internal static void Import(int slot,string context,ItemGrid grid)
        {
            string path=Path.Combine(Paths.ConfigPath,"InventoryLocksV2.json");
            if(!File.Exists(path))return;
            try
            {
                if(new FileInfo(path).Length>4*1024*1024)throw new InvalidDataException("Legacy lock file too large");
                var root=JObject.Parse(File.ReadAllText(path));
                if((int?)root["version"]!=1)throw new InvalidDataException("Unsupported legacy lock version");
                string key="v2/slot/"+slot+"/backpack";
                if(context.StartsWith("stash/",StringComparison.Ordinal))
                {
                    string id=context.Substring(6);
                    key="v2/slot/"+slot+"/stash/"+(id.Length==0?"_":BitConverter.ToString(Encoding.UTF8.GetBytes(id)).Replace("-","").ToLowerInvariant());
                }
                int count=0;
                foreach(var entry in (JArray)root["entries"])
                {
                    if((string)entry["context"]!=key||string.IsNullOrEmpty((string)entry["itemKey"]))continue;
                    int x=(int)entry["x"],y=(int)entry["y"];
                    if(x<0||y<0||x>=grid.gridSize.x||y>=grid.gridSize.y)continue;
                    var cell=grid.GetSlotAt(x,y);var item=cell==null?null:cell.ItemInSlot;
                    if(item==null||item.gridPosition!=new Vector2Int(x,y)||item.InventorySize!=new Vector2Int((int)entry["width"],(int)entry["height"])||Identity(item)!=(string)entry["itemKey"])continue;
                    (item.GetComponent<ItemMark>()??item.gameObject.AddComponent<ItemMark>()).Locked=true;count++;
                }
                Plugin.Instance.Report("Imported "+count+" verified item locks for "+context+"; empty cell locks were not imported.");
            }
            catch(Exception e){Plugin.Instance.Report("Legacy lock import skipped; original file preserved: "+e.Message);}
        }
        private static string Identity(InventoryItem item)
        {
            var data=item.GetSerialized();string identifier=data.identifier??"";
            foreach(string prefix in new[]{"sulfur.random-weapons.v2:","sulfur.equipment-quality-test.v1:"})
            {
                if(!identifier.StartsWith(prefix,StringComparison.Ordinal))continue;
                try
                {
                    var json=JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(identifier.Substring(prefix.Length))));
                    Guid guid;if(Guid.TryParse((string)json["guid"],out guid))identifier="uuid:"+guid.ToString("N");
                }
                catch{}
            }
            string value=JsonConvert.SerializeObject(new{data.id,identifier});
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","");
        }
    }
}
