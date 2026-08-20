using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MooLucio.TileTerrain
{
    public partial class TileTerrainEditor
    {
        internal enum CliffTool { Up, Down, Target, Smudge, Erase }
        internal CliffTool cliffTool = CliffTool.Up;
        private int targetCliffLevel = 0;
        private HashSet<int> _cliffStrokeModifiedVertices = new HashSet<int>();
        private bool _strokeStartedOnWater = false;
        private sbyte _strokeWaterLevel = TileTerrainConstants.NoCliffLevel;
        private sbyte _strokeFloorLevel = TileTerrainConstants.NoCliffLevel;

        private void DrawCliffTools()
        {
            var paintIcon = EditorGUIUtility.IconContent(paintMode ? "d_PauseButton" : "d_PlayButton");
            paintIcon.text = paintMode ? "  Disable Paint Mode" : "  Enable Paint Mode";
            _paintBtnStyle.normal.textColor = paintMode ? new Color(1f, 0.4f, 0.4f) : new Color(1f, 0.7f, 0.2f);
            if (GUILayout.Button(paintIcon, _paintBtnStyle)) paintMode = !paintMode;

            EditorGUILayout.Space(4);
            DrawBrushShapeSelector();
            brushRadius = EditorGUILayout.Slider("Brush Radius", brushRadius, 0.5f, 20f);

            if (cliffTool == CliffTool.Smudge)
                brushStrength = EditorGUILayout.Slider("Brush Strength", brushStrength, 0f, 5f);

            EditorGUILayout.Space(4);

            const float btnMinW = 38f;
            var cUp = EditorGUIUtility.IconContent("d_icon dropdown open@2x"); cUp.text = " Raising";
            var cDown = EditorGUIUtility.IconContent("d_icon dropdown@2x"); cDown.text = " Lowering";
            var cTarget = EditorGUIUtility.IconContent("d_SceneLayersToggle"); cTarget.text = " Targeting";
            var cSmudge = EditorGUIUtility.IconContent("d_scenepicking_pickable_hover"); cSmudge.text = " Smudging";
            var cErase = EditorGUIUtility.IconContent("d_Grid.EraserTool"); cErase.text = " Erasing";

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(cliffTool == CliffTool.Up, cUp, _toolBtnStyle,
                GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true)))
                cliffTool = CliffTool.Up;
            if (GUILayout.Toggle(cliffTool == CliffTool.Down, cDown, _toolBtnStyle,
                GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true)))
                cliffTool = CliffTool.Down;
            if (GUILayout.Toggle(cliffTool == CliffTool.Target, cTarget, _toolBtnStyle,
                GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true)))
                cliffTool = CliffTool.Target;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(cliffTool == CliffTool.Smudge, cSmudge, _toolBtnStyle,
                GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true)))
                cliffTool = CliffTool.Smudge;
            if (GUILayout.Toggle(cliffTool == CliffTool.Erase, cErase, _toolBtnStyle,
                GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true)))
                cliffTool = CliffTool.Erase;
            EditorGUILayout.EndHorizontal();

            if (cliffTool == CliffTool.Target)
            {
                targetCliffLevel = EditorGUILayout.IntSlider("Target Cliff Level", targetCliffLevel, TileTerrainConstants.MinEditableCliff, TileTerrainConstants.MaxEditableCliff);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Paint: sets vertex cliff flag, spawns matching cliff mesh (bitmask 1–14).\n" +
                "Full cell (bitmask 15): flat quad raised by 1 unit per floor tier.\n" +
                "Erase: clears cliff flag; floor resets when all corners are cleared.",
                MessageType.Info);

            var terrain = (TileTerrain)target;
            if (terrain.CliffMeshFbx == null)
                EditorGUILayout.HelpBox("Assign CliffMeshFbx on the TileTerrain component.", MessageType.Warning);
        }

        // ── Cliff painting ───────────────────────────────────────────────────
        /// <summary>
        /// Applies the current cliff brush stroke to the grid around worldHit:
        /// raise/lower floor tiers, sync entanglement groups, clear overlapping
        /// props, and re-bake the affected chunks.
        /// </summary>
        private void PaintCliff(TileTerrain terrain, Vector3 worldHit)
        {
            var data = terrain.GridData;
            if (data == null || data.Vertices == null || data.Vertices.Count == 0) return;
            data.EnsureGridData();
            Undo.RecordObject(data, "Paint Cliff");

            Vector3 localHit = terrain.transform.InverseTransformPoint(worldHit);

            int w = data.Width;
            int h = data.Height;
            int row = w + 1;
            int xMin = Mathf.Max(0, Mathf.FloorToInt(localHit.x - brushRadius + w * 0.5f));
            int xMax = Mathf.Min(w, Mathf.CeilToInt(localHit.x + brushRadius + w * 0.5f));
            int zMin = Mathf.Max(0, Mathf.FloorToInt(localHit.z - brushRadius + h * 0.5f));
            int zMax = Mathf.Min(h, Mathf.CeilToInt(localHit.z + brushRadius + h * 0.5f));
            PrecomputeNeighborCache(data, w, h, row, xMin, xMax, zMin, zMax);

            bool changed = false;
            _propagationQueue.Clear();
            _modifiedVertices.Clear();

            bool hitWater = false;
            sbyte capturedWaterLevel = TileTerrainConstants.NoCliffLevel;

            // ── Remove any props (and its entanglement) touched by the brush ──
            bool anyRemoved = false;
            for (int i = data.Props.Count - 1; i >= 0; i--)
            {
                var a = data.Props[i];
                float dx = a.position.x - localHit.x;
                float dz = a.position.z - localHit.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                float hitRadius = brushRadius;
                if (a.footprintRadius > 0f)
                    hitRadius += a.footprintRadius;
                if (dist <= hitRadius)
                {
                    if (a.entanglementId >= 0)
                        data.RemoveEntanglementGroup(a.entanglementId);
                    data.Props.RemoveAt(i);
                    if (selectedPropInstance == i)
                        selectedPropInstance = -1;
                    else if (selectedPropInstance > i)
                        selectedPropInstance--;
                    anyRemoved = true;
                }
            }
            if (anyRemoved)
            {
                EditorUtility.SetDirty(data);
                RequestPropsRespawn(terrain);
                SceneView.RepaintAll();
            }

            for (int z = zMin; z <= zMax; z++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    int i = z * row + x;
                    var v = data.Vertices[i];

                    float dx = v.position.x - localHit.x;
                    float dz = v.position.z - localHit.z;
                    if (brushShape == BrushShape.Square)
                    {
                        if (Mathf.Abs(dx) > brushRadius || Mathf.Abs(dz) > brushRadius) continue;
                    }
                    else
                    {
                        if (dx * dx + dz * dz > brushRadius * brushRadius) continue;
                    }

                    // Water detection before any modification
                    if (v.IsWater && v.WaterLevel > capturedWaterLevel)
                    {
                        hitWater = true;
                        capturedWaterLevel = v.WaterLevel;
                    }

                    // Water border: strokes from land must never modify water vertices
                    // Exception: Up tool raises the floor — if it reaches the surface, drain the water
                    if (!_strokeStartedOnWater && v.IsWater)
                    {
                        if (cliffTool == CliffTool.Up)
                        {
                            int target = Mathf.Min(TileTerrainConstants.MaxEditableCliff, v.CliffByte + 1);
                            if (target < v.WaterLevel) continue;
                        }
                        else continue;
                    }

                    if (cliffTool == CliffTool.Up)
                    {
                        if (!_cliffStrokeModifiedVertices.Contains(i))
                        {
                            // From land: don't raise vertices at the water border
                            if (!_strokeStartedOnWater && _touchesWaterCache[i]) continue;

                            int target = Mathf.Min(TileTerrainConstants.MaxEditableCliff, v.CliffByte + 1);

                            if (v.IsWater && target >= v.WaterLevel)
                            {
                                v.IsWater = false;
                                v.WaterLevel = 0;
                                data.Vertices[i] = v;
                            }

                            _propagationQueue.Enqueue((i, target, 1));
                            _cliffStrokeModifiedVertices.Add(i);
                        }
                    }
                    else if (cliffTool == CliffTool.Down)
                    {
                        if (_strokeStartedOnWater && (!v.IsWater || v.WaterLevel != _strokeWaterLevel)) continue;

                        if (!_cliffStrokeModifiedVertices.Contains(i))
                        {
                            // From land: don't lower vertices at the water border
                            if (!_strokeStartedOnWater && _touchesWaterCache[i]) continue;

                            if (v.IsWater || _touchesWaterCache[i])
                            {
                                if (!IsSafeToCarve(data, i)) continue;
                            }

                            int target = Mathf.Max(TileTerrainConstants.MinEditableCliff, v.CliffByte - 1);

                            if (hitWater && v.IsWater)
                            {
                                if (target < capturedWaterLevel)
                                {
                                    v.WaterLevel = capturedWaterLevel;
                                    v.IsWater = true;
                                    data.Vertices[i] = v;
                                }
                            }

                            _propagationQueue.Enqueue((i, target, -1));
                            _cliffStrokeModifiedVertices.Add(i);
                        }
                    }
                    else if (cliffTool == CliffTool.Target)
                    {
                        if (_strokeStartedOnWater && (!v.IsWater || v.WaterLevel != _strokeWaterLevel)) continue;

                        if (!_cliffStrokeModifiedVertices.Contains(i))
                        {
                            int target = targetCliffLevel;

                            if (hitWater)
                            {
                                if (target >= capturedWaterLevel)
                                {
                                    v.IsWater = false;
                                    v.WaterLevel = 0;
                                }
                                else if (target < capturedWaterLevel)
                                {
                                    v.WaterLevel = capturedWaterLevel;
                                    v.IsWater = true;
                                }
                            }

                            data.Vertices[i] = v;
                            _propagationQueue.Enqueue((i, target, 0));
                            _cliffStrokeModifiedVertices.Add(i);
                        }
                    }
                    else if (cliffTool == CliffTool.Smudge)
                    {
                        if (_strokeStartedOnWater && (!v.IsWater || v.WaterLevel != _strokeWaterLevel)) continue;

                        if (!_cliffStrokeModifiedVertices.Contains(i))
                        {
                            if (v.IsWater || _touchesWaterCache[i])
                            {
                                if (!IsSafeToCarve(data, i)) continue;
                            }

                            int nx = Mathf.Clamp(x + Random.Range(-1, 2), 0, w);
                            int nz = Mathf.Clamp(z + Random.Range(-1, 2), 0, h);
                            var neighbor = data.Vertices[nz * row + nx];
                            if (Random.value < brushStrength * 0.2f)
                            {
                                int target = neighbor.CliffByte;
                                _propagationQueue.Enqueue((i, target, 0));
                                _cliffStrokeModifiedVertices.Add(i);
                            }
                        }
                    }
                    else
                    {
                        if (!_strokeStartedOnWater && v.IsWater) continue;

                        if (!_cliffStrokeModifiedVertices.Contains(i))
                        {
                            if (v.IsWater)
                            {
                                v.IsWater = false;
                                v.WaterLevel = 0;
                                data.Vertices[i] = v;
                            }
                            int target = 0;
                            _propagationQueue.Enqueue((i, target, 0));
                            _cliffStrokeModifiedVertices.Add(i);
                        }
                    }
                }
            }

            while (_propagationQueue.Count > 0)
            {
                var item = _propagationQueue.Dequeue();
                int idx = item.index;
                int target = item.level;
                int direction = item.direction;

                var v = data.Vertices[idx];
                bool vChanged = false;

                if (direction == 1)
                {
                    if (v.CliffByte < target) { v.CliffByte = (sbyte)target; vChanged = true; }
                    if (v.CliffHalfStep) { v.CliffHalfStep = false; vChanged = true; }
                }
                else if (direction == -1)
                {
                    if (v.CliffByte > target) { v.CliffByte = (sbyte)target; vChanged = true; }
                    if (v.CliffHalfStep) { v.CliffHalfStep = false; vChanged = true; }
                }
                else
                {
                    if (v.CliffByte != target) { v.CliffByte = (sbyte)target; vChanged = true; }
                    if (v.CliffHalfStep) { v.CliffHalfStep = false; vChanged = true; }

                    if (cliffTool == CliffTool.Erase && v.IsWater && _strokeStartedOnWater)
                    {
                        v.IsWater = false;
                        v.WaterLevel = 0;
                        vChanged = true;
                    }
                }

                if (v.IsWater && v.CliffByte >= v.WaterLevel)
                {
                    v.IsWater = false;
                    v.WaterLevel = 0;
                    vChanged = true;
                }

                if (vChanged)
                {
                    data.Vertices[idx] = v;
                    _modifiedVertices.Add(idx);
                    changed = true;

                    int vx = idx % row;
                    int vz = idx / row;
                    for (int nz = vz - 1; nz <= vz + 1; nz++)
                    {
                        for (int nx = vx - 1; nx <= vx + 1; nx++)
                        {
                            if (nx == vx && nz == vz) continue;
                            if (nx < 0 || nz < 0 || nx > w || nz > h) continue;

                            int nIdx = nz * row + nx;
                            var nv = data.Vertices[nIdx];

                            if (cliffTool != CliffTool.Erase && _strokeStartedOnWater && (!nv.IsWater || nv.WaterLevel != _strokeWaterLevel)) continue;

                            int diff = target - nv.CliffByte;
                            bool shouldAdjust = false;
                            int adjustDir = 0;

                            // Water border: max 1-floor step for any vertex at the land-water interface
                            bool atBorder = IsBoundary(data, idx) || IsBoundary(data, nIdx);
                            int maxStep = atBorder ? 1 : 2;

                            if (direction == 1)
                            {
                                if (diff > maxStep)
                                {
                                    shouldAdjust = true;
                                    adjustDir = 1;
                                }
                            }
                            else if (direction == -1)
                            {
                                if (diff < -maxStep)
                                {
                                    shouldAdjust = true;
                                    adjustDir = -1;
                                }
                            }
                            else
                            {
                                if (Mathf.Abs(diff) > maxStep)
                                {
                                    shouldAdjust = true;
                                    adjustDir = diff > 0 ? 1 : -1;
                                }
                            }

                            // Land-origin strokes: skip water neighbors unless raising for step smoothing
                            if (!_strokeStartedOnWater && nv.IsWater)
                            {
                                if (!(shouldAdjust && adjustDir == 1)) continue;
                            }

                            if (shouldAdjust)
                            {
                                if (nv.IsWater || TouchesWater(data, w, h, row, nx, nz))
                                {
                                    if (!IsSafeToCarve(data, nIdx)) continue;
                                }

                                if (v.IsWater)
                                {
                                    nv.IsWater = true;
                                    nv.WaterLevel = v.WaterLevel;
                                    data.Vertices[nIdx] = nv;
                                    _modifiedVertices.Add(nIdx);
                                }

                                int nextTarget = Mathf.Clamp(target - maxStep * adjustDir, TileTerrainConstants.MinEditableCliff, TileTerrainConstants.MaxEditableCliff);
                                _propagationQueue.Enqueue((nIdx, nextTarget, direction));
                            }
                        }
                    }
                }
            }

            // Second-pass: repair any remaining cliff mismatches at full convergence.
            // Overrides water border protection — cliff connectivity takes priority.
            // Skip for Erase tool — its explicit 0-target + propagation handles smoothing.
            if (cliffTool != CliffTool.Erase)
            {
                var repairQueue = new HashSet<int>(_modifiedVertices);
                bool anyRepair = false;
                int maxIter = 10;

                for (int iter = 0; iter < maxIter && repairQueue.Count > 0; iter++)
                {
                    var batch = new List<int>(repairQueue);
                    repairQueue.Clear();

                    foreach (int idx in batch)
                    {
                        int vx = idx % row;
                        int vz = idx / row;
                        var v = data.Vertices[idx];

                        for (int nz = vz - 1; nz <= vz + 1; nz++)
                        {
                            for (int nx = vx - 1; nx <= vx + 1; nx++)
                            {
                                if (nx == vx && nz == vz) continue;
                                if (nx < 0 || nz < 0 || nx > w || nz > h) continue;
                                int nIdx = nz * row + nx;
                                var nv = data.Vertices[nIdx];

                                int diff = v.CliffByte - nv.CliffByte;
                                // Water border: max 1-floor step for any vertex at the land-water interface
                                bool atBorder = IsBoundary(data, idx) || IsBoundary(data, nIdx);
                                int maxStep = atBorder ? 1 : 2;
                                if (Mathf.Abs(diff) <= maxStep) continue;

                                int higher = v.CliffByte > nv.CliffByte ? v.CliffByte : nv.CliffByte;
                                int lowerIdx = v.CliffByte > nv.CliffByte ? nIdx : idx;
                                int target = Mathf.Clamp(higher - maxStep, TileTerrainConstants.MinEditableCliff, TileTerrainConstants.MaxEditableCliff);

                                var lowerV = data.Vertices[lowerIdx];
                                if (lowerV.CliffByte >= target) continue;

                                lowerV.CliffByte = (sbyte)target;
                                if (lowerV.CliffHalfStep) lowerV.CliffHalfStep = false;

                                if (lowerV.IsWater && lowerV.CliffByte >= lowerV.WaterLevel)
                                {
                                    lowerV.IsWater = false;
                                    lowerV.WaterLevel = 0;
                                }

                                data.Vertices[lowerIdx] = lowerV;
                                anyRepair = true;

                                _modifiedVertices.Add(lowerIdx);
                                repairQueue.Add(lowerIdx);
                            }
                        }
                    }
                }

                if (anyRepair) changed = true;
            }

            if (changed)
            {
                _affectedQuadIndices.Clear();
                foreach (int vIdx in _modifiedVertices)
                {
                    int vx = vIdx % row;
                    int vz = vIdx / row;
                    for (int qz = vz - 1; qz <= vz; qz++)
                    for (int qx = vx - 1; qx <= vx; qx++)
                        if (qx >= 0 && qz >= 0 && qx < w && qz < h)
                            _affectedQuadIndices.Add(qz * w + qx);
                }

                foreach (int qi in _affectedQuadIndices)
                {
                    QuadData quad = data.Quads[qi];
                    int minFloor = TileTerrainConstants.MaxCliffLevel;
                    for (int j = 0; j < 4; j++)
                    {
                        int val = data.Vertices[quad.vertexIds[j]].CliffByte;
                        if (val < minFloor) minFloor = val;
                    }

                    if (quad.floor != minFloor)
                    {
                        quad.floor = minFloor;
                        data.Quads[qi] = quad;
                    }
                }

                // Revalidate halfStep vertices affected by cliff modifications
                var rampsToCheck = new HashSet<int>();
                foreach (int vi in _modifiedVertices)
                {
                    if (data.Vertices[vi].CliffHalfStep) rampsToCheck.Add(vi);
                    int vx = vi % row, vz = vi / row;
                    for (int nz = vz - 1; nz <= vz + 1; nz++)
                    for (int nx = vx - 1; nx <= vx + 1; nx++)
                    {
                        if (nx < 0 || nz < 0 || nx > w || nz > h) continue;
                        int ni = nz * row + nx;
                        if (data.Vertices[ni].CliffHalfStep) rampsToCheck.Add(ni);
                    }
                }
                foreach (int vi in rampsToCheck)
                {
                    var v = data.Vertices[vi];
                    if (!v.CliffHalfStep) continue;
                    int vx = vi % row, vz = vi / row;
                    int[] nnx = { vx - 1, vx + 1, vx, vx };
                    int[] nnz = { vz, vz, vz - 1, vz + 1 };
                    bool valid = false;
                    for (int n = 0; n < 4; n++)
                    {
                        if (nnx[n] < 0 || nnz[n] < 0 || nnx[n] > w || nnz[n] > h) continue;
                        if (data.Vertices[nnz[n] * row + nnx[n]].CliffByte == v.CliffByte + 1)
                        { valid = true; break; }
                    }
                    if (!valid)
                    { v.CliffHalfStep = false; data.Vertices[vi] = v; continue; }
                    for (int qz = vz - 1; qz <= vz; qz++)
                    for (int qx = vx - 1; qx <= vx; qx++)
                    {
                        if (qx < 0 || qz < 0 || qx >= w || qz >= h) continue;
                        var quad = data.Quads[qz * w + qx];
                        int qMin = TileTerrainConstants.MaxCliffLevel, qMax = 0;
                        for (int j = 0; j < 4; j++)
                        {
                            int cb = data.Vertices[quad.vertexIds[j]].CliffByte;
                            if (cb < qMin) qMin = cb;
                            if (cb > qMax) qMax = cb;
                        }
                        if (qMax - qMin > 1)
                        { v.CliffHalfStep = false; data.Vertices[vi] = v; break; }
                    }
                }

                _cliffDataChanged = true;
                MarkDirtyChunks(terrain, xMin, xMax, zMin, zMax);
                EditorUtility.SetDirty(data);
                RequestMeshRebuild(terrain);
            }
        }

        // ── Ramp mode ────────────────────────────────────────────────────────
        internal enum RampTool { Paint, Erase }
        internal RampTool rampTool = RampTool.Paint;

        private void DrawRampTools()
        {
            var paintIcon = EditorGUIUtility.IconContent(paintMode ? "d_PauseButton" : "d_PlayButton");
            paintIcon.text = paintMode ? "  Disable Paint Mode" : "  Enable Paint Mode";
            _paintBtnStyle.normal.textColor = paintMode ? new Color(1f, 0.4f, 0.4f) : new Color(1f, 0.7f, 0.2f);
            if (GUILayout.Button(paintIcon, _paintBtnStyle)) paintMode = !paintMode;

            EditorGUILayout.Space(4);
            DrawBrushShapeSelector();
            brushRadius = EditorGUILayout.Slider("Brush Radius", brushRadius, 0.5f, 20f);

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            {
                const float btnMinW = 38f;
                var cPaint = EditorGUIUtility.IconContent("d_DragArrow"); cPaint.text = " Set";
                if (GUILayout.Toggle(rampTool == RampTool.Paint, cPaint, _toolBtnStyle,
                    GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true), GUILayout.Height(32)))
                    rampTool = RampTool.Paint;

                var cErase = EditorGUIUtility.IconContent("d_Grid.EraserTool"); cErase.text = " Erase";
                if (GUILayout.Toggle(rampTool == RampTool.Erase, cErase, _toolBtnStyle,
                    GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true), GUILayout.Height(32)))
                    rampTool = RampTool.Erase;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            if (rampTool == RampTool.Paint)
                EditorGUILayout.HelpBox(
                    "Click on a lower vertex adjacent to a 1-floor cliff edge to " +
                    "toggle a ramp. Yellow gizmos show valid targets in the Scene view.",
                    MessageType.Info);
            else
                EditorGUILayout.HelpBox(
                    "Removes ramp (half-step) from vertices under the brush.",
                    MessageType.Info);

            var terrain = (TileTerrain)target;
            if (terrain.RampMeshFbx == null)
                EditorGUILayout.HelpBox("Assign RampMeshFbx on the TileTerrain component.", MessageType.Warning);
        }

        private void PaintRamp(TileTerrain terrain, Vector3 worldHit)
        {
            var data = terrain.GridData;
            if (data == null || data.Vertices == null || data.Vertices.Count == 0) return;
            data.EnsureGridData();
            Undo.RecordObject(data, "Paint Ramp");

            int w = data.Width;
            int h = data.Height;
            int row = w + 1;

            var localHit = terrain.transform.InverseTransformPoint(worldHit);
            float gx = localHit.x + w / 2f;
            float gz = localHit.z + h / 2f;
            int cx = Mathf.RoundToInt(gx);
            int cz = Mathf.RoundToInt(gz);

            int r = Mathf.CeilToInt(brushRadius);
            int xMin = Mathf.Max(0, cx - r);
            int xMax = Mathf.Min(w, cx + r);
            int zMin = Mathf.Max(0, cz - r);
            int zMax = Mathf.Min(h, cz + r);

            bool changed = false;
            var modifiedVerts = new HashSet<int>();
            var modifiedQuadFloors = new HashSet<int>();
            var freshlyPainted = new HashSet<int>();

            for (int z = zMin; z <= zMax; z++)
            for (int x = xMin; x <= xMax; x++)
            {
                int i = z * row + x;
                var v = data.Vertices[i];
                if (modifiedVerts.Contains(i)) continue;

                float dx = v.position.x - localHit.x;
                float dz = v.position.z - localHit.z;
                if (brushShape == BrushShape.Square)
                {
                    if (Mathf.Abs(dx) > brushRadius || Mathf.Abs(dz) > brushRadius) continue;
                }
                else
                {
                    if (dx * dx + dz * dz > brushRadius * brushRadius) continue;
                }

                if (rampTool == RampTool.Erase)
                {
                    if (v.CliffHalfStep)
                    {
                        v.CliffHalfStep = false;
                        data.Vertices[i] = v;
                        modifiedVerts.Add(i);
                        changed = true;
                    }
                    continue;
                }

                // Paint: check if this vertex has a neighbor exactly 1 floor higher
                bool validRamp = false;
                int[] nnx = { x - 1, x + 1, x, x };
                int[] nnz = { z, z, z - 1, z + 1 };
                for (int n = 0; n < 4; n++)
                {
                    if (nnx[n] < 0 || nnz[n] < 0 || nnx[n] > w || nnz[n] > h) continue;
                    int nIdx = nnz[n] * row + nnx[n];
                    if (data.Vertices[nIdx].CliffByte == v.CliffByte + 1)
                    {
                        validRamp = true;
                        break;
                    }
                }

                if (validRamp)
                {
                    // Validate all 4 quads around this vertex span at most 1 floor
                    bool validQuads = true;
                    for (int qz = z - 1; qz <= z && validQuads; qz++)
                    for (int qx = x - 1; qx <= x && validQuads; qx++)
                    {
                        if (qx < 0 || qz < 0 || qx >= w || qz >= h) continue;
                        int qi = qz * w + qx;
                        var quad = data.Quads[qi];
                        int qMin = TileTerrainConstants.MaxCliffLevel, qMax = 0;
                        for (int j = 0; j < 4; j++)
                        {
                            int cb = data.Vertices[quad.vertexIds[j]].CliffByte;
                            if (cb < qMin) qMin = cb;
                            if (cb > qMax) qMax = cb;
                        }
                        if (qMax - qMin > 1)
                            validQuads = false;
                    }

                    if (validQuads)
                    {
                        v.CliffHalfStep = true;
                        data.Vertices[i] = v;
                        modifiedVerts.Add(i);
                        freshlyPainted.Add(i);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                foreach (int vi in modifiedVerts)
                {
                    int vx = vi % row;
                    int vz = vi / row;
                    for (int qz = vz - 1; qz <= vz; qz++)
                    for (int qx = vx - 1; qx <= vx; qx++)
                    {
                        if (qx >= 0 && qz >= 0 && qx < w && qz < h)
                            modifiedQuadFloors.Add(qz * w + qx);
                    }
                }

                foreach (int qi in modifiedQuadFloors)
                {
                    QuadData quad = data.Quads[qi];
                    int minFloor = TileTerrainConstants.MaxCliffLevel;
                    for (int j = 0; j < 4; j++)
                    {
                        int val = data.Vertices[quad.vertexIds[j]].CliffByte;
                        if (val < minFloor) minFloor = val;
                    }
                    if (quad.floor != minFloor)
                    {
                        quad.floor = minFloor;
                        data.Quads[qi] = quad;
                    }
                }

                // Cleanup: if any modified quad has halfStep but spans >1 floor,
                // clear all halfStep in that quad (invalid ramp configuration)
                foreach (int qi in modifiedQuadFloors)
                {
                    QuadData quad = data.Quads[qi];
                    int minFloor = TileTerrainConstants.MaxCliffLevel, maxFloor = 0;
                    for (int j = 0; j < 4; j++)
                    {
                        int cb = data.Vertices[quad.vertexIds[j]].CliffByte;
                        if (cb < minFloor) minFloor = cb;
                        if (cb > maxFloor) maxFloor = cb;
                    }
                    if (maxFloor - minFloor <= 1) continue;
                    bool hadAny = false;
                    for (int j = 0; j < 4; j++)
                    {
                        var v = data.Vertices[quad.vertexIds[j]];
                        if (v.CliffHalfStep)
                        {
                            v.CliffHalfStep = false;
                            data.Vertices[quad.vertexIds[j]] = v;
                            hadAny = true;
                        }
                    }
                    if (hadAny)
                        modifiedVerts.UnionWith(quad.vertexIds);
                }

                // Cleanup: remove isolated halfStep vertices (no cardinal neighbor → corner ramp)
                foreach (int vi in freshlyPainted)
                {
                    var v = data.Vertices[vi];
                    if (!v.CliffHalfStep) continue;
                    int vx = vi % row;
                    int vz = vi / row;
                    int[] nnx = { vx - 1, vx + 1, vx, vx };
                    int[] nnz = { vz, vz, vz - 1, vz + 1 };
                    bool hasNeighbor = false;
                    for (int n = 0; n < 4; n++)
                    {
                        if (nnx[n] < 0 || nnz[n] < 0 || nnx[n] > w || nnz[n] > h) continue;
                        if (data.Vertices[nnz[n] * row + nnx[n]].CliffHalfStep)
                        { hasNeighbor = true; break; }
                    }
                    if (!hasNeighbor)
                    {
                        v.CliffHalfStep = false;
                        data.Vertices[vi] = v;
                    }
                }

                _cliffDataChanged = true;
                MarkDirtyChunks(terrain, xMin, xMax, zMin, zMax);
                EditorUtility.SetDirty(data);
                RequestMeshRebuild(terrain);
            }
        }
    }
}
