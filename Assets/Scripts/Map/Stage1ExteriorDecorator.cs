using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Builds the authored Stage 1 scenery when the scene starts.</summary>
[DisallowMultipleComponent]
public class Stage1ExteriorDecorator : MonoBehaviour
{
    [Serializable]
    public struct Decoration
    {
        public string group;
        public GameObject model;
        public Material material;
        [Tooltip("Visual centre in tile units on the X/Z plane, relative to the arena origin.")]
        public Vector2 position;
        public float rotation;
        [Min(0.1f)] public float footprint;
    }

    [SerializeField] private GridManager grid;
    [SerializeField, Min(0f)] private float wallClearance = 0.8f;
    [SerializeField, Min(0f)] private float propSpacing = 0.25f;
    [SerializeField] private float groundHeight = 0.015f;
    [SerializeField] private Decoration[] decorations = Array.Empty<Decoration>();

    private Transform sceneryRoot;

    void Start()
    {
        if (grid == null) grid = GetComponent<GridManager>();
        if (grid == null || sceneryRoot != null) return;

        float tile = grid.TileSize;
        // Include the actual generated meshes: some wall variants exceed half a tile.
        Rect arena = Rect.MinMaxRect(-tile * 0.5f, -tile * 0.5f,
            (grid.Width - 0.5f) * tile, (grid.Height - 0.5f) * tile);
        foreach (var renderer in grid.GetComponentsInChildren<Renderer>())
        {
            Bounds bounds = renderer.bounds;
            arena = Rect.MinMaxRect(Mathf.Min(arena.xMin, bounds.min.x),
                Mathf.Min(arena.yMin, bounds.min.z), Mathf.Max(arena.xMax, bounds.max.x),
                Mathf.Max(arena.yMax, bounds.max.z));
        }
        arena = Padded(arena, wallClearance * tile);

        sceneryRoot = new GameObject("Stage1 Exterior Scenery").transform;
        sceneryRoot.SetParent(transform, false);
        var groups = new Dictionary<string, Transform>();
        var occupied = new List<Rect>();

        foreach (var decoration in decorations)
        {
            if (decoration.model == null) continue;

            string groupName = string.IsNullOrEmpty(decoration.group) ? "Scenery" : decoration.group;
            if (!groups.TryGetValue(groupName, out Transform group))
            {
                group = new GameObject(groupName).transform;
                group.SetParent(sceneryRoot, false);
                groups.Add(groupName, group);
            }

            // A wrapper preserves FBX import rotation/scale and compensates for off-centre pivots.
            var placement = new GameObject(decoration.model.name).transform;
            placement.SetParent(group, false);
            placement.rotation = Quaternion.Euler(0f, decoration.rotation, 0f);
            Instantiate(decoration.model, placement, false);
            if (decoration.material != null)
                foreach (var renderer in placement.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = decoration.material;
                    renderer.sharedMaterials = materials;
                }

            if (!TryGetBounds(placement, out Bounds bounds))
            {
                Reject(placement, "has no visible mesh");
                continue;
            }

            float size = Mathf.Max(bounds.size.x, bounds.size.z);
            if (size < 0.001f)
            {
                Reject(placement, "has an empty footprint");
                continue;
            }

            placement.localScale *= Mathf.Max(0.1f, decoration.footprint) * tile / size;
            TryGetBounds(placement, out bounds);
            Vector3 centre = new Vector3(decoration.position.x * tile, groundHeight,
                decoration.position.y * tile);
            placement.position += new Vector3(centre.x - bounds.center.x,
                centre.y - bounds.min.y, centre.z - bounds.center.z);
            TryGetBounds(placement, out bounds);

            Rect footprint = Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);
            if (arena.Overlaps(footprint))
            {
                Reject(placement, "would enter the arena clearance");
                continue;
            }

            bool overlaps = false;
            foreach (var existing in occupied)
                if (Padded(existing, propSpacing * tile).Overlaps(footprint))
                {
                    overlaps = true;
                    break;
                }
            if (overlaps)
            {
                Reject(placement, "would overlap another decoration");
                continue;
            }

            // Scenery is visual only; it cannot obstruct players, enemies, or explosions.
            foreach (var collider in placement.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (var body in placement.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
            occupied.Add(footprint);
        }
    }

    static Rect Padded(Rect rect, float padding)
    {
        return Rect.MinMaxRect(rect.xMin - padding, rect.yMin - padding,
            rect.xMax + padding, rect.yMax + padding);
    }

    static bool TryGetBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (!found) bounds = renderer.bounds;
            else bounds.Encapsulate(renderer.bounds);
            found = true;
        }
        return found;
    }

    static void Reject(Transform placement, string reason)
    {
        Debug.LogWarning($"Stage1ExteriorDecorator: skipped '{placement.name}': {reason}.");
        placement.gameObject.SetActive(false);
        Destroy(placement.gameObject);
    }
}
