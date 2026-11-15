using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Editor authoring only: the saved scene contains ordinary meshes, with no runtime generation.
public static class Stage1GroundDetailsTools
{
    const string Folder = "Assets/Materials/Stage1GroundDetails";
    static System.Random random;
    static readonly List<Bounds> obstacles = new List<Bounds>();
    static readonly List<Vector3> treadPositions = new List<Vector3>();
    static int assetIndex;

    class Geometry
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>() };

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, int material)
        {
            int i = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            uv.Add(new Vector2(a.x, a.z)); uv.Add(new Vector2(b.x, b.z)); uv.Add(new Vector2(c.x, c.z));
            triangles[material].Add(i); triangles[material].Add(i + 1); triangles[material].Add(i + 2);
        }

        public Mesh Save(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.subMeshCount = 3;
            for (int i = 0; i < 3; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + name + ".asset"));
            return mesh;
        }
    }

    static float Range(float min, float max) { return min + (float)random.NextDouble() * (max - min); }

    static bool Clear(Vector3 p, float radius)
    {
        if (p.x - radius < -14.5f || p.x + radius > 44.5f || p.z - radius < -9.7f || p.z + radius > 25.5f) return false;
        if (p.x + radius > -1.2f && p.x - radius < 31.2f && p.z + radius > -1.2f && p.z - radius < 12.3f) return false;
        foreach (var b in obstacles)
            if (p.x > b.min.x - radius && p.x < b.max.x + radius && p.z > b.min.z - radius && p.z < b.max.z + radius) return false;
        foreach (var t in treadPositions)
            if ((new Vector2(p.x - t.x, p.z - t.z)).sqrMagnitude < (radius + .18f) * (radius + .18f)) return false;
        return true;
    }

    static void Rock(Geometry g, Vector3 center, Vector3 scale)
    {
        const int sides = 7;
        var lower = new Vector3[sides]; var upper = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = i * Mathf.PI * 2 / sides;
            float r = Range(.8f, 1.08f);
            lower[i] = center + Vector3.Scale(new Vector3(Mathf.Cos(a) * r, .12f, Mathf.Sin(a) * r), scale);
            upper[i] = center + Vector3.Scale(new Vector3(Mathf.Cos(a + .17f) * r * .65f, Range(.65f, 1), Mathf.Sin(a + .17f) * r * .65f), scale);
        }
        Vector3 peak = center + Vector3.Scale(new Vector3(.12f, 1.05f, -.1f), scale);
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            g.Triangle(lower[i], upper[i], lower[j], random.Next(3));
            g.Triangle(lower[j], upper[i], upper[j], random.Next(3));
            g.Triangle(upper[i], peak, upper[j], random.Next(3));
        }
    }

    static GameObject Place(string name, Transform parent, Vector3 position, Geometry geometry, Material[] materials)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position;
        go.AddComponent<MeshFilter>().sharedMesh = geometry.Save("Ground Detail " + (++assetIndex));
        var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
        return go;
    }

    static Material[] Materials(string name, Color color, Texture texture)
    {
        var result = new Material[3];
        for (int i = 0; i < result.Length; i++)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = name + " " + (i + 1); mat.color = color * (0.86f + i * .13f);
            mat.color = new Color(mat.color.r, mat.color.g, mat.color.b, 1);
            mat.SetFloat("_Smoothness", .06f); mat.SetFloat("_Metallic", 0);
            if (texture != null) { mat.SetTexture("_BaseMap", texture); mat.SetTextureScale("_BaseMap", Vector2.one * 1.8f); }
            AssetDatabase.CreateAsset(mat, AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + mat.name + ".mat"));
            result[i] = mat;
        }
        return result;
    }

    [MenuItem("Tools/Stage1/Add Ground Details")]
    public static void Create()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring ground details.");
        if (GameObject.Find("Stage1 Ground Details") != null) throw new InvalidOperationException("Ground details already exist; edit the existing group.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.name != "Stage1") throw new InvalidOperationException("Open Stage1 first.");
        System.IO.Directory.CreateDirectory("Library/CodexBackups");
        EditorSceneManager.SaveScene(scene, "Library/CodexBackups/Stage1-before-ground-details.unity", true);
        random = new System.Random(81541); assetIndex = 0; obstacles.Clear(); treadPositions.Clear();
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name.Contains("Decoration")) foreach (Transform t in root.transform)
            {
                var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                // Overhead crane arms do not occupy the ground beneath them.
                if (t.name.Contains("Crane")) continue;
                b.Expand(new Vector3(.14f, 0, .14f)); obstacles.Add(b);
            }
            if (root.name == "Stage1 Tire Tracks") foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                var vs = mf.sharedMesh.vertices;
                for (int i = 0; i < vs.Length; i += 6) treadPositions.Add(mf.transform.TransformPoint(vs[i]));
            }
        }
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Materials", "Stage1GroundDetails");
        var texture = new Texture2D(256, 256, TextureFormat.RGB24, false); texture.name = "Soil Grain";
        var pixels = new Color[256 * 256];
        for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
        {
            float n = .64f + Mathf.PerlinNoise(x * .065f, y * .065f) * .28f + Range(-.1f, .1f);
            pixels[y * 256 + x] = new Color(n, n, n);
        }
        texture.SetPixels(pixels); texture.Apply();
        System.IO.File.WriteAllBytes(Folder + "/Soil Grain.png", texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture); AssetDatabase.ImportAsset(Folder + "/Soil Grain.png");
        var grain = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Soil Grain.png");
        var dirt = Materials("Excavated Earth", new Color(.30f, .205f, .12f), grain);
        var sand = Materials("Loose Sand", new Color(.53f, .40f, .25f), grain);
        var stone = Materials("Dusty Stone", new Color(.38f, .37f, .33f), grain);
        var rootObject = new GameObject("Stage1 Ground Details"); Undo.RegisterCreatedObjectUndo(rootObject, "Add ground details");
        var piles = new GameObject("Soil and Sand Piles").transform; piles.SetParent(rootObject.transform, false);
        var rubble = new GameObject("Gravel Patches").transform; rubble.SetParent(rootObject.transform, false);
        var rocks = new GameObject("Scattered Rocks").transform; rocks.SetParent(rootObject.transform, false);
        Vector3[] centers = { new Vector3(2.7f, .016f, 20.1f), new Vector3(-5.7f, .016f, 10.1f), new Vector3(-5.3f, .016f, 6.3f), new Vector3(18, .016f, -6.5f), new Vector3(24, .016f, 22.5f) };
        Vector3[] sizes = { new Vector3(1.75f, 1.0f, 1.35f), new Vector3(1.35f, .63f, 1.0f), new Vector3(1.1f, .48f, .9f), new Vector3(1.5f, .40f, .85f), new Vector3(1.35f, .48f, .9f) };
        int moundCount = 0, rockCount = 0, pebbleCount = 0;
        for (int m = 0; m < centers.Length; m++)
        {
            var g = new Geometry(); const int sides = 32, rings = 6;
            var points = new Vector3[rings, sides];
            for (int ring = 0; ring < rings; ring++) for (int i = 0; i < sides; i++)
            {
                float r = (ring + 1f) / rings, a = i * Mathf.PI * 2 / sides;
                float waviness = 1 + .10f * Mathf.Sin(a * 5 + m) + .06f * Mathf.Sin(a * 9 + 1);
                float h = Mathf.Pow(1 - r, .9f) * (1 + Range(-.13f, .13f));
                points[ring, i] = Vector3.Scale(new Vector3(Mathf.Cos(a) * r * waviness, h, Mathf.Sin(a) * r * waviness), sizes[m]);
            }
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                g.Triangle(points[0, i], Vector3.up * sizes[m].y, points[0, j], random.Next(3));
                for (int ring = 1; ring < rings; ring++)
                {
                    g.Triangle(points[ring, i], points[ring - 1, i], points[ring, j], random.Next(3));
                    g.Triangle(points[ring, j], points[ring - 1, i], points[ring - 1, j], random.Next(3));
                }
            }
            // A little loose earth around the foot blends the silhouette into the ground.
            for (int i = 0; i < 110; i++)
            {
                float a = Range(0, Mathf.PI * 2), r = Range(.65f, 1.3f);
                var p = new Vector3(Mathf.Cos(a) * r * sizes[m].x, 0, Mathf.Sin(a) * r * sizes[m].z);
                float radius = Range(.035f, .095f);
                if (r < 1 || !Clear(centers[m] + p, radius)) continue;
                Rock(g, p, new Vector3(radius, radius * .4f, radius));
            }
            Place((m < 3 ? "Earth Pile " : "Sand Pile ") + (++moundCount), piles, centers[m], g, m < 3 ? dirt : sand);
        }
        Vector3[] patches = { new Vector3(16, .018f, -7.8f), new Vector3(19.5f, .018f, -5.6f), new Vector3(7, .018f, -7), new Vector3(-7.8f, .018f, 10.7f), new Vector3(4.2f, .018f, 21.8f), new Vector3(28.7f, .018f, 21.5f), new Vector3(40.1f, .018f, 8.2f), new Vector3(41, .018f, 2.8f), new Vector3(21.6f, .018f, -5.7f) };
        for (int m = 0; m < patches.Length; m++)
        {
            var g = new Geometry();
            for (int i = 0; i < 190; i++)
            {
                float a = Range(0, Mathf.PI * 2), r = Mathf.Sqrt(Range(0, 1));
                var p = new Vector3(Mathf.Cos(a) * r * 1.55f, 0, Mathf.Sin(a) * r * .9f);
                float radius = Range(.035f, .095f);
                if (!Clear(patches[m] + p, radius)) continue;
                Rock(g, p, new Vector3(radius, radius * Range(.25f, .65f), radius * Range(.7f, 1.2f))); pebbleCount++;
            }
            Place("Gravel Patch " + (m + 1), rubble, patches[m], g, m % 3 == 0 ? sand : stone);
        }
        Vector3[] clusters = { new Vector3(4.8f, 0, 20.9f), new Vector3(-7.8f, 0, 10.7f), new Vector3(-4.1f, 0, 3.2f), new Vector3(28.7f, 0, 21.5f), new Vector3(40.2f, 0, 9), new Vector3(41.7f, 0, 2.9f), new Vector3(21.1f, 0, -6.2f), new Vector3(7, 0, -7.1f) };
        foreach (var center in clusters) for (int i = 0; i < 6; i++)
        {
            var p = center + new Vector3(Range(-1, 1), .012f, Range(-.65f, .65f)); float radius = Range(.12f, .34f);
            if (!Clear(p, radius)) continue;
            var g = new Geometry(); Rock(g, Vector3.zero, new Vector3(radius, radius * Range(.65f, 1.1f), radius * Range(.7f, 1.2f)));
            Place("Loose Rock " + (++rockCount), rocks, p, g, stone);
        }
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Debug.Log("Ground details saved: " + moundCount + " piles, " + rockCount + " rocks, " + pebbleCount + " gravel stones.");
    }
}
