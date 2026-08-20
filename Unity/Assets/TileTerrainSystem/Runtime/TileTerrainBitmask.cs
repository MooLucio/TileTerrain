using System.Collections.Generic;
using UnityEngine;

namespace MooLucio.TileTerrain
{
    /// <summary>
    /// Calculates 4-bit vertex bitmasks for auto-tile texture selection and
    /// cliff mesh assignment. Maps bitmasks to tilemap column/row coordinates.
    ///
    /// Tilemap layout (8 columns × 4 rows, each cell 64×64, sheet 512×256):
    ///   Columns 0–3 (x=0–192)  : connector / corner tiles (bitmask-driven)
    ///   Columns 4–7 (x=256–448): random center tiles
    ///
    /// Mask → column/row table:
    ///   Mask  Col  Row
    ///    0  → random center (cols 4–7)
    ///    1  →  2    3
    ///    2  →  1    3
    ///    3  →  3    3
    ///    4  →  0    1
    ///    5  →  2    1
    ///    6  →  1    1
    ///    7  →  3    1
    ///    8  →  0    2
    ///    9  →  2    2
    ///   10  →  1    2
    ///   11  →  3    2
    ///   12  →  0    0
    ///   13  →  2    0
    ///   14  →  1    0
    ///   15  → random center (cols 4–7)
    /// </summary>
    public static class TileTerrainBitmask
    {
        // Set by TileTerrainEditor's Texture tool to control center-tile randomization.
        public static float TextureRandomness = 0.4f;

        // mask (0–15) → tilemap column (0–3 = connectors, -1 = random center 4–7)
        private static readonly int[] MaskToCol = new int[16]
        {
            -1, // 0  → isolated → random center
             2, // 1  (N)
             1, // 2  (E)
             3, // 3  (N+E)
             0, // 4  (S)
             2, // 5  (N+S)
             1, // 6  (E+S)
             3, // 7  (N+E+S)
             0, // 8  (W)
             2, // 9  (N+W)
             1, // 10 (E+W)
             3, // 11 (N+E+W)
             0, // 12 (S+W)
             2, // 13 (N+S+W)
             1, // 14 (E+S+W)
             -1, // 15 → fully surrounded → random center
        };

        // mask (0–15) → tilemap row (0=y=0, 3=y=192)
        private static readonly int[] MaskToRow = new int[16]
        {
            0, // 0 (random)
            3, // 1
            3, // 2
            3, // 3
            1, // 4
            1, // 5
            1, // 6
            1, // 7
            2, // 8
            2, // 9
            2, // 10
            2, // 11
            0, // 12
            0, // 13
            0, // 14
            0, // 15 (random)
        };

        /// <summary>
        /// Calculate the 4-bit NESW bitmask for a quad at (qx, qz).
        /// A neighbor "connects" if it has the same or higher-priority (lower index) overLayerId.
        /// </summary>
        /// <summary>
        /// Calculate the 4-bit corner mask for a quad based on its 4 vertices.
        /// Bit mapping to match user order [v2, v3, v0, v1]:
        /// Bit 0: v1 (Bottom-Right)
        /// Bit 1: v0 (Bottom-Left)
        /// Bit 2: v3 (Top-Right)
        /// Bit 3: v2 (Top-Left)
        /// </summary>
        public static byte CalculateQuadMask(TileTerrainGridData data, QuadData quad, int targetTextureId, bool isOverLayer)
        {
            byte mask = 0;
        
            // v0 (Bottom-Left)  -> Bit 1
            if (IsMatch(data, quad.vertexIds[0], targetTextureId, isOverLayer)) mask |= (1 << 1);
            // v1 (Bottom-Right) -> Bit 0
            if (IsMatch(data, quad.vertexIds[1], targetTextureId, isOverLayer)) mask |= (1 << 0);
            // v2 (Top-Left)     -> Bit 3
            if (IsMatch(data, quad.vertexIds[2], targetTextureId, isOverLayer)) mask |= (1 << 3);
            // v3 (Top-Right)    -> Bit 2
            if (IsMatch(data, quad.vertexIds[3], targetTextureId, isOverLayer)) mask |= (1 << 2);

            return mask;
        }

