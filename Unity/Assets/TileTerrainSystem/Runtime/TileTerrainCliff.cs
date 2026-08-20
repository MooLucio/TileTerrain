using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MooLucio.TileTerrain
{
    /// <summary>
    /// Loads and caches cliff sub-meshes from FBX prefabs.
    /// Mesh names match matrix IDs (0–13 for single/double cliff, 0–35 for
    /// transitional, 0–35 for ramps). Use <see cref="CliffMaskToMeshID"/> to
    /// convert a 4-bit corner mask to the correct FBX mesh index.
    /// </summary>
    public static class TileTerrainCliff
    {
        public const float CliffHeight = 1f;

        /// <summary>
        /// Maps a 4-bit corner mask (bits: v0=bit0, v1=bit1, v2=bit2, v3=bit3)
        /// to its corresponding cliff-single / cliff-double matrix mesh ID (0–13).
        /// Mask 0 or 15 → -1 (flat quad, no cliff mesh).
        /// </summary>
        private static readonly int[] MaskToCliffId = new int[16]
        {
            -1, // 0:  none
             7, // 1:  v0
             3, // 2:  v1
            11, // 3:  v0+v1
             1, // 4:  v2
             9, // 5:  v0+v2
             5, // 6:  v1+v2
            13, // 7:  v0+v1+v2
             0, // 8:  v3
             8, // 9:  v0+v3
             4, // 10: v1+v3
            12, // 11: v0+v1+v3
             2, // 12: v2+v3
            10, // 13: v0+v2+v3
             6, // 14: v1+v2+v3
            -1, // 15: all
        };

        /// <summary>
        /// Converts a 4-bit corner mask (from <see cref="TileTerrainBitmask.CalculateCliffMaskAtLevel"/>)
        /// to the matrix mesh ID used as the FBX child mesh name. Returns -1 for
        /// mask 0 (no cliff) and mask 15 (full flat, elevated terrain handles it).
        /// </summary>
        public static int CliffMaskToMeshID(byte mask)
        {
            if (mask >= 16) return -1;
            return MaskToCliffId[mask];
        }

        private static readonly Dictionary<GameObject, Dictionary<int, Mesh>> s_meshCache
            = new Dictionary<GameObject, Dictionary<int, Mesh>>();

        /// <summary>
        /// Returns a dictionary mapping mesh indices to Mesh assets, parsed from child
        /// MeshFilters of the given FBX prefab. Keys are matrix mesh IDs
        /// (0–13 for cliff single/double, 0–35 for transitional, 0–35 for ramps).
        /// </summary>
        public static Dictionary<int, Mesh> GetOrLoadMeshes(GameObject cliffPrefab)
        {
            if (cliffPrefab == null) return null;

            GameObject key = cliffPrefab;
            if (s_meshCache.TryGetValue(key, out var cached) && cached.Count > 0) return cached;

            var meshes = new Dictionary<int, Mesh>();

            foreach (MeshFilter mf in cliffPrefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh != null && int.TryParse(mf.sharedMesh.name, out int idx))
                    meshes[idx] = mf.sharedMesh;
            }

    #if UNITY_EDITOR
            string path = AssetDatabase.GetAssetPath(cliffPrefab);
            if (!string.IsNullOrEmpty(path))
                foreach (Object a in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (a is Mesh m && int.TryParse(m.name, out int idx) && !meshes.ContainsKey(idx))
                        meshes[idx] = m;
    #endif

            s_meshCache[key] = meshes;
            return meshes;
        }

        public static void InvalidateCache() => s_meshCache.Clear();

        // ── Ramp matrix ────────────────────────────────────────────────────
        // Each vertex is a socket with one of 5 values:
        //   0.0 / 1.0  — base low / high
        //   0.1 / 1.1  — base low / high with R-variant (column partner has halfStep)
        //   0.5        — half step
        // The 36 valid 4-tuples in ramps-matrix.json map 1:1 to FBX mesh IDs 0–35.
        // We encode each value as 0..4 and pack the 4 sockets into a base-5 key.
        private static readonly int[] s_rampVerticalPartner = { 2, 3, 0, 1 };
        private static readonly int[] s_rampHorizontalPartner = { 1, 0, 3, 2 };

        // 0.0→0, 0.1→1, 0.5→2, 1.0→3, 1.1→4
        private static int RampCode(float v) => v switch
        {
            0.0f => 0,
            0.1f => 1,
            0.5f => 2,
            1.0f => 3,
            _    => 4, // 1.1f
        };

        // Generated from Models/Cliff/Blender/Bases/Matrix/ramps-matrix.json.
        // Key = code(v0) + 5*code(v1) + 25*code(v2) + 125*code(v3).
        private static readonly Dictionary<int, int> s_rampKeyToMesh = new Dictionary<int, int>
        {
            {   7, 25 }, // [0.5, 0.1, 0.0, 0.0]
            {  11, 13 }, // [0.1, 0.5, 0.0, 0.0]
            {  14, 19 }, // [1.1, 0.5, 0.0, 0.0]
            {  22, 27 }, // [0.5, 1.1, 0.0, 0.0]
            {  27, 22 }, // [0.5, 0.0, 0.1, 0.0]
            {  51, 12 }, // [0.1, 0.0, 0.5, 0.0]
            {  54, 17 }, // [1.1, 0.0, 0.5, 0.0]
            {  64, 34 }, // [1.1, 0.5, 0.5, 0.0]
            {  69, 18 }, // [1.1, 1.0, 0.5, 0.0]
            {  89, 20 }, // [1.1, 0.5, 1.0, 0.0]
            { 102, 23 }, // [0.5, 0.0, 1.1, 0.0]
            { 135,  8 }, // [0.0, 0.5, 0.0, 0.1]
            { 157, 26 }, // [0.5, 0.1, 0.1, 0.1]
            { 161, 14 }, // [0.1, 0.5, 0.1, 0.1]
            { 175,  2 }, // [0.0, 0.0, 0.5, 0.1]
            { 255,  4 }, // [0.0, 0.1, 0.0, 0.5]
            { 270,  7 }, // [0.0, 1.1, 0.0, 0.5]
            { 272, 35 }, // [0.5, 1.1, 0.0, 0.5]
            { 273, 16 }, // [1.0, 1.1, 0.0, 0.5]
            { 275,  0 }, // [0.0, 0.0, 0.1, 0.5]
            { 276, 11 }, // [0.1, 0.0, 0.1, 0.5]
            { 280,  5 }, // [0.0, 0.1, 0.1, 0.5]
            { 314, 21 }, // [1.1, 0.5, 0.5, 0.5]
            { 322, 29 }, // [0.5, 1.1, 0.5, 0.5]
            { 350,  1 }, // [0.0, 0.0, 1.1, 0.5]
            { 352, 32 }, // [0.5, 0.0, 1.1, 0.5]
            { 353, 15 }, // [1.0, 0.0, 1.1, 0.5]
            { 362, 30 }, // [0.5, 0.5, 1.1, 0.5]
            { 397, 28 }, // [0.5, 1.1, 0.0, 1.0]
            { 477, 24 }, // [0.5, 0.0, 1.1, 1.0]
            { 510,  9 }, // [0.0, 0.5, 0.0, 1.1]
            { 550,  3 }, // [0.0, 0.0, 0.5, 1.1]
            { 560, 33 }, // [0.0, 0.5, 0.5, 1.1]
            { 562, 31 }, // [0.5, 0.5, 0.5, 1.1]
            { 565,  6 }, // [0.0, 1.0, 0.5, 1.1]
            { 585, 10 }, // [0.0, 0.5, 1.0, 1.1]
        };

        /// <summary>
        /// Computes ramp socket values for the given quad.  Uses vertical
        /// partners for north-south walls and horizontal partners for east-west
        /// walls, deciding orientation from: (a) which two corners share a
        /// halfStep edge, or (b) which edge-partner sits at the elevated floor
        /// level.  Returns 28/32 exact matches vs ramps-matrix.json (the
        /// remaining 4 are complex multi-R entries that still map to a valid
        /// mesh).
        /// </summary>
        public static int ComputeRampMask(TileTerrainGridData data, QuadData quad, int qi,
            out float v0, out float v1, out float v2, out float v3)
        {
            // Cache vertex data for this quad.
            var vtx0 = data.Vertices[quad.vertexIds[0]];
            var vtx1 = data.Vertices[quad.vertexIds[1]];
            var vtx2 = data.Vertices[quad.vertexIds[2]];
            var vtx3 = data.Vertices[quad.vertexIds[3]];

            int floor = quad.floor;
            bool[] hs = { vtx0.CliffHalfStep, vtx1.CliffHalfStep, vtx2.CliffHalfStep, vtx3.CliffHalfStep };
            int[] cb = { vtx0.CliffByte, vtx1.CliffByte, vtx2.CliffByte, vtx3.CliffByte };

            // ── Decide partner orientation ────────────────────────────────
            int[] pSet;
            int hsCount = (hs[0] ? 1 : 0) + (hs[1] ? 1 : 0) + (hs[2] ? 1 : 0) + (hs[3] ? 1 : 0);

            if (hsCount >= 2)
            {
                // Two or more HS: use the edge they share.
                bool vert = (hs[0] && hs[2]) || (hs[1] && hs[3]);
                pSet = vert ? s_rampVerticalPartner : s_rampHorizontalPartner;
            }
            else if (hsCount == 1)
            {
                // Single HS: check which edge-partner is at the elevated floor.
                int j = hs[0] ? 0 : hs[1] ? 1 : hs[2] ? 2 : 3;
                int hP = s_rampHorizontalPartner[j];
                int vP = s_rampVerticalPartner[j];
                bool hElevated = cb[hP] == floor + 1;
                bool vElevated = cb[vP] == floor + 1;

                if (hElevated && !vElevated)
                    pSet = s_rampHorizontalPartner;
                else if (vElevated && !hElevated)
                    pSet = s_rampVerticalPartner;
                else
                {
                    // Ambiguous — check the two grid neighbors outside the quad.
                    // xDir (east/west): v0/v2→west, v1/v3→east
                    // zDir (north/south): v0/v1→north, v2/v3→south
                    // xDir elevated → horizontal wall (east-west)
                    // zDir elevated → vertical wall (north-south)
                    int vidx = quad.vertexIds[j];
                    int row = data.Width + 1;
                    int vx = vidx % row;
                    int vz = vidx / row;
                    int floorP1 = floor + 1;

                    int xDir = (j == 0 || j == 2) ? -1 : 1;
                    int zDir = (j == 0 || j == 1) ? -1 : 1;

                    bool xUp = (xDir == -1 ? vx > 0 : vx < data.Width)
                        && data.Vertices[vz * row + vx + xDir].CliffByte == floorP1;
                    bool zUp = (zDir == -1 ? vz > 0 : vz < data.Height)
                        && data.Vertices[(vz + zDir) * row + vx].CliffByte == floorP1;

                    if (xUp && !zUp)
                        pSet = s_rampHorizontalPartner;    // elevated to east/west → horizontal wall
                    else if (zUp && !xUp)
                        pSet = s_rampVerticalPartner;        // elevated to north/south → vertical wall
                    else
                        pSet = s_rampVerticalPartner;        // both/neither — fallback to vertical
                }
            }
            else
            {
                pSet = s_rampVerticalPartner;
            }

            // ── Helper: compute socket values for a given partner set ────
            float[] Compute(int[] partners)
            {
                float[] s = new float[4];
                // Detect diagonal-HS corners (v0+v3 or v1+v2) — these are "corner ramp"
                // patterns where the two half-steps sit on opposite corners.  In this
                // layout the R‑variant (0.1) must NOT propagate to a base‑level vertex,
                // even when its partner has HS; only the elevated vertex gets 1.1.
                bool diagonalHS = (hs[0] && hs[3]) || (hs[1] && hs[2]);
                for (int j = 0; j < 4; j++)
                {
                    var vtx = data.Vertices[quad.vertexIds[j]];
                    var pvtx = data.Vertices[quad.vertexIds[partners[j]]];
                    int relFloor = vtx.CliffByte - floor;

                    if (vtx.CliffHalfStep)
                        s[j] = 0.5f;
                    else if (pvtx.CliffHalfStep && relFloor == 0)
                        s[j] = diagonalHS ? 0f : 0.1f;
                    else if (pvtx.CliffHalfStep && relFloor == 1)
                        s[j] = 1.1f;
                    else
                        s[j] = relFloor == 0 ? 0f : 1f;
                }
                return s;
            }

            int GetKey(float[] s) =>
                RampCode(s[0]) + 5 * RampCode(s[1]) + 25 * RampCode(s[2]) + 125 * RampCode(s[3]);

            float[] socket = Compute(pSet);
            int key = GetKey(socket);

            v0 = socket[0]; v1 = socket[1]; v2 = socket[2]; v3 = socket[3];
            return s_rampKeyToMesh.TryGetValue(key, out int id) ? id : -1;
        }

        /// <summary>
        /// Strips R-variant types to base values (0.1→0, 1.1→1) for physical offsets.
        /// Socket identity remains untouched — this is only for vertex Y positioning.
        /// </summary>
        public static float StripRvariant(float v)
        {
            return v == 0.1f ? 0f : v == 1.1f ? 1f : v;
        }

    #if UNITY_EDITOR
        private class CliffFbxPostprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (imported.Any(p => p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)))
                    InvalidateCache();
            }
        }

        [MenuItem("Tools/Tile Terrain/Clear Cliff Cache")]
        private static void ClearCacheMenuItem()
        {
            InvalidateCache();
            Debug.Log("TileTerrainCliff cache cleared.");
        }
    #endif
    }
}