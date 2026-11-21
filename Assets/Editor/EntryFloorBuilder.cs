using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Menu: G7l6 > Entrance Floor and Remote.
///   - The entrance (foyer and mudroom) gets a warm limestone floor: big square tiles with soft veining, a little
///     colour variation between tiles and slim grout lines - instead of the tinted blue pool tile.
///     (The texture is generated once: Assets/Materials/House/Entry Stone.png.)
///   - The TV remote is held the right way round (it used to point back at you).
/// Only those floor tiles' material and the remote's hold angle change. Saves the scene (with your own changes).
/// </summary>
public static class EntryFloorBuilder
{
    const string TexturePath = "Assets/Materials/House/Entry Stone.png";
    const string MaterialPath = "Assets/Materials/House/Floor - Entry Stone.mat";
    const float TileMetres = 0.6f; // in plan metres (the house is scaled up a bit on top)

    [MenuItem("G7l6/Entrance Floor and Remote")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[Entrance] Stop Play mode first."); return; }
        var scene = SceneManager.GetActiveScene();
        if (scene.path != HouseBuilder.TargetScene)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(HouseBuilder.TargetScene, OpenSceneMode.Single);
        }
        var house = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "NewHouse");
        if (!house) { Debug.LogWarning("[Entrance] No 'NewHouse' in the scene."); return; }

        var stone = StoneMaterial();
        int tiles = 0;
        float scale = -1f;
        foreach (var r in house.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!mats[i] || mats[i].name != "Floor - Terracotta Tile") continue;
                mats[i] = stone;
                changed = true;
            }
            if (!changed) continue;
            if (scale < 0f) scale = UvPerPlanMetre(r, house.transform.localScale.x);
            Undo.RecordObject(r, "Entrance floor");
            r.sharedMaterials = mats;
            tiles++;
        }
        // Two tiles per texture repeat.
        if (scale > 0f) stone.mainTextureScale = Vector2.one * (1f / scale) / (2f * TileMetres);
        EditorUtility.SetDirty(stone);

        string remote = "no TV remote found";
        foreach (var c in Object.FindObjectsOfType<Carryable>(true))
        {
            if (c.type != ItemType.Remote) continue;
            Undo.RecordObject(c, "Remote hold");
            c.heldRotation = new Vector3(0f, 160f, 0f); // turned round (it was -20: pointing back at you)
            EditorUtility.SetDirty(c);
            remote = $"TV remote now held at {c.heldRotation}";
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Entrance] Stone floor on {tiles} entrance floor tiles; {remote}. Nothing else changed. The scene is saved.");
    }

    /// <summary>How many plan metres one UV unit covers on a floor piece's top (so the stone tiles come out 0.6 m).</summary>
    static float UvPerPlanMetre(Renderer r, float houseScale)
    {
        var mf = r.GetComponent<MeshFilter>();
        if (!mf || !mf.sharedMesh) return -1f;
        var mesh = mf.sharedMesh;
        var v = mesh.vertices;
        var uv = mesh.uv;
        if (uv == null || uv.Length != v.Length) return -1f;
        float best = -1f;
        for (int i = 0; i < v.Length; i++)
        for (int j = i + 1; j < Mathf.Min(v.Length, i + 64); j++)
        {
            Vector3 a = r.transform.TransformPoint(v[i]), b = r.transform.TransformPoint(v[j]);
            float dx = Mathf.Abs(a.x - b.x), du = Mathf.Abs(uv[i].x - uv[j].x), dv = Mathf.Abs(uv[i].y - uv[j].y);
            if (Mathf.Abs(a.y - b.y) > 0.001f || dx < 0.5f) continue;
            float d = Mathf.Max(du, dv);
            if (d < 0.01f) continue;
            float metresPerUv = dx / Mathf.Max(0.0001f, houseScale) / d;
            if (metresPerUv > best) best = metresPerUv;
        }
        return best;
    }

    static Material StoneMaterial()
    {
        if (!File.Exists(TexturePath))
        {
            File.WriteAllBytes(TexturePath, MakeStone(1024).EncodeToPNG());
            AssetDatabase.ImportAsset(TexturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        var m = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!m) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, MaterialPath); }
        m.mainTexture = tex;
        m.color = Color.white;
        m.SetFloat("_Glossiness", 0.42f);
        m.SetFloat("_Metallic", 0f);
        return m;
    }

    /// <summary>2 x 2 limestone tiles, seamless: soft veins, slight shade differences, slim warm-grey grout.</summary>
    static Texture2D MakeStone(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
        var px = new Color32[size * size];
        int tile = size / 2;
        float grout = size * 0.006f;
        var baseColours = new[]
        {
            new Color(0.88f, 0.84f, 0.77f), new Color(0.85f, 0.8f, 0.72f),
            new Color(0.86f, 0.82f, 0.75f), new Color(0.9f, 0.86f, 0.8f),
        };
        var groutColour = new Color(0.62f, 0.58f, 0.53f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int tx = x / tile, ty = y / tile;
            var c = baseColours[(tx + ty * 2) % 4];
            float u = x / (float)size, v = y / (float)size;
            // Veins: stretched, wavy noise; and a fine grain. (Periodic so the texture tiles.)
            float wave = Mathf.Sin((u * 3f + v + tx * 0.37f) * Mathf.PI * 2f + 3f * Fbm(u, v, 4, 4, 1));
            float vein = Mathf.Pow(1f - Mathf.Abs(wave), 14f) * 0.09f;
            float cloud = (Fbm(u, v, 6, 4, 7 + tx + ty * 2) - 0.5f) * 0.06f;
            float grain = (Hash(x, y) - 0.5f) * 0.025f;
            c = c * (1f + cloud + grain) - new Color(vein, vein * 1.05f, vein * 1.15f) * 1f;

            // Grout lines at the tile edges, with a soft bevel next to them.
            float ex = Mathf.Min(x % tile, tile - 1 - x % tile), ey = Mathf.Min(y % tile, tile - 1 - y % tile);
            float e = Mathf.Min(ex, ey);
            if (e < grout) c = groutColour * (0.95f + grain);
            else if (e < grout + 3f) c *= 0.93f + 0.07f * ((e - grout) / 3f);
            px[y * size + x] = (Color32)new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    /// <summary>Value noise that wraps at u, v = 0..1 (so the texture tiles): 'freq' cells across, 'octaves' layers.</summary>
    static float Fbm(float u, float v, int freq, int octaves, int seed)
    {
        float sum = 0f, amp = 0.5f;
        for (int o = 0; o < octaves; o++)
        {
            int f = freq << o;
            sum += amp * Noise(u * f, v * f, f, seed + o * 31);
            amp *= 0.5f;
        }
        return sum;
    }

    static float Noise(float x, float y, int period, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Lattice(xi, yi, period, seed), b = Lattice(xi + 1, yi, period, seed);
        float c = Lattice(xi, yi + 1, period, seed), d = Lattice(xi + 1, yi + 1, period, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    static float Lattice(int x, int y, int period, int seed)
    {
        x = ((x % period) + period) % period;
        y = ((y % period) + period) % period;
        return Hash(x * 7 + 13 + seed * 101, y * 11 + 5 + seed * 57);
    }

    static float Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xffff) / 65535f;
        }
    }
}
