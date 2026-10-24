using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace LocalSulfur.RummageInventoryCompat
{
    [BepInPlugin(Id,"Inventory Redux - Rummage Compatibility","0.1.6")]
    [BepInDependency("local.sulfur.unifiedinventory",BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("ryuka.sulfur.rummage",BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Id="local.sulfur.inventoryredux.rummagecompat";
        internal static Plugin Instance;
        internal ConfigEntry<float> OffsetX,OffsetY;
        private void Awake()
        {
            Instance=this;
            OffsetX=Config.Bind("Layout","OffsetX",0f,"Search panel horizontal offset; uses Inventory Redux layout drag controls / 搜索面板水平偏移，沿用库存位置拖动按键");
            OffsetY=Config.Bind("Layout","OffsetY",0f,"Search panel vertical offset; bar and results move together / 搜索面板垂直偏移，搜索栏和结果一起移动");
            RummageCompatibility.Install();
            LogInfo("0.1.6: Inventory Redux / Rummage UI compatibility only. No third-party shortcut hooks or save changes.");
        }
        internal void Report(string message){Logger.LogWarning(message);}
        internal void LogInfo(string message){Logger.LogInfo(message);}
        private void OnDestroy()
        {
            if(RummageCompatibility.Current!=null)
            {
                RummageCompatibility.Current.Hide();
                Destroy(RummageCompatibility.Current.gameObject);RummageCompatibility.Current=null;
            }
            RummageCompatibility.Patches.UnpatchSelf();
        }
    }
}
