using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A UI graphic made of coloured triangles and four-sided shapes, all in one draw call.
/// The minimap draws its rooms, walls and your view cone with it.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class MapMesh : MaskableGraphic
{
    readonly List<UIVertex> verts = new List<UIVertex>();
    readonly List<int> tris = new List<int>();

    public void Clear()
    {
        verts.Clear();
        tris.Clear();
    }

    /// <summary>A shape with four corners, in order around it.</summary>
    public void Add(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        int i = verts.Count;
        Vert(a, color); Vert(b, color); Vert(c, color); Vert(d, color);
        tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
    }

    /// <summary>An axis-aligned rectangle.</summary>
    public void Rect(Rect r, Color color) =>
        Add(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), color);

    /// <summary>A triangle with a colour per corner (blends between them).</summary>
    public void Triangle(Vector2 a, Vector2 b, Vector2 c, Color ca, Color cb, Color cc)
    {
        int i = verts.Count;
        Vert(a, ca); Vert(b, cb); Vert(c, cc);
        tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
    }

    public void Apply() => SetVerticesDirty();

    void Vert(Vector2 p, Color c)
    {
        var v = UIVertex.simpleVert;
        v.position = p;
        v.color = c;
        verts.Add(v);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        foreach (var v in verts) vh.AddVert(v);
        for (int i = 0; i + 2 < tris.Count; i += 3) vh.AddTriangle(tris[i], tris[i + 1], tris[i + 2]);
    }
}
