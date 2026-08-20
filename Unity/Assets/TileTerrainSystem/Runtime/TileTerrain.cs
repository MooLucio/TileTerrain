using System.Collections.Generic;
using UnityEngine;

namespace MooLucio.TileTerrain
{
    /// <summary>
    /// Editor-only terrain component. <see cref="GenerateMesh"/> splits the terrain
    /// into chunk child GameObjects for occlusion culling. No code runs at runtime
    /// — baked chunks are serialized with the scene.
    /// </summary>
    public class TileTerrain : MonoBehaviour
    {
        public const float HeightMin = -2f;
        public const float HeightMax = 2f;
        public const int TilemapColumns = 8;
        public const float WaterOffset = 0.5f;
        private const float UVInset = 0f;

        [Tooltip("ScriptableObject storing all vertex and quad data for this terrain instance.")]
        public TileTerrainGridData GridData;

        [HideInInspector]
        public Material TileMaterial;

        [HideInInspector]
        public List<Texture2DArray> RegisteredTextures = new List<Texture2DArray>();

        [Tooltip("Palette asset listing available Texture2DArrays with per-texture priority values.")]
        public TileTerrainPalette Palette;

        [Tooltip("Props Box asset listing available props.")]
        public TileTerrainPropsBox PropsBox;

        [Tooltip("FBX with single-step cliff meshes for one-level elevation changes.")]
        public GameObject CliffMeshFbx;

        [Tooltip("FBX with double-height cliff meshes for two-level elevation spans.")]
        public GameObject CliffDoubleMeshFbx;

        [Tooltip("FBX with transitional meshes for quads containing 3 distinct floor levels.")]
        public GameObject CliffTransitionalMeshFbx;

        [Tooltip("FBX with ramp meshes for half-step elevation transitions.")]
        public GameObject RampMeshFbx;

        [Tooltip("Material used for water surface rendering (should be translucent).")]
        public Material WaterMaterial;

        [Tooltip("Toggle the grid overlay in the Scene view.")]
        public bool ShowGrid = true;

        [Tooltip("Color of the small (1x1) grid lines.")]
        public Color GridColor = new Color(1, 1, 1, 0.2f);

        [Tooltip("Color of the big (4x4) grid lines for spatial orientation.")]
        public Color Grid4x4Color = new Color(1, 1, 0, 0.5f);

        [HideInInspector]
        public bool ShowQuadVertexIds = false;

        [Tooltip("Number of quads per chunk side. Larger = fewer draw calls but coarser culling. 16-32 recommended.")]
        [Range(4, 64)]
        public int ChunkSize = 16;

        [Tooltip("When enabled, chunk GameObjects are hidden in the Hierarchy to reduce clutter.")]
        public bool HideChunksInHierarchy = true;

        /// <summary>
        /// Returns the palette priority of the given texture index (lower = higher priority).
        /// </summary>
        public float GetPriority(int textureId)
        {
            if (Palette == null || RegisteredTextures == null || textureId < 0 || textureId >= RegisteredTextures.Count)
                return float.MaxValue;
            return Palette.GetPriority(RegisteredTextures[textureId]);
        }

#if UNITY_EDITOR
    // ── Editor-only state ────────────────────────────────────────────────────
    private Dictionary<int, Material>            _materialCache        = new Dictionary<int, Material>();
    private readonly List<int>                      _flatQuadIndices             = new List<int>();
    private readonly List<int>                      _cliffIdx            = new List<int>();
    private readonly List<Mesh>                     _cliffMeshList       = new List<Mesh>();
    private readonly List<Vector3>                  _verts               = new List<Vector3>();
    private readonly List<Vector3>                  _uvs                 = new List<Vector3>();
    private readonly List<Vector3>                  _uv1s                = new List<Vector3>();
    private readonly List<Vector3>                  _uv2s                = new List<Vector3>();
    private readonly List<Color>                    _cols                = new List<Color>();
    private readonly List<Vector3>                  _cliffVerts          = new List<Vector3>();
    private readonly List<Vector3>                  _cliffUVs            = new List<Vector3>();
    private readonly List<Vector3>                  _cliffUV1s           = new List<Vector3>();
    private readonly List<Vector3>                  _cliffUV2s           = new List<Vector3>();
    private readonly List<Color>                    _cliffCols           = new List<Color>();
    private readonly List<Vector3>                  _cliffNormals        = new List<Vector3>();
    private readonly List<int>                      _cliffLevels         = new List<int>();
    private readonly List<int>                      _flatFloorOffsets    = new List<int>();
    private readonly List<int>                      _rampFlatIdx         = new List<int>();
    private readonly List<Vector4>                  _rampFlatOffsets     = new List<Vector4>();
    private readonly Dictionary<Material, List<int>> _materialGroups     = new Dictionary<Material, List<int>>();
    private Dictionary<string, (GameObject terrain, GameObject water)> _chunkCache;

    private const string ChunkTag = "TTChunk";

    void OnEnable()
    {
        if (!Application.isPlaying)
        {
            if (TileMaterial == null)
                AutoAssignMaterial();
            SyncTexturesFromPalette();
            GenerateMesh();
            SpawnProps();
        }
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private void AutoAssignMaterial()
    {
        string[] guids = UnityEditor.AssetDatabase.FindAssets("TileTerrainShader t:Material");
        if (guids.Length > 0)
            TileMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    void OnDisable()
    {
        UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
    }

    private void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingEditMode)
            TileTerrainCliff.InvalidateCache();
        else if (state == UnityEditor.PlayModeStateChange.EnteredPlayMode)
            SpawnProps();
        else if (state == UnityEditor.PlayModeStateChange.EnteredEditMode)
        {
            TileTerrainCliff.InvalidateCache();
            ClearMaterials();
            SyncTexturesFromPalette();
            GenerateMesh();
            SpawnProps();
        }
    }

    /// <summary>
    /// Rebuilds RegisteredTextures from the current palette entries, de-duplicated.
    /// </summary>
    public void SyncTexturesFromPalette()
    {
        if (Palette == null) return;
        var newList = new List<Texture2DArray>();
        foreach (var e in Palette.Entries)
            if (e.Texture != null && !newList.Contains(e.Texture))
                newList.Add(e.Texture);
        RegisteredTextures = newList;
    }

    /// <summary>
    /// Full rebuild: syncs textures, clears the material cache, recalculates bitmasks,
    /// regenerates the mesh and respawns props (with a progress bar).
    /// </summary>
    public void FullSyncAndRegenerate()
    {
        UnityEditor.EditorUtility.DisplayProgressBar("TileTerrain", "Synchronizing textures and rebuilding mesh...", 0.1f);
        SyncTexturesFromPalette();
        
        UnityEditor.EditorUtility.DisplayProgressBar("TileTerrain", "Clearing material cache...", 0.3f);
        ClearMaterials();
        
        UnityEditor.EditorUtility.DisplayProgressBar("TileTerrain", "Recalculating all bitmasks...", 0.6f);
        TileTerrainBitmask.RecalculateAll(this);
        
        UnityEditor.EditorUtility.DisplayProgressBar("TileTerrain", "Generating chunks...", 0.9f);
        GenerateMesh();

        UnityEditor.EditorUtility.DisplayProgressBar("TileTerrain", "Spawning props...", 0.95f);
        SpawnProps();

        UnityEditor.EditorUtility.ClearProgressBar();
    }

    // ── Chunk management ─────────────────────────────────────────────────────

    private Transform PropsContainer
    {
        get
        {
            const string name = "TT_Props";
            for (int i = 0; i < transform.childCount; i++)
            {
                var c = transform.GetChild(i);
                if (c.name == name) return c;
            }
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.transform;
        }
    }

    private void ClearProps()
    {
        var container = PropsContainer;
        for (int i = container.childCount - 1; i >= 0; i--)
            DestroyImmediate(container.GetChild(i).gameObject);
    }

