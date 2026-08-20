using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace MooLucio.TileTerrain
{
    /// <summary>
    /// Tile-based fog of war with cliff-aware line of sight.
    /// Maintains an RGBA8 mask texture (1 px per grid cell), values are continuous [0,1]:
    ///   R = currently visible  (lerps toward 1 inside revealers, toward 0 outside)
    ///   G = explored           (lerps toward 1 inside revealers; stable when Persistent,
    ///                           follows R when Flashlight; cleared only by HideAll)
    ///   B/A unused
    /// A full-screen URP render pass samples the mask with bilinear filtering for soft edges.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public class FogOfWarManager : MonoBehaviour
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        public static FogOfWarManager Instance { get; private set; }

        // Static revealer registry: works even before Instance is awake.
        // The static HashSet is the source of truth; the manager drains it each LateUpdate.
        private static readonly HashSet<FogOfWarRevealer> s_Revealers = new HashSet<FogOfWarRevealer>();
        public static IReadOnlyCollection<FogOfWarRevealer> Revealers => s_Revealers;

        public static void Register(FogOfWarRevealer r)
        {
            if (r == null) return;
            s_Revealers.Add(r);
        }

        public static void Unregister(FogOfWarRevealer r)
        {
            if (r == null) return;
            s_Revealers.Remove(r);
        }

        // ── Inspector config ──────────────────────────────────────────────────
        [Header("Grid")]
        [Tooltip("Grid whose cells the fog mask matches. Width x Height pixels in the mask texture.")]
        [FormerlySerializedAs("gridData")]
        public TileTerrainGridData GridData;

        [Header("Fog Appearance")]
        [Tooltip("Color used for cells that are Hidden (never seen). Alpha ignored (assumed 1).")]
        [FormerlySerializedAs("fogColor")]
        public Color FogColor = new Color(0.02f, 0.02f, 0.04f, 1f);

        [Tooltip("Color used for cells that are Explored but not currently visible. " +
                 "RGB is the tinted scene, A is the visibility strength (0 = invisible, 1 = full).")]
        [FormerlySerializedAs("exploredColor")]
        public Color ExploredColor = new Color(0.35f, 0.35f, 0.4f, 0.55f);

        [Tooltip("0 = render scene outside the grid normally, 1 = fog everything outside the grid.")]
        [FormerlySerializedAs("outsideGridFog")]
        [Range(0f, 1f)] public float OutsideGridFog = 1f;

        [Header("LOS")]
        [Tooltip("Vertical knee added to the line-of-sight height to avoid 1-cell self-occlusion.")]
        [FormerlySerializedAs("kneeOffset")]
        public float KneeOffset = 0.25f;

        [Header("Performance")]
        [Tooltip("Minimum seconds between mask recomputes. 0 = every LateUpdate.")]
        [FormerlySerializedAs("updateInterval")]
        [Min(0f)] public float UpdateInterval = 0f;

        [Tooltip("Mask texture is rendered at (grid * MaskUpscale) resolution. Higher = smoother cell " +
                 "boundaries (more texels of bilinear feathering) at a small CPU/GPU cost. 1 = 1 px per cell " +
                 "(blocky). 4 = 16 px per cell (recommended). 8 = 64 px per cell (very smooth).")]
        [FormerlySerializedAs("maskUpscale")]
        [Range(1, 16)] public int MaskUpscale = 4;

        [Tooltip("Soft-blur radius applied to the mask in the shader. 0 = sharp bilinear only. " +
                 "0.02 = soft fog edge. 0.05 = wide gradient. Values are in normalised grid UV space " +
                 "(0.01 ≈ 1 cell at upscale 4, ≈ 2 cells at upscale 8).")]
        [FormerlySerializedAs("maskBlur")]
        [Range(0f, 0.1f)] public float MaskBlur = 0.025f;

        [Header("Smoothing")]
        [Tooltip("How fast the visible (R) channel rises toward 1 inside a revealer's area. " +
                 "Per-frame lerp factor at 60 FPS. 0 = snap instantly, 0.3 = ~10 frames to " +
                 "appear (~0.17 s), 0.6 = ~3 frames, 1 = lerp fully in one frame.")]
        [FormerlySerializedAs("visibleRiseRate")]
        [Range(0f, 1f)] public float VisibleRiseRate = 0.35f;

        [Tooltip("How fast the visible (R) channel decays back to 0 outside a revealer's area. " +
                 "Per-frame lerp factor. 0.08 = ~20 frames to fade out (~0.33 s), " +
                 "0.15 = ~10 frames, 0.3 = ~5 frames. Set higher for snappier, lower for trailing fog.")]
        [FormerlySerializedAs("visibleFallRate")]
        [Range(0f, 1f)] public float VisibleFallRate = 0.10f;

        [Tooltip("How fast the explored (G) channel rises toward 1 inside a revealer's area. " +
                 "Should usually be slower than VisibleRiseRate so 'remembered' areas build up " +
                 "gradually. 0.1 = ~20 frames (~0.33 s). Only used by Persistent revealers; " +
                 "Flashlight revealers mirror R directly.")]
        [FormerlySerializedAs("exploredRiseRate")]
        [Range(0f, 1f)] public float ExploredRiseRate = 0.10f;

        [Header("Debug")]
        [Tooltip("Draw the fog mask in the Scene view.")]
        [FormerlySerializedAs("debugDrawMask")]
        public bool DebugDrawMask = true;

        [Tooltip("Vertical offset for the debug mask quad (world units above grid origin).")]
        [FormerlySerializedAs("debugDrawHeight")]
        public float DebugDrawHeight = 5f;

        [Tooltip("Scale for the debug mask (1 = matches the grid in world units).")]
        [FormerlySerializedAs("debugDrawScale")]
        [Min(0.01f)] public float DebugDrawScale = 1f;

        // ── Runtime state ─────────────────────────────────────────────────────
        private Texture2D _mask;
        private Color[] _pixels;
        private int _allocatedGridW, _allocatedGridH;
        private int _allocatedMaskW, _allocatedMaskH;
        private int _allocatedUpscale;
        private float _nextUpdateTime;
        // First UpdateMask call after (re)allocation snaps R/G to 1 instantly so the
        // very first reveal isn't a slow lerp. Subsequent calls honour the public rates.
        private bool _snapNextRise = true;

        // Flood-fill BFS state (reused across revealers, sized to grid).
        // _bfsQueue stores packed cell indices (cy * _allocatedGridW + cx).
        // _bfsVisited uses a frame token so we never need to clear it between revealers;
        // the token wraps every 254 revealers and we clear in that case.
        private int[] _bfsQueue;
        private int _bfsHead, _bfsTail;
        private byte[] _bfsVisited;
        private byte _bfsVisitToken;

        // Per-cell max corner height, cached at mask-allocation time. Lets the flood-fill
        // hot loop (and the DDA) read the blocker height with one float load instead of
        // 4 vertex lookups + 3 compares.
        private float[] _cellMaxHeight;
        // Last GridData.Version we rebuilt the cache from; bumped in TileTerrainGridData
        // on RegenerateGrid / RecalculateFloorOffsets so we pick up runtime terrain edits.
        private int _lastGridVersion = -1;

        public Texture MaskTexture => _mask;
        public event System.Action FogUpdated;

        // ── Lifecycle ─────────────────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[FogOfWar] Multiple FogOfWarManager instances. Destroying duplicate on '{name}'.", this);
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_mask != null)
            {
#if UNITY_EDITOR
                if (Application.isPlaying) Destroy(_mask); else DestroyImmediate(_mask);
#else
                Destroy(_mask);
#endif
            }
        }

        void Start()
        {
            EnsureMask();
        }

        void LateUpdate()
        {
            if (GridData == null) return;
            EnsureMask();
            if (_mask == null) return;

            // Pick up runtime terrain edits (height changes, cliff stack changes) — the
            // grid bumps Version on RegenerateGrid / RecalculateFloorOffsets.
            if (GridData.Version != _lastGridVersion)
            {
                RebuildCellMaxHeights();
                _lastGridVersion = GridData.Version;
            }

            if (UpdateInterval > 0f && Time.unscaledTime < _nextUpdateTime) return;
            _nextUpdateTime = Time.unscaledTime + UpdateInterval;

            UpdateMask();
        }

        // ── Mask management ───────────────────────────────────────────────────
        private void EnsureMask()
        {
            if (GridData == null) return;
            int gw = GridData.Width;
            int gh = GridData.Height;
            if (gw <= 0 || gh <= 0) return;
            int up = Mathf.Max(1, MaskUpscale);
            int mw = gw * up;
            int mh = gh * up;
            if (_mask != null && _allocatedGridW == gw && _allocatedGridH == gh && _allocatedUpscale == up) return;

            if (_mask != null)
            {
#if UNITY_EDITOR
                if (Application.isPlaying) Destroy(_mask); else DestroyImmediate(_mask);
#else
                Destroy(_mask);
#endif
            }

            _mask = new Texture2D(mw, mh, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "FogOfWarMask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            _allocatedGridW = gw;
            _allocatedGridH = gh;
            _allocatedMaskW = mw;
            _allocatedMaskH = mh;
            _allocatedUpscale = up;
            _pixels = new Color[mw * mh];
            ClearAll();
            _snapNextRise = true; // first reveal after (re)allocation snaps
            _mask.SetPixels(_pixels);
            _mask.Apply(false);

            EnsureBfsBuffers(gw * gh);
            RebuildCellMaxHeights();
        }

        private void EnsureBfsBuffers(int cellCount)
        {
            if (_bfsQueue == null || _bfsQueue.Length < cellCount)
            {
                _bfsQueue = new int[cellCount];
                _bfsVisited = new byte[cellCount];
                _bfsVisitToken = 1;
            }
        }

        /// <summary>
        /// Rebuild the per-cell max-corner-height cache from the current GridData.
        /// Stores the *visual* top of the cell, i.e. vertex height + stacked cliff floor
        /// offset (VertexFloorOffset * CliffHeight), so a cell on the 2nd floor of a cliff
        /// actually blocks the eye at the correct world Y.
        /// Call this after any runtime change to vertex heights (tile edits, cliff stack,
        /// runtime terrain deformation). Also rebuilt automatically on mask (re)allocation.
        /// </summary>
        public void RebuildCellMaxHeights()
        {
            if (GridData == null) return;
            int gw = GridData.Width;
            int gh = GridData.Height;
            if (gw <= 0 || gh <= 0) return;
            int cellCount = gw * gh;
            if (_cellMaxHeight == null || _cellMaxHeight.Length != cellCount)
                _cellMaxHeight = new float[cellCount];

            int row = gw + 1;
            var verts = GridData.Vertices;
            var offsets = GridData.VertexFloorOffset;
            // Floor offsets are optional (e.g. on a fresh grid that hasn't been
            // RecalculateFloorOffsets'd yet) and may be stale-sized; guard both.
            bool hasOffsets = offsets != null && offsets.Length >= row * (gh + 1);
            float ch = TileTerrainCliff.CliffHeight;

            for (int cy = 0; cy < gh; cy++)
            {
                int yBase = cy * row;
                int cellYBase = cy * gw;
                for (int cx = 0; cx < gw; cx++)
                {
                    int i00 = yBase + cx;
                    int i10 = i00 + 1;
                    int i01 = i00 + row;
                    int i11 = i01 + 1;
                    float h  = verts[i00].height + (hasOffsets ? offsets[i00] * ch : 0f);
                    float h10 = verts[i10].height + (hasOffsets ? offsets[i10] * ch : 0f); if (h10 > h) h = h10;
                    float h01 = verts[i01].height + (hasOffsets ? offsets[i01] * ch : 0f); if (h01 > h) h = h01;
                    float h11 = verts[i11].height + (hasOffsets ? offsets[i11] * ch : 0f); if (h11 > h) h = h11;
                    _cellMaxHeight[cellYBase + cx] = h;
                }
            }
        }

        private void ClearAll()
        {
            if (_pixels == null) return;
            Color c = new Color(0f, 0f, 0f, 0f);
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = c;
        }

        public void HideAll()
        {
            if (_pixels == null) { EnsureMask(); if (_pixels == null) return; }
            ClearAll();
            if (_mask != null)
            {
                _mask.SetPixels(_pixels);
                _mask.Apply(false);
            }
            FogUpdated?.Invoke();
        }

        public void RevealAll()
        {
            if (_pixels == null) { EnsureMask(); if (_pixels == null) return; }
            Color c = new Color(1f, 1f, 0f, 0f);
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = c;
            _mask.SetPixels(_pixels);
            _mask.Apply(false);
            FogUpdated?.Invoke();
        }

        // ── Update loop ───────────────────────────────────────────────────────
        private void UpdateMask()
        {
            int gw = _allocatedGridW;
            int gh = _allocatedGridH;
            int mw = _allocatedMaskW;
            int mh = _allocatedMaskH;
            int up = _allocatedUpscale;
            int gridRow = gw + 1;

            // Pick rise rates for this frame. The very first update after (re)allocation
            // (or after HideAll) uses 1.0 → instant snap so the first reveal isn't a lerp.
            float baseVRise = _snapNextRise ? 1f : Mathf.Clamp01(VisibleRiseRate);
            float baseERise = _snapNextRise ? 1f : Mathf.Clamp01(ExploredRiseRate);
            _snapNextRise = false;

            // 1) Decay R toward 0 for ALL pixels (visible fade-out).
            //    Cache the rates once; they're stable for the frame.
            float vFall = Mathf.Clamp01(VisibleFallRate);
            if (vFall > 0f)
            {
                // Mathf.Lerp(x, 0, t) == x * (1 - t); cheaper when t is constant.
                float keep = 1f - vFall;
                for (int i = 0; i < _pixels.Length; i++)
                {
                    Color c = _pixels[i];
                    if (c.r > 0f)
                    {
                        c.r *= keep;
                        if (c.r < 1e-4f) c.r = 0f; // snap to 0 once invisible
                        _pixels[i] = c;
                    }
                }
            }

            // 2) Walk every revealer.
            foreach (var r in s_Revealers)
            {
                if (r == null || !r.isActiveAndEnabled) continue;
                UpdateRevealer(r, gw, gh, mw, mh, up, gridRow, baseVRise, baseERise);
            }

            // 3) Push to GPU.
            _mask.SetPixels(_pixels);
            _mask.Apply(false);
            FogUpdated?.Invoke();
        }

        private void UpdateRevealer(FogOfWarRevealer r, int gw, int gh, int mw, int mh, int up, int gridRow,
                                    float baseVRise, float baseERise)
        {
            // World → cell.
            Vector3 world = r.transform.position;
            int cx = Mathf.FloorToInt(world.x + gw * 0.5f);
            int cy = Mathf.FloorToInt(world.z + gh * 0.5f);
            r.GridCell = new Vector2Int(cx, cy);

            if ((uint)cx >= (uint)gw || (uint)cy >= (uint)gh) return; // origin outside grid

            if (r.UseFloodFill)
                RevealByFloodFill(r, cx, cy, gw, gh, mw, up, baseVRise, baseERise);
            else
                RevealByRaycast(r, cx, cy, gw, gh, mw, up, gridRow, baseVRise, baseERise);
        }

        // ── Reveal: DDA per-cell raycast (original path) ─────────────────────
        private void RevealByRaycast(FogOfWarRevealer r, int cx, int cy, int gw, int gh,
                                     int mw, int up, int gridRow,
                                     float baseVRise, float baseERise)
        {
            int rCeil = Mathf.CeilToInt(r.Radius);
            int rSq = (int)(r.Radius * r.Radius);
            float invRadius = r.Radius > 0f ? 1f / r.Radius : 0f;
            float eyeY = r.transform.position.y + r.EyeHeight;
            bool persistent = r.Persistence == FogRevealPersistence.Persistent;

            int xMin = Mathf.Max(0, cx - rCeil);
            int xMax = Mathf.Min(gw - 1, cx + rCeil);
            int yMin = Mathf.Max(0, cy - rCeil);
            int yMax = Mathf.Min(gh - 1, cy + rCeil);

            for (int y = yMin; y <= yMax; y++)
            {
                int dy = y - cy;
                int maskYBase = y * up;
                for (int x = xMin; x <= xMax; x++)
                {
                    int dx = x - cx;
                    int dSq = dx * dx + dy * dy;
                    if (dSq > rSq) continue;

                    // Distance-based rise rate. Cells right under the revealer snap (rate = 1);
                    // cells at the edge lerp at the public rate. Linear in normalised radius.
                    //   t = 0 at centre  → cellRise = 1  (instant)
                    //   t = 1 at edge    → cellRise = baseVRise / baseERise
                    float t = Mathf.Sqrt(dSq) * invRadius;
                    float vRise = Mathf.Lerp(1f, baseVRise, t);
                    float eRise = Mathf.Lerp(1f, baseERise, t);

                    if (r.Occluded)
                    {
                        // Target cell-center surface height (where we want to SEE).
                        float targetY = GetCellCenterHeight(x, y, gridRow);
                        if (!HasLineOfSight(new Vector2Int(cx, cy), new Vector2Int(x, y),
                                            eyeY, targetY, gridRow, gw, gh))
                            continue;
                    }

                    PaintCell(x, y, mw, up, vRise, eRise, persistent);
                }
            }
        }

        // ── Reveal: 4-connected flood fill (shadow-casting, O(r²)) ───────────
        private void RevealByFloodFill(FogOfWarRevealer r, int originX, int originY,
                                        int gw, int gh, int mw, int up,
                                        float baseVRise, float baseERise)
        {
            int rCeil = Mathf.CeilToInt(r.Radius);
            int rSq = (int)(r.Radius * r.Radius);
            float invRadius = r.Radius > 0f ? 1f / r.Radius : 0f;
            float eyeY = r.transform.position.y + r.EyeHeight;
            bool persistent = r.Persistence == FogRevealPersistence.Persistent;
            bool occluded = r.Occluded;

            // Bump frame token. Wrap → clear (after ~254 revealers, 1-time cost).
            byte token = _bfsVisitToken;
            if (++token == 0)
            {
                Array.Clear(_bfsVisited, 0, _bfsVisited.Length);
                token = 1;
            }
            _bfsVisitToken = token;

            int originIdx = originY * gw + originX;
            _bfsQueue[0] = originIdx;
            _bfsVisited[originIdx] = token;
            _bfsHead = 0;
            _bfsTail = 1;

            // Bounding box of any cell that could be within Euclidean radius
            // (any cell outside this is provably beyond r).
            int xMin = originX - rCeil; if (xMin < 0) xMin = 0;
            int xMax = originX + rCeil; if (xMax > gw - 1) xMax = gw - 1;
            int yMin = originY - rCeil; if (yMin < 0) yMin = 0;
            int yMax = originY + rCeil; if (yMax > gh - 1) yMax = gh - 1;

            while (_bfsHead < _bfsTail)
            {
                int idx = _bfsQueue[_bfsHead++];
                int cx = idx % gw;
                int cy = idx / gw;
                int dx = cx - originX;
                int dy = cy - originY;
                int dSq = dx * dx + dy * dy;
                if (dSq > rSq) continue; // corner of the box, outside the circle

                // Distance-based rise (same curve as the raycast path).
                float t = Mathf.Sqrt(dSq) * invRadius;
                float vRise = Mathf.Lerp(1f, baseVRise, t);
                float eRise = Mathf.Lerp(1f, baseERise, t);

                // The cell itself is always painted (blocker cells are still seen).
                PaintCell(cx, cy, mw, up, vRise, eRise, persistent);

                // Spread only if the cell is at or below the eye line.
                // Cells above the eye cast a shadow: they are revealed, but the fill stops.
                if (occluded && _cellMaxHeight[idx] > eyeY) continue;

                // 4-neighbour expansion, each neighbour also Euclidean-tested to avoid
                // enqueuing cells we'll later skip during the paint phase.
                int n;
                int ndx, ndy, ndSq;

                if (cy > yMin)
                {
                    n = idx - gw;
                    if (_bfsVisited[n] != token)
                    {
                        ndx = cx - originX; ndy = (cy - 1) - originY;
                        ndSq = ndx * ndx + ndy * ndy;
                        if (ndSq <= rSq) { _bfsVisited[n] = token; _bfsQueue[_bfsTail++] = n; }
                    }
                }
                if (cy < yMax)
                {
                    n = idx + gw;
                    if (_bfsVisited[n] != token)
                    {
                        ndx = cx - originX; ndy = (cy + 1) - originY;
                        ndSq = ndx * ndx + ndy * ndy;
                        if (ndSq <= rSq) { _bfsVisited[n] = token; _bfsQueue[_bfsTail++] = n; }
                    }
                }
                if (cx > xMin)
                {
                    n = idx - 1;
                    if (_bfsVisited[n] != token)
                    {
                        ndx = (cx - 1) - originX; ndy = cy - originY;
                        ndSq = ndx * ndx + ndy * ndy;
                        if (ndSq <= rSq) { _bfsVisited[n] = token; _bfsQueue[_bfsTail++] = n; }
                    }
                }
                if (cx < xMax)
                {
                    n = idx + 1;
                    if (_bfsVisited[n] != token)
                    {
                        ndx = (cx + 1) - originX; ndy = cy - originY;
                        ndSq = ndx * ndx + ndy * ndy;
                        if (ndSq <= rSq) { _bfsVisited[n] = token; _bfsQueue[_bfsTail++] = n; }
                    }
                }
            }
        }

        // ── Per-cell mask paint (shared by both reveal paths) ────────────────
        private void PaintCell(int x, int y, int mw, int up, float vRise, float eRise, bool persistent)
        {
            int maskXBase = x * up;
            int maskYBase = y * up;
            for (int my = 0; my < up; my++)
            {
                int row = (maskYBase + my) * mw + maskXBase;
                for (int mx = 0; mx < up; mx++)
                {
                    int idx = row + mx;
                    Color c = _pixels[idx];

                    // R: lerp toward 1 at this cell's distance-based rate.
                    if (c.r < 1f) c.r += (1f - c.r) * vRise;

                    // G:
                    //   Persistent: lerp toward 1 at this cell's rate.
                    //   Flashlight: snapshot of R (no per-frame R tracking).
                    if (persistent)
                    {
                        if (c.g < 1f) c.g += (1f - c.g) * eRise;
                    }
                    else
                    {
                        c.g = c.r;
                    }

                    _pixels[idx] = c;
                }
            }
        }

        // ── Height sampling ───────────────────────────────────────────────────
        /// <summary>Average of the 4 corner vertex heights of a cell. Used as the height we want to SEE INTO.</summary>
        private float GetCellCenterHeight(int cx, int cy, int row)
        {
            int i00 = cy * row + cx;
            int i10 = i00 + 1;
            int i01 = i00 + row;
            int i11 = i01 + 1;
            float h = 0.25f * (
                GridData.Vertices[i00].height +
                GridData.Vertices[i10].height +
                GridData.Vertices[i01].height +
                GridData.Vertices[i11].height);
            return h;
        }

        /// <summary>Max of the 4 corner vertex heights of a cell. Used as the LOS BLOCKER (cliff top).
        /// Reads from a pre-computed cache (one float load); the cache is rebuilt by
        /// <see cref="RebuildCellMaxHeights"/> on mask (re)allocation and must be refreshed by
        /// external callers when the heightmap changes at runtime.</summary>
        private float GetCellMaxHeight(int cx, int cy, int row)
        {
            if (_cellMaxHeight != null)
                return _cellMaxHeight[cy * _allocatedGridW + cx];
            // Fallback (cache not yet built) — original 4-vertex computation.
            int i00 = cy * row + cx;
            int i10 = i00 + 1;
            int i01 = i00 + row;
            int i11 = i01 + 1;
            float h = GridData.Vertices[i00].height;
            if (GridData.Vertices[i10].height > h) h = GridData.Vertices[i10].height;
            if (GridData.Vertices[i01].height > h) h = GridData.Vertices[i01].height;
            if (GridData.Vertices[i11].height > h) h = GridData.Vertices[i11].height;
            return h;
        }

        // ── LOS: 2D DDA (Amanatides-Woo) ─────────────────────────────────────
        private bool HasLineOfSight(Vector2Int from, Vector2Int to,
                                    float eyeY, float targetY,
                                    int row, int w, int h)
        {
            int x0 = from.x, y0 = from.y;
            int x1 = to.x,   y1 = to.y;
            int dx = x1 - x0;
            int dy = y1 - y0;
            int adx = dx < 0 ? -dx : dx;
            int ady = dy < 0 ? -dy : dy;
            int steps = adx > ady ? adx : ady;
            if (steps == 0) return true;

            // DDA stepping
            int stepX = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
            int stepY = dy > 0 ? 1 : (dy < 0 ? -1 : 0);

            // tDeltaX = how much t advances when we cross one cell boundary in X
            float tDeltaX = stepX != 0 ? 1f / adx : float.PositiveInfinity;
            float tDeltaY = stepY != 0 ? 1f / ady : float.PositiveInfinity;

            // tMaxX = t at which we cross the first X cell boundary from the origin cell
            // Origin cell centered at (x0 + 0.5, y0 + 0.5) in cell coordinates.
            float tMaxX = ComputeInitialT(x0, x1, stepX, dx);
            float tMaxY = ComputeInitialT(y0, y1, stepY, dy);

            int cx = x0, cy = y0;
            float t = 0f;

            for (int i = 0; i <= steps; i++)
            {
                // Check the current cell (skip origin).
                if (!(i == 0))
                {
                    if ((uint)cx >= (uint)w || (uint)cy >= (uint)h) return true; // ran off the grid edge
                    float cellMax = GetCellMaxHeight(cx, cy, row);
                    float lineY = Mathf.Lerp(eyeY, targetY, t) + KneeOffset;
                    if (cellMax > lineY) return false;
                }

                if (i == steps) break;

                if (tMaxX < tMaxY)
                {
                    t = tMaxX;
                    tMaxX += tDeltaX;
                    cx += stepX;
                }
                else
                {
                    t = tMaxY;
                    tMaxY += tDeltaY;
                    cy += stepY;
                }
            }
            return true;
        }

        private static float ComputeInitialT(int origin, int target, int step, int delta)
        {
            if (step == 0) return float.PositiveInfinity;
            int absDelta = delta < 0 ? -delta : delta;
            if (absDelta == 0) return float.PositiveInfinity;
            // Distance from the origin cell center to the first cell boundary in the step direction is always 0.5 cells.
            // t for the full traversal in this axis is 1.0, so t at the first boundary is 0.5 / |delta|.
            return 0.5f / absDelta;
        }

        // ── Debug visualisation ───────────────────────────────────────────────
        void OnDrawGizmos()
        {
            if (!DebugDrawMask || GridData == null) return;

            int gw = GridData.Width;
            int gh = GridData.Height;
            int mw = _allocatedMaskW;
            int mh = _allocatedMaskH;
            int up = _allocatedUpscale;
            float y = DebugDrawHeight;

            // Grid bounds outline
            Gizmos.color = new Color(1f, 1f, 0f, 0.5f);
            Vector3 c = new Vector3(0, y, 0);
            Vector3 size = new Vector3(gw * DebugDrawScale, 0.01f, gh * DebugDrawScale);
            Gizmos.DrawWireCube(c, size);

            // Per-cell debug (samples the top-left texel of each cell block).
            if (_pixels == null) return;
            if (mw <= 0 || mh <= 0) return;
            if (_pixels.Length != mw * mh) return;

            float cell = DebugDrawScale;
            for (int py = 0; py < gh; py++)
            {
                for (int px = 0; px < gw; px++)
                {
                    int maskIdx = (py * up) * mw + (px * up);
                    if ((uint)maskIdx >= (uint)_pixels.Length) continue;
                    Color c0 = _pixels[maskIdx];
                    if (c0.r <= 0.001f && c0.g <= 0.001f) continue;
                    float v = c0.r;
                    float e = c0.g;
                    Color col;
                    if (v > 0.5f) col = new Color(0f, 1f, 0f, 0.6f);          // visible
                    else if (e > 0.5f) col = new Color(1f, 1f, 0f, 0.4f);     // explored
                    else col = new Color(1f, 0f, 0f, 0.4f);
                    Vector3 world = new Vector3(
                        (px + 0.5f) - gw * 0.5f,
                        y + 0.02f,
                        (py + 0.5f) - gh * 0.5f) * cell;
                    Gizmos.color = col;
                    Gizmos.DrawCube(world, new Vector3(cell * 0.9f, 0.02f, cell * 0.9f));
                }
            }
        }
    }
}
