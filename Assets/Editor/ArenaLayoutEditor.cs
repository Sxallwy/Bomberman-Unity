using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ArenaLayoutEditorTools
{
    static ArenaLayoutEditorTools()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    static void OnSceneGUI(SceneView view)
    {
        if (!Application.isPlaying && Event.current.rawType == EventType.MouseUp && Tools.current == Tool.Move)
            EditorApplication.delayCall += SnapSelection;
    }

    public static void SnapSelection()
    {
        if (Application.isPlaying) return;
        var visited = new HashSet<ArenaLayoutTile>();
        foreach (var selected in Selection.transforms)
        {
            var tile = selected.GetComponentInParent<ArenaLayoutTile>();
            if (tile == null || !visited.Add(tile) || tile.Layout == null || tile.Layout.Grid == null) continue;
            var grid = tile.Layout.Grid;
            Vector3 snapped = grid.GridToWorld(grid.WorldToGrid(tile.transform.position));
            snapped.y = tile.transform.position.y;
            if ((tile.transform.position - snapped).sqrMagnitude < 0.000001f) continue;
            Undo.RecordObject(tile.transform, "Snap arena tile to grid");
            tile.transform.position = snapped;
        }
    }

    public static ArenaLayout CreateLayout(GridManager grid)
    {
        if (Application.isPlaying || grid == null || grid.SceneLayout != null) return grid != null ? grid.SceneLayout : null;
        var settings = new SerializedObject(grid);
        var floors = ReadPrefabs(settings.FindProperty("floorPrefabs"));
        var walls = ReadPrefabs(settings.FindProperty("wallPrefabs"));
        var blocks = ReadPrefabs(settings.FindProperty("blockPrefabs"));
        float density = settings.FindProperty("blockDensity").floatValue;
        int clearRadius = settings.FindProperty("spawnClearRadius").intValue;

        var root = new GameObject(grid.gameObject.scene.name + " Interior");
        var layout = root.AddComponent<ArenaLayout>();
        layout.Configure(grid, null);
        var floorGroup = MakeGroup(root.transform, "Floors");
        var wallGroup = MakeGroup(root.transform, "Walls");
        var blockGroup = MakeGroup(root.transform, "Blocks");
        // Used once to provide a starting layout. The saved scene owns every later edit.
        var random = new System.Random(417);
        ArenaLayoutTile exit = null;
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var pos = new Vector2Int(x, y);
                MakeTile(grid, floorGroup, ArenaTileKind.Floor, Pick(floors, random), pos);
                bool wall = x == 0 || y == 0 || x == grid.Width - 1 || y == grid.Height - 1 || (x % 2 == 0 && y % 2 == 0);
                if (wall)
                {
                    MakeTile(grid, wallGroup, ArenaTileKind.Wall, Pick(walls, random), pos);
                    continue;
                }
                bool nearSpawn = false;
                foreach (var spawn in new[] { new Vector2Int(1, 1), new Vector2Int(grid.Width - 2, 1),
                             new Vector2Int(1, grid.Height - 2), new Vector2Int(grid.Width - 2, grid.Height - 2) })
                    if (Mathf.Abs(x - spawn.x) + Mathf.Abs(y - spawn.y) <= clearRadius) nearSpawn = true;
                if (!nearSpawn && random.NextDouble() < density)
                {
                    var tile = MakeTile(grid, blockGroup, ArenaTileKind.Block, Pick(blocks, random), pos);
                    if (tile != null && (exit == null || x > grid.WorldToGrid(exit.transform.position).x)) exit = tile;
                }
            }

        layout.Configure(grid, exit);
        Undo.RegisterCreatedObjectUndo(root, "Create editable interior");
        settings.FindProperty("sceneLayout").objectReferenceValue = layout;
        settings.ApplyModifiedProperties();
        Selection.activeGameObject = root;
        return layout;
    }

    static GameObject[] ReadPrefabs(SerializedProperty array)
    {
        var result = new List<GameObject>();
        for (int i = 0; i < array.arraySize; i++)
        {
            var prefab = array.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
            if (prefab != null) result.Add(prefab);
        }
        return result.ToArray();
    }

    static GameObject Pick(GameObject[] prefabs, System.Random random) =>
        prefabs.Length == 0 ? null : prefabs[random.Next(prefabs.Length)];

    static Transform MakeGroup(Transform parent, string name)
    {
        var group = new GameObject(name).transform;
        group.SetParent(parent, false);
        return group;
    }

    static ArenaLayoutTile MakeTile(GridManager grid, Transform parent, ArenaTileKind kind, GameObject prefab, Vector2Int pos)
    {
        if (prefab == null) return null;
        var root = new GameObject($"{kind} ({pos.x}, {pos.y})");
        root.transform.SetParent(parent, false);
        root.transform.position = grid.GridToWorld(pos) + (kind == ArenaTileKind.Floor ? Vector3.zero : Vector3.up * 0.5f);
        var tile = root.AddComponent<ArenaLayoutTile>();
        tile.Configure(kind, prefab);
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        return tile;
    }

    public static void ApplyModel(ArenaLayoutTile tile)
    {
        if (Application.isPlaying || tile.SourcePrefab == null || EditorUtility.IsPersistent(tile)) return;
        if (!PrefabUtility.IsPartOfPrefabAsset(tile.SourcePrefab))
        {
            Debug.LogWarning("Choose a prefab asset from the Project panel.", tile);
            return;
        }
        for (int i = tile.transform.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(tile.transform.GetChild(i).gameObject);
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(tile.SourcePrefab, tile.transform);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        Undo.RegisterCreatedObjectUndo(visual, "Replace arena tile model");
    }
}

