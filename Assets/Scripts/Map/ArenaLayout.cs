using UnityEngine;

/// <summary>The scene objects are the source of truth for an editable arena.</summary>
[DisallowMultipleComponent]
public class ArenaLayout : MonoBehaviour
{
    [SerializeField] private GridManager grid;
    [Tooltip("A destructible tile that hides the exit. If removed, another block is chosen.")]
    [SerializeField] private ArenaLayoutTile hiddenExitBlock;

    public GridManager Grid => grid;
    public ArenaLayoutTile HiddenExitBlock => hiddenExitBlock;

    public void Configure(GridManager owner, ArenaLayoutTile exitBlock)
    {
        grid = owner;
        hiddenExitBlock = exitBlock;
    }

    void OnDrawGizmosSelected()
    {
        if (grid == null) return;
        float tile = grid.TileSize;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(new Vector3((grid.Width - 1) * tile / 2f, 0.1f,
            (grid.Height - 1) * tile / 2f), new Vector3(grid.Width * tile, 0.2f, grid.Height * tile));

        foreach (var pos in new[] { new Vector2Int(1, 1), new Vector2Int(grid.Width - 2, 1),
                     new Vector2Int(1, grid.Height - 2), new Vector2Int(grid.Width - 2, grid.Height - 2) })
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(grid.GridToWorld(pos) + Vector3.up * 0.6f, Vector3.one * tile * 0.85f);
        }
        if (hiddenExitBlock != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(hiddenExitBlock.transform.position, Vector3.one * tile * 1.1f);
        }
    }
}
