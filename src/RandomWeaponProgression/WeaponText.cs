using System;
using System.Collections.Generic;

namespace SulfurRandomWeapons
{
    public static class WeaponText
    {
        public static Func<string> Language = delegate { return "English"; };
        public static bool IsChinese(string language)
        {
            string value = (language ?? "").ToLowerInvariant();
            return value.StartsWith("zh") || value.Contains("chinese") || value.Contains("中文");
        }
        public static string Pick(string chinese, string english) { return IsChinese(Language()) ? chinese : english; }
        public static string[] Labels { get { return IsChinese(Language()) ? ChineseLabels : EnglishLabels; } }
        private static readonly string[] ChineseLabels = { "伤害", "射速", "换弹", "精准", "控枪", "耐久上限" };
        private static readonly string[] EnglishLabels = { "DMG", "RPM", "Reload", "Accuracy", "Control", "Max Durability" };
        private static readonly string[] ShortLabels = { "DMG", "RPM", "Rld", "Acc", "Ctrl", "Dur" };
        public static string HistoryLabel(int stat) { return IsChinese(Language()) ? ChineseLabels[stat] : ShortLabels[stat]; }
        public static readonly Dictionary<string, string[]> Settings = new Dictionary<string, string[]>
        {
            { "Category", new[] { "更RPG的火湖", "More RPG Sulfur" } },
            { "Section", new[] { "枪械属性", "Firearm Stats" } },
            { "Enabled", new[] { "启用枪械属性", "Enable firearm stats" } },
            { "EnabledTip", new[] { "保留每把枪的初始词条和升级记录，关闭只暂停属性效果。", "Disabling pauses stat effects but preserves weapon IDs, initial rolls and upgrade history." } },
            { "InitialRange", new[] { "新枪初始浮动范围 (%)", "New weapon initial range (%)" } },
            { "UpgradeMin", new[] { "新枪每级增益下限 (%)", "New weapon rank gain minimum (%)" } },
            { "UpgradeMax", new[] { "新枪每级增益上限 (%)", "New weapon rank gain maximum (%)" } },
            { "NewWeaponTip", new[] { "只影响此后创建的新武器记录；已有武器的词条、升级记录和成长规则保持不变。", "Only affects newly created weapon records. Existing rolls, upgrade history and growth rules remain unchanged." } }
        };
        public static string Key(string name) { return "MoreRPGSulfur." + name; }
    }
}