[CustomEditor(typeof(GridManager))]
public class ArenaGridManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var grid = (GridManager)target;
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (grid.SceneLayout == null)
            {
                if (GUILayout.Button("Create Editable Interior")) ArenaLayoutEditorTools.CreateLayout(grid);
            }
            else if (GUILayout.Button("Select Editable Interior")) Selection.activeGameObject = grid.SceneLayout.gameObject;
        }
    }
}

[CustomEditor(typeof(ArenaLayout))]
public class ArenaLayoutEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("Edit Floors, Walls and Blocks while stopped. Move or duplicate tile roots; Ctrl+S saves your layout. Green gizmos mark spawn corners.", MessageType.Info);
        var layout = (ArenaLayout)target;
        if (layout.Grid == null) return;
        int invalid = 0;
        var seen = new HashSet<Vector2Int>();
        foreach (var tile in layout.GetComponentsInChildren<ArenaLayoutTile>())
        {
            if (!tile.isActiveAndEnabled || tile.Kind == ArenaTileKind.Floor) continue;
            var pos = layout.Grid.WorldToGrid(tile.transform.position);
            if (pos.x < 0 || pos.y < 0 || pos.x >= layout.Grid.Width || pos.y >= layout.Grid.Height || !seen.Add(pos)) invalid++;
        }
        if (invalid > 0) EditorGUILayout.HelpBox($"{invalid} solid tile(s) overlap or are outside the grid. Move or remove them before playing.", MessageType.Warning);
    }
}

[CustomEditor(typeof(ArenaLayoutTile)), CanEditMultipleObjects]
public class ArenaLayoutTileEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var tile = (ArenaLayoutTile)target;
        if (tile.Layout != null && tile.Layout.Grid != null)
            EditorGUILayout.LabelField("Grid Cell", tile.Layout.Grid.WorldToGrid(tile.transform.position).ToString());
        EditorGUILayout.HelpBox("Move with W; tiles snap after dragging. Ctrl+D adds a tile. Change Source Prefab and press Apply Model to replace its appearance.", MessageType.Info);
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (GUILayout.Button("Snap Selected Tiles")) ArenaLayoutEditorTools.SnapSelection();
            if (GUILayout.Button("Apply Model"))
                foreach (var selected in targets) ArenaLayoutEditorTools.ApplyModel((ArenaLayoutTile)selected);
        }
    }
}
