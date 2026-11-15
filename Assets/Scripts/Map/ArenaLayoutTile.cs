using UnityEngine;

public enum ArenaTileKind { Floor, Wall, Block }

/// <summary>Move or duplicate this tile root; its visual children stay with the gameplay cell.</summary>
[DisallowMultipleComponent]
[SelectionBase]
public class ArenaLayoutTile : MonoBehaviour
{
    [SerializeField] private ArenaTileKind kind;
    [Tooltip("Use Apply Model in the Inspector after changing this prefab.")]
    [SerializeField] private GameObject sourcePrefab;

    public ArenaTileKind Kind => kind;
    public GameObject SourcePrefab => sourcePrefab;
    public ArenaLayout Layout => GetComponentInParent<ArenaLayout>();

    public void Configure(ArenaTileKind tileKind, GameObject prefab)
    {
        kind = tileKind;
        sourcePrefab = prefab;
    }

    void OnDrawGizmosSelected()
    {
        var layout = Layout;
        if (layout == null || layout.Grid == null) return;
        var grid = layout.Grid;
        Gizmos.color = kind == ArenaTileKind.Block ? Color.yellow : Color.cyan;
        Gizmos.DrawWireCube(grid.GridToWorld(grid.WorldToGrid(transform.position)) + Vector3.up * 0.5f,
            Vector3.one * grid.TileSize * 0.95f);
    }
}
