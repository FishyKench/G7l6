using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Art Swapper data + runtime. The editor window lives in Assets/Editor/ArtSwapWindow.cs.
// The list is stored in Assets/Resources/ArtSwaps.asset so swaps also hit objects the
// game builds while it runs (town, fair, clothes, trophies...).

public enum ArtSwapMatch { ThisObject, EverythingWithThisName }
public enum ArtSwapFit { FitInside, Stretch, KeepMyScale }

[System.Serializable]
public class ArtSwapEntry
{
    public bool enabled = true;
    public ArtSwapMatch match = ArtSwapMatch.ThisObject;
    public string scene = "";        // scene the object lives in ("" = any)
    public string path = "";         // hierarchy path, e.g. "Town/Fair/Ferris wheel"
    public string objectName = "";   // last part of the path
    public GameObject replacement;
    public ArtSwapFit fit = ArtSwapFit.FitInside;
    public Vector3 rotation;         // extra rotation for your model
    public Vector3 offset;           // extra offset, in the old object's space
    public float scale = 1f;         // extra multiplier

    public string Key => match == ArtSwapMatch.ThisObject ? scene + "|" + path : "*|" + objectName;
}

public class ArtSwapList : ScriptableObject
{
    public List<ArtSwapEntry> entries = new List<ArtSwapEntry>();
}

// Put on an object once it has been swapped. Remembers what was hidden so it can be undone.
public class ArtSwapMarker : MonoBehaviour
{
    public string key;
    public GameObject replacement;
    public List<Renderer> hidden = new List<Renderer>();
}

public static class ArtSwap
{
    public const string ChildName = "[Your model]";

