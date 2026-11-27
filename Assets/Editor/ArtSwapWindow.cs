using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Tools > Art Swapper
// Left box: the object I generated. Right box: your model. Press Swap.
public class ArtSwapWindow : EditorWindow
{
    const string AssetPath = "Assets/Resources/ArtSwaps.asset";

    ArtSwapList list;
    Vector2 scroll;
    string search = "";
    readonly HashSet<int> open = new HashSet<int>();

    static readonly Color Accent = new Color(0.36f, 0.62f, 1f);
    static readonly Color Good = new Color(0.36f, 0.8f, 0.45f);
    static readonly Color Warn = new Color(1f, 0.72f, 0.3f);
    static readonly Color Bad = new Color(0.95f, 0.4f, 0.4f);

    GUIStyle titleStyle, sub, card, boxLabel, pill, arrow, drop, bigButton;

    [MenuItem("Tools/Art Swapper", false, 0)]
    static void Open()
    {
        var w = GetWindow<ArtSwapWindow>("Art Swapper");
        w.minSize = new Vector2(560, 360);
    }

    void OnEnable() { HookEditor(); }

    static void HookEditor()
    {
        ArtSwap.Spawn = (p, parent) =>
        {
            if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabAsset(p))
                return (GameObject)PrefabUtility.InstantiatePrefab(p, parent);
            return Instantiate(p, parent);
        };
        ArtSwap.Created = o => { if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(o, "Art swap"); };
        ArtSwap.BeforeChange = o => { if (!Application.isPlaying) Undo.RecordObject(o, "Art swap"); };
        ArtSwap.Destroy = o =>
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Undo.DestroyObjectImmediate(o);
        };
    }

    ArtSwapList List()
    {
        if (list != null) return list;
        list = AssetDatabase.LoadAssetAtPath<ArtSwapList>(AssetPath);
        if (list == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            list = CreateInstance<ArtSwapList>();
            AssetDatabase.CreateAsset(list, AssetPath);
            AssetDatabase.SaveAssets();
        }
        return list;
    }

    void Styles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 };
        sub = new GUIStyle(EditorStyles.label) { wordWrap = true, normal = { textColor = new Color(0.65f, 0.65f, 0.65f) } };
        card = new GUIStyle("HelpBox") { padding = new RectOffset(10, 10, 8, 8), margin = new RectOffset(6, 6, 4, 6) };
        boxLabel = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(0.6f, 0.6f, 0.6f) } };
        pill = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, richText = true };
        arrow = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22, alignment = TextAnchor.MiddleCenter, normal = { textColor = Accent } };
        drop = new GUIStyle("HelpBox") { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
        bigButton = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold, fixedHeight = 30 };
    }

    void OnGUI()
    {
        Styles();
        HookEditor();
        var l = List();

        // header
        var head = EditorGUILayout.BeginVertical();
        EditorGUI.DrawRect(new Rect(0, 0, position.width, 62), new Color(0, 0, 0, 0.18f));
        EditorGUI.DrawRect(new Rect(0, 60, position.width, 2), Accent);
        GUILayout.Space(6);
        GUILayout.Label("  Art Swapper", titleStyle);
        GUILayout.Label("   Put the generated object on the left, your model on the right, press Swap.", sub);
        GUILayout.Space(10);
        EditorGUILayout.EndVertical();

        // toolbar
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("+ New swap", EditorStyles.toolbarButton, GUILayout.Width(90))) AddEntry(null);
        GUI.enabled = Selection.gameObjects.Length > 0;
        if (GUILayout.Button($"+ Add selected ({Selection.gameObjects.Length})", EditorStyles.toolbarButton, GUILayout.Width(130)))
            foreach (var g in Selection.gameObjects) if (g.scene.IsValid()) AddEntry(g);
        GUI.enabled = true;
        GUILayout.FlexibleSpace();
        search = GUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.Width(180));
        EditorGUILayout.EndHorizontal();

        DropZone();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (l.entries.Count == 0)
        {
            GUILayout.Space(30);
            GUILayout.Label("No swaps yet.\nDrag generated objects from the Hierarchy into the box above,\nor select them and press  + Add selected.", new GUIStyle(sub) { alignment = TextAnchor.MiddleCenter });
        }
        for (int i = 0; i < l.entries.Count; i++)
        {
            var e = l.entries[i];
            if (!string.IsNullOrEmpty(search) && e.objectName.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (Card(e, i)) { Undo.RecordObject(l, "Remove swap"); l.entries.RemoveAt(i); EditorUtility.SetDirty(l); GUIUtility.ExitGUI(); }
        }
        EditorGUILayout.EndScrollView();

        // footer
        EditorGUI.DrawRect(GUILayoutUtility.GetRect(position.width, 1), new Color(0, 0, 0, 0.3f));
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(6);
        GUI.backgroundColor = Good;
        if (GUILayout.Button("Swap all", bigButton)) { foreach (var e in l.entries) if (e.enabled) DoSwap(e); }
        GUI.backgroundColor = Color.white;
        if (GUILayout.Button("Undo all swaps", bigButton, GUILayout.Width(130))) { foreach (var e in l.entries) DoRevert(e); }
        GUILayout.Space(6);
        EditorGUILayout.EndHorizontal();
        GUILayout.Label("  Swaps are saved in Assets/Resources/ArtSwaps.asset and are also applied in-game to things that are built while the game runs.", sub);
        GUILayout.Space(4);
    }

    void DropZone()
    {
        Rect r = GUILayoutUtility.GetRect(position.width, 44);
        r.x += 6; r.width -= 12; r.y += 4; r.height -= 6;
        var ev = Event.current;
        bool over = r.Contains(ev.mousePosition) && DragAndDrop.objectReferences.Length > 0;
        GUI.color = over ? Accent : Color.white;
        GUI.Box(r, over ? "Drop to add" : "Drag generated objects here from the Hierarchy", drop);
        GUI.color = Color.white;
        if (!r.Contains(ev.mousePosition)) return;
        if (ev.type == EventType.DragUpdated) { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; ev.Use(); }
        else if (ev.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            foreach (var o in DragAndDrop.objectReferences)
                if (o is GameObject g && g.scene.IsValid()) AddEntry(g);
            ev.Use();
        }
    }

    void AddEntry(GameObject g)
    {
        var l = List();
        Undo.RecordObject(l, "Add swap");
        var e = new ArtSwapEntry();
        if (g != null) SetTarget(e, g);
        l.entries.Add(e);
        EditorUtility.SetDirty(l);
    }

    static void SetTarget(ArtSwapEntry e, GameObject g)
    {
        // if they picked a part inside a swap, use the object itself
        e.scene = g.scene.name;
        e.path = ArtSwap.PathOf(g.transform);
        e.objectName = g.name;
    }

    // returns true if the card asked to be removed
    bool Card(ArtSwapEntry e, int index)
    {
        var l = List();
        var targets = string.IsNullOrEmpty(e.objectName) ? new List<Transform>() : ArtSwap.FindTargets(e);
        int swapped = 0;
        foreach (var t in targets) if (t.GetComponent<ArtSwapMarker>() != null) swapped++;

        EditorGUILayout.BeginVertical(card);
        EditorGUI.BeginChangeCheck();

        // top line: on/off, name, status, remove
        EditorGUILayout.BeginHorizontal();
        e.enabled = EditorGUILayout.Toggle(e.enabled, GUILayout.Width(16));
        GUILayout.Label(string.IsNullOrEmpty(e.objectName) ? "New swap" : e.objectName, EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        Status(e, targets.Count, swapped);
        if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
        {
            EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical();
            return true;
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(4);

        // the two boxes
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.BeginVertical(GUILayout.MinWidth(180));
        GUILayout.Label("GENERATED OBJECT", boxLabel);
        GameObject current = targets.Count > 0 ? targets[0].gameObject : null;
        var picked = (GameObject)EditorGUILayout.ObjectField(current, typeof(GameObject), true);
        if (picked != current && picked != null && picked.scene.IsValid()) SetTarget(e, picked);
        if (current == null && !string.IsNullOrEmpty(e.path))
            GUILayout.Label(e.path, new GUIStyle(EditorStyles.miniLabel) { wordWrap = true });
        e.match = (ArtSwapMatch)EditorGUILayout.Popup((int)e.match, new[] { "Just this one", "Every object with this name" });
        EditorGUILayout.EndVertical();

        GUILayout.Label("→", arrow, GUILayout.Width(36), GUILayout.Height(58));

        EditorGUILayout.BeginVertical(GUILayout.MinWidth(180));
        GUILayout.Label("YOUR MODEL", boxLabel);
        e.replacement = (GameObject)EditorGUILayout.ObjectField(e.replacement, typeof(GameObject), false);
        GUILayout.Label(e.replacement == null ? "Drag a prefab or .fbx here" : AssetDatabase.GetAssetPath(e.replacement), EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        var tex = e.replacement != null ? AssetPreview.GetAssetPreview(e.replacement) : null;
        Rect pr = GUILayoutUtility.GetRect(58, 58, GUILayout.Width(58), GUILayout.Height(58));
        EditorGUI.DrawRect(pr, new Color(0, 0, 0, 0.2f));
        if (tex != null) GUI.DrawTexture(pr, tex, ScaleMode.ScaleToFit);
        else if (e.replacement != null) Repaint(); // preview still loading

        EditorGUILayout.EndHorizontal();

        // options
        bool isOpen = open.Contains(index);
        bool nowOpen = EditorGUILayout.Foldout(isOpen, "Size & position", true);
        if (nowOpen != isOpen) { if (nowOpen) open.Add(index); else open.Remove(index); }
        if (nowOpen)
        {
            EditorGUI.indentLevel++;
            e.fit = (ArtSwapFit)EditorGUILayout.Popup("Size", (int)e.fit, new[] { "Fit inside the old one", "Stretch to the old one", "Keep my model's size" });
            e.scale = EditorGUILayout.FloatField("Extra scale", e.scale);
            e.rotation = EditorGUILayout.Vector3Field("Rotate", e.rotation);
            e.offset = EditorGUILayout.Vector3Field("Move", e.offset);
            EditorGUI.indentLevel--;
        }

        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(l);
            if (swapped > 0 && e.replacement != null) DoSwap(e); // live update while tweaking
        }

        // buttons
        EditorGUILayout.BeginHorizontal();
        GUI.enabled = e.replacement != null && targets.Count > 0;
        GUI.backgroundColor = Good;
        if (GUILayout.Button(swapped > 0 ? "Swap again" : "Swap", GUILayout.Height(22))) DoSwap(e);
        GUI.backgroundColor = Color.white;
        GUI.enabled = swapped > 0;
        if (GUILayout.Button("Undo swap", GUILayout.Height(22), GUILayout.Width(90))) DoRevert(e);
        GUI.enabled = targets.Count > 0;
        if (GUILayout.Button("Show", GUILayout.Height(22), GUILayout.Width(60)))
        {
            Selection.objects = targets.ConvertAll(t => (Object)t.gameObject).ToArray();
            SceneView.FrameLastActiveSceneView();
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
        return false;
    }

    void Status(ArtSwapEntry e, int found, int swapped)
    {
        string text; Color c;
        if (string.IsNullOrEmpty(e.objectName)) { text = "pick an object"; c = Warn; }
        else if (found == 0) { text = "built in-game · swaps when you press Play"; c = Accent; }
        else if (swapped == found) { text = found > 1 ? $"swapped {found}" : "swapped"; c = Good; }
        else if (swapped > 0) { text = $"swapped {swapped} of {found}"; c = Warn; }
        else { text = found > 1 ? $"found {found}" : "found"; c = new Color(0.7f, 0.7f, 0.7f); }
        if (e.replacement == null && !string.IsNullOrEmpty(e.objectName)) { text = "needs your model"; c = Bad; }
        Rect r = GUILayoutUtility.GetRect(new GUIContent("  ● " + text), pill);
        GUI.Label(r, $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>●</color> {text}", pill);
    }

    static void DoSwap(ArtSwapEntry e)
    {
        if (e.replacement == null) return;
        foreach (var t in ArtSwap.FindTargets(e))
        {
            ArtSwap.Apply(t, e);
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
        }
    }

    static void DoRevert(ArtSwapEntry e)
    {
        foreach (var t in ArtSwap.FindTargets(e))
        {
            ArtSwap.Revert(t);
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
        }
    }

    void OnSelectionChange() => Repaint();
    void OnHierarchyChange() => Repaint();
}
