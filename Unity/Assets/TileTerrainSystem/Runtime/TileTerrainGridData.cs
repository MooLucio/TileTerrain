using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MooLucio.TileTerrain
{
    [CreateAssetMenu(menuName = "Tiled terrain/Tile Terrain Grid", fileName = "TileTerrainGrid")]
    public class TileTerrainGridData : ScriptableObject
    {
        [Tooltip("Number of quads along the X axis, excluding border (total vertex columns = InternalWidth + 1 + BorderSize*2).")]
        [FormerlySerializedAs("Width")] public int InternalWidth = 64;
        [Tooltip("Number of quads along the Z axis, excluding border (total vertex rows = InternalHeight + 1 + BorderSize*2).")]
        [FormerlySerializedAs("Height")] public int InternalHeight = 64;
        [Tooltip("Number of border cells on each side. Border cells render visually but have no collider. " +
                 "Total grid dimensions are automatically extended by BorderSize\u00d72 on each axis.")]
        public int BorderSize = 0;

        /// <summary>Total grid width including border on both sides.</summary>
        public int Width => InternalWidth + BorderSize * 2;
        /// <summary>Total grid height including border on both sides.</summary>
        public int Height => InternalHeight + BorderSize * 2;
        /// <summary>
        /// One-shot migration flag for old assets that used the subtractive border model
        /// (Width/Height already included border). After the first save this stays true.
        /// </summary>
        [SerializeField] private bool _migratedFromSubtractive;
        [Tooltip("All vertex data for the total grid ((Width+1) × (Height+1)).")]
        public List<VertexData> Vertices = new List<VertexData>();
        [Tooltip("All quad data for the grid (Width × Height).")]
        public List<QuadData> Quads = new List<QuadData>();
        [Tooltip("Pre-calculated Y-offset per vertex from cliff floor accumulation.")]
        [FormerlySerializedAs("vertexFloorOffset")]
        public float[] VertexFloorOffset;

        [Tooltip("Placed props (decorative objects) on the terrain.")]
        public List<PropInstance> Props = new List<PropInstance>();
        [Tooltip("Entanglement groups — each group is a set of vertices synced together by a prop footprint.")]
        [FormerlySerializedAs("entanglementGroups")]
        public List<EntanglementGroup> EntanglementGroups = new List<EntanglementGroup>();
        [Tooltip("Incremented each time a new entanglement group is created, used to generate unique group ids.")]
        [FormerlySerializedAs("nextEntanglementId")]
        public int NextEntanglementId = 0;
        /// <summary>
        /// Bumped on any structural change (RegenerateGrid, RecalculateFloorOffsets).
        /// Consumers (e.g. FogOfWarManager) cache this to know when to refresh derived data.
        /// </summary>
        [System.NonSerialized] public int Version;

        private void OnValidate()
        {
            if (!_migratedFromSubtractive && BorderSize > 0)
            {
                InternalWidth = Mathf.Max(1, InternalWidth - BorderSize * 2);
                InternalHeight = Mathf.Max(1, InternalHeight - BorderSize * 2);
                _migratedFromSubtractive = true;
            }
            if (InternalWidth < 1) InternalWidth = 1;
            if (InternalHeight < 1) InternalHeight = 1;
            if (BorderSize < 0) BorderSize = 0;
            EnsureGridData();
        }

                /// <summary>
        /// Ensures the vertex/quad/prop/entanglement lists exist and match the grid size,
        /// regenerating the grid from scratch if the counts are off.
        /// </summary>
        public void EnsureGridData()
        {
            if (Vertices == null)
                Vertices = new List<VertexData>();
            if (Quads == null)
                Quads = new List<QuadData>();
            if (Props == null)
                Props = new List<PropInstance>();
            if (EntanglementGroups == null)
                EntanglementGroups = new List<EntanglementGroup>();

            int targetVertexCount = (Width + 1) * (Height + 1);
            int targetQuadCount = Width * Height;

            if (Vertices.Count != targetVertexCount || Quads.Count != targetQuadCount)
            {
                RegenerateGrid();
            }
        }

                /// <summary>
        /// Rebuilds all vertices and quads from the current Width/Height and recomputes
        /// floor offsets. Clears entanglement groups since their vertex indices go stale.
        /// </summary>
        public void RegenerateGrid()
        {
            Version++;
            Vertices = new List<VertexData>( (Width + 1) * (Height + 1) );
            Quads = new List<QuadData>( Width * Height );

            // Clear entanglement groups — stale vertex references after grid rebuild
            EntanglementGroups.Clear();
            for (int i = 0; i < Props.Count; i++)
            {
                var a = Props[i];
                a.entanglementId = -1;
                Props[i] = a;
            }

            for (int z = 0; z <= Height; z++)
            {
                for (int x = 0; x <= Width; x++)
                {
                    var vertex = new VertexData
                    {
                        id = Vertices.Count,
                        position = new Vector3(x - Width / 2f, 0, z - Height / 2f),
                        height = 0f,
                        color = Color.white,
                        uv = new Vector2(x, z),
                        overTextureId = -1,
                        midTextureId  = -1,
                        underTextureId = -1
                    };

                    Vertices.Add(vertex);
                }
            }

            for (int z = 0; z < Height; z++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int v0 = z * (Width + 1) + x;
                    int v1 = v0 + 1;
                    int v2 = v0 + (Width + 1);
                    int v3 = v2 + 1;

                    var quad = new QuadData
                    {
                        id = Quads.Count,
                        vertexIds = new int[] { v0, v1, v2, v3 },
                        zBuffer = 0f,
                        overlayColor = Color.white,
                        gridX = x,
                        gridZ = z
                    };

                    Quads.Add(quad);
                }
            }
            RecalculateFloorOffsets();
        }

                /// <summary>
        /// Bilinear-interpolated vertex height at a local-space position.
        /// </summary>
        public float GetHeightAt(Vector3 localPos)
        {
            if (Vertices == null || Vertices.Count == 0) return 0f;

            int w = Width;
            int h = Height;
            int row = w + 1;
            int totalExpected = row * (h + 1);

            if (Vertices.Count < totalExpected) return 0f;

            float gx = localPos.x + w / 2f;
            float gz = localPos.z + h / 2f;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, w);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(gz), 0, h);
            int x1 = Mathf.Min(x0 + 1, w);
            int z1 = Mathf.Min(z0 + 1, h);

            float tx = Mathf.Clamp01(gx - x0);
            float tz = Mathf.Clamp01(gz - z0);

            // Double check indices before access
            int i00 = z0 * row + x0;
            int i10 = z0 * row + x1;
            int i01 = z1 * row + x0;
            int i11 = z1 * row + x1;

            if (i11 >= Vertices.Count) return 0f;

            float h00 = Vertices[i00].height;
            float h10 = Vertices[i10].height;
            float h01 = Vertices[i01].height;
            float h11 = Vertices[i11].height;

            return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
        }
                /// <summary>
        /// True if the quad lies in the border (decorative) region defined by BorderSize.
        /// </summary>
        public bool IsBorderQuad(int gridX, int gridZ)
        {
            return BorderSize > 0 && (gridX < BorderSize || gridX >= Width - BorderSize || gridZ < BorderSize || gridZ >= Height - BorderSize);
        }

                /// <summary>
        /// True if the vertex lies in the border (decorative) region defined by BorderSize.
        /// </summary>
        public bool IsBorderVertex(int gridX, int gridZ)
        {
            return BorderSize > 0 && (gridX < BorderSize || gridX > Width - BorderSize || gridZ < BorderSize || gridZ > Height - BorderSize);
        }

                /// <summary>
        /// Clears texture references pointing beyond the terrain's registered textures,
        /// used after the texture palette changes so stale ids never leak into baking.
        /// </summary>
        public void Sanitize(TileTerrain terrain)
        {
            int texCount = terrain.RegisteredTextures != null ? terrain.RegisteredTextures.Count : 0;
            for (int i = 0; i < Vertices.Count; i++)
            {
                var v = Vertices[i];
                if (v.overTextureId >= texCount) v.overTextureId = -1;
                if (v.midTextureId >= texCount) v.midTextureId = -1;
                if (v.underTextureId >= texCount) v.underTextureId = -1;
                Vertices[i] = v;
            }
            for (int i = 0; i < Quads.Count; i++)
            {
                var q = Quads[i];
                if (q.overLayerId >= texCount) q.overLayerId = -1;
                if (q.midLayerId >= texCount) q.midLayerId = -1;
                if (q.underLayerId >= texCount) q.underLayerId = -1;
                Quads[i] = q;
            }
        }

                /// <summary>
        /// Creates a new entanglement group, tagging every vertex with its id.
        /// Entangled vertices stay synced when the owning prop is moved.
        /// </summary>
        public int CreateEntanglementGroup(int propIndex, List<int> vertexIds)
        {
            int id = NextEntanglementId++;
            var group = new EntanglementGroup
            {
                id = id,
                propIndex = propIndex,
                vertexIds = new List<int>(vertexIds)
            };
            EntanglementGroups.Add(group);
            for (int i = 0; i < vertexIds.Count; i++)
            {
                var v = Vertices[vertexIds[i]];
                v.EntanglementGroupId = id;
                Vertices[vertexIds[i]] = v;
            }
            return id;
        }

                /// <summary>
        /// Removes an entanglement group and clears the tag on all of its vertices.
        /// </summary>
        public void RemoveEntanglementGroup(int groupId)
        {
            for (int i = EntanglementGroups.Count - 1; i >= 0; i--)
            {
                if (EntanglementGroups[i].id == groupId)
                {
                    var group = EntanglementGroups[i];
                    for (int j = 0; j < group.vertexIds.Count; j++)
                    {
                        var v = Vertices[group.vertexIds[j]];
                        v.EntanglementGroupId = -1;
                        Vertices[group.vertexIds[j]] = v;
                    }
                    EntanglementGroups.RemoveAt(i);
                    break;
                }
            }
        }

                /// <summary>
        /// Returns the entanglement group with the given id, or default if none.
        /// </summary>
        public EntanglementGroup GetEntanglementGroup(int groupId)
        {
            for (int i = 0; i < EntanglementGroups.Count; i++)
                if (EntanglementGroups[i].id == groupId)
                    return EntanglementGroups[i];
            return default;
        }

        /// <summary>
        /// Recomputes the per-vertex Y offset accumulated from the cliff tiers that cover
        /// each vertex, and bumps Version so consumers (e.g. FogOfWarManager) refresh.
        /// </summary>
        public void RecalculateFloorOffsets()
        {
            int w = Width;
            int h = Height;
            int row = w + 1;
            if (VertexFloorOffset == null || VertexFloorOffset.Length != row * (h + 1))
                VertexFloorOffset = new float[row * (h + 1)];

            for (int vz = 0; vz <= h; vz++)
            {
                for (int vx = 0; vx <= w; vx++)
                {
                    float maxOffset = TileTerrainConstants.NoFloorOffset;
                    bool anyQuad = false;
                    for (int qz = vz - 1; qz <= vz; qz++)
                    {
                        for (int qx = vx - 1; qx <= vx; qx++)
                        {
                            if (qx < 0 || qz < 0 || qx >= w || qz >= h) continue;
                            var quad = Quads[qz * w + qx];
                            anyQuad = true;
                            int cornerIdx = -1;
                            if (qx == vx && qz == vz) cornerIdx = 0;
                            else if (qx == vx - 1 && qz == vz) cornerIdx = 1;
                            else if (qx == vx && qz == vz - 1) cornerIdx = 2;
                            else if (qx == vx - 1 && qz == vz - 1) cornerIdx = 3;
                            float floorAccum = quad.floor;
                            int maxV = TileTerrainConstants.NoCliffLevel;
                            for (int j = 0; j < 4; j++)
                                if (Vertices[quad.vertexIds[j]].CliffByte > maxV)
                                    maxV = Vertices[quad.vertexIds[j]].CliffByte;
                            for (int level = quad.floor; level < maxV; level++)
                            {
                                byte mask = TileTerrainBitmask.CalculateCliffMaskAtLevel(this, quad, level);
                                if (cornerIdx != -1 && (mask & (1 << cornerIdx)) != 0)
                                    floorAccum += 1f;
                            }
                            maxOffset = Mathf.Max(maxOffset, floorAccum * TileTerrainCliff.CliffHeight);
                        }
                    }
                    VertexFloorOffset[vz * row + vx] = anyQuad ? maxOffset : 0f;
                }
            }
            Version++;
        }
    }

