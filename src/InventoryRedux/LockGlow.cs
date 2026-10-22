using UnityEngine;
using UnityEngine.UI;

namespace LocalSulfur.UnifiedInventory
{
    // Inward-fading border; no texture allocation, animation or pointer interception.
    public sealed class LockGlow:MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();var r=rectTransform.rect;
            float depth=Mathf.Min(9f,Mathf.Min(r.width,r.height)*.25f);
            for(int i=0;i<8;i++)
            {
                float a=depth*i/8f,b=depth*(i+1)/8f;
                Color outer=new Color(.12f,.58f,1f,.7f*(1-i/8f)*(1-i/8f));
                Color inner=new Color(.12f,.58f,1f,.7f*(1-(i+1)/8f)*(1-(i+1)/8f));
                int start=mesh.currentVertCount;
                Vector2[] points={new Vector2(r.xMin+a,r.yMin+a),new Vector2(r.xMin+a,r.yMax-a),new Vector2(r.xMax-a,r.yMax-a),new Vector2(r.xMax-a,r.yMin+a),new Vector2(r.xMin+b,r.yMin+b),new Vector2(r.xMin+b,r.yMax-b),new Vector2(r.xMax-b,r.yMax-b),new Vector2(r.xMax-b,r.yMin+b)};
                for(int p=0;p<8;p++)mesh.AddVert(points[p],p<4?outer:inner,Vector2.zero);
                for(int p=0;p<4;p++){int next=(p+1)%4;mesh.AddTriangle(start+p,start+next,start+4+next);mesh.AddTriangle(start+p,start+4+next,start+4+p);}
            }
        }
    }
}