    /// <summary>
    /// Clears and re-instantiates all props from GridData.Props using the PropsBox definitions.
    /// </summary>
    public void SpawnProps()
    {
        ClearProps();
        if (GridData == null || PropsBox == null) return;
        if (GridData.Props == null) return;
        Transform container = PropsContainer;
        for (int i = 0; i < GridData.Props.Count; i++)
        {
            var a = GridData.Props[i];
            if (a.propIndex < 0 || a.propIndex >= PropsBox.Props.Count) continue;
            var prop = PropsBox.Props[a.propIndex];
            var spawnPrefab = prop.Prefabs != null && a.variant < prop.Prefabs.Count ? prop.Prefabs[a.variant] : null;
            if (spawnPrefab == null) continue;
            GameObject go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(spawnPrefab, container);
            go.name = $"TTProps_{i}";
            Vector3 pos = a.position;
            if (a.pinnedToGround)
                pos.y = GetTerrainHeightAt(new Vector3(pos.x, 0, pos.z));
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, a.rotationY, 0);
            go.transform.localScale = Vector3.one * a.scale;
            if (HideChunksInHierarchy)
                go.hideFlags = HideFlags.HideInHierarchy;
        }
    }

    /// <summary>
    /// Ground height at a local-space position, including cliff floor offsets
    /// and water-level clamping.
    /// </summary>
    public float GetTerrainHeightAt(Vector3 localPos)
    {
        if (GridData == null) return localPos.y;
        var data = GridData;
        float h = data.GetHeightAt(localPos);

        int w = data.Width;
        int gridHeight = data.Height;
        float gx = localPos.x + w / 2f;
        float gz = localPos.z + gridHeight / 2f;

        int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, w - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(gz), 0, gridHeight - 1);

        float tx = Mathf.Clamp01(gx - x0);
        float tz = Mathf.Clamp01(gz - z0);

        var quad = data.Quads[z0 * w + x0];

        int maxV = TileTerrainConstants.NoCliffLevel;
        for (int j = 0; j < 4; j++)
            if (data.Vertices[quad.vertexIds[j]].CliffByte > maxV)
                maxV = data.Vertices[quad.vertexIds[j]].CliffByte;

        float cliffAccum = 0f;
        for (int level = quad.floor; level < maxV; level++)
        {
            byte mask = TileTerrainBitmask.CalculateCliffMaskAtLevel(data, quad, level);
            if (mask == TileTerrainConstants.FullQuadMask) cliffAccum += 1f;
            else if (mask > 0)
            {
                float b0 = (mask & 1) != 0 ? 1f : 0f;
                float b1 = (mask & 2) != 0 ? 1f : 0f;
                float b2 = (mask & 4) != 0 ? 1f : 0f;
                float b3 = (mask & 8) != 0 ? 1f : 0f;
                cliffAccum += Mathf.Lerp(Mathf.Lerp(b0, b1, tx), Mathf.Lerp(b2, b3, tx), tz);
            }
        }

        float terrainY = h + (quad.floor + cliffAccum) * TileTerrainCliff.CliffHeight;