[System.Serializable]
public class VertexData
{
    public int id;
    public Vector3 position;
    public Color color = Color.white;
    public Vector2 uv;
    public float height;

    // Corner-based auto-tiling data
    public int overTextureId   = -1;
    public int midTextureId    = -1;
    public int underTextureId  = -1;
    public byte overMask;
    public byte midMask;
    public byte underMask;

    // Cliff layer — stores the floor level at this vertex (TileTerrainConstants.NoCliffLevel to TileTerrainConstants.MaxCliffLevel)
    [FormerlySerializedAs("cliffByte")]
    public sbyte CliffByte;
    // Ramp layer — when true, effective height is CliffByte + 0.5
    [FormerlySerializedAs("cliffHalfStep")]
    public bool CliffHalfStep;

    // Water layer
    [FormerlySerializedAs("isWater")]
    public bool IsWater;
    [FormerlySerializedAs("waterLevel")]
    public sbyte WaterLevel = 0;

    // Entanglement: which group this vertex belongs to (-1 = none)
    [FormerlySerializedAs("entanglementGroupId")]
    public int EntanglementGroupId = -1;
}

[System.Serializable]
public class QuadData
{
    public int id;
    public int[] vertexIds = new int[4];
    public float zBuffer;
    public Color overlayColor = Color.white;

    // Auto-tile texture layers
    public int overLayerId  = -1;   // index into TileTerrain.RegisteredTextures (-1 = none)
    public int midLayerId   = -1;
    public int underLayerId = -1;
    public int overTileCol;         // 0–7, column in tilemap
    public int overTileRow;         // 0–3, row in tilemap
    public int midTileCol;
    public int midTileRow;
    public int underTileCol;
    public int underTileRow;

    // Grid coordinates (set when quad is created, used for bitmask)
    public int gridX;
    public int gridZ;

    // Cliff floor level — increments each time all 4 corners become cliff
    public int floor;
}

[System.Serializable]
public struct EntanglementGroup
{
    public int id;
    public int propIndex; // index into TileTerrainGridData.Props, -1 if orphaned
    public List<int> vertexIds; // all unique vertex indices in this group
}

[System.Serializable]
public struct PropInstance
{
    public int propIndex;
    public int variant;
    public Vector3 position;
    public float rotationY;
    public float scale;
    public bool snapToGrid;
    public bool pinnedToGround;
    public float footprintRadius;
    public int entanglementId; // -1 = none
}
}
