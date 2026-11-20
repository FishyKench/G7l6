using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The house's floor plan (written by G7l6 > Build New House): every room's name, floor and
/// outline. Used by the minimap to draw the house and tell you which room you're in.
/// </summary>
public class HouseLayout : MonoBehaviour
{
    [System.Serializable]
    public class RoomInfo
    {
        public string name;
        public int floor;
        [Tooltip("Outline in plan metres (before the house scale): x, z, width, depth.")]
        public Rect rect;
        public string kind;
    }

    public float storeyHeight = 3f;
    [Tooltip("The house was built this much bigger than the plan.")]
    public float scale = 1f;
    public List<RoomInfo> rooms = new List<RoomInfo>();

    static HouseLayout instance;
    public static HouseLayout Instance
    {
        get
        {
            if (!instance) instance = FindObjectOfType<HouseLayout>();
            return instance;
        }
    }

    public int Floors
    {
        get
        {
            int max = 0;
            foreach (var r in rooms) max = Mathf.Max(max, r.floor);
            return max + 1;
        }
    }

    /// <summary>Which floor a height (feet level) is on.</summary>
    public int FloorAt(float y) => Mathf.Clamp(Mathf.FloorToInt((y + 0.6f) / (storeyHeight * scale)), 0, Floors - 1);

    /// <summary>A world position in plan metres.</summary>
    public Vector2 ToPlan(Vector3 world)
    {
        Vector3 local = transform.InverseTransformPoint(world);
        return new Vector2(local.x, local.z);
    }

    public RoomInfo RoomAt(Vector3 world, int floor)
    {
        Vector2 p = ToPlan(world);
        foreach (var r in rooms)
            if (r.floor == floor && r.rect.Contains(p)) return r;
        return null;
    }

    public Rect PlanBounds()
    {
        bool any = false;
        Rect b = default;
        foreach (var r in rooms)
        {
            if (!any) { b = r.rect; any = true; continue; }
            b.xMin = Mathf.Min(b.xMin, r.rect.xMin);
            b.yMin = Mathf.Min(b.yMin, r.rect.yMin);
            b.xMax = Mathf.Max(b.xMax, r.rect.xMax);
            b.yMax = Mathf.Max(b.yMax, r.rect.yMax);
        }
        return b;
    }
}
