using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace SulfurEquipmentQualityTest
{
    internal static class QualitySettings
    {
        private static readonly Dictionary<string,ConfigEntry<float>> values=new Dictionary<string,ConfigEntry<float>>();
        private static readonly Dictionary<string,string[]> text=new Dictionary<string,string[]>();
        private static ConfigFile legacy;
        internal static ConfigEntry<float> CareCap;
        private static void Bind(ConfigFile config,string key,string cn,string en,float initial,float limit)
        {
            float prior=legacy==null?initial:legacy.Bind("Quality3",key,initial).Value;
            values[key]=config.Bind("Quality3",key,prior,new ConfigDescription(en+". Applies to newly created quality/affix records only.",new AcceptableValueRange<float>(0,limit)));
            text[key]=new[]{cn,en};
        }
        internal static void Initialize(ConfigFile config)
        {
            string old=Path.Combine(Paths.ConfigPath,"local.sulfur.equipmentqualitytest.cfg");
            if(File.Exists(old)){legacy=new ConfigFile(old,false);legacy.SaveOnConfigSet=false;}
            var defaults=new EquipmentRules();
            for(int i=0;i<6;i++)
            {
                string key="Tier"+i,cn=Affixes.ChineseGrades[i],en=Affixes.EnglishGrades[i];
                Bind(config,key+"Weight",cn+"权重",en+" weight",defaults.weights[i],100);
                if(i!=1){Bind(config,key+"Min",cn+"原属性下限 (%)",en+" original stat minimum (%)",defaults.minimum[i],90);Bind(config,key+"Max",cn+"原属性上限 (%)",en+" original stat maximum (%)",defaults.maximum[i],90);}
                if(i>=3)Bind(config,key+"Scale",cn+"额外词条倍率",en+" affix strength",defaults.scales[i],5);
            }
            foreach(var d in Affixes.Pool.Where(d=>!d.integer))
            {
                Bind(config,d.key+"Min",d.chinese+"词条下限",d.english+" affix minimum",d.min,100);
                Bind(config,d.key+"Max",d.chinese+"词条上限",d.english+" affix maximum",d.max,100);
            }
            float cap=legacy==null?80:legacy.Bind("Quality3","CareCapPercent",80f).Value;
            CareCap=config.Bind("Quality3","CareCapPercent",cap,new ConfigDescription("Maximum total firearm and armor wear reduction. Applies immediately without rerolling.",new AcceptableValueRange<float>(0,99)));
            values["CareCapPercent"]=CareCap;text["CareCapPercent"]=new[]{"枪械与护甲总减耗上限 (%)","Total firearm and armor wear reduction cap (%)"};
        }
        internal static EquipmentRules Rules()
        {
            var r=new EquipmentRules();
            for(int i=0;i<6;i++)
            {
                r.weights[i]=values["Tier"+i+"Weight"].Value;
                if(i!=1){r.minimum[i]=values["Tier"+i+"Min"].Value;r.maximum[i]=Math.Max(r.minimum[i],values["Tier"+i+"Max"].Value);}
                if(i>=3)r.scales[i]=values["Tier"+i+"Scale"].Value;
            }
            foreach(var d in Affixes.Pool.Where(d=>!d.integer)){float min=values[d.key+"Min"].Value;r.ranges[d.key]=new[]{min,Math.Max(min,values[d.key+"Max"].Value)};}
            if(r.weights.Sum()<=0)r.weights[1]=1;
            return r;
        }
        internal static void Register(object category)
        {
            Type registry=AccessTools.TypeByName("SulfurOptionsScreenSettings.ModOptionsRegistry");if(registry==null)return;
            if(category==null)return;
            text["Section"]=new[]{"装备属性","Equipment Stats"};
            text["Enabled"]=new[]{"启用装备属性","Enable equipment stats"};
            text["EnabledTip"]=new[]{"暂停品质、词条与养护效果, 保留物品编号和记录。","Pause quality, affixes and Preservation while retaining item IDs and records."};
            string folder=Path.Combine(Paths.ConfigPath,"OptionsScreenSettings","Localization","EquipmentQuality3");Directory.CreateDirectory(folder);
            for(int lang=0;lang<2;lang++)File.WriteAllLines(Path.Combine(folder,lang==0?"zh.lang":"en.lang"),text.Select(p=>"EquipmentQuality3."+p.Key+"="+p.Value[lang]).ToArray(),Encoding.UTF8);
            var loc=AccessTools.TypeByName("SulfurOptionsScreenSettings.ModLocalization");if(loc!=null)loc.GetMethod("Reload").Invoke(null,null);
            registry.GetMethods().Single(m=>m.Name=="AddInfoLoc"&&m.GetParameters().Length==7).Invoke(null,new object[]{category,"EquipmentQuality3_Section","EquipmentQuality3.Section",1000,null,"Equipment Stats",null});
            registry.GetMethods().Single(m=>m.Name=="AddBoolLoc"&&m.GetParameters().Length==9).Invoke(null,new object[]{category,"EquipmentQuality3_Enabled","EquipmentQuality3.Enabled",EquipmentQualityTest.Enabled.Value,new Action<bool>(v=>{EquipmentQualityTest.Enabled.Value=v;EquipmentQualityTest.Refresh();}),1001,"EquipmentQuality3.EnabledTip","Enable equipment stats",text["EnabledTip"][1]});
            MethodInfo slider=registry.GetMethods().Single(m=>m.Name=="AddSliderLoc"&&m.GetParameters().Length==13);
            int order=1001;
            foreach(var pair in values)
            {
                var entry=pair.Value;bool scale=pair.Key.EndsWith("Scale",StringComparison.Ordinal);float max=scale?5:pair.Key.StartsWith("Tier",StringComparison.Ordinal)&&!pair.Key.EndsWith("Weight",StringComparison.Ordinal)?90:100;
                if(pair.Key=="CareCapPercent")max=99;
                slider.Invoke(null,new object[]{category,"EquipmentQuality3_"+pair.Key,"EquipmentQuality3."+pair.Key,entry.Value,0f,max,scale?.1f:.5f,true,new Action<float>(v=>entry.Value=v),++order,null,text[pair.Key][1],"New records only. Existing item IDs, rolls and history are retained. Maximum is at least minimum; weights are normalized."});
            }
        }
    }
}
