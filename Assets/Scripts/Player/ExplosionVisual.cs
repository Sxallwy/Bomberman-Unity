using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Visuals only. ExplosionHazard remains responsible for damage and lifetime.</summary>
[DisallowMultipleComponent]
public class ExplosionVisual : MonoBehaviour
{
    [SerializeField] private Material effectMaterial;

    static readonly int ProgressId = Shader.PropertyToID("_Progress");
    static readonly int SeedId = Shader.PropertyToID("_Seed");
    static readonly int LayerId = Shader.PropertyToID("_Layer");

    readonly MeshRenderer[] layers = new MeshRenderer[4];
    MaterialPropertyBlock properties;
    Mesh cellMesh;
    Mesh emberMesh;
    float lifetime;
    float elapsed;
    float seed;
    bool initialized;

    public void Initialize(float duration, float tileSize, bool isOrigin)
    {
        if (initialized || effectMaterial == null) return;
        initialized = true;
        lifetime = Mathf.Max(0.01f, duration);
        // Local PRNG: visual variation must not consume gameplay's random sequence.
        var random = new System.Random(unchecked(transform.position.GetHashCode() ^ Time.frameCount * 397));
        seed = (float)random.NextDouble() * 100f;
        properties = new MaterialPropertyBlock();
        cellMesh = MakeMesh(false, random);
        emberMesh = MakeMesh(true, random);

        var graphics = new GameObject("Explosion Graphics").transform;
        graphics.SetParent(transform, false);
        Vector3 scale = transform.lossyScale;
        // The original hazard prefab has scale 0.1; keep its collider untouched.
        graphics.localScale = new Vector3(tileSize / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
            tileSize / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
            tileSize / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));

        layers[0] = MakeLayer(graphics, "Flame", cellMesh, 0.08f);
        layers[1] = MakeLayer(graphics, "Embers", emberMesh, 0.15f);
        if (isOrigin) layers[2] = MakeLayer(graphics, "Impact Ring", cellMesh, 0.12f);
        layers[3] = MakeLayer(graphics, "Smoke", cellMesh, 0.02f);
        var previousVisual = GetComponent<MeshRenderer>();
        if (previousVisual != null) previousVisual.enabled = false;
        UpdateLayers();
    }

    MeshRenderer MakeLayer(Transform parent, string name, Mesh mesh, float height)
    {
        var layer = new GameObject(name);
        layer.layer = gameObject.layer;
        layer.transform.SetParent(parent, false);
        layer.transform.localPosition = Vector3.up * height;
        layer.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = layer.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = effectMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    void Update()
    {
        if (!initialized) return;
        elapsed += Time.deltaTime;
        UpdateLayers();
    }

    void UpdateLayers()
    {
        // Keep a readable flame until the hazard object is removed, including low frame rates.
        properties.SetFloat(ProgressId, Mathf.Min(elapsed / lifetime, 0.98f));
        properties.SetFloat(SeedId, seed);
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] == null) continue;
            properties.SetFloat(LayerId, i);
            layers[i].SetPropertyBlock(properties);
        }
    }

    static Mesh MakeMesh(bool embers, System.Random random)
    {
        int count = embers ? 8 : 1;
        var vertices = new Vector3[count * 4];
        var uv = new Vector2[count * 4];
        var data = new Vector2[count * 4];
        var triangles = new int[count * 6];
        Vector3[] quad = { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
            new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
        Vector2[] corners = { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
        for (int i = 0; i < count; i++)
        {
            float angle = (i + (float)random.NextDouble()) * Mathf.PI * 2f / count;
            float variation = 0.8f + (float)random.NextDouble() * 0.2f;
            for (int j = 0; j < 4; j++)
            {
                vertices[i * 4 + j] = quad[j];
                uv[i * 4 + j] = corners[j];
                data[i * 4 + j] = new Vector2(angle, variation);
            }
            int v = i * 4;
            int t = i * 6;
            triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
            triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
        }
        var mesh = new Mesh { name = embers ? "Explosion Embers" : "Explosion Cell" };
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.uv2 = data;
        mesh.triangles = triangles;
        // Shader animation stays inside this cell; sparks rise slightly above the flame.
        mesh.bounds = new Bounds(Vector3.up * 0.2f, new Vector3(1f, 0.8f, 1f));
        return mesh;
    }

    void OnDestroy()
    {
        if (cellMesh != null) Destroy(cellMesh);
        if (emberMesh != null) Destroy(emberMesh);
    }
}
