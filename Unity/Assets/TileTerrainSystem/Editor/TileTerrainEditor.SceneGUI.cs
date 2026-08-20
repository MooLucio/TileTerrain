using UnityEditor;
using UnityEngine;

namespace MooLucio.TileTerrain
{
    public partial class TileTerrainEditor
    {
        private int _undoGroup = -1;

        private void BeginStroke(string label)
        {
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(label);
        }

        private void EndStroke()
        {
            if (_undoGroup >= 0)
            {
                Undo.CollapseUndoOperations(_undoGroup);
                _undoGroup = -1;
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            var terrain = (TileTerrain)target;
            if (terrain == null || terrain.GridData == null) return;

            var mr = terrain.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                EditorUtility.SetSelectedRenderState(mr, EditorSelectedRenderState.Hidden);
            }

            terrain.GridData.EnsureGridData();
            if (terrain.GridData.Width <= 0 || terrain.GridData.Height <= 0) return;

            // Safety net: regenerate mesh if it doesn't exist (e.g. AssetDatabase wasn't ready in OnEnable).
            bool hasChunks = false;
            foreach (Transform c in terrain.transform)
                if (c.name.StartsWith("TTChunk")) { hasChunks = true; break; }
            if (!hasChunks && terrain.TileMaterial == null)
            {
                AutoAssignMaterial(terrain);
                terrain.SyncTexturesFromPalette();
            }
            if (!hasChunks && terrain.TileMaterial != null)
                terrain.GenerateMesh();

            Event e = Event.current;

            _paintControlID = GUIUtility.GetControlID(PaintControlHint, FocusType.Passive);

            if (paintMode)
                HandleUtility.AddDefaultControl(_paintControlID);

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!RaycastTerrain(terrain, ray, out Vector3 worldHit)) return;
            Vector3 localHit = terrain.transform.InverseTransformPoint(worldHit);

            int snapIdx = FindNearestVertexIndex(terrain.GridData, localHit);
            Vector3 snapLocal = Vector3.zero;
            Vector3 snapWorld = worldHit;

            if (snapIdx >= 0)
            {
                var sv = terrain.GridData.Vertices[snapIdx];
                int w = terrain.GridData.Width;
                int h = terrain.GridData.Height;
                float floorY = GetVertexFloorOffset(terrain.GridData, snapIdx % (w + 1), snapIdx / (w + 1), w, h);
                snapLocal = new Vector3(sv.position.x, sv.height + floorY, sv.position.z);
                snapWorld = terrain.transform.TransformPoint(snapLocal);
            }

            float eps = 0.05f;
            Vector3 elevated = snapWorld + terrain.transform.up * eps;

            // ── Props tools that work without paintMode toggle ──
            if (editorMode == EditorMode.Props
                && e.type == EventType.MouseDown && e.button == 0
                && !e.alt && !e.shift && !e.control)
            {
                if (propsTool == PropsTool.Select || propsTool == PropsTool.Remove || propsTool == PropsTool.Erase)
                {
                    PaintProps(terrain, snapWorld);
                    e.Use();
                }
            }

            // ── Props Rotate/Scale work without paintMode (need MouseDrag) ──
            if (editorMode == EditorMode.Props
                && !paintMode
                && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag)
                && e.button == 0
                && !e.alt && !e.shift && !e.control)
            {
                if (propsTool == PropsTool.Rotate || propsTool == PropsTool.Scale)
                {
                    PaintProps(terrain, snapWorld);
                    e.Use();
                }
            }