        /// <summary>
        /// Calculate the cliff corner mask for a quad — identical logic to
        /// RecalculateQuad's texture mask: bit i is set when vertex i has cliff.
        /// Pass the result through GetTextureIndex to get the FBX mesh name.
        /// Mask 15 = fully cliffed → elevated flat quad, no cliff mesh.
        /// </summary>
        public static byte CalculateCliffMask(TileTerrainGridData data, QuadData quad)
        {
            return CalculateCliffMaskAtLevel(data, quad, quad.floor);
        }

        public static byte CalculateCliffMaskAtLevel(TileTerrainGridData data, QuadData quad, int level)
        {
            byte mask = 0;
            for (int i = 0; i < 4; i++)
            {
                if (data.Vertices[quad.vertexIds[i]].CliffByte > level)
                    mask |= (byte)(1 << i);
            }
            return mask;
        }

        public static int GetUniqueFloorCount(TileTerrainGridData data, QuadData quad)
        {
            int a = data.Vertices[quad.vertexIds[0]].CliffByte;
            int b = data.Vertices[quad.vertexIds[1]].CliffByte;
            int c = data.Vertices[quad.vertexIds[2]].CliffByte;
            int d = data.Vertices[quad.vertexIds[3]].CliffByte;
            int count = 1;
            if (b != a) count++;
            if (c != a && c != b) count++;
            if (d != a && d != b && d != c) count++;
            return count;
        }

        private static readonly int[] HeightToTransitionIndex = new int[256];

        static TileTerrainBitmask()
        {
            for (int i = 0; i < 256; i++) HeightToTransitionIndex[i] = -1;

            HeightToTransitionIndex[MakeKey(0,0,1,2)] = 0;
            HeightToTransitionIndex[MakeKey(0,0,2,1)] = 1;
            HeightToTransitionIndex[MakeKey(0,1,0,2)] = 2;
            HeightToTransitionIndex[MakeKey(0,1,1,2)] = 3;
            HeightToTransitionIndex[MakeKey(0,1,2,0)] = 4;
            HeightToTransitionIndex[MakeKey(0,1,2,1)] = 5;
            HeightToTransitionIndex[MakeKey(0,1,2,2)] = 6;
            HeightToTransitionIndex[MakeKey(0,2,0,1)] = 7;
            HeightToTransitionIndex[MakeKey(0,2,1,0)] = 8;
            HeightToTransitionIndex[MakeKey(0,2,1,1)] = 9;
            HeightToTransitionIndex[MakeKey(0,2,1,2)] = 10;
            HeightToTransitionIndex[MakeKey(0,2,2,1)] = 11;
            HeightToTransitionIndex[MakeKey(1,0,0,2)] = 12;
            HeightToTransitionIndex[MakeKey(1,0,1,2)] = 13;
            HeightToTransitionIndex[MakeKey(1,0,2,0)] = 14;
            HeightToTransitionIndex[MakeKey(1,0,2,1)] = 15;
            HeightToTransitionIndex[MakeKey(1,0,2,2)] = 16;
            HeightToTransitionIndex[MakeKey(1,1,0,2)] = 17;
            HeightToTransitionIndex[MakeKey(1,1,2,0)] = 18;
            HeightToTransitionIndex[MakeKey(1,2,0,0)] = 19;
            HeightToTransitionIndex[MakeKey(1,2,0,1)] = 20;
            HeightToTransitionIndex[MakeKey(1,2,0,2)] = 21;
            HeightToTransitionIndex[MakeKey(1,2,1,0)] = 22;
            HeightToTransitionIndex[MakeKey(1,2,2,0)] = 23;
            HeightToTransitionIndex[MakeKey(2,0,0,1)] = 24;
            HeightToTransitionIndex[MakeKey(2,0,1,0)] = 25;
            HeightToTransitionIndex[MakeKey(2,0,1,1)] = 26;
            HeightToTransitionIndex[MakeKey(2,0,1,2)] = 27;
            HeightToTransitionIndex[MakeKey(2,0,2,1)] = 28;
            HeightToTransitionIndex[MakeKey(2,1,0,0)] = 29;
            HeightToTransitionIndex[MakeKey(2,1,0,1)] = 30;
            HeightToTransitionIndex[MakeKey(2,1,0,2)] = 31;
            HeightToTransitionIndex[MakeKey(2,1,1,0)] = 32;
            HeightToTransitionIndex[MakeKey(2,1,2,0)] = 33;
            HeightToTransitionIndex[MakeKey(2,2,0,1)] = 34;
            HeightToTransitionIndex[MakeKey(2,2,1,0)] = 35;
        }

