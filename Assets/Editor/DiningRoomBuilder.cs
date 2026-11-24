using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menu: G7l6 > Dining Room (long table). Swaps the dining room's small round table for the house set's long Dining Table
/// (same spot, long side along the room), pulls six chairs up to it properly - two along each side, one at each end, seats
/// tucked under the top where they fit and never through it - and stands the vase in the middle. Nothing else in the house
/// moves. Undo with Ctrl+Z; save the scene to keep it. Running it again just re-seats the chairs.
/// </summary>
public static class DiningRoomBuilder
{
    const string TablePrefab = "Assets/ImportedAssets/VNB - Home Set/Prefabs/Livingroom/Dining Table.prefab";

    [MenuItem("G7l6/Dining Room (long table)")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[Dining] Stop Play mode first."); return; }
        Debug.Log("[Dining] " + Do(true));
    }

    /// <summary>Does it (with undo steps when 'undo'); returns what it did.</summary>
    public static string Do(bool undo)
    {
        var dining = GameObject.Find("Ground - Dining");
        if (!dining) return "No 'Ground - Dining' in the open scene.";
        if (undo) Undo.SetCurrentGroupName("Dining room (long table)");
        int group = undo ? Undo.GetCurrentGroup() : 0;

        Transform table = null, vase = null;
        var chairs = new List<Transform>();
        foreach (Transform t in dining.transform)
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("table") && !table) table = t;
            else if (n.Contains("chair")) chairs.Add(t);
            else if (n.StartsWith("vase")) vase = t;
        }
        if (!table) return "No table in the dining room.";
        string did = "";

        // ---- the table
        if (table.name != "Dining Table")
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(TablePrefab);
            if (!asset) return "Can't find " + TablePrefab;
            var ob = B(table.gameObject);
            var floor = new Vector3(ob.center.x, ob.min.y, ob.center.z);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, dining.scene);
            go.name = "Dining Table";
            go.transform.SetParent(dining.transform, false);
            go.transform.localScale = table.localScale;
            go.transform.rotation = Quaternion.Euler(0f, RoomLongAlongZ(dining) ? 90f : 0f, 0f); // (the model is long along its x)
            var nb = B(go);
            // the model's taller than the house's other tables: the same height as the old one (a touch longer and wider)
            float f = nb.size.y > 0.01f ? ob.size.y / nb.size.y : 1f;
            go.transform.localScale = Vector3.Scale(table.localScale, new Vector3(f * 1.15f, f, f * 1.15f));
            nb = B(go);
            go.transform.position += floor - new Vector3(nb.center.x, nb.min.y, nb.center.z);
            go.transform.SetSiblingIndex(table.GetSiblingIndex());
            var tight = typeof(HouseBuilder).GetMethod("TightColliders", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (tight != null) tight.Invoke(null, new object[] { go });
            if (!go.GetComponentsInChildren<Collider>().Any(c => !c.isTrigger)) go.AddComponent<BoxCollider>();
            if (undo) { Undo.RegisterCreatedObjectUndo(go, "Dining table"); Undo.DestroyObjectImmediate(table.gameObject); }
            else Object.DestroyImmediate(table.gameObject);
            table = go.transform;
            did += "Round table swapped for the long Dining Table. ";
        }
        Physics.SyncTransforms();
        var tb = B(table.gameObject);
        float top = SurfaceY(table, new Vector3(tb.center.x + tb.extents.x * 0.3f, 0f, tb.center.z + tb.extents.z * 0.3f), tb.max.y);
        Vector3 c = new Vector3(tb.center.x, 0f, tb.center.z);

        // ---- six seats
        if (chairs.Count == 0) return did + "No chairs in the dining room.";
        while (chairs.Count < 6)
        {
            var copy = Object.Instantiate(chairs[0].gameObject, dining.transform);
            copy.name = chairs[0].name;
            if (undo) Undo.RegisterCreatedObjectUndo(copy, "Dining chair");
            chairs.Add(copy.transform);
        }
        bool longZ = tb.size.z >= tb.size.x;
        Vector3 L = longZ ? Vector3.forward : Vector3.right, S = longZ ? Vector3.right : Vector3.forward;
        float halfL = Vector3.Dot(tb.extents, L), halfS = Vector3.Dot(tb.extents, S);
        float a = Mathf.Min(0.55f, halfL - 0.4f);
        var seats = new List<(Vector3 dir, float along)> { (S, -a), (S, a), (-S, -a), (-S, a), (L, 0f), (-L, 0f) };
        float floorY = tb.min.y;
        for (int i = 0; i < chairs.Count; i++)
        {
            var ch = chairs[i];
            if (undo) Undo.RecordObject(ch, "Dining chair");
            if (i >= seats.Count) { ch.gameObject.SetActive(false); continue; } // (more than six: the rest step out)
            var (d, along) = seats[i];
            Vector3 side = Vector3.Cross(Vector3.up, d);
            ch.rotation = Quaternion.LookRotation(-d, Vector3.up);   // facing the table
            ch.position = c + d * 3f + Vector3.up * 50f;              // (measured on its own, away from everything)
            Physics.SyncTransforms();
            var cb = B(ch.gameObject);
            float depth = Vector3.Dot(cb.extents, new Vector3(Mathf.Abs(d.x), 0f, Mathf.Abs(d.z))) * 2f;
            // the seat's height, at the front of the chair (the side that faces the table)
            Vector3 front = cb.center - d * depth * 0.3f;
            float seatTop = SurfaceY(ch, front, cb.min.y + 0.45f) - (cb.min.y);
            float edge = Vector3.Dot(tb.extents, new Vector3(Mathf.Abs(d.x), 0f, Mathf.Abs(d.z)));
            float tuck = floorY + seatTop < top - 0.12f ? 0.12f : -0.04f; // seat slides under the top, or stands just clear of it
            Vector3 want = c + d * (edge - tuck + depth * 0.5f) + side * Vector3.Dot(side, L) * along;
            ch.position += new Vector3(want.x - cb.center.x, floorY - cb.min.y, want.z - cb.center.z);
            ch.gameObject.SetActive(true);
        }
        did += $"{Mathf.Min(6, chairs.Count)} chairs pulled up. ";

        // ---- the vase in the middle
        if (vase)
        {
            if (undo) Undo.RecordObject(vase, "Vase");
            var vb = B(vase.gameObject);
            vase.position += new Vector3(tb.center.x - vb.center.x, top + 0.002f - vb.min.y, tb.center.z - vb.center.z);
            did += "Vase on the table.";
        }
        Physics.SyncTransforms();
        if (undo)
        {
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(dining.scene);
            Selection.activeGameObject = table.gameObject;
        }
        return did + " Save the scene to keep it.";
    }

    static bool RoomLongAlongZ(GameObject dining)
    {
        var layout = Object.FindObjectOfType<HouseLayout>();
        if (layout)
            foreach (var r in layout.rooms)
                if (r.name == "Dining") return r.rect.height >= r.rect.width;
        return true;
    }

    /// <summary>The top of this piece (its own colliders) under a point, looking straight down; 'fallback' if it can't tell.</summary>
    static float SurfaceY(Transform piece, Vector3 at, float fallback)
    {
        var hits = Physics.RaycastAll(new Vector3(at.x, 200f, at.z), Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        foreach (var h in hits) if (h.collider.transform.IsChildOf(piece) && h.point.y > best) best = h.point.y;
        return best > float.MinValue ? best : fallback;
    }

    static Bounds B(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>().Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
