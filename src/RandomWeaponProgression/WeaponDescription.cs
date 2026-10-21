using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Description = PerfectRandom.Sulfur.Core.UI.ItemDescription.ItemDescription;

namespace SulfurRandomWeapons
{
    [HarmonyPatch]
    internal static class WeaponDescription
    {
        private static readonly MethodInfo addText = AccessTools.Method(typeof(Description), "AddDescriptionText");
        private static readonly FieldInfo content = AccessTools.Field(typeof(Description), "contentTransform");
        private static readonly FieldInfo flavor = AccessTools.Field(typeof(Description), "flavorText");
        private static readonly ConditionalWeakTable<Description, HistoryView> views = new ConditionalWeakTable<Description, HistoryView>();

        private static void Add(Description description, string text, ref int index)
        {
            object[] args = { text, index };
            addText.Invoke(description, args);
            index = (int)args[1];
        }

        [HarmonyPatch(typeof(Description), "ClearDescription")]
        [HarmonyPrefix]
        private static void Clear(Description __instance)
        {
            HistoryView view;
            if (views.TryGetValue(__instance, out view)) view.Hide();
        }

        [HarmonyPatch(typeof(Description), "AddPriceInfo")]
        [HarmonyPrefix]
        private static void AddWeaponInfo(Description __instance, InventoryItem item, ref int childIndex)
        {
            if (item == null || item.IsDummyItem || !RandomWeapons.Eligible(item.itemDefinition)) return;
            try
            {
                WeaponRecord record;
                if (!WeaponState.TryGet(item.stats, out record))
                {
                    Add(__instance, "<color=#F09D94>" + WeaponText.Pick("未找到武器词条记录", "Weapon roll record not found") + "</color>", ref childIndex);
                    return;
                }
                string title = "<b><color=#F5BD58>" + WeaponText.Pick("初始随机属性", "Initial Rolls") + "</color></b>  <size=75%>" + record.guid.Substring(0, 8) + "</size>\n";
                Add(__instance, title + Progression.Summary(record.initial), ref childIndex);
                if (record.inheritedDamage != 0) Add(__instance, WeaponText.Pick("试验继承：伤害 ", "Legacy DMG bonus: ") + Progression.Colored(record.inheritedDamage), ref childIndex);
                Add(__instance, "<b><color=#F5BD58>" + WeaponText.Pick("升级累计 · " + record.upgrades.Count + " 级", "Rank Gains · " + record.upgrades.Count) + "</color></b>\n" +
                    (record.upgrades.Count == 0 ? WeaponText.Pick("尚无升级收益", "No rank gains yet") : Progression.Summary(Progression.Totals(record))), ref childIndex);
                if (!RandomWeapons.Enabled.Value) Add(__instance, "<color=#F09D94>" + WeaponText.Pick("词条效果已暂停", "Stat effects paused") + "</color>", ref childIndex);
                if (record.upgrades.Count > 0)
                {
                    Add(__instance, "<b>" + WeaponText.Pick("逐级升级记录", "Rank History") + "</b>", ref childIndex);
                    var view = views.GetValue(__instance, description => new HistoryView((Transform)content.GetValue(description), ((ItemDescriptionText)flavor.GetValue(description)).textComp));
                    view.Show(record, childIndex++, ((ItemDescriptionText)flavor.GetValue(__instance)).textComp);
                }
            }
            catch (Exception ex) { RandomWeapons.LogWarning("Weapon info rendering failed: " + ex.Message); }
        }

        private sealed class HistoryView
        {
            private readonly GameObject root;
            private readonly RectTransform scrollContent;
            private readonly TextMeshProUGUI text;
            private readonly ScrollRect scroll;
            private readonly LayoutElement layout;
            private WeaponRecord record;
            private int visible;
            private const float RowHeight = 21;