        private static int MakeKey(int v0, int v1, int v2, int v3)
        {
            return v0 | (v1 << 2) | (v2 << 4) | (v3 << 6);
        }

        public static bool IsTransitionalPattern(TileTerrainGridData data, QuadData quad, int floor)
        {
            return GetTransitionalMeshIndex(data, quad, floor) >= 0;
        }

        public static int GetTransitionalMeshIndex(TileTerrainGridData data, QuadData quad, int floor)
        {
            int v0 = data.Vertices[quad.vertexIds[0]].CliffByte - floor;
            int v1 = data.Vertices[quad.vertexIds[1]].CliffByte - floor;
            int v2 = data.Vertices[quad.vertexIds[2]].CliffByte - floor;
            int v3 = data.Vertices[quad.vertexIds[3]].CliffByte - floor;

            if (v0 < 0 || v0 > 2 || v1 < 0 || v1 > 2 || v2 < 0 || v2 > 2 || v3 < 0 || v3 > 2)
                return -1;

            return HeightToTransitionIndex[MakeKey(v0, v1, v2, v3)];
        }

        private static bool IsMatch(TileTerrainGridData data, int vertexId, int targetId, bool isOverLayer)
        {
            var v = data.Vertices[vertexId];
            return (isOverLayer ? v.overTextureId : v.underTextureId) == targetId;
        }

        /// <summary>
        /// Maps a 4-bit corner mask to a slice index in the Texture2DArray.
        /// Formula derived from user mapping: (mask % 4) + (mask / 4) * 8
        /// </summary>
        public static int GetTextureIndex(byte mask, int x, int z)
        {
            // ── Solid tile (1,1,1,1) randomization ──────────────────────────
            if (mask == TileTerrainConstants.FullQuadMask)
            {
                // Deterministic random based on grid coordinates
                System.Random prng = new System.Random(x * 73856093 ^ z * 19349663);
                double chance = prng.NextDouble();
            
                // TextureRandomness controls probability of random tile (0 = always base, 1 = always random)
                if (chance < 1.0 - TextureRandomness) return TileTerrainConstants.SolidBaseTile;

                // 40% chance to mix in a random variation
                int[] variations = { 0, 4, 5, 7, 12, 13, 14, 15, 20, 21, 22, 23, 28, 29, 30, 31 };
                return variations[prng.Next(0, variations.Length)];
            }

            // ── Bitmask mapping based on matrixSolution.txt ────────────────
            // Mask bits from RecalculateQuad: bit0=v0, bit1=v1, bit2=v2, bit3=v3
            // Weights from matrixSolution.txt: v1=1, v0=2, v3=8, v2=16
            bool v0 = (mask & 1) != 0; // bit 0
            bool v1 = (mask & 2) != 0; // bit 1
            bool v2 = (mask & 4) != 0; // bit 2
            bool v3 = (mask & 8) != 0; // bit 3

            int index = 0;
            if (v1) index += 1;
            if (v0) index += 2;
            if (v3) index += 8;
            if (v2) index += 16;

            return index;
        }