        var sv0 = data.Vertices[quad.vertexIds[0]];
        var sv1 = data.Vertices[quad.vertexIds[1]];
        var sv2 = data.Vertices[quad.vertexIds[2]];
        var sv3 = data.Vertices[quad.vertexIds[3]];
        int mwl = Mathf.Max(sv0.IsWater ? sv0.WaterLevel : TileTerrainConstants.NoCliffLevel,
                            sv1.IsWater ? sv1.WaterLevel : TileTerrainConstants.NoCliffLevel,
                            sv2.IsWater ? sv2.WaterLevel : TileTerrainConstants.NoCliffLevel,
                            sv3.IsWater ? sv3.WaterLevel : TileTerrainConstants.NoCliffLevel);
        if (mwl > TileTerrainConstants.NoCliffLevel)
        {
            float waterY = (mwl - 0.5f) * TileTerrainCliff.CliffHeight;
            return Mathf.Max(terrainY, waterY);
        }
        return terrainY;
    }

    /// <summary>
    /// Average terrain height across a circular footprint — keeps props level on slopes.
    /// </summary>
    public float GetFootprintHeightAt(Vector3 center, float radius)
    {
        if (GridData == null || radius <= 0f) return GetTerrainHeightAt(center);
        int w = GridData.Width;
        int h = GridData.Height;
        float total = 0f;
        int count = 0;
        float rSq = radius * radius;
        int xMin = Mathf.Max(0, Mathf.FloorToInt(center.x - radius + w * 0.5f));
        int xMax = Mathf.Min(w, Mathf.CeilToInt(center.x + radius + w * 0.5f));
        int zMin = Mathf.Max(0, Mathf.FloorToInt(center.z - radius + h * 0.5f));
        int zMax = Mathf.Min(h, Mathf.CeilToInt(center.z + radius + h * 0.5f));
        for (int z = zMin; z <= zMax; z++)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                float gx = x + 0.5f - w * 0.5f;
                float gz = z + 0.5f - h * 0.5f;
                float dx = gx - center.x;
                float dz = gz - center.z;
                if (dx * dx + dz * dz > rSq) continue;
                total += GetTerrainHeightAt(new Vector3(gx, 0, gz));
                count++;
            }
        }
        return count > 0 ? total / count : GetTerrainHeightAt(center);
    }

    /// <summary>
    /// Snaps pinned props to the terrain height and repositions their GameObjects.
    /// </summary>
    public void PinPropsToTerrain()
    {
        if (GridData == null || GridData.Props == null) return;
        bool anyChanged = false;
        for (int i = 0; i < GridData.Props.Count; i++)
        {
            var a = GridData.Props[i];
            if (!a.pinnedToGround) continue;
            float newY = GetTerrainHeightAt(new Vector3(a.position.x, 0, a.position.z));
            if (Mathf.Abs(a.position.y - newY) > 0.0001f)
            {
                a.position.y = newY;
                GridData.Props[i] = a;
                anyChanged = true;
            }
        }
        if (anyChanged)
        {
            UnityEditor.EditorUtility.SetDirty(GridData);
        }

        var container = PropsContainer;
        for (int i = container.childCount - 1; i >= 0; i--)
        {
            var child = container.GetChild(i);
            if (!child.name.StartsWith("TTProps_")) continue;
            if (int.TryParse(child.name.Substring(8), out int idx) && idx >= 0 && idx < GridData.Props.Count)
            {
                var a = GridData.Props[idx];
                if (a.pinnedToGround)
                {
                    var pos = child.position;
                    pos.y = a.position.y;
                    child.position = pos;
                }
            }
        }
    }

    /// <summary>Destroy all previously baked chunk children.</summary>
    private void ClearChunks()
    {
        // Iterate backwards so destroying doesn't shift indices.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name.StartsWith(ChunkTag))
                DestroyImmediate(child.gameObject);
        }
        if (_chunkCache != null) _chunkCache.Clear();
    }

    private (GameObject terrain, GameObject water) GetOrCreateChunk(int cx, int cz)
    {
        string rootName = $"{ChunkTag}_{cx}_{cz}";
        if (_chunkCache != null && _chunkCache.TryGetValue(rootName, out var cached))
            return cached;

        GameObject root = null;
        for (int i = 0; i < transform.childCount; i++)
        {
            var c = transform.GetChild(i);
            if (c.name == rootName) { root = c.gameObject; break; }
        }

        if (root == null)
        {
            root = new GameObject(rootName);
            root.transform.SetParent(transform, false);
            UnityEditor.GameObjectUtility.SetStaticEditorFlags(root,
                UnityEditor.StaticEditorFlags.OccluderStatic |
                UnityEditor.StaticEditorFlags.OccludeeStatic |
                UnityEditor.StaticEditorFlags.BatchingStatic);
        }
        root.hideFlags = HideChunksInHierarchy ? HideFlags.HideInHierarchy : HideFlags.None;

        GameObject tGo = null, wGo = null;
        for (int i = 0; i < root.transform.childCount; i++)
        {
            var c = root.transform.GetChild(i);
            if (c.name == "Terrain") tGo = c.gameObject;
            else if (c.name == "Water") wGo = c.gameObject;
        }

        if (tGo == null)
        {
            tGo = new GameObject("Terrain");
            tGo.transform.SetParent(root.transform, false);
            UnityEditor.GameObjectUtility.SetStaticEditorFlags(tGo, UnityEditor.StaticEditorFlags.BatchingStatic | UnityEditor.StaticEditorFlags.OccludeeStatic);
        }
        if (wGo == null)
        {
            wGo = new GameObject("Water");
            wGo.transform.SetParent(root.transform, false);
            UnityEditor.GameObjectUtility.SetStaticEditorFlags(wGo, UnityEditor.StaticEditorFlags.BatchingStatic | UnityEditor.StaticEditorFlags.OccludeeStatic);
        }

        var result = (tGo, wGo);
        if (_chunkCache == null) _chunkCache = new Dictionary<string, (GameObject terrain, GameObject water)>();
        _chunkCache[rootName] = result;
        return result;
    }

    // ── Main generation ──────────────────────────────────────────────────────

    /// <summary>
    /// Seeds texture data, recalculates bitmasks and regenerates the full terrain mesh
    /// (chunks, cliff meshes, materials). Editor-only entry point for baking.
    /// </summary>
    public void GenerateMesh(bool recalcFloor = true)
    {
        if (GridData == null || TileMaterial == null) return;
        GridData.EnsureGridData();

        if ((RegisteredTextures == null || RegisteredTextures.Count == 0) && Palette != null)
            SyncTexturesFromPalette();

        // Seed uninitialized vertices.
        if (RegisteredTextures != null && RegisteredTextures.Count > 0)
        {
            GridData.Sanitize(this);
            bool anyDirty = false;
            for (int i = 0; i < GridData.Vertices.Count; i++)
                if (GridData.Vertices[i].overTextureId == -1) { anyDirty = true; break; }

            if (anyDirty)
            {
                for (int i = 0; i < GridData.Vertices.Count; i++)
                {
                    var v = GridData.Vertices[i];
                    // Migration: if over is empty but mid or under has data, promote upward.
                    if (v.overTextureId == -1 && v.midTextureId >= 0)
                    { v.overTextureId = v.midTextureId; v.overMask = v.midMask; v.midTextureId = -1; v.midMask = 0; }
                    if (v.overTextureId == -1 && v.underTextureId >= 0)
                    { v.overTextureId = v.underTextureId; v.overMask = v.underMask; v.underTextureId = -1; v.underMask = 0; }
                    // If still empty, seed with highest-priority texture.
                    if (v.overTextureId == -1)
                    {
                        int bestIdx = -1; float bestP = float.MaxValue;
                        for (int j = 0; j < RegisteredTextures.Count; j++) { float p = GetPriority(j); if (p < bestP) { bestP = p; bestIdx = j; } }
                        if (bestIdx != -1) { v.overTextureId = bestIdx; v.overMask = 0xFF; }
                    }
                    GridData.Vertices[i] = v;
                }
                TileTerrainBitmask.RecalculateAll(this);
            }
        }

        var cliffMeshes = CliffMeshFbx != null ? TileTerrainCliff.GetOrLoadMeshes(CliffMeshFbx) : null;
        var cliffDoubleMeshes = CliffDoubleMeshFbx != null ? TileTerrainCliff.GetOrLoadMeshes(CliffDoubleMeshFbx) : null;
        var cliffTransitionalMeshes = CliffTransitionalMeshFbx != null ? TileTerrainCliff.GetOrLoadMeshes(CliffTransitionalMeshFbx) : null;
        var rampMeshes = RampMeshFbx != null ? TileTerrainCliff.GetOrLoadMeshes(RampMeshFbx) : null;

        int cs     = ChunkSize;
        int cCols  = Mathf.CeilToInt((float)GridData.Width  / cs);
        int cRows  = Mathf.CeilToInt((float)GridData.Height / cs);

        ClearChunks();

        for (int cz = 0; cz < cRows; cz++)
        for (int cx = 0; cx < cCols; cx++)
        {
            int qxMin = cx * cs;
            int qzMin = cz * cs;
            int qxMax = Mathf.Min(qxMin + cs, GridData.Width);
            int qzMax = Mathf.Min(qzMin + cs, GridData.Height);

            BuildChunkMesh(cx, cz, qxMin, qxMax, qzMin, qzMax, cliffMeshes, cliffDoubleMeshes, cliffTransitionalMeshes, rampMeshes);
        }

        SmoothChunkSeams();

        var mf2 = GetComponent<MeshFilter>();
        if (mf2 != null) mf2.sharedMesh = null;
        var mr2 = GetComponent<MeshRenderer>();
        if (mr2 != null) mr2.sharedMaterials = new Material[0];
        if (recalcFloor) GridData.RecalculateFloorOffsets();
    }

    /// <summary>
    /// Regenerates only the given chunk coordinates into child GameObjects,
    /// rebuilding their cliff boundary meshes too.
    /// </summary>
    public void GenerateChunks(HashSet<(int, int)> chunkCoords, bool recalcFloor = true)
    {
        if (GridData == null || TileMaterial == null) return;
        GridData.EnsureGridData();

        if ((RegisteredTextures == null || RegisteredTextures.Count == 0) && Palette != null)
            SyncTexturesFromPalette();

        if (RegisteredTextures != null && RegisteredTextures.Count > 0)
        {
            GridData.Sanitize(this);
            bool anyDirty = false;
            for (int i = 0; i < GridData.Vertices.Count; i++)
                if (GridData.Vertices[i].overTextureId == -1) { anyDirty = true; break; }

            if (anyDirty)
            {
                for (int i = 0; i < GridData.Vertices.Count; i++)
                {
                    var v = GridData.Vertices[i];
                    if (v.overTextureId == -1 && v.midTextureId >= 0)
                    { v.overTextureId = v.midTextureId; v.overMask = v.midMask; v.midTextureId = -1; v.midMask = 0; }
                    if (v.overTextureId == -1 && v.underTextureId >= 0)
                    { v.overTextureId = v.underTextureId; v.overMask = v.underMask; v.underTextureId = -1; v.underMask = 0; }
                    if (v.overTextureId == -1)
                    {
                        int bestIdx = -1; float bestP = float.MaxValue;
                        for (int j = 0; j < RegisteredTextures.Count; j++) { float p = GetPriority(j); if (p < bestP) { bestP = p; bestIdx = j; } }
                        if (bestIdx != -1) { v.overTextureId = bestIdx; v.overMask = 0xFF; }
                    }
                    GridData.Vertices[i] = v;
                }
                TileTerrainBitmask.RecalculateAll(this);
            }
        }

        var cliffMeshes = CliffMeshFbx != null ? TileTerrainCliff.GetOrLoadMeshes(CliffMeshFbx) : null;
        var cliffDoubleMeshes = CliffDoubleMeshFbx != null ? TileTerrainCliff.GetOrLoadMeshes(CliffDoubleMeshFbx) : null;
        var cliffTransitionalMeshes = CliffTransitionalMeshFbx != null ? TileTerrainCliff.GetOrLoadMeshes(CliffTransitionalMeshFbx) : null;
        var rampMeshes = RampMeshFbx != null ? TileTerrainCliff.GetOrLoadMeshes(RampMeshFbx) : null;

        int cs = ChunkSize;
        int cCols = Mathf.CeilToInt((float)GridData.Width / cs);
        int cRows = Mathf.CeilToInt((float)GridData.Height / cs);

        foreach (var (cx, cz) in chunkCoords)
        {
            if (cx < 0 || cz < 0 || cx >= cCols || cz >= cRows) continue;

            int qxMin = cx * cs;
            int qzMin = cz * cs;
            int qxMax = Mathf.Min(qxMin + cs, GridData.Width);
            int qzMax = Mathf.Min(qzMin + cs, GridData.Height);

            BuildChunkMesh(cx, cz, qxMin, qxMax, qzMin, qzMax, cliffMeshes, cliffDoubleMeshes, cliffTransitionalMeshes, rampMeshes);
        }

        SmoothChunkSeams();

        if (recalcFloor) GridData.RecalculateFloorOffsets();
    }

    // ── Chunk baking ─────────────────────────────────────────────────────────
    /// <summary>
    /// Builds the mesh for one chunk: spawns cliff meshes (single/double/transitional/ramp),
    /// generates the flat terrain quad grid, water pass, collider and combines everything
    /// into a single child GameObject with the shared terrain material.
    /// </summary>
    private void BuildChunkMesh(int cx, int cz, int qxMin, int qxMax, int qzMin, int qzMax,
                                 Dictionary<int, Mesh> cliffMeshes,
                                 Dictionary<int, Mesh> cliffDoubleMeshes,
                                 Dictionary<int, Mesh> cliffTransitionalMeshes,
                                 Dictionary<int, Mesh> rampMeshes)
    {
        _flatQuadIndices.Clear(); _cliffIdx.Clear(); _cliffMeshList.Clear(); _cliffLevels.Clear(); _flatFloorOffsets.Clear();
        _rampFlatIdx.Clear(); _rampFlatOffsets.Clear();
        _materialGroups.Clear();

        // ── Phase 1: Spawn single/double-height cliffs (parity rule) ─────────
        // Even level → prefer double-height; odd level → single-height.
        for (int qz = qzMin; qz < qzMax; qz++)
        for (int qx = qxMin; qx < qxMax; qx++)
        {
            int qi = qz * GridData.Width + qx;
            var q = GridData.Quads[qi];
            int maxV = TileTerrainConstants.NoCliffLevel;
            int minV = TileTerrainConstants.MaxCliffLevel;
            for (int j = 0; j < 4; j++)
            {
                int cb = GridData.Vertices[q.vertexIds[j]].CliffByte;
                if (cb > maxV) maxV = cb;
                if (cb < minV) minV = cb;
            }

            if (cliffMeshes != null)
            {
                for (int level = minV; level < maxV; level++)
                {
                    // Prefer double-height whenever there is room (span ≥ 2)
                    if (level + 2 <= maxV && cliffDoubleMeshes != null)
                    {
                        byte evenMask = TileTerrainBitmask.CalculateCliffMaskAtLevel(GridData, q, level + 1);
                        if (evenMask > 0)
                        {
                            int meshIdx = TileTerrainCliff.CliffMaskToMeshID(evenMask);
                            if (meshIdx >= 0 && cliffDoubleMeshes.TryGetValue(meshIdx, out Mesh cm) && cm != null)
                            {
                                _cliffIdx.Add(qi); _cliffMeshList.Add(cm); _cliffLevels.Add(level);
                                level++; continue;
                            }
                        }
                    }
                    // Fallback: single-height
                    byte stdMask = TileTerrainBitmask.CalculateCliffMaskAtLevel(GridData, q, level);
                    if (stdMask > 0)
                    {
                        int meshIdx = TileTerrainCliff.CliffMaskToMeshID(stdMask);
                        if (meshIdx >= 0 && cliffMeshes.TryGetValue(meshIdx, out Mesh cm) && cm != null)
                        {
                            _cliffIdx.Add(qi); _cliffMeshList.Add(cm); _cliffLevels.Add(level);
                        }
                    }
                }
            }

            byte baseMask = TileTerrainBitmask.CalculateCliffMaskAtLevel(GridData, q, minV);
            if (baseMask == 0) { _flatQuadIndices.Add(qi); _flatFloorOffsets.Add(minV); }
        }

        // ── Phase 2: Replace n=3 quad cliffs with transitional meshes ─────────
        // Transitional tiles use natural vertex order v0,v1,v2,v3
        // (NOT the [v2,v3,v0,v1] remapping used by regular cliff/texture tiles).
        if (cliffTransitionalMeshes != null)
        {
            // Collect n=3 quad indices in this chunk
            var transitionalQis = new HashSet<int>();
            for (int qz = qzMin; qz < qzMax; qz++)
            for (int qx = qxMin; qx < qxMax; qx++)
            {
                int qi = qz * GridData.Width + qx;
                var q  = GridData.Quads[qi];
                if (TileTerrainBitmask.GetUniqueFloorCount(GridData, q) == 3)
                    transitionalQis.Add(qi);
            }

            if (transitionalQis.Count > 0)
            {
                // Remove all Phase-1 entries for those quads (walk backwards to keep indices valid)
                for (int i = _cliffIdx.Count - 1; i >= 0; i--)
                {
                    if (transitionalQis.Contains(_cliffIdx[i]))
                    {
                        _cliffIdx.RemoveAt(i);
                        _cliffMeshList.RemoveAt(i);
                        _cliffLevels.RemoveAt(i);
                    }
                }

                // Add exactly one transitional entry per qualifying quad
                foreach (int qi in transitionalQis)
                {
                    var q    = GridData.Quads[qi];
                    int minV = TileTerrainConstants.MaxCliffLevel;
                    for (int j = 0; j < 4; j++)
                    {
                        int cb = GridData.Vertices[q.vertexIds[j]].CliffByte;
                        if (cb < minV) minV = cb;
                    }
                    int meshIdx = TileTerrainBitmask.GetTransitionalMeshIndex(GridData, q, minV);
                    if (meshIdx >= 0 && cliffTransitionalMeshes.TryGetValue(meshIdx, out Mesh cm) && cm != null)
                    {
                        _cliffIdx.Add(qi); _cliffMeshList.Add(cm); _cliffLevels.Add(minV);
                    }
                }
            }
        }

        // ── Phase 3: Ramp pass — replace cliff meshes with ramp meshes where halfStep exists ──
        for (int qz = qzMin; qz < qzMax; qz++)
        for (int qx = qxMin; qx < qxMax; qx++)
        {
            int qi = qz * GridData.Width + qx;
            var q = GridData.Quads[qi];

            bool hasHalfStep = false;
            for (int j = 0; j < 4 && !hasHalfStep; j++)
                if (GridData.Vertices[q.vertexIds[j]].CliffHalfStep)
                    hasHalfStep = true;
            if (!hasHalfStep) continue;

            int rampId = TileTerrainCliff.ComputeRampMask(GridData, q, qi,
                out float v0, out float v1, out float v2, out float v3);
            Mesh rampMesh = null;
            bool matched = rampId >= 0 && rampMeshes != null
                           && rampMeshes.TryGetValue(rampId, out rampMesh)
                           && rampMesh != null;

            // Remove existing cliff entries at level == floor for this quad
            for (int i = _cliffIdx.Count - 1; i >= 0; i--)
            {
                if (_cliffIdx[i] == qi && _cliffLevels[i] == q.floor)
                {
                    _cliffIdx.RemoveAt(i);
                    _cliffMeshList.RemoveAt(i);
                    _cliffLevels.RemoveAt(i);
                }
            }

            // Also remove from flat quads if it was added there (baseMask == 0)
            for (int i = _flatQuadIndices.Count - 1; i >= 0; i--)
            {
                if (_flatQuadIndices[i] == qi)
                {
                    _flatQuadIndices.RemoveAt(i);
                    _flatFloorOffsets.RemoveAt(i);
                }
            }

            if (matched)
            {
                _cliffIdx.Add(qi);
                _cliffMeshList.Add(rampMesh);
                _cliffLevels.Add(q.floor);
            }
            else
            {
                // No custom mesh — bake effective heights into terrain vertices
                _rampFlatIdx.Add(qi);
                _rampFlatOffsets.Add(new Vector4(
                    TileTerrainCliff.StripRvariant(v0),
                    TileTerrainCliff.StripRvariant(v1),
                    TileTerrainCliff.StripRvariant(v2),
                    TileTerrainCliff.StripRvariant(v3)
                ));
            }
        }

        // ── 1. Terrain Pass (Flat + Cliffs) ───────────────────────────────
        _verts.Clear(); _uvs.Clear(); _uv1s.Clear(); _uv2s.Clear(); _cols.Clear();
        _cliffVerts.Clear(); _cliffUVs.Clear(); _cliffUV1s.Clear(); _cliffUV2s.Clear(); _cliffCols.Clear(); _cliffNormals.Clear();

        for (int fi = 0; fi < _flatQuadIndices.Count; fi++)
        {
            var q = GridData.Quads[_flatQuadIndices[fi]];
            var sv0 = GridData.Vertices[q.vertexIds[0]]; var sv1 = GridData.Vertices[q.vertexIds[1]];
            var sv2 = GridData.Vertices[q.vertexIds[2]]; var sv3 = GridData.Vertices[q.vertexIds[3]];
            float floorOffset = _flatFloorOffsets[fi] * TileTerrainCliff.CliffHeight;
            int baseV = _verts.Count;
            _verts.Add(new Vector3(sv0.position.x, Mathf.Clamp(sv0.height, HeightMin, HeightMax) + floorOffset, sv0.position.z));
            _verts.Add(new Vector3(sv1.position.x, Mathf.Clamp(sv1.height, HeightMin, HeightMax) + floorOffset, sv1.position.z));
            _verts.Add(new Vector3(sv2.position.x, Mathf.Clamp(sv2.height, HeightMin, HeightMax) + floorOffset, sv2.position.z));
            _verts.Add(new Vector3(sv3.position.x, Mathf.Clamp(sv3.height, HeightMin, HeightMax) + floorOffset, sv3.position.z));
            _uvs.Add(new Vector3(UVInset, UVInset, 0)); _uvs.Add(new Vector3(1f-UVInset, UVInset, 0)); _uvs.Add(new Vector3(UVInset, 1f-UVInset, 0)); _uvs.Add(new Vector3(1f-UVInset, 1f-UVInset, 0));
            _uv1s.Add(Vector3.zero); _uv1s.Add(Vector3.zero); _uv1s.Add(Vector3.zero); _uv1s.Add(Vector3.zero);
            float oIdx = q.overLayerId >= 0 ? (q.overTileCol + q.overTileRow * TilemapColumns) : -1f;
            float mIdx = q.midLayerId >= 0 ? (q.midTileCol + q.midTileRow * TilemapColumns) : -1f;
            float uIdx = q.underLayerId >= 0 ? (q.underTileCol + q.underTileRow * TilemapColumns) : -1f;
            Vector3 indices = new Vector3(oIdx, mIdx, uIdx);
            _uv2s.Add(indices); _uv2s.Add(indices); _uv2s.Add(indices); _uv2s.Add(indices);
            _cols.Add(sv0.color); _cols.Add(sv1.color); _cols.Add(sv2.color); _cols.Add(sv3.color);
            Material mat = GetSharedMaterial(q);
            if (!_materialGroups.TryGetValue(mat, out var tl)) _materialGroups[mat] = tl = new List<int>();
            tl.Add(baseV); tl.Add(baseV+2); tl.Add(baseV+1); tl.Add(baseV+1); tl.Add(baseV+2); tl.Add(baseV+3);
        }

        // ── 1b. Ramp-flat pass — quads with halfStep but no custom ramp mesh ──
        for (int fi = 0; fi < _rampFlatIdx.Count; fi++)
        {
            var q = GridData.Quads[_rampFlatIdx[fi]];
            var off = _rampFlatOffsets[fi];
            var sv0 = GridData.Vertices[q.vertexIds[0]]; var sv1 = GridData.Vertices[q.vertexIds[1]];
            var sv2 = GridData.Vertices[q.vertexIds[2]]; var sv3 = GridData.Vertices[q.vertexIds[3]];
            float floorOffset = q.floor * TileTerrainCliff.CliffHeight;
            float ch = TileTerrainCliff.CliffHeight;
            int baseV = _verts.Count;
            _verts.Add(new Vector3(sv0.position.x, Mathf.Clamp(sv0.height, HeightMin, HeightMax) + floorOffset + off.x * ch, sv0.position.z));
            _verts.Add(new Vector3(sv1.position.x, Mathf.Clamp(sv1.height, HeightMin, HeightMax) + floorOffset + off.y * ch, sv1.position.z));
            _verts.Add(new Vector3(sv2.position.x, Mathf.Clamp(sv2.height, HeightMin, HeightMax) + floorOffset + off.z * ch, sv2.position.z));
            _verts.Add(new Vector3(sv3.position.x, Mathf.Clamp(sv3.height, HeightMin, HeightMax) + floorOffset + off.w * ch, sv3.position.z));
            _uvs.Add(new Vector3(UVInset, UVInset, 0)); _uvs.Add(new Vector3(1f-UVInset, UVInset, 0)); _uvs.Add(new Vector3(UVInset, 1f-UVInset, 0)); _uvs.Add(new Vector3(1f-UVInset, 1f-UVInset, 0));
            _uv1s.Add(Vector3.zero); _uv1s.Add(Vector3.zero); _uv1s.Add(Vector3.zero); _uv1s.Add(Vector3.zero);
            float oIdx = q.overLayerId >= 0 ? (q.overTileCol + q.overTileRow * TilemapColumns) : -1f;
            float mIdx = q.midLayerId >= 0 ? (q.midTileCol + q.midTileRow * TilemapColumns) : -1f;
            float uIdx = q.underLayerId >= 0 ? (q.underTileCol + q.underTileRow * TilemapColumns) : -1f;
            Vector3 indices = new Vector3(oIdx, mIdx, uIdx);
            _uv2s.Add(indices); _uv2s.Add(indices); _uv2s.Add(indices); _uv2s.Add(indices);
            _cols.Add(sv0.color); _cols.Add(sv1.color); _cols.Add(sv2.color); _cols.Add(sv3.color);
            Material mat = GetSharedMaterial(q);
            if (!_materialGroups.TryGetValue(mat, out var tl)) _materialGroups[mat] = tl = new List<int>();
            tl.Add(baseV); tl.Add(baseV+2); tl.Add(baseV+1); tl.Add(baseV+1); tl.Add(baseV+2); tl.Add(baseV+3);
        }

        for (int ci = 0; ci < _cliffIdx.Count; ci++)
        {
            var q = GridData.Quads[_cliffIdx[ci]]; Mesh cm = _cliffMeshList[ci]; int level = _cliffLevels[ci];
            var sv0 = GridData.Vertices[q.vertexIds[0]]; var sv1 = GridData.Vertices[q.vertexIds[1]];
            var sv2 = GridData.Vertices[q.vertexIds[2]]; var sv3 = GridData.Vertices[q.vertexIds[3]];
            float cx2 = (sv0.position.x+sv1.position.x+sv2.position.x+sv3.position.x)*0.25f;
            float cz2 = (sv0.position.z+sv1.position.z+sv2.position.z+sv3.position.z)*0.25f;
            int baseVert = _verts.Count + _cliffVerts.Count;
            Vector3[] cmV = cm.vertices; Vector2[] cmU = cm.uv; Vector3[] cmN = cm.normals; int[] cmT = cm.triangles;
            Vector2[] cmU2 = cm.uv2;
            float oIdx = q.overLayerId >= 0 ? (q.overTileCol + q.overTileRow * TilemapColumns) : -1f;
            float mIdx = q.midLayerId >= 0 ? (q.midTileCol + q.midTileRow * TilemapColumns) : -1f;
            float uIdx = q.underLayerId >= 0 ? (q.underTileCol + q.underTileRow * TilemapColumns) : -1f;
            Vector3 indices = new Vector3(oIdx, mIdx, uIdx);
            for (int v = 0; v < cmV.Length; v++)
            {
                float tx = cmV[v].x+0.5f, tz = cmV[v].z+0.5f;
                float hLerp = Mathf.Lerp(Mathf.Lerp(sv0.height,sv1.height,tx),Mathf.Lerp(sv2.height,sv3.height,tx),tz);
                float finalY = Mathf.Clamp(hLerp, HeightMin, HeightMax) + level * TileTerrainCliff.CliffHeight;
                _cliffVerts.Add(new Vector3(cx2+cmV[v].x, finalY+cmV[v].y, cz2+cmV[v].z));
                _cliffUVs.Add(new Vector3(cmU[v].x, cmU[v].y, 0f));
                if (cmU2 != null && cmU2.Length == cmV.Length)
                {
                    _cliffUV1s.Add(new Vector3(cmU2[v].x, cmU2[v].y, 1f));
                }
                else
                {
                    _cliffUV1s.Add(Vector3.zero);
                }
                _cliffUV2s.Add(indices); _cliffCols.Add(Color.white);
                _cliffNormals.Add(cmN!=null&&cmN.Length==cmV.Length?cmN[v]:Vector3.up);
            }
            Material mat = GetSharedMaterial(q);
            if (!_materialGroups.TryGetValue(mat, out var tl)) _materialGroups[mat] = tl = new List<int>();
            for (int t = 0; t < cmT.Length; t++) tl.Add(baseVert+cmT[t]);
        }

        Mesh terrainMesh = null; var terrainMats = new List<Material>();
        if (_verts.Count + _cliffVerts.Count > 0)
        {
            terrainMesh = new Mesh { name = "TerrainMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            int tvc = _verts.Count + _cliffVerts.Count;
            var tv = new Vector3[tvc]; var tu = new Vector3[tvc]; var tu1 = new Vector3[tvc]; var tu2 = new Vector3[tvc]; var tc = new Color[tvc];
            _verts.CopyTo(tv,0); _cliffVerts.CopyTo(tv, _verts.Count);
            _uvs.CopyTo(tu,0); _cliffUVs.CopyTo(tu, _uvs.Count);
            _uv1s.CopyTo(tu1,0); _cliffUV1s.CopyTo(tu1, _uv1s.Count);
            _uv2s.CopyTo(tu2,0); _cliffUV2s.CopyTo(tu2, _uv2s.Count);
            _cols.CopyTo(tc,0); _cliffCols.CopyTo(tc, _cols.Count);
            terrainMesh.vertices = tv; terrainMesh.SetUVs(0, tu); terrainMesh.SetUVs(1, tu1); terrainMesh.SetUVs(2, tu2); terrainMesh.colors = tc;
            terrainMesh.subMeshCount = _materialGroups.Count;
            int si = 0; foreach (var kvp in _materialGroups) { terrainMesh.SetTriangles(kvp.Value, si++); terrainMats.Add(kvp.Key); }
            RecalculateSmoothNormals(terrainMesh);
            terrainMesh.RecalculateTangents();
            terrainMesh.RecalculateBounds();
        }

        // ── Collider mesh (exclude border quads) ──────────────────────────
        Mesh colliderMesh = terrainMesh;
        if (GridData.BorderSize > 0 && terrainMesh != null)
        {
            int b = GridData.BorderSize;
            int tw = GridData.Width;
            int th = GridData.Height;

            var colFlatIdx = new List<int>();
            var colFlatOff = new List<int>();
            for (int i = 0; i < _flatQuadIndices.Count; i++)
            {
                int qi = _flatQuadIndices[i];
                var qd = GridData.Quads[qi];
                if (qd.gridX >= b && qd.gridX < tw - b && qd.gridZ >= b && qd.gridZ < th - b)
                {
                    colFlatIdx.Add(qi);
                    colFlatOff.Add(_flatFloorOffsets[i]);
                }
            }

            var colCliffIdx = new List<int>();
            var colCliffMesh = new List<Mesh>();
            var colCliffLevel = new List<int>();
            for (int i = 0; i < _cliffIdx.Count; i++)
            {
                int qi = _cliffIdx[i];
                var qd = GridData.Quads[qi];
                if (qd.gridX >= b && qd.gridX < tw - b && qd.gridZ >= b && qd.gridZ < th - b)
                {
                    colCliffIdx.Add(qi);
                    colCliffMesh.Add(_cliffMeshList[i]);
                    colCliffLevel.Add(_cliffLevels[i]);
                }
            }

            var colRampIdx = new List<int>();
            var colRampOff = new List<Vector4>();
            for (int i = 0; i < _rampFlatIdx.Count; i++)
            {
                int qi = _rampFlatIdx[i];
                var qd = GridData.Quads[qi];
                if (qd.gridX >= b && qd.gridX < tw - b && qd.gridZ >= b && qd.gridZ < th - b)
                {
                    colRampIdx.Add(qi);
                    colRampOff.Add(_rampFlatOffsets[i]);
                }
            }

            colliderMesh = BuildColliderMeshFromLists(colFlatIdx, colFlatOff, colCliffIdx, colCliffMesh, colCliffLevel, colRampIdx, colRampOff);
        }

        // ── 2. Water Pass (fully merged vertices) ────────────────────────
        _verts.Clear(); _uvs.Clear(); _uv2s.Clear(); _cols.Clear(); _materialGroups.Clear();
        if (WaterMaterial != null)
        {
            int vxMax = qxMax + (qxMax == GridData.Width ? 1 : 0);
            int vzMax = qzMax + (qzMax == GridData.Height ? 1 : 0);

            // Unified vertex map: key = (RoundToInt(worldX * 2), RoundToInt(worldZ * 2))
            // This merges ALL water verts at the same XZ — including fill-patch centres
            // that coincide with main-tile shared corners — giving RecalculateNormals
            // consistent data across the whole mesh.
            var waterVertMap = new Dictionary<(int, int), int>();

            int GetOrCreateWaterVert(float wx, float wY, float wz)
            {
                int kx = Mathf.RoundToInt(wx * 2);
                int kz = Mathf.RoundToInt(wz * 2);
                if (waterVertMap.TryGetValue((kx, kz), out int existingIdx)) return existingIdx;
                int newIdx = _verts.Count;
                waterVertMap[(kx, kz)] = newIdx;
                _verts.Add(new Vector3(wx, wY, wz));
                _uvs.Add(new Vector3(wx, wz, 0));
                _uv2s.Add(Vector3.zero);
                _cols.Add(Color.white);
                return newIdx;
            }

            if (!_materialGroups.TryGetValue(WaterMaterial, out var wl))
                _materialGroups[WaterMaterial] = wl = new List<int>();

            // Pass 1 — one 1x1 tile per water vertex, subdivided into four 0.5x0.5 quads
            // to eliminate T-junctions with the 3-water-corner fill patches.
            for (int vz = qzMin; vz < vzMax; vz++)
            for (int vx = qxMin; vx < vxMax; vx++)
            {
                int vi = vz * (GridData.Width + 1) + vx;
                var v = GridData.Vertices[vi];
                if (!v.IsWater) continue;

                float wY = (v.WaterLevel - WaterOffset) * TileTerrainCliff.CliffHeight;
                Vector3 p = v.position;

                // 9 vertices for the four 0.5x0.5 quads
                int v_bl = GetOrCreateWaterVert(p.x - 0.5f, wY, p.z - 0.5f);
                int v_bm = GetOrCreateWaterVert(p.x,        wY, p.z - 0.5f);
                int v_br = GetOrCreateWaterVert(p.x + 0.5f, wY, p.z - 0.5f);

                int v_ml = GetOrCreateWaterVert(p.x - 0.5f, wY, p.z);
                int v_mm = GetOrCreateWaterVert(p.x,        wY, p.z);
                int v_mr = GetOrCreateWaterVert(p.x + 0.5f, wY, p.z);

                int v_tl = GetOrCreateWaterVert(p.x - 0.5f, wY, p.z + 0.5f);
                int v_tm = GetOrCreateWaterVert(p.x,        wY, p.z + 0.5f);
                int v_tr = GetOrCreateWaterVert(p.x + 0.5f, wY, p.z + 0.5f);

                // Bottom-Left Quad
                wl.Add(v_bl); wl.Add(v_ml); wl.Add(v_bm);
                wl.Add(v_bm); wl.Add(v_ml); wl.Add(v_mm);

                // Bottom-Right Quad
                wl.Add(v_bm); wl.Add(v_mm); wl.Add(v_br);
                wl.Add(v_br); wl.Add(v_mm); wl.Add(v_mr);

                // Top-Left Quad
                wl.Add(v_ml); wl.Add(v_tl); wl.Add(v_mm);
                wl.Add(v_mm); wl.Add(v_tl); wl.Add(v_tm);

                // Top-Right Quad
                wl.Add(v_mm); wl.Add(v_tm); wl.Add(v_mr);
                wl.Add(v_mr); wl.Add(v_tm); wl.Add(v_tr);
            }

            // Pass 2 — 3-water-corner fill patches, also using waterVertMap so pM is merged
            for (int qz = qzMin; qz < qzMax; qz++)
            for (int qx = qxMin; qx < qxMax; qx++)
            {
                var q = GridData.Quads[qz * GridData.Width + qx];
                var sv0 = GridData.Vertices[q.vertexIds[0]];
                var sv1 = GridData.Vertices[q.vertexIds[1]];
                var sv2 = GridData.Vertices[q.vertexIds[2]];
                var sv3 = GridData.Vertices[q.vertexIds[3]];
                int wCount = (sv0.IsWater ? 1 : 0) + (sv1.IsWater ? 1 : 0) + (sv2.IsWater ? 1 : 0) + (sv3.IsWater ? 1 : 0);
                if (wCount != 3) continue;

                int mwl = Mathf.Max(
                    sv0.IsWater ? sv0.WaterLevel : TileTerrainConstants.NoCliffLevel,
                    sv1.IsWater ? sv1.WaterLevel : TileTerrainConstants.NoCliffLevel,
                    sv2.IsWater ? sv2.WaterLevel : TileTerrainConstants.NoCliffLevel,
                    sv3.IsWater ? sv3.WaterLevel : TileTerrainConstants.NoCliffLevel);
                float wY = (mwl - WaterOffset) * TileTerrainCliff.CliffHeight;
                Vector3 p0 = sv0.position;
                Vector3 pM = (p0 + sv3.position) * 0.5f; pM.y = wY;

                Vector3 pA, pB;
                if (!sv3.IsWater)
                {
                    pA = new Vector3(p0.x + 0.5f, wY, p0.z + 1f);
                    pB = new Vector3(p0.x + 1f, wY, p0.z + 0.5f);
                }
                else if (!sv0.IsWater)
                {
                    pA = new Vector3(p0.x + 0.5f, wY, p0.z);
                    pB = new Vector3(p0.x, wY, p0.z + 0.5f);
                }
                else if (!sv2.IsWater)
                {
                    pA = new Vector3(p0.x, wY, p0.z + 0.5f);
                    pB = new Vector3(p0.x + 0.5f, wY, p0.z + 1f);
                }
                else
                {
                    pA = new Vector3(p0.x + 1f, wY, p0.z + 0.5f);
                    pB = new Vector3(p0.x + 0.5f, wY, p0.z);
                }

                // All 3 patch verts go through the same map — pM always merges with
                // the shared corner already created by adjacent main-water tiles.
                int vm = GetOrCreateWaterVert(pM.x, wY, pM.z);
                int va = GetOrCreateWaterVert(pA.x, wY, pA.z);
                int vb = GetOrCreateWaterVert(pB.x, wY, pB.z);
                wl.Add(vm); wl.Add(va); wl.Add(vb);
            }
        }

        Mesh waterMesh = null; var waterMats = new List<Material>();
        if (_verts.Count > 0)
        {
            waterMesh = new Mesh { name = "WaterMesh" };
            waterMesh.vertices = _verts.ToArray(); waterMesh.SetUVs(0, _uvs); waterMesh.SetUVs(2, _uv2s); waterMesh.colors = _cols.ToArray();
            waterMesh.subMeshCount = _materialGroups.Count;
            int si = 0; foreach (var kvp in _materialGroups) { waterMesh.SetTriangles(kvp.Value, si++); waterMats.Add(kvp.Key); }
            var wn = new Vector3[_verts.Count]; for (int i = 0; i < wn.Length; i++) wn[i] = Vector3.up; waterMesh.normals = wn;
            var wt = new Vector4[_verts.Count]; for (int i = 0; i < wt.Length; i++) wt[i] = new Vector4(1, 0, 0, 1); waterMesh.tangents = wt;
            waterMesh.RecalculateBounds();
        }

        var chunks = GetOrCreateChunk(cx, cz);
        // Terrain GO
        var tMf = chunks.terrain.GetComponent<MeshFilter>(); if (tMf == null) tMf = chunks.terrain.AddComponent<MeshFilter>();
        tMf.sharedMesh = terrainMesh;
        var tMr = chunks.terrain.GetComponent<MeshRenderer>(); if (tMr == null) tMr = chunks.terrain.AddComponent<MeshRenderer>();
        tMr.sharedMaterials = terrainMats.ToArray();
        var tMc = chunks.terrain.GetComponent<MeshCollider>(); if (tMc == null) tMc = chunks.terrain.AddComponent<MeshCollider>();
        tMc.sharedMesh = colliderMesh;

        // Water GO
        var wMf = chunks.water.GetComponent<MeshFilter>(); if (wMf == null) wMf = chunks.water.AddComponent<MeshFilter>();
        wMf.sharedMesh = waterMesh;
        var wMr = chunks.water.GetComponent<MeshRenderer>(); if (wMr == null) wMr = chunks.water.AddComponent<MeshRenderer>();
        wMr.sharedMaterials = waterMats.ToArray();
    }

    private Mesh BuildColliderMeshFromLists(
        List<int> flatIdx, List<int> flatFloorOffsets,
        List<int> cliffIdx, List<Mesh> cliffMeshList, List<int> cliffLevels,
        List<int> rampFlatIdx, List<Vector4> rampFlatOffsets)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();

        var clampedHeights = new float[4];

        // Flat quads
        for (int fi = 0; fi < flatIdx.Count; fi++)
        {
            var q = GridData.Quads[flatIdx[fi]];
            float floorOffset = flatFloorOffsets[fi] * TileTerrainCliff.CliffHeight;
            int baseV = verts.Count;
            for (int j = 0; j < 4; j++)
            {
                var sv = GridData.Vertices[q.vertexIds[j]];
                clampedHeights[j] = Mathf.Clamp(sv.height, HeightMin, HeightMax) + floorOffset;
            }
            verts.Add(new Vector3(GridData.Vertices[q.vertexIds[0]].position.x, clampedHeights[0], GridData.Vertices[q.vertexIds[0]].position.z));
            verts.Add(new Vector3(GridData.Vertices[q.vertexIds[1]].position.x, clampedHeights[1], GridData.Vertices[q.vertexIds[1]].position.z));
            verts.Add(new Vector3(GridData.Vertices[q.vertexIds[2]].position.x, clampedHeights[2], GridData.Vertices[q.vertexIds[2]].position.z));
            verts.Add(new Vector3(GridData.Vertices[q.vertexIds[3]].position.x, clampedHeights[3], GridData.Vertices[q.vertexIds[3]].position.z));
            tris.Add(baseV); tris.Add(baseV + 2); tris.Add(baseV + 1);
            tris.Add(baseV + 1); tris.Add(baseV + 2); tris.Add(baseV + 3);
        }

        // Ramp-flat quads
        for (int fi = 0; fi < rampFlatIdx.Count; fi++)
        {
            var q = GridData.Quads[rampFlatIdx[fi]];
            var off = rampFlatOffsets[fi];
            float floorOffset = q.floor * TileTerrainCliff.CliffHeight;
            float ch = TileTerrainCliff.CliffHeight;
            int baseV = verts.Count;
            for (int j = 0; j < 4; j++)
            {
                var sv = GridData.Vertices[q.vertexIds[j]];
                clampedHeights[j] = Mathf.Clamp(sv.height, HeightMin, HeightMax) + floorOffset + off[j] * ch;
            }
            verts.Add(new Vector3(GridData.Vertices[q.vertexIds[0]].position.x, clampedHeights[0], GridData.Vertices[q.vertexIds[0]].position.z));
            verts.Add(new Vector3(GridData.Vertices[q.vertexIds[1]].position.x, clampedHeights[1], GridData.Vertices[q.vertexIds[1]].position.z));
            verts.Add(new Vector3(GridData.Vertices[q.vertexIds[2]].position.x, clampedHeights[2], GridData.Vertices[q.vertexIds[2]].position.z));
            verts.Add(new Vector3(GridData.Vertices[q.vertexIds[3]].position.x, clampedHeights[3], GridData.Vertices[q.vertexIds[3]].position.z));
            tris.Add(baseV); tris.Add(baseV + 2); tris.Add(baseV + 1);
            tris.Add(baseV + 1); tris.Add(baseV + 2); tris.Add(baseV + 3);
        }

        // Cliff quads
        for (int ci = 0; ci < cliffIdx.Count; ci++)
        {
            var q = GridData.Quads[cliffIdx[ci]];
            Mesh cm = cliffMeshList[ci];
            int level = cliffLevels[ci];
            var sv0 = GridData.Vertices[q.vertexIds[0]];
            var sv1 = GridData.Vertices[q.vertexIds[1]];
            var sv2 = GridData.Vertices[q.vertexIds[2]];
            var sv3 = GridData.Vertices[q.vertexIds[3]];
            float cx2 = (sv0.position.x + sv1.position.x + sv2.position.x + sv3.position.x) * 0.25f;
            float cz2 = (sv0.position.z + sv1.position.z + sv2.position.z + sv3.position.z) * 0.25f;
            int baseV = verts.Count;
            Vector3[] cmV = cm.vertices;
            int[] cmT = cm.triangles;
            for (int v = 0; v < cmV.Length; v++)
            {
                float tx = cmV[v].x + 0.5f;
                float tz = cmV[v].z + 0.5f;
                float hLerp = Mathf.Lerp(Mathf.Lerp(sv0.height, sv1.height, tx), Mathf.Lerp(sv2.height, sv3.height, tx), tz);
                float finalY = Mathf.Clamp(hLerp, HeightMin, HeightMax) + level * TileTerrainCliff.CliffHeight;
                verts.Add(new Vector3(cx2 + cmV[v].x, finalY + cmV[v].y, cz2 + cmV[v].z));
            }
            for (int t = 0; t < cmT.Length; t++)
                tris.Add(baseV + cmT[t]);
        }

        Mesh mesh = null;
        if (verts.Count > 0)
        {
            mesh = new Mesh { name = "ColliderMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
        return mesh;
    }

    private Material GetSharedMaterial(QuadData q)
    {
        // Batch materials by texture array trio only. Indices are handled via UV2.
        int key = ((q.overLayerId + 1) << 20) | ((q.midLayerId + 1) << 10) | (q.underLayerId + 1);
        if (_materialCache.TryGetValue(key, out Material mat) && mat != null) return mat;

        Material newMat = new Material(TileMaterial) { name = $"TileMat_{q.overLayerId}_{q.midLayerId}_{q.underLayerId}" };
        if (q.overLayerId >= 0 && q.overLayerId < RegisteredTextures.Count && RegisteredTextures[q.overLayerId] != null)
            newMat.SetTexture("_Texture_Over", RegisteredTextures[q.overLayerId]);
        
        if (q.midLayerId >= 0 && q.midLayerId < RegisteredTextures.Count && RegisteredTextures[q.midLayerId] != null)
            newMat.SetTexture("_Texture_Mid", RegisteredTextures[q.midLayerId]);

        if (q.underLayerId >= 0 && q.underLayerId < RegisteredTextures.Count && RegisteredTextures[q.underLayerId] != null)
            newMat.SetTexture("_Texture_Under", RegisteredTextures[q.underLayerId]);

        if (Palette != null && Palette.CliffTexture != null)
            newMat.SetTexture("_CliffSideTex", Palette.CliffTexture);

        _materialCache[key] = newMat;
        return newMat;
    }

    /// <summary>
    /// Destroys all cached materials so they are re-created on the next bake.
    /// </summary>
    public void ClearMaterials()
    {
        foreach (var m in _materialCache.Values) if (m != null) DestroyImmediate(m);
        _materialCache.Clear();
    }

    private void RecalculateSmoothNormals(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = new Vector3[vertices.Length];
        var positionGroups = new Dictionary<(int, int, int), List<int>>();
        const float epsilon = 0.001f;

        for (int i = 0; i < vertices.Length; i++)
        {
            var p = vertices[i];
            var key = (Mathf.RoundToInt(p.x / epsilon), Mathf.RoundToInt(p.y / epsilon), Mathf.RoundToInt(p.z / epsilon));
            if (!positionGroups.TryGetValue(key, out var list))
            {
                list = new List<int>();
                positionGroups[key] = list;
            }
            list.Add(i);
        }

        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            int[] triangles = mesh.GetTriangles(submesh);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int i0 = triangles[t];
                int i1 = triangles[t + 1];
                int i2 = triangles[t + 2];

                Vector3 v0 = vertices[i0];
                Vector3 v1 = vertices[i1];
                Vector3 v2 = vertices[i2];

                Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0);

                normals[i0] += faceNormal;
                normals[i1] += faceNormal;
                normals[i2] += faceNormal;
            }
        }

        var averagedNormals = new Vector3[vertices.Length];
        foreach (var group in positionGroups.Values)
        {
            Vector3 sum = Vector3.zero;
            foreach (int index in group)
            {
                sum += normals[index];
            }

            Vector3 avgNormal = sum.normalized;
            if (avgNormal == Vector3.zero) avgNormal = Vector3.up;

            foreach (int index in group)
            {
                averagedNormals[index] = avgNormal;
            }
        }

        mesh.normals = averagedNormals;
    }

    private void SmoothChunkSeams()
    {
        const float eps = 0.001f;
        var posMap = new Dictionary<(int, int, int), List<(Mesh mesh, int vi)>>();

        if (_chunkCache == null) return;

        foreach (var kvp in _chunkCache)
        {
            var mf = kvp.Value.terrain.GetComponent<MeshFilter>();
            if (mf == null) continue;
            Mesh mesh = mf.sharedMesh;
            if (mesh == null || mesh.vertexCount == 0) continue;

            Vector3[] verts = mesh.vertices;
            for (int vi = 0; vi < verts.Length; vi++)
            {
                var p = verts[vi];
                var key = (Mathf.RoundToInt(p.x / eps), Mathf.RoundToInt(p.y / eps), Mathf.RoundToInt(p.z / eps));
                if (!posMap.TryGetValue(key, out var list))
                    posMap[key] = list = new List<(Mesh, int)>();
                list.Add((mesh, vi));
            }
        }

        var cachedNorms = new Dictionary<Mesh, Vector3[]>();
        var meshWrites = new Dictionary<Mesh, Dictionary<int, Vector3>>();

        foreach (var kvp in posMap)
        {
            var entries = kvp.Value;

            var meshSet = new HashSet<Mesh>();
            foreach (var (m, _) in entries) meshSet.Add(m);
            if (meshSet.Count < 2) continue;

            Vector3 sum = Vector3.zero;
            foreach (var (m, vi) in entries)
            {
                if (!cachedNorms.TryGetValue(m, out var norms))
                {
                    norms = m.normals;
                    cachedNorms[m] = norms;
                }
                if (norms != null && vi < norms.Length)
                    sum += norms[vi];
            }

            Vector3 avg = sum.normalized;
            if (avg == Vector3.zero) avg = Vector3.up;

            foreach (var (m, vi) in entries)
            {
                if (!meshWrites.TryGetValue(m, out var mods))
                    meshWrites[m] = mods = new Dictionary<int, Vector3>();
                mods[vi] = avg;
            }
        }

        foreach (var kvp in meshWrites)
        {
            Mesh mesh = kvp.Key;
            var mods = kvp.Value;
            if (!cachedNorms.TryGetValue(mesh, out var norms))
                norms = mesh.normals;
            foreach (var modKvp in mods)
                if (modKvp.Key < norms.Length)
                    norms[modKvp.Key] = modKvp.Value;
            mesh.normals = norms;
        }
    }
#endif // UNITY_EDITOR
}
}
