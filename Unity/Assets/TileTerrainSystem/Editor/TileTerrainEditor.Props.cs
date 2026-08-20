using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MooLucio.TileTerrain
{
    public partial class TileTerrainEditor
    {
        internal enum PropsTool { Place, Paint, Select, Remove, Rotate, Scale, Erase }
        internal PropsTool propsTool = PropsTool.Place;
        internal int selectedPropIndex = 0;
        internal int selectedPropInstance = -1;
        internal float propsBrushDensity = 0.3f;
        internal bool propsSnapToGrid = true;
        internal bool propsRandomRotate = true;


        private double _lastPropsRespawnTime;
        private const double PropsRespawnInterval = 1.0 / 15.0;
        private int _cachedPropCount = -1;

        internal void RequestPropsRespawn(TileTerrain terrain, bool force = false)
        {
            if (force || EditorApplication.timeSinceStartup - _lastPropsRespawnTime >= PropsRespawnInterval)
            {
                _lastPropsRespawnTime = EditorApplication.timeSinceStartup;
                terrain.SpawnProps();
            }
        }

        private void DrawPropsTools()
        {
            var paintIcon = EditorGUIUtility.IconContent(paintMode ? "d_PauseButton" : "d_PlayButton");
            paintIcon.text = paintMode ? "  Disable Paint Mode" : "  Enable Paint Mode";
            var paintBtnStyle = new GUIStyle(GUI.skin.button) { fixedHeight = 24, fontStyle = FontStyle.Bold };
            paintBtnStyle.normal.textColor = paintMode ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 1f, 0.6f);
            if (GUILayout.Button(paintIcon, paintBtnStyle)) paintMode = !paintMode;

            var terrain = (TileTerrain)target;
            EditorGUILayout.Space(4);

            if (terrain.PropsBox == null)
            {
                EditorGUILayout.HelpBox("Assign a PropsBox in the System Setup section.", MessageType.Warning);
                return;
            }

            int count = terrain.PropsBox.Props.Count;

            // Detect palette changes (add/remove/reorder) and force repaint
            if (count != _cachedPropCount)
            {
                _cachedPropCount = count;
                Repaint();
                SceneView.RepaintAll();
            }

            if (count == 0)
            {
                EditorGUILayout.HelpBox("Add at least one Prop to the PropsBox.", MessageType.Warning);
                return;
            }

            // ── Toolbar ──
            const float btnMinW = 38f;
            var cPlace = EditorGUIUtility.IconContent("d_Grid.PaintTool"); cPlace.text = " Place";
            var cPaint = EditorGUIUtility.IconContent("d_TerrainInspector.TerrainToolSplat"); cPaint.text = " Paint";
            var cSelect = EditorGUIUtility.IconContent("d_scenepicking_pickable_hover"); cSelect.text = " Select";
            var cRemove = EditorGUIUtility.IconContent("d_TreeEditor.Trash"); cRemove.text = " Remove";
            if (cRemove.image == null) cRemove = EditorGUIUtility.IconContent("d_winbtn_mac_close");
            var cErase = EditorGUIUtility.IconContent("d_Grid.EraserTool"); cErase.text = " Erase";
            var cRotate = EditorGUIUtility.IconContent("d_RotateTool"); cRotate.text = " Rotate";
            var cScale = EditorGUIUtility.IconContent("d_ScaleTool"); cScale.text = " Scale";

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(propsTool == PropsTool.Place, cPlace, _toolBtnStyle,
                GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true)))
                propsTool = PropsTool.Place;
            if (GUILayout.Toggle(propsTool == PropsTool.Paint, cPaint, _toolBtnStyle,
                GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true)))
                propsTool = PropsTool.Paint;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(propsTool == PropsTool.Remove, cRemove, _toolBtnStyle,
                GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true)))
                propsTool = PropsTool.Remove;
            if (GUILayout.Toggle(propsTool == PropsTool.Erase, cErase, _toolBtnStyle,
                GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true)))
                propsTool = PropsTool.Erase;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(propsTool == PropsTool.Select, cSelect, _toolBtnStyle,
                GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true)))
                propsTool = PropsTool.Select;
            if (GUILayout.Toggle(propsTool == PropsTool.Rotate, cRotate, _toolBtnStyle,
                GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true)))
                propsTool = PropsTool.Rotate;
            if (GUILayout.Toggle(propsTool == PropsTool.Scale, cScale, _toolBtnStyle,
                GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true)))
                propsTool = PropsTool.Scale;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // ── Entry grid ──
            DrawPropsGrid(terrain);

            EditorGUILayout.Space(4);

            // ── Settings ──
            if (propsTool == PropsTool.Paint || propsTool == PropsTool.Erase)
            {
                DrawBrushShapeSelector();
                brushRadius = EditorGUILayout.Slider("Brush Radius", brushRadius, 0.5f, 20f);
                if (propsTool == PropsTool.Paint)
                    propsBrushDensity = EditorGUILayout.Slider("Density", propsBrushDensity, 0f, 1f);
            }

            propsSnapToGrid = EditorGUILayout.Toggle("Snap to Grid", propsSnapToGrid);
            propsRandomRotate = EditorGUILayout.Toggle("Random Rotation", propsRandomRotate);

            // ── Selected instance info ──
            if (selectedPropInstance >= 0 && selectedPropInstance < terrain.GridData.Props.Count)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Selected Prop", EditorStyles.boldLabel);
                var a = terrain.GridData.Props[selectedPropInstance];
                EditorGUI.BeginChangeCheck();
                Vector3 pos = EditorGUILayout.Vector3Field("Position", a.position);
                float rot = EditorGUILayout.Slider("Rotation", a.rotationY, 0f, 360f);
                float scl = EditorGUILayout.Slider("Scale", a.scale, 0.1f, 5f);
                bool pinned = EditorGUILayout.Toggle("Pin to Ground", a.pinnedToGround);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(terrain.GridData, "Modify Prop");
                    a.position = pos;
                    a.rotationY = rot;
                    a.scale = scl;
                    a.pinnedToGround = pinned;
                    terrain.GridData.Props[selectedPropInstance] = a;
                    EditorUtility.SetDirty(terrain.GridData);
                    RequestPropsRespawn(terrain);
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("Remove Selected", GUILayout.Height(24)))
                {
                    RemovePropInstance(terrain, selectedPropInstance);
                }
            }

            EditorGUILayout.Space(4);
            if (GUILayout.Button($"Total: {terrain.GridData.Props.Count} props placed", EditorStyles.miniLabel))
            {
                selectedPropInstance = -1;
            }
        }

        private void DrawPropsGrid(TileTerrain terrain)
        {
            var palette = terrain.PropsBox;
            int count = palette.Props.Count;

            int columns = Mathf.Max(1, (int)(EditorGUIUtility.currentViewWidth - 32) / 80);
            int previewSize = 72;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            int col = 0;
            EditorGUILayout.BeginHorizontal();

            for (int i = 0; i < count; i++)
            {
                var prop = palette.Props[i];
                bool isSelected = (i == selectedPropIndex);

                Texture2D preview = null;
                var firstPrefab = prop.Prefabs != null && prop.Prefabs.Count > 0 ? prop.Prefabs[0] : null;
                if (firstPrefab != null)
                    preview = AssetPreview.GetAssetPreview(firstPrefab);

                var rect = EditorGUILayout.BeginVertical(GUILayout.Width(previewSize + 8));
                if (isSelected)
                {
                    var selRect = new Rect(rect.x - 2, rect.y - 2, rect.xMax - rect.x + 4, rect.yMax - rect.y + 4);
                    EditorGUI.DrawRect(selRect, new Color(0.2f, 0.8f, 0.4f, 0.3f));
                    Handles.DrawSolidRectangleWithOutline(selRect, new Color(0, 0, 0, 0), new Color(0.2f, 1f, 0.5f, 1f));
                }

                GUIContent btnContent = preview != null
                    ? new GUIContent(preview, prop.Label)
                    : new GUIContent(firstPrefab != null ? firstPrefab.name : "(null)");

                if (GUILayout.Button(btnContent, GUILayout.Width(previewSize + 4), GUILayout.Height(previewSize + 4)))
                {
                    selectedPropIndex = i;
                    GUI.FocusControl(null);
                }

                string label = !string.IsNullOrEmpty(prop.Label) ? prop.Label : (firstPrefab != null ? firstPrefab.name : "null");
                EditorGUILayout.LabelField(label,
                    isSelected ? EditorStyles.boldLabel : EditorStyles.miniLabel,
                    GUILayout.Width(previewSize + 4));

                EditorGUILayout.EndVertical();

                col++;
                if (col >= columns)
                {
                    col = 0;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            selectedPropIndex = Mathf.Clamp(selectedPropIndex, 0, count - 1);
        }

        // ── Scene painting ──

        internal void PaintProps(TileTerrain terrain, Vector3 worldHit)
        {
            var data = terrain.GridData;
            if (data == null || terrain.PropsBox == null) return;

            Vector3 localHit = terrain.transform.InverseTransformPoint(worldHit);

            if (propsTool == PropsTool.Place)
            {
                if (selectedPropIndex < 0 || selectedPropIndex >= terrain.PropsBox.Props.Count) return;
                var entry = terrain.PropsBox.Props[selectedPropIndex];
                if (Event.current.type == EventType.MouseDown && HasValidPrefab(entry))
                {
                    PlacePropInstance(terrain, localHit, entry);
                    Event.current.Use();
                }
            }
            else if (propsTool == PropsTool.Paint)
            {
                if (selectedPropIndex < 0 || selectedPropIndex >= terrain.PropsBox.Props.Count) return;
                var entry = terrain.PropsBox.Props[selectedPropIndex];
                if (Event.current.type == EventType.MouseDrag || Event.current.type == EventType.MouseDown)
                {
                    if (HasValidPrefab(entry))
                        PaintPropsBrush(terrain, localHit, entry);
                    Event.current.Use();
                }
            }
            else if (propsTool == PropsTool.Erase)
            {
                if (Event.current.type == EventType.MouseDrag || Event.current.type == EventType.MouseDown)
                {
                    ErasePropsBrush(terrain, localHit);
                    Event.current.Use();
                }
            }
            else if (propsTool == PropsTool.Select)
            {
                if (Event.current.type == EventType.MouseDown)
                {
                    PickPropInstance(terrain, worldHit);
                    Event.current.Use();
                }
            }
            else if (propsTool == PropsTool.Remove)
            {
                if (Event.current.type == EventType.MouseDown)
                {
                    int idx = FindPropInstanceAt(terrain, worldHit);
                    if (idx >= 0)
                        RemovePropInstance(terrain, idx);
                    Event.current.Use();
                }
            }
            else if (propsTool == PropsTool.Rotate && selectedPropInstance >= 0)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    float delta = Event.current.delta.x * 0.5f;
                    RotateSelectedProp(terrain, delta);
                    Event.current.Use();
                }
            }
            else if (propsTool == PropsTool.Scale && selectedPropInstance >= 0)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    float delta = Event.current.delta.y * 0.01f;
                    ScaleSelectedProp(terrain, delta);
                    Event.current.Use();
                }
            }
        }

        private static bool HasValidPrefab(TileTerrainProp entry)
        {
            return entry.Prefabs != null && entry.Prefabs.Count > 0 && entry.Prefabs[0] != null;
        }

        private void PlacePropInstance(TileTerrain terrain, Vector3 localPos, TileTerrainProp entry)
        {
            var data = terrain.GridData;
            int w = data.Width;
            int h = data.Height;

            Vector3 placePos = localPos;
            int qx = 0, qz = 0;
            if (propsSnapToGrid)
            {
                float gx = localPos.x + w / 2f;
                float gz = localPos.z + h / 2f;
                qx = Mathf.Clamp(Mathf.FloorToInt(gx), 0, w - 1);
                qz = Mathf.Clamp(Mathf.FloorToInt(gz), 0, h - 1);
                var q = data.Quads[qz * w + qx];
                float cx = (data.Vertices[q.vertexIds[0]].position.x + data.Vertices[q.vertexIds[1]].position.x) * 0.5f;
                float cz = (data.Vertices[q.vertexIds[2]].position.z + data.Vertices[q.vertexIds[0]].position.z) * 0.5f;
                placePos = new Vector3(cx, 0, cz);
            }
            else
            {
                float gx = localPos.x + w / 2f;
                float gz = localPos.z + h / 2f;
                qx = Mathf.Clamp(Mathf.FloorToInt(gx), 0, w - 1);
                qz = Mathf.Clamp(Mathf.FloorToInt(gz), 0, h - 1);
            }

            placePos.y = GetTotalHeightAt(terrain, placePos);
            Vector3 worldPos = terrain.transform.TransformPoint(placePos);

            float rot = propsRandomRotate ? Random.Range(0f, 360f) : 0f;
            float scl = Random.Range(entry.MinScale, entry.MaxScale);

            int prefabIdx = Random.Range(0, entry.Prefabs.Count);
            var placedPrefab = entry.Prefabs[prefabIdx];
            var mf = placedPrefab.GetComponentInChildren<MeshFilter>();
            Vector3 bCenter = Vector3.zero;
            if (mf != null && mf.sharedMesh != null)
                bCenter = terrain.transform.TransformVector(mf.sharedMesh.bounds.center);

            // Center placement pivot at mesh bounds center
            Vector3 finalPos = placePos + terrain.transform.InverseTransformVector(bCenter);

            // ── Footprint: collect all occupied quad vertices ──
            int halfW = entry.OccupyWidth / 2;
            int halfH = entry.OccupyHeight / 2;
            int startQx = Mathf.Clamp(qx - halfW, 0, w - 1);
            int startQz = Mathf.Clamp(qz - halfH, 0, h - 1);
            int endQx = Mathf.Clamp(startQx + entry.OccupyWidth, 1, w);
            int endQz = Mathf.Clamp(startQz + entry.OccupyHeight, 1, h);

            var footprintVertices = new HashSet<int>();
            for (int qiz = startQz; qiz < endQz; qiz++)
            {
                for (int qix = startQx; qix < endQx; qix++)
                {
                    var quad = data.Quads[qiz * w + qix];
                    footprintVertices.Add(quad.vertexIds[0]);
                    footprintVertices.Add(quad.vertexIds[1]);
                    footprintVertices.Add(quad.vertexIds[2]);
                    footprintVertices.Add(quad.vertexIds[3]);
                }
            }

            // Check all footprint quads are on the same floor
            int firstFloor = data.Quads[startQz * w + startQx].floor;
            for (int qiz = startQz; qiz < endQz; qiz++)
                for (int qix = startQx; qix < endQx; qix++)
                    if (data.Quads[qiz * w + qix].floor != firstFloor)
                        return;

            // Water check
            if (!entry.CanPlaceInWater)
            {
                bool onWater = false;
                foreach (int vi in footprintVertices)
                    if (data.Vertices[vi].IsWater) { onWater = true; break; }
                if (onWater) return;
            }

            // Check for vertex conflicts with existing entanglement groups
            foreach (int vi in footprintVertices)
            {
                if (data.Vertices[vi].EntanglementGroupId >= 0)
                    return;
            }

            // Round-level: set all vertices to the same height + CliffByte as the center-most vertex
            int centerVi = FindNearestVertexIndex(data, placePos);
            if (centerVi >= 0 && centerVi < data.Vertices.Count)
            {
                var centerV = data.Vertices[centerVi];
                float targetHeight = centerV.height;
                sbyte targetCliff = centerV.CliffByte;
                foreach (int vi in footprintVertices)
                {
                    var v = data.Vertices[vi];
                    v.height = targetHeight;
                    v.CliffByte = targetCliff;
                    v.CliffHalfStep = false;
                    data.Vertices[vi] = v;
                }
            }

            float fpRadius = Mathf.Max(entry.OccupyWidth, entry.OccupyHeight) * 0.5f;

            var instance = new PropInstance
            {
                propIndex = selectedPropIndex,
                variant = prefabIdx,
                position = finalPos,
                rotationY = rot,
                scale = scl,
                snapToGrid = propsSnapToGrid,
                pinnedToGround = true,
                footprintRadius = fpRadius,
                entanglementId = -1
            };

            Undo.RecordObject(data, "Place Prop");
            data.Props.Add(instance);

            // Create entanglement group
            if (footprintVertices.Count > 0)
            {
                int propIdx = data.Props.Count - 1;
                int groupId = data.CreateEntanglementGroup(propIdx, new List<int>(footprintVertices));
                var a = data.Props[propIdx];
                a.entanglementId = groupId;
                data.Props[propIdx] = a;
            }

            EditorUtility.SetDirty(data);
            RequestPropsRespawn(terrain);
            SceneView.RepaintAll();
        }

        private void PaintPropsBrush(TileTerrain terrain, Vector3 localPos, TileTerrainProp entry)
        {
            var data = terrain.GridData;
            int w = data.Width;
            int h = data.Height;
            int row = w + 1;

            int xMin = Mathf.Max(0, Mathf.FloorToInt(localPos.x - brushRadius + w * 0.5f));
            int xMax = Mathf.Min(w, Mathf.CeilToInt(localPos.x + brushRadius + w * 0.5f));
            int zMin = Mathf.Max(0, Mathf.FloorToInt(localPos.z - brushRadius + h * 0.5f));
            int zMax = Mathf.Min(h, Mathf.CeilToInt(localPos.z + brushRadius + h * 0.5f));

            bool anyPlaced = false;
            Undo.RecordObject(data, "Paint Prop");

            for (int z = zMin; z <= zMax; z++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    int vi = z * row + x;
                    var v = data.Vertices[vi];
                    float dx = v.position.x - localPos.x;
                    float dz = v.position.z - localPos.z;
                    if (brushShape == BrushShape.Circle)
                    {
                        if (dx * dx + dz * dz > brushRadius * brushRadius) continue;
                    }
                    else
                    {
                        if (Mathf.Abs(dx) > brushRadius || Mathf.Abs(dz) > brushRadius) continue;
                    }

                    if (Random.value > propsBrushDensity) continue;

                    Vector3 candidatePos = new Vector3(v.position.x, 0, v.position.z);
                    candidatePos.y = GetTotalHeightAt(terrain, candidatePos);

                    float gx = candidatePos.x + w / 2f;
                    float gz = candidatePos.z + h / 2f;
                    int pQx = Mathf.Clamp(Mathf.FloorToInt(gx), 0, w - 1);
                    int pQz = Mathf.Clamp(Mathf.FloorToInt(gz), 0, h - 1);
                    int halfW = entry.OccupyWidth / 2;
                    int halfH = entry.OccupyHeight / 2;
                    int sQx = Mathf.Clamp(pQx - halfW, 0, w - 1);
                    int sQz = Mathf.Clamp(pQz - halfH, 0, h - 1);
                    int eQx = Mathf.Clamp(sQx + entry.OccupyWidth, 1, w);
                    int eQz = Mathf.Clamp(sQz + entry.OccupyHeight, 1, h);

                    // Collect footprint vertices
                    var footprintVertices = new HashSet<int>();
                    for (int qiz = sQz; qiz < eQz; qiz++)
                        for (int qix = sQx; qix < eQx; qix++)
                        {
                            var quad = data.Quads[qiz * w + qix];
                            footprintVertices.Add(quad.vertexIds[0]);
                            footprintVertices.Add(quad.vertexIds[1]);
                            footprintVertices.Add(quad.vertexIds[2]);
                            footprintVertices.Add(quad.vertexIds[3]);
                        }

                    // Floor uniformity check
                    int firstFl = data.Quads[sQz * w + sQx].floor;
                    bool floorOk = true;
                    for (int qiz = sQz; qiz < eQz && floorOk; qiz++)
                        for (int qix = sQx; qix < eQx && floorOk; qix++)
                            if (data.Quads[qiz * w + qix].floor != firstFl)
                                floorOk = false;
                    if (!floorOk) continue;

                    // Water check
                    if (!entry.CanPlaceInWater)
                    {
                        bool onWater = false;
                        foreach (int fvi in footprintVertices)
                            if (data.Vertices[fvi].IsWater) { onWater = true; break; }
                        if (onWater) continue;
                    }

                    // Vertex conflict check
                    bool hasConflict = false;
                    foreach (int fvi in footprintVertices)
                    {
                        if (data.Vertices[fvi].EntanglementGroupId >= 0)
                        { hasConflict = true; break; }
                    }
                    if (hasConflict) continue;

                    // Round-level all footprint vertices
                    int centerVi = FindNearestVertexIndex(data, candidatePos);
                    if (centerVi >= 0 && centerVi < data.Vertices.Count)
                    {
                        var centerV = data.Vertices[centerVi];
                        float targetHeight = centerV.height;
                        sbyte targetCliff = centerV.CliffByte;
                        foreach (int fvi in footprintVertices)
                        {
                            var fv = data.Vertices[fvi];
                            fv.height = targetHeight;
                            fv.CliffByte = targetCliff;
                            fv.CliffHalfStep = false;
                            data.Vertices[fvi] = fv;
                        }
                    }

                    float rot = propsRandomRotate ? Random.Range(0f, 360f) : 0f;
                    float scl = Random.Range(entry.MinScale, entry.MaxScale);

                    int prefabIdx = Random.Range(0, entry.Prefabs.Count);
                    float fpRadius = Mathf.Max(entry.OccupyWidth, entry.OccupyHeight) * 0.5f;

                    var instance = new PropInstance
                    {
                        propIndex = selectedPropIndex,
                        variant = prefabIdx,
                        position = candidatePos,
                        rotationY = rot,
                        scale = scl,
                        snapToGrid = false,
                        pinnedToGround = true,
                        footprintRadius = fpRadius,
                        entanglementId = -1
                    };

                    data.Props.Add(instance);

                    // Create entanglement group
                    if (footprintVertices.Count > 0)
                    {
                        int propIdx = data.Props.Count - 1;
                        int groupId = data.CreateEntanglementGroup(propIdx, new List<int>(footprintVertices));
                        var a = data.Props[propIdx];
                        a.entanglementId = groupId;
                        data.Props[propIdx] = a;
                    }

                    anyPlaced = true;
                }
            }

            if (anyPlaced)
            {
                EditorUtility.SetDirty(data);
                RequestPropsRespawn(terrain);
                SceneView.RepaintAll();
            }
        }

        private void PickPropInstance(TileTerrain terrain, Vector3 worldHit)
        {
            int idx = FindPropInstanceAt(terrain, worldHit);
            selectedPropInstance = idx;
            SceneView.RepaintAll();
        }

        private int FindPropInstanceAt(TileTerrain terrain, Vector3 worldPos)
        {
            var data = terrain.GridData;
            if (data == null) return -1;

            Vector3 localPos = terrain.transform.InverseTransformPoint(worldPos);
            int bestIdx = -1;
            float bestDist = 4f;

            for (int i = 0; i < data.Props.Count; i++)
            {
                var a = data.Props[i];
                float dist = Vector3.Distance(localPos, a.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        private void RemovePropInstance(TileTerrain terrain, int index)
        {
            var data = terrain.GridData;
            if (index < 0 || index >= data.Props.Count) return;

            Undo.RecordObject(data, "Delete Prop");

            // Remove entanglement group if any
            var a = data.Props[index];
            if (a.entanglementId >= 0)
                data.RemoveEntanglementGroup(a.entanglementId);

            data.Props.RemoveAt(index);
            if (selectedPropInstance == index)
                selectedPropInstance = -1;
            else if (selectedPropInstance > index)
                selectedPropInstance--;
            EditorUtility.SetDirty(data);
            RequestPropsRespawn(terrain);
            SceneView.RepaintAll();
        }

        private void ErasePropsBrush(TileTerrain terrain, Vector3 localPos)
        {
            var data = terrain.GridData;
            if (data == null) return;

            Undo.RecordObject(data, "Erase Prop");
            bool anyRemoved = false;
            for (int i = data.Props.Count - 1; i >= 0; i--)
            {
                var a = data.Props[i];
                float dx = a.position.x - localPos.x;
                float dz = a.position.z - localPos.z;
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
        }

        private void RotateSelectedProp(TileTerrain terrain, float delta)
        {
            if (selectedPropInstance < 0) return;
            var data = terrain.GridData;
            Undo.RecordObject(data, "Rotate Prop");
            var a = data.Props[selectedPropInstance];
            a.rotationY = (a.rotationY + delta) % 360f;
            if (a.rotationY < 0) a.rotationY += 360f;
            data.Props[selectedPropInstance] = a;
            EditorUtility.SetDirty(data);
            RequestPropsRespawn(terrain);
            SceneView.RepaintAll();
        }

        private void ScaleSelectedProp(TileTerrain terrain, float delta)
        {
            if (selectedPropInstance < 0) return;
            var data = terrain.GridData;
            Undo.RecordObject(data, "Scale Prop");
            var a = data.Props[selectedPropInstance];
            a.scale = Mathf.Max(0.1f, a.scale + delta);
            data.Props[selectedPropInstance] = a;
            EditorUtility.SetDirty(data);
            RequestPropsRespawn(terrain);
            SceneView.RepaintAll();
        }

        // ── Scene drawing ──

        internal void DrawPropsPreview(TileTerrain terrain, Vector3 localHit)
        {
            if (terrain.PropsBox == null) return;
            if (selectedPropIndex < 0 || selectedPropIndex >= terrain.PropsBox.Props.Count) return;
            var entry = terrain.PropsBox.Props[selectedPropIndex];
            var previewPrefab = entry.Prefabs != null && entry.Prefabs.Count > 0 ? entry.Prefabs[0] : null;
            if (previewPrefab == null) return;

            var mf = previewPrefab.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;

            var data = terrain.GridData;
            int w = data.Width;
            int h = data.Height;

            Vector3 previewPos = localHit;
            int snapQx = 0, snapQz = 0;
            if (propsSnapToGrid)
            {
                float gx = localHit.x + w / 2f;
                float gz = localHit.z + h / 2f;
                snapQx = Mathf.Clamp(Mathf.FloorToInt(gx), 0, w - 1);
                snapQz = Mathf.Clamp(Mathf.FloorToInt(gz), 0, h - 1);
                var q = data.Quads[snapQz * w + snapQx];
                float cx = (data.Vertices[q.vertexIds[0]].position.x + data.Vertices[q.vertexIds[1]].position.x) * 0.5f;
                float cz = (data.Vertices[q.vertexIds[2]].position.z + data.Vertices[q.vertexIds[0]].position.z) * 0.5f;
                previewPos = new Vector3(cx, 0, cz);
            }
            else
            {
                float gx = localHit.x + w / 2f;
                float gz = localHit.z + h / 2f;
                snapQx = Mathf.Clamp(Mathf.FloorToInt(gx), 0, w - 1);
                snapQz = Mathf.Clamp(Mathf.FloorToInt(gz), 0, h - 1);
            }

            previewPos.y = GetTotalHeightAt(terrain, previewPos);

            // Compute footprint quads
            int halfW = entry.OccupyWidth / 2;
            int halfH = entry.OccupyHeight / 2;
            int startQx = Mathf.Clamp(snapQx - halfW, 0, w - 1);
            int startQz = Mathf.Clamp(snapQz - halfH, 0, h - 1);
            int endQx = Mathf.Clamp(startQx + entry.OccupyWidth, 1, w);
            int endQz = Mathf.Clamp(startQz + entry.OccupyHeight, 1, h);

            // Collect footprint vertices
            var footprintVertices = new HashSet<int>();
            for (int qiz = startQz; qiz < endQz; qiz++)
                for (int qix = startQx; qix < endQx; qix++)
                {
                    var quad = data.Quads[qiz * w + qix];
                    footprintVertices.Add(quad.vertexIds[0]);
                    footprintVertices.Add(quad.vertexIds[1]);
                    footprintVertices.Add(quad.vertexIds[2]);
                    footprintVertices.Add(quad.vertexIds[3]);
                }

            // Check floor uniformity
            bool floorOk = true;
            int refFloor = data.Quads[startQz * w + startQx].floor;
            for (int qiz = startQz; qiz < endQz && floorOk; qiz++)
                for (int qix = startQx; qix < endQx && floorOk; qix++)
                    if (data.Quads[qiz * w + qix].floor != refFloor)
                        floorOk = false;

            // Check water
            bool waterConflict = false;
            if (!entry.CanPlaceInWater)
            {
                foreach (int vi in footprintVertices)
                    if (data.Vertices[vi].IsWater) { waterConflict = true; break; }
            }

            // Check vertex conflicts
            bool vertexConflict = false;
            foreach (int vi in footprintVertices)
            {
                if (data.Vertices[vi].EntanglementGroupId >= 0)
                { vertexConflict = true; break; }
            }

            bool canPlace = floorOk && !waterConflict && !vertexConflict;

            // Draw footprint rectangle
            var qBL = data.Quads[startQz * w + startQx];
            var qTR = data.Quads[(endQz - 1) * w + (endQx - 1)];
            Vector3 bl = data.Vertices[qBL.vertexIds[0]].position;
            Vector3 tr = data.Vertices[qTR.vertexIds[3]].position;
            float footY = previewPos.y + 0.05f;

            Vector3 p0 = terrain.transform.TransformPoint(new Vector3(bl.x, footY, bl.z));
            Vector3 p1 = terrain.transform.TransformPoint(new Vector3(tr.x, footY, bl.z));
            Vector3 p2 = terrain.transform.TransformPoint(new Vector3(tr.x, footY, tr.z));
            Vector3 p3 = terrain.transform.TransformPoint(new Vector3(bl.x, footY, tr.z));

            var prvColor = Handles.color;
            Handles.color = canPlace
                ? new Color(0.2f, 1f, 0.5f, 0.15f)
                : new Color(1f, 0.2f, 0.2f, 0.15f);
            Handles.DrawAAConvexPolygon(p0, p1, p2, p3);
            Handles.color = canPlace
                ? new Color(0.2f, 1f, 0.5f, 0.7f)
                : new Color(1f, 0.2f, 0.2f, 0.7f);
            Handles.DrawLine(p0, p1);
            Handles.DrawLine(p1, p2);
            Handles.DrawLine(p2, p3);
            Handles.DrawLine(p3, p0);
            Handles.color = prvColor;

            Bounds b = mf.sharedMesh.bounds;
            Vector3 pivotLocal = previewPos + b.center;
            Vector3 worldPivot = terrain.transform.TransformPoint(pivotLocal);

            var prevColor2 = Handles.color;

            // Wireframe bounding box at actual placement position (centered at pivot)
            Vector3 bSize = b.size;
            Handles.color = new Color(1, 1, 1, 0.25f);
            Handles.DrawWireCube(worldPivot, bSize);

            // Crosshair at placement point
            Handles.color = new Color(1, 1, 1, 0.6f);
            float cs = Mathf.Max(bSize.x, bSize.z) * 0.3f;
            Handles.DrawLine(worldPivot - Vector3.right * cs, worldPivot + Vector3.right * cs);
            Handles.DrawLine(worldPivot - Vector3.forward * cs, worldPivot + Vector3.forward * cs);

            Handles.color = prevColor2;
        }

        internal void DrawPropsSelection(TileTerrain terrain)
        {
            if (selectedPropInstance < 0) return;
            var data = terrain.GridData;
            if (selectedPropInstance >= data.Props.Count)
            {
                selectedPropInstance = -1;
                return;
            }

            var a = data.Props[selectedPropInstance];
            Vector3 worldPos = terrain.transform.TransformPoint(a.position);

            var prevZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            Handles.color = Color.cyan;
            Handles.DrawWireDisc(worldPos, Vector3.up, 0.5f * a.scale);
            Handles.DrawSolidDisc(worldPos, Vector3.up, 0.04f);

            // Connection line to terrain surface
            Vector3 surfaceLocal = new Vector3(a.position.x, 0, a.position.z);
            surfaceLocal.y = GetTotalHeightAt(terrain, surfaceLocal);
            Vector3 surfaceWorld = terrain.transform.TransformPoint(surfaceLocal);
            Handles.color = new Color(0, 1, 1, 0.3f);
            Handles.DrawLine(worldPos, surfaceWorld);

            Handles.zTest = prevZTest;
        }

        internal void DrawPropsModeIndicators(TileTerrain terrain)
        {
            string label = propsTool switch
            {
                PropsTool.Place => "Place Mode",
                PropsTool.Paint => "Paint Mode",
                PropsTool.Select => "Select Mode",
                PropsTool.Remove => "Remove Mode",
                PropsTool.Rotate => "Rotate Mode",
                PropsTool.Scale => "Scale Mode",
                PropsTool.Erase => "Erase Mode",
                _ => ""
            };
            if (!string.IsNullOrEmpty(label))
            {
                Handles.BeginGUI();
                GUI.Label(new Rect(12, 40, 200, 20), label, EditorStyles.boldLabel);
                Handles.EndGUI();
            }
        }
    }
}
