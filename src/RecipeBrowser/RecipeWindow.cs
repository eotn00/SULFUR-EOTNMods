using System;
using System.Collections.Generic;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Description=PerfectRandom.Sulfur.Core.UI.ItemDescription.ItemDescription;

namespace LocalSulfur.RecipeBrowser
{
    internal sealed class RecipeWindow
    {
        private static readonly Color Gold=new Color(1,.70f,.23f);
        private GameObject root,previousSelection;
        private TMP_FontAsset font;
        private TextMeshProUGUI title,groupTitle,variantTitle;
        private Image icon;
        private ScrollRect groups,variants;
        private Button usesButton,sourcesButton;
        private Toggle allRecipes;
        private TextMeshProUGUI allRecipesLabel;
        private RecipeIndex index;
        private int item,selected;
        private bool showingUses;
        private readonly Dictionary<int,List<RecipeEntry>> byProduct=new Dictionary<int,List<RecipeEntry>>();
        private readonly Dictionary<int,Image> borders=new Dictionary<int,Image>();
        private Description tooltip;
        private Canvas tooltipCanvas;
        private Canvas ownedTooltipCanvas;
        private bool addedCanvas,oldOverride;
        private int oldSorting;
        private object tooltipOwner;
        private Transform tooltipParent;
        private int tooltipSibling;
        private Vector2 tooltipAnchorMin,tooltipAnchorMax,tooltipPivot,tooltipSize;
        private Vector3 tooltipPosition,tooltipScale;
        private Quaternion tooltipRotation;
        internal bool IsOpen {get{return root!=null&&root.activeSelf;}}
        private static RectTransform Rect(string name,Transform parent,Vector2 min,Vector2 max,Vector2 low,Vector2 high)
        {
            var g=new GameObject(name,typeof(RectTransform));g.transform.SetParent(parent,false);
            var r=(RectTransform)g.transform;r.anchorMin=min;r.anchorMax=max;r.offsetMin=low;r.offsetMax=high;return r;
        }
        private TextMeshProUGUI Label(Transform parent,string text,int size,TextAlignmentOptions align=TextAlignmentOptions.MidlineLeft)
        {
            var r=Rect("Text",parent,Vector2.zero,Vector2.one,new Vector2(8,2),new Vector2(-8,-2));
            var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.fontSize=size;t.color=Gold;t.text=text;t.richText=false;t.raycastTarget=false;t.alignment=align;
            t.enableWordWrapping=true;t.overflowMode=TextOverflowModes.Ellipsis;return t;
        }
        private Button Button(Transform parent,string name,Vector2 min,Vector2 max,string label,Action action)
        {
            var r=Rect(name,parent,min,max,new Vector2(4,4),new Vector2(-4,-4));var image=r.gameObject.AddComponent<Image>();image.color=new Color(.27f,.16f,.07f,.65f);
            var button=r.gameObject.AddComponent<Button>();button.targetGraphic=image;var colors=button.colors;colors.highlightedColor=new Color(1,.82f,.5f);button.colors=colors;
            Label(r,label,24,TextAlignmentOptions.Center);button.onClick.AddListener(()=>action());return button;
        }
        private ScrollRect Scroll(Transform parent,string name,Vector2 min,Vector2 max)
        {
            var r=Rect(name,parent,min,max,new Vector2(8,12),new Vector2(-8,-8));var scroll=r.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=42;
            var view=Rect("Viewport",r,Vector2.zero,Vector2.one,Vector2.zero,new Vector2(-18,0));view.gameObject.AddComponent<Image>().color=new Color(.16f,.09f,.035f,.14f);view.gameObject.AddComponent<RectMask2D>();scroll.viewport=view;
            var content=Rect("Content",view,new Vector2(0,1),Vector2.one,Vector2.zero,Vector2.zero);content.pivot=new Vector2(.5f,1);scroll.content=content;
            var layout=content.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=8;layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
            var fit=content.gameObject.AddComponent<ContentSizeFitter>();fit.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            var bar=Rect("Scrollbar",r,new Vector2(1,0),Vector2.one,new Vector2(-9,0),Vector2.zero);bar.gameObject.AddComponent<Image>().color=new Color(.3f,.17f,.05f,.4f);
            var handle=Rect("Handle",bar,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);var image=handle.gameObject.AddComponent<Image>();image.color=Gold;
            var scrollbar=bar.gameObject.AddComponent<Scrollbar>();scrollbar.handleRect=handle;scrollbar.targetGraphic=image;scrollbar.direction=Scrollbar.Direction.BottomToTop;scroll.verticalScrollbar=scrollbar;
            return scroll;
        }
        private static void Clear(ScrollRect scroll)
        {
            for(int i=scroll.content.childCount-1;i>=0;i--){var g=scroll.content.GetChild(i).gameObject;g.SetActive(false);UnityEngine.Object.Destroy(g);}
        }
        private static RectTransform Row(ScrollRect scroll,string name,float height)
        {
            var r=Rect(name,scroll.content,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);var layout=r.gameObject.AddComponent<LayoutElement>();layout.preferredHeight=height;layout.minHeight=height;return r;
        }
        private void Build(Action close)
        {
            var native=StaticInstance<UIManager>.Instance.InventoryUI.itemDescription;
            var sample=(ItemDescriptionText)AccessTools.Field(typeof(Description),"flavorText").GetValue(native);font=sample.textComp.font;
            root=new GameObject("CraftingReference",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));UnityEngine.Object.DontDestroyOnLoad(root);
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=32000;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var shade=Rect("Backdrop",root.transform,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);shade.gameObject.AddComponent<Image>().color=new Color(.04f,.025f,.01f,.76f);
            var panel=Rect("Panel",shade,new Vector2(.085f,.09f),new Vector2(.915f,.91f),Vector2.zero,Vector2.zero);panel.gameObject.AddComponent<Image>().color=new Color(.15f,.095f,.045f,.91f);
            var header=Rect("Header",panel,new Vector2(0,.88f),Vector2.one,new Vector2(16,0),new Vector2(-16,-8));
            var art=Rect("Icon",header,Vector2.zero,new Vector2(.065f,1),new Vector2(8,8),new Vector2(-8,-8));icon=art.gameObject.AddComponent<Image>();icon.preserveAspect=true;icon.raycastTarget=false;
            var heading=Rect("Heading",header,new Vector2(.075f,0),new Vector2(.62f,1),Vector2.zero,Vector2.zero);title=Label(heading,"",30);
            var option=Rect("ShowAllRecipes",header,new Vector2(.63f,.15f),new Vector2(.93f,.85f),Vector2.zero,Vector2.zero);
            option.gameObject.AddComponent<Image>().color=Color.clear;
            allRecipes=option.gameObject.AddComponent<Toggle>();
            var box=Rect("Box",option,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(0,-13),new Vector2(26,13));
            var background=box.gameObject.AddComponent<Image>();background.color=new Color(.5f,.32f,.12f,1);
            var mark=Rect("Checked",box,Vector2.zero,Vector2.one,new Vector2(5,5),new Vector2(-5,-5));var check=mark.gameObject.AddComponent<Image>();check.color=Gold;check.raycastTarget=false;
            allRecipes.targetGraphic=background;allRecipes.graphic=check;
            var optionText=Rect("Label",option,Vector2.zero,Vector2.one,new Vector2(30,0),Vector2.zero);allRecipesLabel=Label(optionText,"",22);
            allRecipes.onValueChanged.AddListener(RecipeBrowserPlugin.SetAllRecipes);
            Button(header,"Close",new Vector2(.94f,.15f),new Vector2(1,.85f),"X",close);
            var tabs=Rect("Tabs",panel,new Vector2(0,.79f),new Vector2(1,.88f),new Vector2(16,0),new Vector2(-16,0));
            usesButton=Button(tabs,"Uses",Vector2.zero,new Vector2(.5f,1),"",()=>Render(true));sourcesButton=Button(tabs,"Sources",new Vector2(.5f,0),Vector2.one,"",()=>Render(false));
            var leftHeading=Rect("ProductsHeading",panel,new Vector2(.01f,.72f),new Vector2(.34f,.79f),Vector2.zero,Vector2.zero);groupTitle=Label(leftHeading,"",25);
            var rightHeading=Rect("VariantsHeading",panel,new Vector2(.36f,.72f),new Vector2(.99f,.79f),Vector2.zero,Vector2.zero);variantTitle=Label(rightHeading,"",25);
            var rule=Rect("Rule",panel,new Vector2(.01f,.72f),new Vector2(.99f,.72f),Vector2.zero,new Vector2(0,2));rule.gameObject.AddComponent<Image>().color=Gold;
            groups=Scroll(panel,"Products",new Vector2(.01f,0),new Vector2(.34f,.72f));variants=Scroll(panel,"Variants",new Vector2(.36f,0),new Vector2(.99f,.72f));
            groups.onValueChanged.AddListener(_=>HideTooltip());variants.onValueChanged.AddListener(_=>HideTooltip());
        }
        internal void Show(ItemDefinition definition,RecipeIndex data,Action close)
        {
            if(EventSystem.current!=null)previousSelection=EventSystem.current.currentSelectedGameObject;
            if(root==null)Build(close);index=data;item=definition.id.value;root.SetActive(true);title.text=RecipeBrowserPlugin.Pick("我食纪事","what's this")+" | "+definition.LocalizedDisplayName;icon.sprite=definition.artwork;icon.enabled=icon.sprite!=null;
            usesButton.GetComponentInChildren<TextMeshProUGUI>().text=RecipeBrowserPlugin.Pick("可用于制作","Used to craft");sourcesButton.GetComponentInChildren<TextMeshProUGUI>().text=RecipeBrowserPlugin.Pick("制作来源","Crafting sources");
            Render(data.Uses.ContainsKey(item));(usesButton.interactable?usesButton:sourcesButton).Select();
        }
        internal void Refresh(RecipeIndex data){index=data;Render(showingUses);}
        private void Render(bool uses)
        {
            HideTooltip();showingUses=uses;byProduct.Clear();borders.Clear();Clear(groups);Clear(variants);List<RecipeEntry> recipes;
            allRecipes.SetIsOnWithoutNotify(RecipeBrowserPlugin.AllRecipes);
            allRecipesLabel.text=RecipeBrowserPlugin.Pick("查看所有配方","Show all recipes");
            if((uses?index.Uses:index.Sources).TryGetValue(item,out recipes))foreach(var r in recipes){List<RecipeEntry> list;if(!byProduct.TryGetValue(r.Product,out list))byProduct.Add(r.Product,list=new List<RecipeEntry>());list.Add(r);}
            groupTitle.text=RecipeBrowserPlugin.Pick("成品","Products");variantTitle.text=RecipeBrowserPlugin.AllRecipes?RecipeBrowserPlugin.Pick("全部配方变体","All recipe variants"):RecipeBrowserPlugin.Pick("已解锁的配方变体","Learned recipe variants");
            var products=new List<int>(byProduct.Keys);products.Sort((a,b)=>string.Compare(RecipeBrowserPlugin.Name(a),RecipeBrowserPlugin.Name(b),StringComparison.CurrentCulture));
            foreach(int id in products)
            {
                int product=id;var row=Row(groups,"Product",64);var image=row.gameObject.AddComponent<Image>();image.color=new Color(.3f,.18f,.05f,.2f);borders.Add(id,image);
                var button=row.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(()=>SelectProduct(product));
                var art=Rect("ProductIcon",row,new Vector2(0,0),new Vector2(.19f,1),new Vector2(6,8),new Vector2(-6,-8));AddIcon(art,id);
                var label=Rect("ProductName",row,new Vector2(.2f,0),Vector2.one,Vector2.zero,Vector2.zero);Label(label,RecipeBrowserPlugin.Name(id),23);
            }
            usesButton.interactable=!uses;sourcesButton.interactable=uses;
            if(products.Count>0)SelectProduct(products[0]);else Label(Row(variants,"Empty",70),RecipeBrowserPlugin.AllRecipes?RecipeBrowserPlugin.Pick("暂无配方","No recipes"):RecipeBrowserPlugin.Pick("暂无已解锁配方","No learned recipes"),23);
            Canvas.ForceUpdateCanvases();groups.verticalNormalizedPosition=1;
        }
        private void AddIcon(RectTransform parent,int id)
        {
            var definition=new ItemId((ushort)id).GetAsset();var image=parent.gameObject.AddComponent<Image>();image.sprite=definition==null?null:definition.artwork;image.preserveAspect=true;
            if(image.sprite==null){image.color=new Color(1,1,1,.08f);Label(parent,"?",23,TextAlignmentOptions.Center);}
            var hover=parent.gameObject.AddComponent<RecipeIconHover>();hover.Owner=this;hover.Item=id;
        }
        private void Token(Transform parent,int id,int count,bool highlight)
        {
            var slot=Rect("Ingredient",parent,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);var element=slot.gameObject.AddComponent<LayoutElement>();element.preferredWidth=102;element.minWidth=102;element.preferredHeight=60;element.minHeight=60;
            if(highlight){var bg=slot.gameObject.AddComponent<Image>();bg.color=new Color(.85f,.5f,.1f,.13f);bg.raycastTarget=false;}
            var number=Rect("Quantity",slot,Vector2.zero,new Vector2(.38f,1),Vector2.zero,Vector2.zero);Label(number,count.ToString(),27,TextAlignmentOptions.MidlineRight);
            var art=Rect("Artwork",slot,new Vector2(.38f,0),Vector2.one,new Vector2(0,5),new Vector2(-3,-5));AddIcon(art,id);
        }
        private void SelectProduct(int product)
        {
            HideTooltip();selected=product;Clear(variants);
            foreach(var entry in borders)entry.Value.color=entry.Key==product?new Color(.85f,.46f,.08f,.38f):new Color(.3f,.18f,.05f,.2f);
            var list=new List<RecipeEntry>(byProduct[product]);list.Sort((a,b)=>a.Id.CompareTo(b.Id));
            foreach(var recipe in list)
            {
                var amounts=new Dictionary<int,int>();var ids=new List<int>();foreach(var m in recipe.Ingredients){int count;if(!amounts.TryGetValue(m.Id,out count))ids.Add(m.Id);amounts[m.Id]=checked(count+m.Count);}
                // Fixed-size tokens wrap instead of shrinking artwork or overflowing a recipe row.
                var row=Row(variants,"RecipeVariant",80);var flow=row.gameObject.AddComponent<RecipeFlowLayout>();
                foreach(int id in ids)Token(row,id,amounts[id],showingUses&&id==item);
                var equals=Rect("Equals",row,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);var e=equals.gameObject.AddComponent<LayoutElement>();e.preferredWidth=40;e.minWidth=40;e.preferredHeight=60;Label(equals,"=",30,TextAlignmentOptions.Center);
                Token(row,recipe.Product,recipe.Count,!showingUses);
            }
            Canvas.ForceUpdateCanvases();variants.verticalNormalizedPosition=1;
        }
        internal void ShowTooltip(object owner,int id)
        {
            var ui=StaticInstance<UIManager>.Instance;if(ui==null||ui.InventoryUI==null)return;var definition=new ItemId((ushort)id).GetAsset();if(definition==null)return;
            HideTooltip();tooltip=ui.InventoryUI.itemDescription;tooltipOwner=owner;
            // An override-sorted child of the game's camera canvas cannot reliably cover an overlay canvas.
            var rect=(RectTransform)tooltip.transform;
            tooltipParent=rect.parent;tooltipSibling=rect.GetSiblingIndex();
            tooltipAnchorMin=rect.anchorMin;tooltipAnchorMax=rect.anchorMax;tooltipPivot=rect.pivot;tooltipSize=rect.sizeDelta;
            tooltipPosition=rect.anchoredPosition3D;tooltipScale=rect.localScale;tooltipRotation=rect.localRotation;
            rect.SetParent(root.transform,false);rect.SetAsLastSibling();
            tooltipCanvas=tooltip.GetComponent<Canvas>();addedCanvas=tooltipCanvas==null;if(addedCanvas){tooltipCanvas=tooltip.gameObject.AddComponent<Canvas>();ownedTooltipCanvas=tooltipCanvas;}
            oldOverride=tooltipCanvas.overrideSorting;oldSorting=tooltipCanvas.sortingOrder;tooltipCanvas.overrideSorting=true;tooltipCanvas.sortingOrder=32001;
            tooltip.Setup(definition);tooltip.SetOverridenPosition(Vector3.zero);tooltip.ToggleRaycast(false);tooltip.Show();
            var ratio=ui.inventoryCanvasConversionRatio;float scale=Math.Max(.001f,root.GetComponent<Canvas>().scaleFactor);
            rect.localScale=new Vector3(rect.localScale.x*ratio.x/scale,rect.localScale.y*ratio.y/scale,rect.localScale.z);
        }
        internal void HideTooltip(object owner=null)
        {
            if(owner!=null&&!ReferenceEquals(owner,tooltipOwner))return;
            try{if(tooltip!=null){tooltip.Hide();tooltip.SetOverridenPosition(Vector3.zero);}}
            finally
            {
                if(tooltipCanvas!=null){tooltipCanvas.overrideSorting=oldOverride;tooltipCanvas.sortingOrder=oldSorting;}
                if(tooltip!=null)
                {
                    if(tooltipParent!=null)
                    {
                        var rect=(RectTransform)tooltip.transform;rect.SetParent(tooltipParent,false);rect.SetSiblingIndex(tooltipSibling);
                        rect.anchorMin=tooltipAnchorMin;rect.anchorMax=tooltipAnchorMax;rect.pivot=tooltipPivot;rect.sizeDelta=tooltipSize;
                        rect.anchoredPosition3D=tooltipPosition;rect.localScale=tooltipScale;rect.localRotation=tooltipRotation;
                    }
                    else UnityEngine.Object.Destroy(tooltip.gameObject);
                }
                tooltip=null;tooltipCanvas=null;tooltipOwner=null;tooltipParent=null;addedCanvas=false;
            }
        }
        internal void Hide(){HideTooltip();if(root!=null)root.SetActive(false);if(EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(previousSelection!=null&&previousSelection.activeInHierarchy?previousSelection:null);previousSelection=null;}
        internal void Dispose(){HideTooltip();if(ownedTooltipCanvas!=null)UnityEngine.Object.Destroy(ownedTooltipCanvas);if(root!=null)UnityEngine.Object.Destroy(root);}
    }
    internal sealed class RecipeIconHover:MonoBehaviour,IPointerEnterHandler,IPointerExitHandler
    {
        internal RecipeWindow Owner;internal int Item;
        public void OnPointerEnter(PointerEventData e){try{Owner.ShowTooltip(this,Item);}catch(Exception ex){Owner.HideTooltip(this);Debug.LogWarning("Crafting Reference tooltip: "+ex.Message);}}
        public void OnPointerExit(PointerEventData e){Owner.HideTooltip(this);}
        private void OnDisable(){if(Owner!=null)Owner.HideTooltip(this);}
    }
    internal sealed class RecipeFlowLayout:LayoutGroup
    {
        private float height=80;
        public override void CalculateLayoutInputHorizontal(){base.CalculateLayoutInputHorizontal();SetLayoutInputForAxis(0,0,-1,0);}
        public override void CalculateLayoutInputVertical(){Arrange(false);SetLayoutInputForAxis(height,height,-1,1);var e=GetComponent<LayoutElement>();if(e!=null&&e.preferredHeight!=height)e.preferredHeight=height;}
        public override void SetLayoutHorizontal(){Arrange(true);}
        public override void SetLayoutVertical(){Arrange(true);}
        private void Arrange(bool apply)
        {
            float width=Math.Max(1,rectTransform.rect.width),x=0,y=0;
            foreach(var child in rectChildren){float w=LayoutUtility.GetPreferredWidth(child);if(x>0&&x+w>width){x=0;y+=72;}if(apply){SetChildAlongAxis(child,0,x,w);SetChildAlongAxis(child,1,y,60);}x+=w+4;}
            height=y+72;
        }
    }
}