        /// <summary>
        /// Recalculate bitmask tile coordinates for a single quad by looking at its corners.
        /// Fix #5: Uses a static scratch buffer instead of allocating SortedSet+PriorityComparer
        /// on every call — zero heap allocation per quad.
        /// </summary>
        // Static scratch: at most 12 texture IDs (4 verts × over+mid+under). Never grows beyond that.
        private static readonly int[] _texScratch = new int[12];

        public static void RecalculateQuad(TileTerrain terrain, int quadIndex)
        {
            var data = terrain.GridData;
            if (quadIndex < 0 || quadIndex >= data.Quads.Count) return;
            var quad = data.Quads[quadIndex];

            // 1. Collect unique texture IDs from the 4 corner vertices.
            int scratchCount = 0;
            for (int i = 0; i < 4; i++)
            {
                var v = data.Vertices[quad.vertexIds[i]];
                AddUnique(_texScratch, ref scratchCount, v.overTextureId);
                AddUnique(_texScratch, ref scratchCount, v.midTextureId);
                AddUnique(_texScratch, ref scratchCount, v.underTextureId);
            }

            // 2. Sort by priority (Highest priority = lowest index first).
            for (int i = 1; i < scratchCount; i++)
            {
                int key = _texScratch[i];
                float keyP = terrain.GetPriority(key);
                int j = i - 1;
                while (j >= 0 && terrain.GetPriority(_texScratch[j]) > keyP)
                {
                    _texScratch[j + 1] = _texScratch[j];
                    j--;
                }
                _texScratch[j + 1] = key;
            }

            // 3. Initial slot assignment based on user rules:
            //    1 texture  -> over
            //    2 textures -> over + under
            //    3+ textures -> over + mid + under (taking top 3)
            int overTex  = scratchCount >= 1 ? _texScratch[0] : -1;
            int midTex   = scratchCount >= 3 ? _texScratch[1] : -1;
            int underTex = scratchCount >= 2 ? _texScratch[scratchCount >= 3 ? 2 : 1] : -1;

            byte overMask = 0;
            byte midMask  = 0;

            // 4. Calculate 'over' layer.
            if (overTex >= 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    var v = data.Vertices[quad.vertexIds[i]];
                    if (v.overTextureId == overTex) overMask |= (byte)(1 << i);
                }
                quad.overLayerId = overTex;
                quad.overTileCol = GetTextureIndex(overMask, quad.gridX, quad.gridZ);
                quad.overTileRow = 0;
            }
            else quad.overLayerId = -1;

            // 5. Calculate 'mid' layer.
            if (midTex >= 0 && overMask != TileTerrainConstants.FullQuadMask) // Only if over isn't solid
            {
                for (int i = 0; i < 4; i++)
                {
                    var v = data.Vertices[quad.vertexIds[i]];
                    // Vertex contributes if midTex is in over OR mid slot.
                    if (v.overTextureId == midTex || v.midTextureId == midTex) midMask |= (byte)(1 << i);
                }
            
                // If mid is solid (15), it occludes everything below it.
                // Under layer will be suppressed later in step 6.
                quad.midLayerId = midTex;
                quad.midTileCol = GetTextureIndex(midMask, quad.gridX, quad.gridZ);
                quad.midTileRow = 0;

                // If mid mask matches over mask, mid is entirely hidden — drop it.
                if (midMask == overMask)
                {
                    quad.midLayerId = -1;
                    midMask = 0;
                }
            }
            else quad.midLayerId = -1;

            // 6. Calculate 'under' layer.
            // Rule: Under is only visible if neither Over nor Mid is solid (mask 15).
            if (underTex >= 0 && overMask != TileTerrainConstants.FullQuadMask && midMask != TileTerrainConstants.FullQuadMask)
            {
                quad.underLayerId = underTex;
                // Under is always a solid base (tile 15).
                quad.underTileCol = GetTextureIndex(TileTerrainConstants.FullQuadMask, quad.gridX, quad.gridZ);
                quad.underTileRow = 0;
            }
            else quad.underLayerId = -1;

            data.Quads[quadIndex] = quad;
        }

