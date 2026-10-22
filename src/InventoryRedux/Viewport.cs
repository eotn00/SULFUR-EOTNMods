using System;
using System.Collections.Generic;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;
using PerfectRandom.Sulfur.Core.UI.Inventory;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LocalSulfur.UnifiedInventory
{
    public sealed class Viewport:MonoBehaviour
    {
        internal static readonly HashSet<Viewport> Live=new HashSet<Viewport>();
        internal ItemGrid Grid {get{return grid;}}
        internal ContainerSettings Options {get{return settings;}}
        private ItemGrid grid;
        private ContainerSettings settings;
        private RectTransform frame,content,parent;
        private InventoryUI ui;
        private GridSlotVisualPool pool;
        private readonly Dictionary<int,GridSlotVisual> visuals=new Dictionary<int,GridSlotVisual>();
        private readonly List<int> retired=new List<int>();
        private readonly List<RaycastResult> hits=new List<RaycastResult>();
        private PointerEventData pointer;
        private EventSystem events;
        private int first,visible,lastScreenWidth,lastScreenHeight;
        private int currentPage;
        private readonly Dictionary<int,int> pageScroll=new Dictionary<int,int>();
        internal bool Resizing;
        private bool initialized,layoutDrag;
        internal static bool IsLayoutDragging;
        private float nextEdge;
        private Vector2 dragStart,offsetStart,layoutOffset;
        private Vector2 lastParentSize;
        private Vector3 lastParentPosition,lastParentScale;
        private float lastScale;

        public int First {get{return first;}}
        public int Visible {get{return visible;}}
        internal Rect ScreenRect
        {
            get {if(!initialized)return new Rect();var c=new Vector3[4];frame.GetWorldCorners(c);return Rect.MinMaxRect(c[0].x,Screen.height-c[2].y,c[2].x,Screen.height-c[0].y);}
        }
        internal void ScrollTo(int row){Move(row-first);}
        internal int PageRows {get{return Math.Min(settings.Rows.Value,grid.gridSize.y);}}
        internal int PageCount {get{return Math.Max(1,grid.gridSize.y/PageRows);}}
        internal int CurrentPage {get{return currentPage;}}
        internal int PageBottom {get{return Operations.PageBottom(PageRows,PageCount,currentPage);}}
        internal int PageTopScroll {get{return PageBottom+PageRows-visible;}}
        internal void TurnPage(int direction)
        {
            pageScroll[currentPage]=first-PageBottom;
            currentPage=Geometry.Clamp(currentPage+direction,0,PageCount-1);
            int saved;first=PageBottom+(pageScroll.TryGetValue(currentPage,out saved)?saved:PageRows-visible);
            grid.SetHoveredSlot(null);grid.UnhighlightAllSlots();Layout();
        }
        internal bool OwnsSurface(GameObject obj){return frame!=null&&frame.gameObject==obj;}

        internal void Configure(ItemGrid owner,ContainerSettings options)
        {Release();grid=owner;settings=options;Live.Add(this);}

        internal void Initialize()
        {
            if(initialized||grid==null||grid.slots==null)return;
            content=grid.RectTransform;parent=content.parent as RectTransform;
            if(parent==null)throw new InvalidOperationException("Inventory requires RectTransform parent");
            ui=StaticInstance<UIManager>.Instance.InventoryUI;
            pool=ui.gridSlotVisualPool;
            var obj=new GameObject("UnifiedInventoryViewport",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(RectMask2D),typeof(LayoutElement));
            // The native parent may lay out its children after our screen-space placement.
            obj.GetComponent<LayoutElement>().ignoreLayout=true;
            var surface=obj.GetComponent<Image>();surface.color=Color.clear;surface.raycastTarget=true;
            frame=obj.GetComponent<RectTransform>();frame.SetParent(parent,false);
            frame.SetSiblingIndex(content.GetSiblingIndex());
            frame.anchorMin=frame.anchorMax=new Vector2(.5f,.5f);frame.pivot=Vector2.zero;
            content.SetParent(frame,false);
            content.anchorMin=content.anchorMax=content.pivot=Vector2.zero;
            content.localScale=Vector3.one;
            initialized=true;
            visible=Math.Min(settings.VisibleRows.Value,PageRows);
            Layout();
            RestorePosition();
        }
        internal void RestorePosition()
        {
            if(settings.ActivePage.Value<0){FocusItems();return;}
            currentPage=Geometry.Clamp(settings.ActivePage.Value,0,PageCount-1);
            pageScroll.Clear();var offsets=settings.PageScroll.Value.Split(',');
            for(int i=0;i<offsets.Length&&i<PageCount;i++){int offset;if(int.TryParse(offsets[i],out offset))pageScroll[i]=Geometry.Clamp(offset,0,PageRows-visible);}
            int saved;first=PageBottom+(pageScroll.TryGetValue(currentPage,out saved)?saved:PageRows-visible);Layout();
        }
        private void SavePosition()
        {
            if(!initialized||settings==null)return;
            pageScroll[currentPage]=first-PageBottom;var offsets=new string[PageCount];
            for(int i=0;i<offsets.Length;i++){int value;offsets[i]=(pageScroll.TryGetValue(i,out value)?value:PageRows-visible).ToString();}
            settings.ActivePage.Value=currentPage;settings.PageScroll.Value=string.Join(",",offsets);
        }

        internal void Layout()
        {
            if(!initialized)return;
            visible=Math.Min(settings.VisibleRows.Value,PageRows);
            currentPage=Geometry.Clamp(currentPage,0,PageCount-1);
            first=Geometry.Clamp(first,PageBottom,PageTopScroll);
            float width=grid.gridSize.x*grid.tileSize.x,height=visible*grid.tileSize.y;
            frame.sizeDelta=new Vector2(width,height);
            // Native containers are positional anchors, not reliable layout bounds.
            bool backpack=grid.IsPlayerInventory;
            Vector2 lower,upper;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,new Vector2(Screen.width*(backpack?.24f:.52f),Screen.height*.08f),null,out lower);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,new Vector2(Screen.width*(backpack?.50f:.98f),Screen.height*.92f),null,out upper);
            float fit=Mathf.Min((upper.x-lower.x)/width,(upper.y-lower.y)/height);
            float scale=Mathf.Max(.1f,fit)*settings.Scale.Value;
            frame.localScale=Vector3.one*scale;
            layoutOffset=new Vector2(settings.X.Value,settings.Y.Value);
            frame.localPosition=(Vector3)((lower+upper)*.5f-new Vector2(width,height)*scale*.5f+layoutOffset);
            content.anchoredPosition=new Vector2(0,-first*grid.tileSize.y);
            lastParentSize=parent.rect.size;lastScreenWidth=Screen.width;lastScreenHeight=Screen.height;lastScale=settings.Scale.Value;
            lastParentPosition=parent.position;lastParentScale=parent.lossyScale;
            if(grid.isActiveAndEnabled)Acquire();
        }

        internal bool Contains(Vector2 screen)
        {return initialized&&grid.isActiveAndEnabled&&RectTransformUtility.RectangleContainsScreenPoint(frame,screen,null);}

        internal void ReportLayout()
        {
            if(!initialized)return;
            var corners=new Vector3[4];frame.GetWorldCorners(corners);
            Plugin.Instance.Report("Layout "+grid.name+": frame="+corners[0]+".."+corners[2]+", content position="+content.anchoredPosition+", anchors="+content.anchorMin+"/"+content.anchorMax+", pivot="+content.pivot+", parent="+parent.name+", parent components="+string.Join(",",Array.ConvertAll(parent.GetComponents<Component>(),c=>c==null?"missing":c.GetType().Name)));
        }

        internal void Acquire()
        {
            if(!initialized||pool==null||!grid.isActiveAndEnabled)return;
            retired.Clear();
            foreach(var pair in visuals)
            {
                int row=pair.Key/grid.gridSize.x;
                if(row<first||row>=first+visible)retired.Add(pair.Key);
            }
            foreach(int key in retired)ReleaseOne(key);
            for(int y=first;y<first+visible;y++)for(int x=0;x<grid.gridSize.x;x++)
            {
                int key=y*grid.gridSize.x+x;if(visuals.ContainsKey(key))continue;
                var visual=pool.Get();
                pool.AttachTo(visual,content);
                var rect=(RectTransform)visual.transform;
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,grid.tileSize.x);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,grid.tileSize.y);
                grid.slots[x,y].AttachVisual(visual);
                visuals.Add(key,visual);
            }
            // Keep native deferred parenting: SetParent is illegal during OnEnable/OnDisable.
            foreach(var pair in visuals)
            {
                int x=pair.Key%grid.gridSize.x,y=pair.Key/grid.gridSize.x;
                var t=pair.Value.transform;var p=new Vector3(x*grid.tileSize.x,y*grid.tileSize.y,0);
                if(t.localPosition!=p)t.localPosition=p;
            }
        }

        private void ReleaseOne(int key)
        {
            var visual=visuals[key];int x=key%grid.gridSize.x,y=key/grid.gridSize.x;
            grid.slots[x,y].DetachVisual();
            if(visual!=null&&pool!=null){if(ui!=null)ui.OnGridSlotVisualReleased(visual);pool.Release(visual);}
            visuals.Remove(key);
        }

        internal void Release()
        {
            retired.Clear();retired.AddRange(visuals.Keys);
            foreach(int key in retired)ReleaseOne(key);
        }

        internal void BeforeGridDestroy()
        {
            // Called explicitly by the game before Destroy, never from OnDisable.
            Release();if(pool!=null)pool.FlushPendingReparents();
        }

        private void Move(int rows)
        {
            int value=Geometry.Clamp(first+rows,PageBottom,PageTopScroll);if(value==first)return;
            grid.SetHoveredSlot(null);grid.UnhighlightAllSlots();first=value;
            content.anchoredPosition=new Vector2(0,-first*grid.tileSize.y);
            Acquire();
        }

        internal void FocusItems()
        {
            if(!initialized)return;
            int top=-1;
            foreach(var item in grid.AllItemsInGridOnly())
                if(item!=null)top=Math.Max(top,item.gridPosition.y+item.InventorySize.y);
            currentPage=top<=0?0:Geometry.Clamp((grid.gridSize.y-top)/PageRows,0,PageCount-1);
            first=Geometry.Clamp(top-visible,PageBottom,PageTopScroll);
            Layout();
        }

        private void OnEnable()
        {
            if(initialized)Layout();
        }

        internal void Reveal(InventoryItem item)
        {
            if(item==null)return;
            currentPage=Geometry.Clamp((grid.gridSize.y-1-item.gridPosition.y)/PageRows,0,PageCount-1);
            first=Geometry.Clamp(item.gridPosition.y+item.InventorySize.y-visible,PageBottom,PageTopScroll);
            Layout();
        }

        internal bool Topmost(Vector2 screen)
        {
            if(Menu.Open||InventoryOverlay.Captures(screen)||!Contains(screen)||EventSystem.current==null)return false;
            if(events!=EventSystem.current){events=EventSystem.current;pointer=new PointerEventData(events);}
            pointer.Reset();pointer.position=screen;hits.Clear();events.RaycastAll(pointer,hits);
            foreach(var hit in hits)
            {
                if(hit.gameObject==null)continue;
                var item=hit.gameObject.GetComponentInParent<InventoryItem>();
                if(item!=null&&item.isBeingDragged)continue;
                if(hit.gameObject==frame.gameObject)return true;
                return hit.gameObject.GetComponentInParent<ItemGrid>()==grid;
            }
            return false;
        }

        private void Update()
        {
            if(!initialized||!grid.isActiveAndEnabled||ui==null||!ui.IsOpened)return;
            var game=StaticInstance<GameManager>.Instance;
            if(game==null||!game.InputActions.Inventory.Get().enabled)return;
            var mouse=Mouse.current;if(mouse==null)return;
            Vector2 screen=mouse.position.ReadValue();var keyboard=Keyboard.current;
            bool alt=Controls.LayoutHeld;
            if(layoutDrag)
            {
                if(Controls.DragHeld&&alt)
                {
                    Vector2 point;RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,screen,null,out point);
                    Vector2 offset=offsetStart+point-dragStart;
                    frame.anchoredPosition+=offset-layoutOffset;layoutOffset=offset;
                }
                else
                {settings.X.Value=layoutOffset.x;settings.Y.Value=layoutOffset.y;layoutDrag=false;IsLayoutDragging=false;}
                return;
            }
            if(!Topmost(screen))return;
            if(alt&&ui.draggingItem==null&&Controls.DragStarted)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,screen,null,out dragStart);
                offsetStart=layoutOffset;layoutDrag=true;IsLayoutDragging=true;return;
            }
            float wheel=mouse.scroll.ReadValue().y;
            if(Math.Abs(wheel)>.01f)
            {
                if(alt&&ui.draggingItem==null)
                {settings.Scale.Value=Mathf.Clamp(settings.Scale.Value+(wheel>0?.05f:-.05f),.25f,2f);Layout();}
                else if(!alt)
                {
                    int step=Plugin.Instance.Settings.WheelPages.Value?visible:Plugin.Instance.Settings.WheelRows.Value;
                    Move(wheel>0?step:-step);
                }
            }
            if(ui.draggingItem!=null&&Time.unscaledTime>=nextEdge)
            {
                Vector2 p;RectTransformUtility.ScreenPointToLocalPointInRectangle(frame,screen,null,out p);
                if(p.y<grid.tileSize.y*.5f){Move(-1);nextEdge=Time.unscaledTime+.12f;}
                else if(p.y>frame.rect.height-grid.tileSize.y*.5f){Move(1);nextEdge=Time.unscaledTime+.12f;}
            }
        }

        private void LateUpdate()
        {
            if(!initialized||!grid.isActiveAndEnabled)return;
            if(lastScreenWidth!=Screen.width||lastScreenHeight!=Screen.height||lastParentSize!=parent.rect.size||lastParentPosition!=parent.position||lastParentScale!=parent.lossyScale||lastScale!=settings.Scale.Value||visible!=Math.Min(settings.VisibleRows.Value,PageRows))Layout();
        }

        private void OnDisable()
        {
            SavePosition();
            if(layoutDrag){settings.X.Value=layoutOffset.x;settings.Y.Value=layoutOffset.y;IsLayoutDragging=false;}
            Release();layoutDrag=false;
        }
        private void OnDestroy()
        {
            Live.Remove(this);
            Release();if(grid!=null)Plugin.Stashes.Remove(grid);
            if(frame!=null)Destroy(frame.gameObject);
        }
    }
}