            private static RectTransform Rect(string name, Transform parent)
            {
                var obj = new GameObject(name, typeof(RectTransform));
                obj.transform.SetParent(parent, false);
                return (RectTransform)obj.transform;
            }
            private static void Stretch(RectTransform rect)
            {
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            }
            internal HistoryView(Transform parent, TextMeshProUGUI source)
            {
                var rect = Rect("RandomWeaponRankHistory", parent); root = rect.gameObject;
                layout = root.AddComponent<LayoutElement>(); layout.flexibleWidth = 1; layout.flexibleHeight = 0;
                root.AddComponent<Image>().color = new Color(0, 0, 0, 0.08f);
                scroll = root.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped; scroll.inertia = false; scroll.scrollSensitivity = RowHeight * 2;
                var viewport = Rect("Viewport", rect); Stretch(viewport); viewport.offsetMax = new Vector2(-9, 0);
                viewport.gameObject.AddComponent<RectMask2D>();
                scrollContent = Rect("Content", viewport);
                scrollContent.anchorMin = new Vector2(0, 1); scrollContent.anchorMax = Vector2.one; scrollContent.pivot = new Vector2(0, 1);
                scrollContent.anchoredPosition = Vector2.zero;
                var textRect = Rect("VisibleRankRows", scrollContent);
                textRect.anchorMin = new Vector2(0, 1); textRect.anchorMax = Vector2.one; textRect.pivot = new Vector2(0, 1);
                text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
                text.font = source.font; text.fontSize = Math.Min(16, source.fontSize); text.color = source.color;
                text.richText = true; text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Ellipsis;
                text.alignment = TextAlignmentOptions.TopLeft; text.raycastTarget = false;
                text.lineSpacing = 0; text.margin = new Vector4(2, 1, 2, 0);
                var barRect = Rect("Scrollbar", rect); barRect.anchorMin = new Vector2(1, 0); barRect.anchorMax = Vector2.one;
                barRect.pivot = new Vector2(1, 0.5f); barRect.sizeDelta = new Vector2(6, 0); barRect.anchoredPosition = Vector2.zero;
                barRect.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.08f);
                var handle = Rect("Handle", barRect); Stretch(handle); var handleImage = handle.gameObject.AddComponent<Image>();
                handleImage.color = new Color(0.95f, 0.7f, 0.3f, 0.85f);
                var bar = barRect.gameObject.AddComponent<Scrollbar>(); bar.direction = Scrollbar.Direction.BottomToTop;
                bar.handleRect = handle; bar.targetGraphic = handleImage;
                scroll.viewport = viewport; scroll.content = scrollContent; scroll.verticalScrollbar = bar;
                scroll.onValueChanged.AddListener(delegate(Vector2 ignored) { RenderRows(); });
                root.SetActive(false);
            }
            internal void Hide() { root.SetActive(false); }
            internal void Show(WeaponRecord value, int sibling, TextMeshProUGUI source)
            {
                text.font = source.font;
                text.fontSize = Math.Min(WeaponText.IsChinese(WeaponText.Language()) ? 16 : 13, source.fontSize);
                record = value; visible = Math.Min(4, record.upgrades.Count);
                layout.minHeight = layout.preferredHeight = visible * RowHeight + 3;
                scrollContent.sizeDelta = new Vector2(0, record.upgrades.Count * RowHeight);
                scrollContent.anchoredPosition = Vector2.zero;
                root.transform.SetSiblingIndex(sibling); root.SetActive(true);
                scroll.verticalNormalizedPosition = 1; RenderRows();
            }
            private void RenderRows()
            {
                if (record == null) return;
                int start = Math.Max(0, Math.Min(record.upgrades.Count - visible, (int)(scrollContent.anchoredPosition.y / RowHeight)));
                var rows = record.upgrades.AsEnumerable().Reverse().Skip(start).Take(visible + 1);
                text.text = string.Join("\n", rows.Select(level => "Lv." + level.rank.ToString("00") + "  " + string.Join("  ", level.gains.Select(g => WeaponText.HistoryLabel(g.stat) + " " + Progression.Colored(g.percent)).ToArray())).ToArray());
                text.rectTransform.anchoredPosition = new Vector2(0, -start * RowHeight);
                text.rectTransform.sizeDelta = new Vector2(0, (visible + 1) * RowHeight);
            }
        }
    }
}