            if (paintMode
                && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag)
                && e.button == 0
                && !e.alt && !e.shift && !e.control)
            {
                if (e.type == EventType.MouseDown)
                {
                    string modeLabel = editorMode.ToString();
                    BeginStroke($"Paint {modeLabel}");

                    if (editorMode != EditorMode.Props)
                    {
                        _cliffStrokeModifiedVertices.Clear();
                        _dirtyChunks.Clear();

                        if (snapIdx >= 0)
                        {
                            var centerV = terrain.GridData.Vertices[snapIdx];
                            _strokeStartedOnWater = centerV.IsWater;
                            _strokeWaterLevel = centerV.WaterLevel;
                            _strokeFloorLevel = centerV.CliffByte;
                        }
                    }
                }

                if (editorMode == EditorMode.Height) PaintHeight(terrain, snapWorld);
                else if (editorMode == EditorMode.Texture)
                {
                    if (textureTool == TextureTool.Fill && e.type == EventType.MouseDown)
                        FillTexture(terrain, snapWorld);
                    else if (textureTool != TextureTool.Fill)
                        PaintTexture(terrain, snapWorld);
                }
                else if (editorMode == EditorMode.Cliff) PaintCliff(terrain, snapWorld);
                else if (editorMode == EditorMode.Ramp) PaintRamp(terrain, snapWorld);
                else if (editorMode == EditorMode.Water) PaintWater(terrain, snapWorld);
                else if (editorMode == EditorMode.Props
                         && (propsTool == PropsTool.Place
                             || propsTool == PropsTool.Paint
                             || propsTool == PropsTool.Erase
                             || propsTool == PropsTool.Rotate
                             || propsTool == PropsTool.Scale))
                    PaintProps(terrain, snapWorld);

                if (editorMode == EditorMode.Height || editorMode == EditorMode.Cliff || editorMode == EditorMode.Ramp)
                {
                    terrain.PinPropsToTerrain();
                    RequestPropsRespawn(terrain);
                }
                e.Use();
            }
            else if (paintMode && e.type == EventType.MouseUp && e.button == 0)
            {
                EndStroke();
                if (editorMode == EditorMode.Height || editorMode == EditorMode.Cliff || editorMode == EditorMode.Ramp)
                    terrain.PinPropsToTerrain();
                RequestPropsRespawn(terrain, true);
            }

            HandleShortcuts(e, sceneView);

            if ((paintMode || editorMode == EditorMode.Props) && (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.MouseDown || e.type == EventType.MouseUp))
            {
                if (e.mousePosition != _lastMousePos || e.type != EventType.MouseMove || _isResizingBrush)
                {
                    _lastMousePos = e.mousePosition;
                    sceneView.Repaint();
                }
            }

            if (e.type == EventType.Repaint)
            {
                if (terrain.ShowGrid) DrawGrid(terrain);
                if (terrain.ShowQuadVertexIds) DrawQuadVertexIds(terrain);
                DrawBorderLine(terrain);

                if (editorMode == EditorMode.Props)
                {
                    if (propsTool == PropsTool.Place)
                        DrawPropsPreview(terrain, localHit);
                    if (propsTool == PropsTool.Paint || propsTool == PropsTool.Erase)
                        DrawBrush(terrain, localHit, elevated);
                    DrawPropsSelection(terrain);
                    DrawPropsModeIndicators(terrain);
                }
                else if (paintMode)
                {
                    DrawBrush(terrain, localHit, elevated);
                }
                if (paintMode && editorMode == EditorMode.Ramp)
                    DrawRampTargets(terrain, localHit);
            }

            DrawShortcutsHUD(sceneView);
        }

        private bool _showShortcuts = false;

        private void HandleShortcuts(Event e, SceneView sceneView)
        {
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.B)
            {
                _isResizingBrush = true;
                _brushResizeStartVal = brushRadius;
                _brushResizeStartMouse = e.mousePosition;
                e.Use();
            }
            if (e.type == EventType.KeyUp && e.keyCode == KeyCode.B)
            {
                _isResizingBrush = false;
                e.Use();
            }

            if (_isResizingBrush && e.type == EventType.MouseDrag)
            {
                float diff = (e.mousePosition.x - _brushResizeStartMouse.x) * 0.1f;
                brushRadius = Mathf.Max(0.1f, _brushResizeStartVal + diff);
                e.Use();
                GUI.changed = true;
            }

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.LeftBracket) { brushRadius = Mathf.Max(0.1f, brushRadius - 0.5f); e.Use(); }
                if (e.keyCode == KeyCode.RightBracket) { brushRadius = Mathf.Min(20f, brushRadius + 0.5f); e.Use(); }

                if (e.keyCode == KeyCode.Alpha1) { editorMode = EditorMode.Height; e.Use(); }
                if (e.keyCode == KeyCode.Alpha2) { editorMode = EditorMode.Texture; e.Use(); }
                if (e.keyCode == KeyCode.Alpha3) { editorMode = EditorMode.Cliff; e.Use(); }
                if (e.keyCode == KeyCode.Alpha4) { editorMode = EditorMode.Water; e.Use(); }
                if (e.keyCode == KeyCode.Alpha5) { editorMode = EditorMode.Props; e.Use(); }
                if (e.keyCode == KeyCode.Q && editorMode == EditorMode.Props) { propsTool = PropsTool.Place; e.Use(); }
                if (e.keyCode == KeyCode.W && editorMode == EditorMode.Props) { propsTool = PropsTool.Paint; e.Use(); }
                if (e.keyCode == KeyCode.E && editorMode == EditorMode.Props) { propsTool = PropsTool.Select; e.Use(); }
                if (e.keyCode == KeyCode.D && editorMode == EditorMode.Props) { propsTool = PropsTool.Remove; e.Use(); }
                if (e.keyCode == KeyCode.R && editorMode == EditorMode.Props) { propsTool = PropsTool.Rotate; e.Use(); }
                if (e.keyCode == KeyCode.T && editorMode == EditorMode.Props) { propsTool = PropsTool.Scale; e.Use(); }
                if (e.keyCode == KeyCode.F && editorMode == EditorMode.Props) { propsTool = PropsTool.Erase; e.Use(); }
                if (e.keyCode == KeyCode.G && editorMode == EditorMode.Props) { propsSnapToGrid = !propsSnapToGrid; e.Use(); }
                if (e.keyCode == KeyCode.Delete && editorMode == EditorMode.Props && selectedPropInstance >= 0)
                {
                    var terrain = (TileTerrain)target;
                    RemovePropInstance(terrain, selectedPropInstance);
                    e.Use();
                }

                if (e.keyCode == KeyCode.S && !e.control) { paintMode = !paintMode; e.Use(); }

                if (e.keyCode == KeyCode.M) { brushShape = brushShape == BrushShape.Circle ? BrushShape.Square : BrushShape.Circle; e.Use(); }

                if (e.keyCode == KeyCode.H) { _showShortcuts = !_showShortcuts; e.Use(); }
            }
        }

        private void DrawShortcutsHUD(SceneView sceneView)
        {
            if (!_showShortcuts) return;

            Handles.BeginGUI();

            float w = 300;
            float h = 300;
            Rect rect = new Rect((sceneView.position.width - w) * 0.5f, (sceneView.position.height - h) * 0.5f, w, h);

            GUILayout.BeginArea(rect, GUI.skin.box);
            EditorGUILayout.LabelField("Keyboard Shortcuts", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("1-5", "Switch mode (H/T/C/W/A)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("S", "Toggle paint mode", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("[ / ]", "Decrease / Increase brush", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("B + Drag", "Resize brush (mouse)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("M", "Toggle brush shape", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("H", "Toggle this help", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("--- Paint Modes ---", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Height", "Raise/Lower/Target/Smooth/Noise", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Texture", "Paint/Smudge/Erase/Fill", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Cliff", "Up/Down/Target/Smudge/Erase", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Water", "Flood paint", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Props", "Q=Pl W=Pa E=Sel D=Rem R=Rot T=Scl F=Ers", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("G", "Toggle props snap-to-grid", EditorStyles.miniLabel);
            GUILayout.EndArea();

            Handles.EndGUI();
        }

        private int FindNearestVertexIndex(TileTerrainGridData data, Vector3 localPos)
        {
            if (data.Vertices == null || data.Vertices.Count == 0) return -1;

            int w = data.Width;
            int h = data.Height;
            int row = w + 1;

            float gx = localPos.x + w / 2f;
            float gz = localPos.z + h / 2f;

            int ix = Mathf.Clamp(Mathf.RoundToInt(gx), 0, w);
            int iz = Mathf.Clamp(Mathf.RoundToInt(gz), 0, h);

            return iz * row + ix;
        }

        private void DrawGrid(TileTerrain terrain)
        {
            var data = terrain.GridData;
            if (data == null || data.Vertices == null || data.Vertices.Count == 0) return;

            int w = data.Width;
            int h = data.Height;
            int row = w + 1;
            int expectedCount = data.Vertices.Count;

            if (_gridDirty || _cachedGridPositions == null || _cachedHLines == null || _cachedVLines == null ||
                _cachedGridPositions.Length != expectedCount || _cachedHLines.Length != h + 1 || _cachedVLines.Length != w + 1)
            {
                _cachedGridPositions = new Vector3[expectedCount];
                _cachedHLines = new Vector3[h + 1][];
                _cachedVLines = new Vector3[w + 1][];
                for (int z = 0; z <= h; z++) _cachedHLines[z] = new Vector3[w + 1];
                for (int x = 0; x <= w; x++) _cachedVLines[x] = new Vector3[h + 1];

                for (int z = 0; z <= h; z++)
                for (int x = 0; x <= w; x++)
                {
                    int vi = z * row + x;
                    var v = data.Vertices[vi];
                    float floorY = GetVertexFloorOffset(data, x, z, w, h);
                    float terrainY = v.height + floorY;
                    float waterY = (v.WaterLevel - 0.5f) * TileTerrainCliff.CliffHeight;
                    float yPos = v.IsWater ? Mathf.Max(waterY, terrainY) : terrainY;
                    var wp = terrain.transform.TransformPoint(new Vector3(v.position.x, yPos + 0.05f, v.position.z));
                    _cachedGridPositions[vi] = wp;
                    _cachedHLines[z][x] = wp;
                    _cachedVLines[x][z] = wp;
                }
                _gridDirty = false;
            }

            var prevZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

            for (int z = 0; z <= h; z++)
            {
                Handles.color = (z % 4 == 0) ? terrain.Grid4x4Color : terrain.GridColor;
                Handles.DrawPolyLine(_cachedHLines[z]);
            }
            for (int x = 0; x <= w; x++)
            {
                Handles.color = (x % 4 == 0) ? terrain.Grid4x4Color : terrain.GridColor;
                Handles.DrawPolyLine(_cachedVLines[x]);
            }

            Handles.zTest = prevZTest;
        }

        private void DrawQuadVertexIds(TileTerrain terrain)
        {
            var data = terrain.GridData;
            if (data == null || data.Quads == null || data.Quads.Count == 0 || data.Vertices == null) return;

            var quad = data.Quads[0];

            var prevZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = Color.yellow }
            };

            for (int vi = 0; vi < 4; vi++)
            {
                int globalVid = quad.vertexIds[vi];
                var v = data.Vertices[globalVid];
                Vector3 localPos = new Vector3(v.position.x, v.height + 0.2f, v.position.z);
                Vector3 worldPos = terrain.transform.TransformPoint(localPos);
                Handles.Label(worldPos, vi.ToString(), labelStyle);
            }

            Handles.zTest = prevZTest;
        }

        private static float GetVertexFloorOffset(TileTerrainGridData data, int vx, int vz, int w, int h)
        {
            if (data.VertexFloorOffset != null)
            {
                int row = w + 1;
                return data.VertexFloorOffset[vz * row + vx];
            }
            float maxOffset = TileTerrainConstants.NoFloorOffset;
            bool anyQuad = false;
            for (int qz = vz - 1; qz <= vz; qz++)
            {
                for (int qx = vx - 1; qx <= vx; qx++)
                {
                    if (qx < 0 || qz < 0 || qx >= w || qz >= h) continue;
                    var quad = data.Quads[qz * w + qx];
                    anyQuad = true;

                    int cornerIdx = -1;
                    if (qx == vx && qz == vz) cornerIdx = 0;
                    else if (qx == vx - 1 && qz == vz) cornerIdx = 1;
                    else if (qx == vx && qz == vz - 1) cornerIdx = 2;
                    else if (qx == vx - 1 && qz == vz - 1) cornerIdx = 3;

                    float floorAccum = quad.floor;
                    int maxV = TileTerrainConstants.NoCliffLevel;
                    for (int j = 0; j < 4; j++)
                        if (data.Vertices[quad.vertexIds[j]].CliffByte > maxV)
                            maxV = data.Vertices[quad.vertexIds[j]].CliffByte;

                    for (int level = quad.floor; level < maxV; level++)
                    {
                        byte mask = TileTerrainBitmask.CalculateCliffMaskAtLevel(data, quad, level);
                        if (cornerIdx != -1 && (mask & (1 << cornerIdx)) != 0)
                            floorAccum += 1f;
                    }

                    maxOffset = Mathf.Max(maxOffset, floorAccum * TileTerrainCliff.CliffHeight);
                }
            }
            return anyQuad ? maxOffset : 0f;
        }

        private void DrawRampTargets(TileTerrain terrain, Vector3 localHit)
        {
            var data = terrain.GridData;
            if (data == null) return;
            int w = data.Width;
            int h = data.Height;
            int row = w + 1;

            // Only draw near the cursor
            float gx = localHit.x + w / 2f;
            float gz = localHit.z + h / 2f;
            int cx = Mathf.RoundToInt(gx);
            int cz = Mathf.RoundToInt(gz);
            int range = Mathf.CeilToInt(brushRadius);

            var prevZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            for (int vz = Mathf.Max(0, cz - range); vz <= Mathf.Min(h, cz + range); vz++)
            for (int vx = Mathf.Max(0, cx - range); vx <= Mathf.Min(w, cx + range); vx++)
            {
                int vi = vz * row + vx;
                var v = data.Vertices[vi];

                // Check if this vertex has a neighbor exactly 1 floor higher
                bool validRamp = false;
                int[] nnx = { vx - 1, vx + 1, vx, vx };
                int[] nnz = { vz, vz, vz - 1, vz + 1 };
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

                Color col = v.CliffHalfStep ? new Color(0, 1, 0.5f, 0.8f) : new Color(1, 0.8f, 0, 0.8f);
                var worldPos = terrain.transform.TransformPoint(
                    new Vector3(v.position.x, v.height + GetVertexFloorOffset(data, vx, vz, w, h) + 0.1f, v.position.z));
                Handles.color = col;

                if (validRamp)
                {
                    // Draw a diamond marker
                    float s = 0.3f;
                    Vector3 p = worldPos;
                    Handles.DrawLine(p + new Vector3(s, 0, 0), p + new Vector3(0, 0, s));
                    Handles.DrawLine(p + new Vector3(0, 0, s), p + new Vector3(-s, 0, 0));
                    Handles.DrawLine(p + new Vector3(-s, 0, 0), p + new Vector3(0, 0, -s));
                    Handles.DrawLine(p + new Vector3(0, 0, -s), p + new Vector3(s, 0, 0));
                }
                else if (v.CliffHalfStep)
                {
                    // Half-step vertex without valid neighbor — show small dot
                    Handles.DrawSolidDisc(worldPos, Vector3.up, 0.08f);
                }
            }

            Handles.zTest = prevZTest;
        }

        private bool RaycastTerrain(TileTerrain terrain, Ray ray, out Vector3 worldHit)
        {
            worldHit = Vector3.zero;
            var data = terrain.GridData;
            if (data == null) return false;

            Vector3 origin = terrain.transform.InverseTransformPoint(ray.origin);
            Vector3 dir = terrain.transform.InverseTransformDirection(ray.direction);
            Ray localRay = new Ray(origin, dir);

            float t = 0f;
            float step = 0.25f;
            float maxDist = 500f;

            for (int i = 0; i < 400; i++)
            {
                Vector3 p = localRay.GetPoint(t);
                if (Mathf.Abs(p.x) > data.Width * 0.5f + 2f || Mathf.Abs(p.z) > data.Height * 0.5f + 2f)
                {
                    t += step * 2f;
                    if (t > maxDist) return false;
                    continue;
                }

                float h = GetTotalHeightAt(terrain, p);
                if (p.y <= h)
                {
                    float t0 = t - step;
                    float t1 = t;
                    for (int j = 0; j < 6; j++)
                    {
                        float tm = (t0 + t1) * 0.5f;
                        Vector3 pm = localRay.GetPoint(tm);
                        if (pm.y <= GetTotalHeightAt(terrain, pm)) t1 = tm;
                        else t0 = tm;
                    }
                    worldHit = terrain.transform.TransformPoint(localRay.GetPoint(t1));
                    return true;
                }
                t += step;
                if (t > maxDist) break;
            }
            return false;
        }

        private float GetTotalHeightAt(TileTerrain terrain, Vector3 localPos)
        {
            var data = terrain.GridData;
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
                if (mask == TileTerrainConstants.FullQuadMask)
                {
                    cliffAccum += 1f;
                }
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

        private void DrawBrush(TileTerrain terrain, Vector3 localHit, Vector3 elevated)
        {
            float eps = 0.05f;
            Color col;
            if (editorMode == EditorMode.Texture)
            {
                if (textureTool == TextureTool.Fill)
                {
                    col = Color.yellow;
                    Handles.color = col;
                    Handles.DrawSolidDisc(elevated, terrain.transform.up, brushRadius * 0.03f);
                    float cs = brushRadius * 0.12f;
                    Handles.DrawLine(elevated - terrain.transform.right * cs, elevated + terrain.transform.right * cs);
                    Handles.DrawLine(elevated - terrain.transform.forward * cs, elevated + terrain.transform.forward * cs);
                    Handles.Label(elevated + terrain.transform.up * 0.4f, "Fill", EditorStyles.boldLabel);
                    return;
                }
                else col = eraseMode ? Color.red : Color.cyan;
            }
            else if (editorMode == EditorMode.Cliff)
            {
                if (cliffTool == CliffTool.Up) col = new Color(1f, 0.55f, 0f);
                else if (cliffTool == CliffTool.Down) col = new Color(0f, 0.7f, 1f);
                else col = new Color(0.6f, 0.6f, 0.6f);
            }
            else if (editorMode == EditorMode.Water)
                col = new Color(0f, 0.5f, 1f);
            else
                col = Color.white;

            Handles.color = col;
            if (brushShape == BrushShape.Square)
            {
                float r = brushRadius;
                DrawBrushSquare(terrain, localHit, eps, r);
                Handles.color = new Color(col.r, col.g, col.b, 0.2f);
                DrawBrushSquare(terrain, localHit, eps, r * 1.1f);
            }
            else
            {
                const int Segs = 60;
                var pts = new Vector3[Segs + 1];
                for (int i = 0; i <= Segs; i++)
                {
                    float a = (float)i / Segs * Mathf.PI * 2f;
                    var pLoc = localHit + new Vector3(Mathf.Cos(a) * brushRadius, 0, Mathf.Sin(a) * brushRadius);
                    float h = GetTotalHeightAt(terrain, pLoc) + eps;
                    pts[i] = terrain.transform.TransformPoint(new Vector3(pLoc.x, h, pLoc.z));
                }
                Handles.DrawPolyLine(pts);

                Handles.color = new Color(col.r, col.g, col.b, 0.2f);
                for (int i = 0; i <= Segs; i++)
                {
                    float a = (float)i / Segs * Mathf.PI * 2f;
                    var pLoc = localHit + new Vector3(Mathf.Cos(a) * brushRadius * 1.1f, 0, Mathf.Sin(a) * brushRadius * 1.1f);
                    float h = GetTotalHeightAt(terrain, pLoc) + eps;
                    pts[i] = terrain.transform.TransformPoint(new Vector3(pLoc.x, h, pLoc.z));
                }
                Handles.DrawPolyLine(pts);
            }

            Handles.color = col;
            Handles.DrawSolidDisc(elevated, terrain.transform.up, brushRadius * 0.03f);

            float crossSize = brushRadius * 0.2f;
            Handles.DrawLine(elevated - terrain.transform.right * crossSize, elevated + terrain.transform.right * crossSize);
            Handles.DrawLine(elevated - terrain.transform.forward * crossSize, elevated + terrain.transform.forward * crossSize);

            Handles.DrawLine(elevated, elevated + terrain.transform.up * 0.5f);

            if (_isResizingBrush)
            {
                string label = brushShape == BrushShape.Square
                    ? $"Size: {brushRadius:F1}"
                    : $"Radius: {brushRadius:F1}";
                Handles.Label(elevated + Vector3.up * 0.5f, label, EditorStyles.boldLabel);
            }
        }

        private void DrawBrushSquare(TileTerrain terrain, Vector3 localHit, float eps, float r)
        {
            float hx = r;
            float hz = r;
            int segsPerEdge = 15;
            int totalPts = segsPerEdge * 4;
            var pts = new Vector3[totalPts + 1];
            int pi = 0;
            for (int edge = 0; edge < 4; edge++)
            {
                Vector3 p0, p1;
                if (edge == 0) { p0 = new Vector3(-hx, 0, -hz); p1 = new Vector3( hx, 0, -hz); }
                else if (edge == 1) { p0 = new Vector3( hx, 0, -hz); p1 = new Vector3( hx, 0,  hz); }
                else if (edge == 2) { p0 = new Vector3( hx, 0,  hz); p1 = new Vector3(-hx, 0,  hz); }
                else { p0 = new Vector3(-hx, 0,  hz); p1 = new Vector3(-hx, 0, -hz); }
                for (int s = 0; s < segsPerEdge; s++)
                {
                    float t = (float)s / segsPerEdge;
                    var pLoc = localHit + Vector3.Lerp(p0, p1, t);
                    pts[pi++] = ProjectToTerrain(terrain, pLoc, eps);
                }
            }
            pts[totalPts] = pts[0];
            Handles.DrawPolyLine(pts);
        }

        private Vector3 ProjectToTerrain(TileTerrain terrain, Vector3 localPos, float eps)
        {
            float h = GetTotalHeightAt(terrain, localPos) + eps;
            return terrain.transform.TransformPoint(new Vector3(localPos.x, h, localPos.z));
        }

        private void MarkDirtyChunks(TileTerrain terrain, int xMin, int xMax, int zMin, int zMax)
        {
            int cs = terrain.ChunkSize;
            int w = terrain.GridData.Width;
            int h = terrain.GridData.Height;
            int minQx = xMin > 0 ? xMin - 1 : 0;
            int maxQx = xMax < w ? xMax : w - 1;
            int minQz = zMin > 0 ? zMin - 1 : 0;
            int maxQz = zMax < h ? zMax : h - 1;
            int cxMin = minQx / cs;
            int cxMax = maxQx / cs;
            int czMin = minQz / cs;
            int czMax = maxQz / cs;
            for (int cz = czMin; cz <= czMax; cz++)
                for (int cx = cxMin; cx <= cxMax; cx++)
                    _dirtyChunks.Add((cx, cz));
        }

        private void PrecomputeNeighborCache(TileTerrainGridData data, int w, int h, int row, int xMin, int xMax, int zMin, int zMax)
        {
            int vertCount = (w + 1) * (h + 1);
            if (_touchesWaterCache.Length < vertCount) _touchesWaterCache = new bool[vertCount];
            if (_isBoundaryCache.Length < vertCount) _isBoundaryCache = new bool[vertCount];

            for (int z = zMin; z <= zMax; z++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    int idx = z * row + x;
                    _touchesWaterCache[idx] = TouchesWater(data, w, h, row, x, z);
                    _isBoundaryCache[idx] = IsBoundary(data, idx);
                }
            }
        }

        private void RequestMeshRebuild(TileTerrain terrain)
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastMeshRebuildTime < MeshRebuildInterval) return;
            _lastMeshRebuildTime = now;
            _gridDirty = true;
            if (_dirtyChunks.Count > 0)
            {
                terrain.GenerateChunks(_dirtyChunks, _cliffDataChanged);
                _dirtyChunks.Clear();
            }
            else
            {
                terrain.GenerateMesh(_cliffDataChanged);
            }
            _cliffDataChanged = false;
        }

        private void DrawBorderLine(TileTerrain terrain)
        {
            var data = terrain.GridData;
            if (data == null || data.BorderSize <= 0) return;

            int b = data.BorderSize;
            int w = data.Width;
            int h = data.Height;
            if (w <= 2 * b || h <= 2 * b) return;
            int row = w + 1;

            Vector3 GetWorldPos(int vx, int vz)
            {
                var v = data.Vertices[vz * row + vx];
                float floorY = GetVertexFloorOffset(data, vx, vz, w, h);
                float terrainY = v.height + floorY;
                float waterY = (v.WaterLevel - 0.5f) * TileTerrainCliff.CliffHeight;
                float yPos = v.IsWater ? Mathf.Max(waterY, terrainY) : terrainY;
                return terrain.transform.TransformPoint(new Vector3(v.position.x, yPos + 0.05f, v.position.z));
            }

            var prevZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
            Handles.color = Color.red;

            int edgeLen = w - 2 * b + 1;
            var bottom = new Vector3[edgeLen];
            for (int i = 0; i < edgeLen; i++)
                bottom[i] = GetWorldPos(b + i, b);
            Handles.DrawPolyLine(bottom);

            var right = new Vector3[h - 2 * b + 1];
            for (int i = 0; i < right.Length; i++)
                right[i] = GetWorldPos(w - b, b + i);
            Handles.DrawPolyLine(right);

            var top = new Vector3[edgeLen];
            for (int i = 0; i < top.Length; i++)
                top[i] = GetWorldPos(w - b - i, h - b);
            Handles.DrawPolyLine(top);

            var left = new Vector3[h - 2 * b + 1];
            for (int i = 0; i < left.Length; i++)
                left[i] = GetWorldPos(b, h - b - i);
            Handles.DrawPolyLine(left);

            Handles.zTest = prevZTest;
        }
    }
}