        /// <summary>Adds <paramref name="id"/> to the scratch array only if id >= 0 and not already present.</summary>
        private static void AddUnique(int[] arr, ref int count, int id)
        {
            if (id < 0) return;
            for (int i = 0; i < count; i++)
                if (arr[i] == id) return;
            arr[count++] = id;
        }

        // PriorityComparer removed — replaced by static scratch buffer + insertion sort in RecalculateQuad.

        public static void RecalculateAroundVertex(TileTerrain terrain, int vi)
        {
            var data = terrain.GridData;
            if (vi < 0 || vi >= data.Vertices.Count) return;
        
            int w = data.Width;
            int row = w + 1;
            int vx = vi % row;
            int vz = vi / row;

            for (int qz = vz - 1; qz <= vz; qz++)
            {
                for (int qx = vx - 1; qx <= vx; qx++)
                {
                    if (qx >= 0 && qz >= 0 && qx < w && qz < data.Height)
                    {
                        RecalculateAt(terrain, qx, qz);
                    }
                }
            }
        }

        public static void CleanupPaintedVertices(TileTerrain terrain, HashSet<int> vertexIndices)
        {
            var data = terrain.GridData;
            foreach (int vi in vertexIndices)
            {
                if (vi < 0 || vi >= data.Vertices.Count) continue;
                var v = data.Vertices[vi];
                if (CleanupVertex(ref v))
                    data.Vertices[vi] = v;
            }
        }

        private static bool CleanupVertex(ref VertexData v)
        {
            bool changed = false;

            if (v.overTextureId >= 0 && v.overMask == 0xFF)
            {
                if (v.midTextureId >= 0 || v.underTextureId >= 0)
                {
                    v.midTextureId = -1; v.midMask = 0;
                    v.underTextureId = -1; v.underMask = 0;
                    changed = true;
                }
                return changed;
            }

            if (v.midTextureId >= 0 && v.midMask == 0xFF)
            {
                if (v.underTextureId >= 0)
                {
                    v.underTextureId = -1; v.underMask = 0;
                    changed = true;
                }
            }

            if (v.overTextureId >= 0 && v.overTextureId == v.midTextureId)
            {
                v.midTextureId = -1; v.midMask = 0;
                changed = true;
            }
            if (v.midTextureId >= 0 && v.midTextureId == v.underTextureId)
            {
                v.underTextureId = -1; v.underMask = 0;
                changed = true;
            }
            if (v.overTextureId >= 0 && v.underTextureId >= 0 && v.overTextureId == v.underTextureId)
            {
                v.underTextureId = -1; v.underMask = 0;
                changed = true;
            }

            return changed;
        }

        public static void BatchRecalculateVertices(TileTerrain terrain, HashSet<int> vertexIndices)
        {
            var data = terrain.GridData;

            CleanupPaintedVertices(terrain, vertexIndices);

            HashSet<(int, int)> quadsToUpdate = new HashSet<(int, int)>();
            int w = data.Width;
            int row = w + 1;

            foreach (int vi in vertexIndices)
            {
                int vx = vi % row;
                int vz = vi / row;
                for (int qz = vz - 1; qz <= vz; qz++)
                {
                    for (int qx = vx - 1; qx <= vx; qx++)
                    {
                        if (qx >= 0 && qz >= 0 && qx < w && qz < data.Height)
                            quadsToUpdate.Add((qx, qz));
                    }
                }
            }

            foreach (var (qx, qz) in quadsToUpdate)
                RecalculateAt(terrain, qx, qz);
        }

        private static void RecalculateAt(TileTerrain terrain, int qx, int qz)
        {
            var data = terrain.GridData;
            if (qx < 0 || qz < 0 || qx >= data.Width || qz >= data.Height) return;
            RecalculateQuad(terrain, qz * data.Width + qx);
        }

        public static void RecalculateAll(TileTerrain terrain)
        {
            var data = terrain.GridData;
            for (int i = 0; i < data.Quads.Count; i++)
                RecalculateQuad(terrain, i);
        }
    }
}