    public static string PathOf(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    public static Transform FindByPath(Scene scene, string path)
    {
        if (!scene.IsValid() || string.IsNullOrEmpty(path)) return null;
        string[] parts = path.Split('/');
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name != parts[0]) continue;
            Transform t = root.transform;
            for (int i = 1; i < parts.Length && t != null; i++) t = FindChild(t, parts[i]);
            if (t != null) return t;
        }
        return null;
    }

    static Transform FindChild(Transform t, string name)
    {
        for (int i = 0; i < t.childCount; i++) if (t.GetChild(i).name == name) return t.GetChild(i);
        return null;
    }

    public static List<Transform> FindTargets(ArtSwapEntry e)
    {
        var list = new List<Transform>();
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene sc = SceneManager.GetSceneAt(s);
            if (!sc.isLoaded) continue;
            if (e.match == ArtSwapMatch.ThisObject)
            {
                if (!string.IsNullOrEmpty(e.scene) && sc.name != e.scene) continue;
                var t = FindByPath(sc, e.path);
                if (t != null) list.Add(t);
            }
            else
            {
                foreach (var root in sc.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (t.name == e.objectName && !InsideReplacement(t))
                            list.Add(t);
            }
        }
        return list;
    }

    static bool InsideReplacement(Transform t)
    {
        for (var p = t; p != null; p = p.parent) if (p.name == ChildName) return true;
        return false;
    }

    // Local-space bounds of all visible renderers under 'root' (ignoring a previous swap).
    public static bool LocalBounds(Transform root, IEnumerable<Renderer> rends, out Bounds b)
    {
        b = new Bounds(); bool any = false;
        foreach (var r in rends)
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            Bounds wb;
            if (r.isPartOfStaticBatch) wb = WorldToLocal(root, r.bounds);
            else if (r is MeshRenderer && r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                wb = Transform(r.transform, root, mf.sharedMesh.bounds);
            else if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                wb = Transform(r.transform, root, smr.sharedMesh.bounds);
            else wb = WorldToLocal(root, r.bounds);
            if (!any) { b = wb; any = true; } else b.Encapsulate(wb);
        }
        return any;
    }

    static Bounds Transform(Transform from, Transform to, Bounds lb)
    {
        Matrix4x4 m = to.worldToLocalMatrix * from.localToWorldMatrix;
        Bounds o = new Bounds(m.MultiplyPoint3x4(lb.center), Vector3.zero);
        Vector3 e = lb.extents;
        for (int i = 0; i < 8; i++)
            o.Encapsulate(m.MultiplyPoint3x4(lb.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
        return o;
    }

    static Bounds WorldToLocal(Transform to, Bounds wb)
    {
        Matrix4x4 m = to.worldToLocalMatrix;
        Bounds o = new Bounds(m.MultiplyPoint3x4(wb.center), Vector3.zero);
        Vector3 e = wb.extents;
        for (int i = 0; i < 8; i++)
            o.Encapsulate(m.MultiplyPoint3x4(wb.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
        return o;
    }

    static List<Renderer> OwnRenderers(Transform target)
    {
        var list = new List<Renderer>();
        foreach (var r in target.GetComponentsInChildren<Renderer>(true))
        {
            var m = r.GetComponentInParent<ArtSwapMarker>();
            if (m != null && m.replacement != null && r.transform.IsChildOf(m.replacement.transform)) continue;
            list.Add(r);
        }
        return list;
    }

    // Creates the replacement instance. Editor passes its own instantiate (keeps prefab link + undo).
    public static System.Func<GameObject, Transform, GameObject> Spawn = (p, parent) => Object.Instantiate(p, parent);
    public static System.Action<Object> BeforeChange = o => { };
    public static System.Action<Object> Created = o => { };
    public static System.Action<Object> Destroy = o => { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); };

    public static bool Apply(Transform target, ArtSwapEntry e)
    {
        if (target == null || e.replacement == null) return false;
        Revert(target, false);

        var rends = OwnRenderers(target);
        bool hasOld = LocalBounds(target, rends, out Bounds old);

        var marker = target.GetComponent<ArtSwapMarker>();
        if (marker == null)
        {
            marker = target.gameObject.AddComponent<ArtSwapMarker>();
            Created(marker);
        }
        else BeforeChange(marker);
        marker.key = e.Key;

        GameObject inst = Spawn(e.replacement, target);
        inst.name = ChildName;
        Created(inst);
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.Euler(e.rotation);
        inst.transform.localScale = Vector3.one;

        Vector3 lossy = target.lossyScale;
        Vector3 own = e.replacement.transform.localScale;
        if (!hasOld || !LocalBounds(target, inst.GetComponentsInChildren<Renderer>(true), out Bounds mine))
        {
            // nothing to measure: keep your model's own size at the old pivot
            inst.transform.localScale = new Vector3(own.x / Safe(lossy.x), own.y / Safe(lossy.y), own.z / Safe(lossy.z)) * e.scale;
        }
        else
        {
            Vector3 s = Vector3.one;
            Vector3 ms = mine.size, os = old.size;
            if (e.fit == ArtSwapFit.KeepMyScale)
                s = new Vector3(own.x / Safe(lossy.x), own.y / Safe(lossy.y), own.z / Safe(lossy.z));
            else if (e.fit == ArtSwapFit.Stretch)
                s = new Vector3(Div(os.x, ms.x), Div(os.y, ms.y), Div(os.z, ms.z));
            else
            {
                float k = Mathf.Min(Div(os.x, ms.x, 1e9f), Div(os.y, ms.y, 1e9f), Div(os.z, ms.z, 1e9f));
                if (k > 1e8f) k = 1f;
                s = Vector3.one * k;
            }
            s *= e.scale;
            inst.transform.localScale = s;
            // sit the bottom-centre of your model on the bottom-centre of the old one
            Vector3 mineBottom = new Vector3(mine.center.x, mine.min.y, mine.center.z);
            Vector3 oldBottom = new Vector3(old.center.x, old.min.y, old.center.z);
            inst.transform.localPosition = oldBottom - Vector3.Scale(mineBottom, s);
        }
        inst.transform.localPosition += e.offset;

        marker.replacement = inst;
        marker.hidden.Clear();
        foreach (var r in rends)
        {
            if (!r.enabled) continue;
            BeforeChange(r);
            r.enabled = false;
            marker.hidden.Add(r);
        }
        return true;
    }

    public static void Revert(Transform target, bool removeMarker = true)
    {
        if (target == null) return;
        var m = target.GetComponent<ArtSwapMarker>();
        if (m == null) return;
        BeforeChange(m);
        foreach (var r in m.hidden) if (r != null) { BeforeChange(r); r.enabled = true; }
        m.hidden.Clear();
        if (m.replacement != null)
        {
            m.replacement.SetActive(false);
            m.replacement.name = ChildName + " (old)";
            Destroy(m.replacement);
            m.replacement = null;
        }
        if (removeMarker) Destroy(m);
    }

    static float Safe(float v) => Mathf.Abs(v) < 1e-6f ? 1f : v;
    static float Div(float a, float b, float fallback = 1f) => b < 1e-5f ? fallback : a / b;
}

// Applies the list while the game runs, including to things built after the scene loads.
public class ArtSwapRunner : MonoBehaviour
{
    static ArtSwapList list;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        list = Resources.Load<ArtSwapList>("ArtSwaps");
        if (list == null || list.entries.Count == 0) return;
        var go = new GameObject("Art Swap Runner");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<ArtSwapRunner>();
    }

    void OnEnable() { SceneManager.sceneLoaded += Loaded; StartCoroutine(Loop()); }
    void OnDisable() { SceneManager.sceneLoaded -= Loaded; }
    void Loaded(Scene s, LoadSceneMode m) { quickScans = 10; }

    int quickScans = 10;

    IEnumerator Loop()
    {
        while (true)
        {
            ScanOnce();
            if (quickScans > 0) { quickScans--; yield return new WaitForSeconds(0.25f); }
            else yield return new WaitForSeconds(1.5f);
        }
    }

    static void ScanOnce()
    {
        foreach (var e in list.entries)
        {
            if (!e.enabled || e.replacement == null) continue;
            foreach (var t in ArtSwap.FindTargets(e))
            {
                var m = t.GetComponent<ArtSwapMarker>();
                if (m != null && m.key == e.Key && m.replacement != null) continue;
                ArtSwap.Apply(t, e);
            }
        }
    }
}
