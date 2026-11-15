using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using System;
using Random = UnityEngine.Random;

public enum TileType { Empty, Wall, Block, Bomb, Powerup, Exit }

public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    [Header("Debug")]
    [SerializeField] private bool debugShowExitBlock = true;
    [SerializeField] private Color debugExitColor = Color.yellow;
    [SerializeField] private bool debugLogLayout = false;

    [Header("Grid Size")]
    [SerializeField] private int width = 31;
    [SerializeField] private int height = 13;
    [SerializeField] private float tileSize = 1f;

    [Header("Prefab Variants")]
    [SerializeField] private GameObject[] floorPrefabs;
    [SerializeField] private GameObject[] wallPrefabs;
    [SerializeField] private GameObject[] blockPrefabs;

    [Header("Generation")]
    [Range(0f, 1f)]
    [SerializeField] private float blockDensity = 0.4f;
    [SerializeField] private int spawnClearRadius = 2;

    [Header("Editable Scene Layout")]
    [Tooltip("When assigned, use these scene tiles instead of generating a random map.")]
    [SerializeField] private ArenaLayout sceneLayout;

    [Header("Exit")]
    [Range(0f, 1f)]
    [SerializeField] private float exitZoneWidthFraction = 0.25f;

    [System.Serializable]
    public struct ItemDrop
    {
        public GameObject prefab;
        [Min(0.01f)] public float weight;
    }

    [Header("Item Drops")]
    [SerializeField] private ItemDrop[] itemDrops;
    [Range(0f, 1f)]
    [SerializeField] private float dropChance = 0.2f;

    private TileType[,] grid;
    private GameObject[,] spawnedTiles;
    private int[,] blockPrefabIndex; // -1 = no block
    private List<Vector2Int> spawnPoints;
    private Vector2Int? exitTilePos;
    private bool revealExitOnStart = false;

    public int Width => width;
    public int Height => height;
    public float TileSize => tileSize;
    public List<Vector2Int> SpawnPoints => spawnPoints;
    public Vector2Int? ExitTilePos => exitTilePos;
    public ArenaLayout SceneLayout => sceneLayout;

    public event Action<Vector2Int> OnExitRevealed;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"GridManager: duplicate instance on '{gameObject.name}' destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        grid = new TileType[width, height];
        spawnedTiles = new GameObject[width, height];
        blockPrefabIndex = new int[width, height];
        DefineSpawnPoints();

        // Use the saved layout only if it was saved from THIS stage
        SaveSlotData pending = null;
        if (HUDManager.Instance != null && HUDManager.Instance.HasPendingGridData)
        {
            var candidate = HUDManager.Instance.PeekPendingGridData();
            if (candidate.stageIndex == gameObject.scene.buildIndex)
                pending = candidate;
        }

        if (sceneLayout != null)
        {
            BuildFromSceneLayout(pending);
            if (pending != null && pending.hasExit) RestoreExit(pending);
            else PlaceSceneExit();
            return;
        }

        string layout = pending != null ? pending.blockGrid : GenerateLayoutString();

        if (debugLogLayout)
            Debug.Log($"[GridManager] {(pending != null ? "LOADED" : "GENERATED")} layout:\n{layout}");

        BuildFromLayoutString(layout);

        if (pending != null) RestoreExit(pending);
        else PlaceHiddenExit();
    }

    void Start()
    {
        // Every Awake of this scene load has finished, so the saved layout can be released now
        HUDManager.Instance?.ClearPendingGridData();

        // Fire after every other script's Start() has subscribed
        if (revealExitOnStart)
            StartCoroutine(FireExitRevealNextFrame());
    }

    IEnumerator FireExitRevealNextFrame()
    {
        yield return null;
        if (exitTilePos.HasValue)
            OnExitRevealed?.Invoke(exitTilePos.Value);
    }

    void DefineSpawnPoints()
    {
        spawnPoints = new List<Vector2Int>
        {
            new Vector2Int(1, 1),
            new Vector2Int(width - 2, 1),
            new Vector2Int(1, height - 2),
            new Vector2Int(width - 2, height - 2)
        };
    }

    bool IsWallCell(int x, int y)
    {
        if (x == 0 || y == 0 || x == width - 1 || y == height - 1) return true;
        if (x % 2 == 0 && y % 2 == 0) return true;
        return false;
    }

    bool IsNearAnySpawn(int x, int y)
    {
        foreach (var spawn in spawnPoints)
            if (Mathf.Abs(x - spawn.x) + Mathf.Abs(y - spawn.y) <= spawnClearRadius) return true;
        return false;
    }

    // ================= LAYOUT STRING =================
    // Token = XXYYBB   XX = x+1, YY = y+1
    //                  BB = 00 no block, 01 = blockPrefabs[0], 02 = blockPrefabs[1], ...
    // Walls are left blank. One line per row (y).

    string GenerateLayoutString()
    {
        int variantCount = Mathf.Max(1, blockPrefabs != null ? blockPrefabs.Length : 1);
        var sb = new StringBuilder();

        for (int y = 0; y < height; y++)
        {
            var row = new StringBuilder();
            bool anyCell = false;

            for (int x = 0; x < width; x++)
            {
                if (IsWallCell(x, y))
                {
                    row.Append("       ");
                    continue;
                }

                int bb = 0;
                if (!IsNearAnySpawn(x, y) && Random.value < blockDensity)
                    bb = Random.Range(1, variantCount + 1);

                row.Append((x + 1).ToString("00"))
                   .Append((y + 1).ToString("00"))
                   .Append(bb.ToString("00"))
                   .Append(' ');
                anyCell = true;
            }

            if (anyCell)
                sb.AppendLine(row.ToString().TrimEnd());
        }

        return sb.ToString().TrimEnd();
    }

    // Current live state in the same format (destroyed blocks show as 00) — this is what Save stores
    public string GetBlockGridString()
    {
        var sb = new StringBuilder();

        for (int y = 0; y < height; y++)
        {
            var row = new StringBuilder();
            bool anyCell = false;

            for (int x = 0; x < width; x++)
            {
                if (sceneLayout != null ? grid[x, y] == TileType.Wall : IsWallCell(x, y))
                {
                    row.Append("       ");
                    continue;
                }

                int bb = 0;
                if (grid[x, y] == TileType.Block)
                    bb = (blockPrefabIndex[x, y] >= 0 ? blockPrefabIndex[x, y] : 0) + 1;

                row.Append((x + 1).ToString("00"))
                   .Append((y + 1).ToString("00"))
                   .Append(bb.ToString("00"))
                   .Append(' ');
                anyCell = true;
            }

            if (anyCell)
                sb.AppendLine(row.ToString().TrimEnd());
        }

        return sb.ToString().TrimEnd();
    }

    // Procedural scenes keep their original generation path.
    void BuildFromLayoutString(string layout)
    {
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                grid[x, y] = IsWallCell(x, y) ? TileType.Wall : TileType.Empty;
                blockPrefabIndex[x, y] = -1;
            }

        int maxIndex = Mathf.Max(0, (blockPrefabs != null ? blockPrefabs.Length : 1) - 1);

        if (!string.IsNullOrEmpty(layout))
        {
            var tokens = layout.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                if (token.Length != 6) continue;
                if (!int.TryParse(token.Substring(0, 2), out int xx)) continue;
                if (!int.TryParse(token.Substring(2, 2), out int yy)) continue;
                if (!int.TryParse(token.Substring(4, 2), out int bb)) continue;

                int x = xx - 1;
                int y = yy - 1;

                if (x < 0 || x >= width || y < 0 || y >= height) continue;
                if (IsWallCell(x, y)) continue;

                if (bb > 0)
                {
                    grid[x, y] = TileType.Block;
                    blockPrefabIndex[x, y] = Mathf.Clamp(bb - 1, 0, maxIndex);
                }
            }
        }

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                SpawnTileVisual(x, y, grid[x, y]);
    }

    void SpawnTileVisual(int x, int y, TileType type)
    {
        Vector3 worldPos = GridToWorld(new Vector2Int(x, y));

        GameObject floorPrefab = GetRandomPrefab(floorPrefabs); // cosmetic only, not saved
        if (floorPrefab != null)
            Instantiate(floorPrefab, worldPos, Quaternion.identity, transform);

        if (type == TileType.Wall)
        {
            GameObject prefab = GetRandomPrefab(wallPrefabs); // cosmetic only, not saved
            if (prefab != null)
                spawnedTiles[x, y] = Instantiate(prefab, worldPos + Vector3.up * 0.5f, Quaternion.identity, transform);
        }
        else if (type == TileType.Block)
        {
            int idx = blockPrefabIndex[x, y];
            if (blockPrefabs != null && idx >= 0 && idx < blockPrefabs.Length && blockPrefabs[idx] != null)
                spawnedTiles[x, y] = Instantiate(blockPrefabs[idx], worldPos + Vector3.up * 0.5f, Quaternion.identity, transform);
        }
    }

    GameObject GetRandomPrefab(GameObject[] arr)
    {
        if (arr == null || arr.Length == 0) return null;
        return arr[Random.Range(0, arr.Length)];
    }

    void BuildFromSceneLayout(SaveSlotData pending)
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                blockPrefabIndex[x, y] = -1;

        var tiles = sceneLayout.GetComponentsInChildren<ArenaLayoutTile>();
        // Register walls first so overlapping blocks cannot replace permanent walls.
        foreach (var tile in tiles)
            if (tile.isActiveAndEnabled && tile.Kind == ArenaTileKind.Wall)
                RegisterSceneTile(tile, TileType.Wall);
        foreach (var tile in tiles)
            if (tile.isActiveAndEnabled && tile.Kind == ArenaTileKind.Block)
                RegisterSceneTile(tile, TileType.Block);

        if (pending != null) ApplySavedSceneBlocks(pending.blockGrid);
    }

    void RegisterSceneTile(ArenaLayoutTile tile, TileType type)
    {
        Vector2Int pos = WorldToGrid(tile.transform.position);
        if (!IsInsideGrid(pos) || grid[pos.x, pos.y] != TileType.Empty)
        {
            Debug.LogWarning($"GridManager: '{tile.name}' is outside the grid or overlaps another solid tile; disabled for this run.", tile);
            tile.gameObject.SetActive(false);
            return;
        }

        Vector3 snapped = GridToWorld(pos);
        tile.transform.position = new Vector3(snapped.x, tile.transform.position.y, snapped.z);
        grid[pos.x, pos.y] = type;
        spawnedTiles[pos.x, pos.y] = tile.gameObject;
        if (type == TileType.Block)
            blockPrefabIndex[pos.x, pos.y] = Mathf.Max(0,
                blockPrefabs == null ? -1 : Array.IndexOf(blockPrefabs, tile.SourcePrefab));
    }

    void ApplySavedSceneBlocks(string layout)
    {
        var saved = new Dictionary<Vector2Int, int>();
        if (!string.IsNullOrEmpty(layout))
            foreach (var token in layout.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length != 6 || !int.TryParse(token.Substring(0, 2), out int xx) ||
                    !int.TryParse(token.Substring(2, 2), out int yy) ||
                    !int.TryParse(token.Substring(4, 2), out int bb) || bb <= 0) continue;
                var pos = new Vector2Int(xx - 1, yy - 1);
                if (IsInsideGrid(pos)) saved[pos] = bb - 1;
            }

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var pos = new Vector2Int(x, y);
                if (grid[x, y] != TileType.Block) continue;
                if (saved.TryGetValue(pos, out int variant) && variant == blockPrefabIndex[x, y])
                {
                    saved.Remove(pos); // Keep the authored mesh, materials, rotation and scale.
                    continue;
                }
                spawnedTiles[x, y].SetActive(false);
                spawnedTiles[x, y] = null;
                grid[x, y] = TileType.Empty;
                blockPrefabIndex[x, y] = -1;
            }

        foreach (var entry in saved)
        {
            Vector2Int pos = entry.Key;
            if (grid[pos.x, pos.y] == TileType.Wall || blockPrefabs == null || blockPrefabs.Length == 0) continue;
            int variant = Mathf.Clamp(entry.Value, 0, blockPrefabs.Length - 1);
            var prefab = blockPrefabs[variant];
            if (prefab == null) continue;
            grid[pos.x, pos.y] = TileType.Block;
            blockPrefabIndex[pos.x, pos.y] = variant;
            spawnedTiles[pos.x, pos.y] = Instantiate(prefab, GridToWorld(pos) + Vector3.up * 0.5f,
                Quaternion.identity, transform);
        }
    }

    bool IsInsideGrid(Vector2Int pos) => pos.x >= 0 && pos.y >= 0 && pos.x < width && pos.y < height;

    void PlaceSceneExit()
    {
        var chosen = sceneLayout.HiddenExitBlock;
        if (chosen != null && chosen.isActiveAndEnabled && chosen.Kind == ArenaTileKind.Block)
        {
            var pos = WorldToGrid(chosen.transform.position);
            if (IsInsideGrid(pos) && grid[pos.x, pos.y] == TileType.Block)
                exitTilePos = pos;
        }

        // A deleted exit block should not make a hand-built level impossible to finish.
        for (int x = width - 1; x >= 0 && !exitTilePos.HasValue; x--)
            for (int y = 0; y < height; y++)
                if (grid[x, y] == TileType.Block)
                {
                    exitTilePos = new Vector2Int(x, y);
                    break;
                }

        if (exitTilePos.HasValue)
        {
            if (debugShowExitBlock) HighlightExitBlock(exitTilePos.Value);
            return;
        }

        for (int x = width - 1; x >= 0; x--)
            for (int y = 0; y < height; y++)
                if (IsWalkable(new Vector2Int(x, y)))
                {
                    exitTilePos = new Vector2Int(x, y);
                    revealExitOnStart = true;
                    return;
                }
    }

    // ================= EXIT =================

    void PlaceHiddenExit()
    {
        var blockTiles = new List<Vector2Int>();
        int rightZoneStart = Mathf.FloorToInt(width * (1f - exitZoneWidthFraction));

        for (int x = rightZoneStart; x < width; x++)
            for (int y = 0; y < height; y++)
                if (grid[x, y] == TileType.Block)
                    blockTiles.Add(new Vector2Int(x, y));

        if (blockTiles.Count > 0)
        {
            exitTilePos = blockTiles[Random.Range(0, blockTiles.Count)];
            if (debugShowExitBlock) HighlightExitBlock(exitTilePos.Value);
        }
    }

    void RestoreExit(SaveSlotData data)
    {
        if (!data.hasExit) return;

        var restoredPos = new Vector2Int(data.exitX, data.exitY);
        if (!IsInsideGrid(restoredPos) || grid[data.exitX, data.exitY] == TileType.Wall)
        {
            if (sceneLayout != null) PlaceSceneExit();
            return;
        }

        exitTilePos = restoredPos;

        if (grid[data.exitX, data.exitY] == TileType.Block)
        {
            // Exit still hidden under its block
            if (debugShowExitBlock) HighlightExitBlock(exitTilePos.Value);
        }
        else
        {
            // Block was already destroyed before saving: the exit was revealed, bring the item back
            grid[data.exitX, data.exitY] = TileType.Exit;
            revealExitOnStart = true;
        }
    }

    void HighlightExitBlock(Vector2Int pos)
    {
        GameObject blockObj = spawnedTiles[pos.x, pos.y];
        if (blockObj == null) return;

        Renderer rend = blockObj.GetComponentInChildren<Renderer>();
        if (rend == null) return;

        var propBlock = new MaterialPropertyBlock();
        rend.GetPropertyBlock(propBlock);
        propBlock.SetColor("_BaseColor", debugExitColor);
        propBlock.SetColor("_Color", debugExitColor);
        rend.SetPropertyBlock(propBlock);
    }

    // ================= GRID QUERIES =================

    public Vector3 GridToWorld(Vector2Int gridPos) =>
        new Vector3(gridPos.x * tileSize, 0, (height - 1 - gridPos.y) * tileSize);

    public Vector3 GridDirectionToWorld(Vector2Int gridDir) => new Vector3(gridDir.x, 0, -gridDir.y);

    public Vector2Int WorldToGrid(Vector3 worldPos) => new Vector2Int(
        Mathf.RoundToInt(worldPos.x / tileSize),
        height - 1 - Mathf.RoundToInt(worldPos.z / tileSize));

    public bool IsWalkable(Vector2Int gridPos)
    {
        if (gridPos.x < 0 || gridPos.x >= width || gridPos.y < 0 || gridPos.y >= height) return false;
        TileType type = grid[gridPos.x, gridPos.y];
        return type == TileType.Empty || type == TileType.Powerup || type == TileType.Exit;
    }

    public bool CanEnemyEnter(Vector2Int pos, bool passBlocks, bool passWalls)
    {
        if (pos.x < 0 || pos.x >= width || pos.y < 0 || pos.y >= height) return false;

        TileType type = grid[pos.x, pos.y];

        if (type == TileType.Wall)
        {
            bool isBorder = pos.x == 0 || pos.y == 0 || pos.x == width - 1 || pos.y == height - 1;
            return passWalls && !isBorder; // the outer border always stops everyone
        }

        if (type == TileType.Block) return passBlocks;
        if (type == TileType.Bomb) return false;

        return true; // Empty, Powerup, Exit
    }

    public TileType GetTile(Vector2Int gridPos) => grid[gridPos.x, gridPos.y];

    public void SetTile(Vector2Int gridPos, TileType type)
    {
        grid[gridPos.x, gridPos.y] = type;
    }

    public bool CanPlaceBomb(Vector2Int gridPos)
    {
        if (gridPos.x < 0 || gridPos.x >= width || gridPos.y < 0 || gridPos.y >= height) return false;
        return grid[gridPos.x, gridPos.y] == TileType.Empty;
    }

    public void PlaceBomb(Vector2Int gridPos, GameObject bombObject)
    {
        grid[gridPos.x, gridPos.y] = TileType.Bomb;
        spawnedTiles[gridPos.x, gridPos.y] = bombObject;
    }

    public void ClearTile(Vector2Int gridPos)
    {
        bool wasExitTile = exitTilePos.HasValue && exitTilePos.Value == gridPos;
        TileType previousType = grid[gridPos.x, gridPos.y];

        grid[gridPos.x, gridPos.y] = TileType.Empty;

        GameObject obj = spawnedTiles[gridPos.x, gridPos.y];
        if (obj != null)
        {
            if (!obj.TryGetComponent<Bomb>(out _))
                Destroy(obj);
            spawnedTiles[gridPos.x, gridPos.y] = null;
        }

        if (previousType == TileType.Block)
        {
            blockPrefabIndex[gridPos.x, gridPos.y] = -1;
            TrySpawnDrop(gridPos);
        }

        if (wasExitTile && previousType == TileType.Block)
            OnExitRevealed?.Invoke(gridPos);
    }

    void TrySpawnDrop(Vector2Int gridPos)
    {
        if (itemDrops == null || itemDrops.Length == 0) return;
        if (Random.value > dropChance) return;

        GameObject prefab = GetWeightedDropPrefab();
        if (prefab == null) return;

        Vector3 worldPos = GridToWorld(gridPos);
        Instantiate(prefab, worldPos + Vector3.up * 0.5f, Quaternion.identity);
    }

    GameObject GetWeightedDropPrefab()
    {
        float totalWeight = 0f;
        foreach (var item in itemDrops) totalWeight += item.weight;
        if (totalWeight <= 0f) return null;

        float roll = Random.value * totalWeight;
        float cumulative = 0f;

        foreach (var item in itemDrops)
        {
            cumulative += item.weight;
            if (roll <= cumulative) return item.prefab;
        }
        return itemDrops[itemDrops.Length - 1].prefab;
    }

    public GameObject GetTileObject(Vector2Int gridPos) => spawnedTiles[gridPos.x, gridPos.y];

    public List<Vector2Int> GetAllWalkableTiles()
    {
        var result = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var pos = new Vector2Int(x, y);
                if (IsWalkable(pos)) result.Add(pos);
            }
        return result;
    }

    public bool IsNearSpawnPoint(Vector2Int pos, int radius)
    {
        foreach (var spawn in spawnPoints)
            if (Mathf.Abs(pos.x - spawn.x) + Mathf.Abs(pos.y - spawn.y) <= radius) return true;
        return false;
    }

    public bool IsWorldPositionFree(Vector3 worldPos, float radius, Vector2Int? exemptTile = null)
    {
        Vector3[] corners =
        {
            worldPos + new Vector3(-radius, 0, -radius),
            worldPos + new Vector3( radius, 0, -radius),
            worldPos + new Vector3(-radius, 0,  radius),
            worldPos + new Vector3( radius, 0,  radius)
        };

        foreach (var corner in corners)
        {
            Vector2Int cornerGrid = WorldToGrid(corner);
            if (exemptTile.HasValue && cornerGrid == exemptTile.Value) continue;
            if (!IsWalkable(cornerGrid)) return false;
        }
        return true;
    }
}
