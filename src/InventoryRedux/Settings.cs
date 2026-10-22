using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;

namespace LocalSulfur.UnifiedInventory
{
    internal sealed class ContainerSettings
    {
        internal ConfigEntry<int> Columns, Rows, VisibleRows, Pages;
        internal ConfigEntry<int> ActivePage;
        internal ConfigEntry<string> PageScroll;
        internal ConfigEntry<float> Scale, X, Y;
    }

    internal sealed class Settings
    {
        private readonly ConfigFile config;
        private readonly Dictionary<string,ContainerSettings> containers=new Dictionary<string,ContainerSettings>(StringComparer.Ordinal);
        private readonly Dictionary<string,string> legacy=new Dictionary<string,string>(StringComparer.Ordinal);
        private readonly Dictionary<string,JObject> profiles=new Dictionary<string,JObject>(StringComparer.Ordinal);
        internal readonly ConfigEntry<int> WheelRows;
        internal readonly ConfigEntry<bool> WheelPages;
        internal readonly ConfigEntry<int> MaxStack;
        internal readonly ConfigEntry<Modifier> LayoutModifier, SplitModifier, SingleModifier;
        internal readonly ConfigEntry<LayoutButton> DragButton;
        internal readonly ConfigEntry<UnityEngine.InputSystem.Key> LockKey;
        internal readonly ConfigEntry<float>[] HudX=new ConfigEntry<float>[3],HudY=new ConfigEntry<float>[3];
        internal readonly ConfigEntry<float> ActionX,ActionY;

        internal Settings(ConfigFile file)
        {
            config=file;
            ActionX=config.Bind("WarehouseActions","X",.53f,"Toolbar horizontal screen fraction / 按钮栏水平屏幕比例");
            ActionY=config.Bind("WarehouseActions","Y",.02f,"Toolbar vertical screen fraction / 按钮栏垂直屏幕比例");
            for(int i=0;i<3;i++)
            {
                string section=new[]{"LayoutHints","LayoutAmmo","LayoutCurrency"}[i];
                HudX[i]=config.Bind(section,"OffsetX",0f,"Horizontal offset / 水平偏移");
                HudY[i]=config.Bind(section,"OffsetY",0f,"Vertical offset / 垂直偏移");
            }
            LayoutModifier=config.Bind("Controls","LayoutModifier",Modifier.Alt,"Hold with drag to reposition, or wheel to scale / 按住并拖动调整位置，配合滚轮缩放");
            DragButton=config.Bind("Controls","DragButton",LayoutButton.Left,"Mouse button for repositioning / 调整位置使用的鼠标键");
            SplitModifier=config.Bind("Controls","SplitModifier",Modifier.Alt,"Hold and right-click to split half / 按住并右击拆分一半");
            SingleModifier=config.Bind("Controls","SingleModifier",Modifier.Control,"Hold and right-click to split one / 按住并右击只取一个");
            LockKey=config.Bind("Controls","LockKey",UnityEngine.InputSystem.Key.L,"Toggle hovered item lock / 切换悬停物品锁定");
            string path=Path.Combine(Paths.GameRootPath,"Sulfur_Data","bigPackSet.txt");
            if(File.Exists(path))
            {
                foreach(string line in File.ReadAllLines(path))
                {
                    string clean=line.Split('#')[0].Trim(); int split=clean.IndexOf('=');
                    if(split>0)legacy[clean.Substring(0,split).Trim()]=clean.Substring(split+1).Trim();
                }
                string encoded;
                if(legacy.TryGetValue("stashProfilesJson",out encoded))
                {
                    foreach(JObject profile in JArray.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded))))
                    {
                        string[] parts=((string)profile["Key"]??"").Split('|');
                        if(parts.Length<4||parts[1]!="PerfectRandom.Sulfur.Core.PlayerStash")continue;
                        string id=parts[2]=="NoStashId"?"":parts[2]; JObject prior;
                        if(!profiles.TryGetValue(id,out prior)||String.CompareOrdinal((string)profile["UpdatedAtUtc"],(string)prior["UpdatedAtUtc"])>0)
                            profiles[id]=profile;
                    }
                }
            }
            WheelRows=config.Bind("Scrolling","RowsPerNotch",3,new ConfigDescription("Rows per wheel step / 每次滚动行数",new AcceptableValueRange<int>(1,20)));
            WheelPages=config.Bind("Scrolling","PageMode",false,"Scroll one visible page per wheel step / 滚轮整页翻动");
            MaxStack=config.Bind("Stacking","Maximum",1024,new ConfigDescription("Maximum non-equipment stack size; 1 disables merging, existing counts are preserved / 非装备堆叠上限；1关闭额外合并，已有数量不截断",new AcceptableValueRange<int>(1,99999)));
        }

        private float Old(string key,float fallback)
        {
            string value; float result;
            return legacy.TryGetValue(key,out value)&&float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out result)&&!float.IsNaN(result)&&!float.IsInfinity(result)?result:fallback;
        }

        internal ContainerSettings Get(string id)
        {
            ContainerSettings result;if(containers.TryGetValue(id,out result))return result;
            bool bag=id=="backpack"; int width=bag?(int)Old("sizX",13):(int)Old("csizX",13);
            int visible=bag?(int)Old("sizY",14):14;
            int height=bag?checked(visible*(int)Old("backpackPageCount",1)):(int)Old("csizY",14);
            JObject p;
            if(!bag&&profiles.TryGetValue(id.Substring(6),out p))
            {width=(int?)p["Width"]??width;height=(int?)p["Height"]??height;}
            string section=bag?"Backpack":"Stash_"+BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes(id.Substring(6))).Replace("-","");
            result=new ContainerSettings();
            result.Columns=config.Bind(section,"Columns",Geometry.Clamp(width,1,100),new ConfigDescription("Capacity columns; applied next load / 容量列数，下次读档应用",new AcceptableValueRange<int>(1,100)));
            result.Rows=config.Bind(section,"Rows",Geometry.Clamp(height,1,500),new ConfigDescription("Rows per page, shared by all pages / 每页行数，所有页面使用相同行数",new AcceptableValueRange<int>(1,500)));
            result.Pages=config.Bind(section,"Pages",1,new ConfigDescription("Equal-sized pages; rows times pages must not exceed 500 / 等容量页数，每页行数乘页数不得超过500",new AcceptableValueRange<int>(1,20)));
            result.ActivePage=config.Bind(section,"ActivePage",-1,"Remembered page index / 记忆的页面索引");
            result.PageScroll=config.Bind(section,"PageScroll","","Remembered scroll offsets per page / 每页滚动位置");
            result.VisibleRows=config.Bind(section,"VisibleRows",Geometry.Clamp(visible,4,30),new ConfigDescription("Visible rows / 可见行数",new AcceptableValueRange<int>(4,40)));
            result.Scale=config.Bind(section,"Scale",1f,new ConfigDescription("Viewport scale relative to automatic fit / 相对自动适配尺寸的缩放",new AcceptableValueRange<float>(.25f,2f)));
            result.X=config.Bind(section,"OffsetX",0f,"Horizontal layout offset / 水平位置偏移");
            result.Y=config.Bind(section,"OffsetY",0f,"Vertical layout offset / 垂直位置偏移");
            containers.Add(id,result);return result;
        }
    }
}
