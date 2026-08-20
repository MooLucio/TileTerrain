using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MooLucio.TileTerrain
{
    public partial class TileTerrainEditor
    {
        private void DrawWaterTools()
        {
            var paintIcon = EditorGUIUtility.IconContent(paintMode ? "d_PauseButton" : "d_PlayButton");
            paintIcon.text = paintMode ? "  Disable Paint Mode" : "  Enable Paint Mode";
            _paintBtnStyle.normal.textColor = paintMode ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 0.7f, 1f);
            if (GUILayout.Button(paintIcon, _paintBtnStyle)) paintMode = !paintMode;

            EditorGUILayout.Space(4);
            DrawBrushShapeSelector();
            brushRadius = EditorGUILayout.Slider("Brush Radius", brushRadius, 0.5f, 20f);

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Paint: Sets floor as water level, marks as water, and lowers floor by 1.",
                MessageType.Info);

            var terrain = (TileTerrain)target;
            if (terrain.WaterMaterial == null)
                EditorGUILayout.HelpBox("Assign WaterMaterial on the TileTerrain component.", MessageType.Warning);
        }

        private void PaintWater(TileTerrain terrain, Vector3 worldHit)
        {
            var data = terrain.GridData;
            if (data == null || data.Vertices == null || data.Vertices.Count == 0) return;
            data.EnsureGridData();
            Undo.RecordObject(data, "Paint Water");

            Vector3 localHit = terrain.transform.InverseTransformPoint(worldHit);

            int w = data.Width;
            int h = data.Height;
            int row = w + 1;
            int xMin = Mathf.Max(0, Mathf.FloorToInt(localHit.x - brushRadius + w * 0.5f));
            int xMax = Mathf.Min(w, Mathf.CeilToInt(localHit.x + brushRadius + w * 0.5f));
            int zMin = Mathf.Max(0, Mathf.FloorToInt(localHit.z - brushRadius + h * 0.5f));
            int zMax = Mathf.Min(h, Mathf.CeilToInt(localHit.z + brushRadius + h * 0.5f));

            _propagationQueue.Clear();
            _modifiedVertices.Clear();

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

                    if (!_cliffStrokeModifiedVertices.Contains(i))
                    {
                        if (!_strokeStartedOnWater)
                        {
                            if (v.CliffByte != _strokeFloorLevel) continue;
                        }

                        if (v.IsWater) continue;
                        if (IsCliffEdge(data, i)) continue;

                        v.WaterLevel = v.CliffByte;
                        v.IsWater = true;
                        v.height = 0f;
                        ClearRampAt(data, i, w, h, row);

                        int target = Mathf.Max(TileTerrainConstants.MinEditableCliff, v.CliffByte - 1);

                        data.Vertices[i] = v;
                        _propagationQueue.Enqueue((i, target, -1));

                        _cliffStrokeModifiedVertices.Add(i);
                    }
                }
            }

            bool changed = false;

            while (_propagationQueue.Count > 0)
            {
                var item = _propagationQueue.Dequeue();
                int idx = item.index;
                int target = item.level;

                var v = data.Vertices[idx];
                bool vChanged = false;

                if (v.CliffByte > target)
                {
                    v.CliffByte = (sbyte)target;
                    vChanged = true;
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

                            int diff = target - nv.CliffByte;
                            if (diff < -2)
                            {
                                if (nv.IsWater || TouchesWater(data, w, h, row, nx, nz))
                                {
                                    if (!IsSafeToCarve(data, nIdx)) continue;
                                }

                                if (v.IsWater)
                                {
                                    nv.IsWater = true;
                                    nv.WaterLevel = v.WaterLevel;
                                    ClearRampAt(data, nIdx, w, h, row);
                                    data.Vertices[nIdx] = nv;
                                    _modifiedVertices.Add(nIdx);
                                }

                                int nextTarget = nv.CliffByte - 1;
                                _propagationQueue.Enqueue((nIdx, nextTarget, -1));
                            }
                        }
                    }
                }
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

                _cliffDataChanged = true;
                MarkDirtyChunks(terrain, xMin, xMax, zMin, zMax);
                EditorUtility.SetDirty(data);
                RequestMeshRebuild(terrain);
            }
        }
        private void ClearRampAt(TileTerrainGridData data, int idx, int w, int h, int row)
        {
            var v = data.Vertices[idx];
            if (v.CliffHalfStep)
            {
                v.CliffHalfStep = false;
                data.Vertices[idx] = v;
                _modifiedVertices.Add(idx);
            }

            int vx = idx % row;
            int vz = idx / row;
            int[] nnx = { vx - 1, vx + 1, vx, vx };
            int[] nnz = { vz, vz, vz - 1, vz + 1 };
            for (int n = 0; n < 4; n++)
            {
                if (nnx[n] < 0 || nnz[n] < 0 || nnx[n] > w || nnz[n] > h) continue;
                int nIdx = nnz[n] * row + nnx[n];
                var nv = data.Vertices[nIdx];
                if (nv.CliffHalfStep && Mathf.Abs(nv.CliffByte - v.CliffByte) == 1)
                {
                    nv.CliffHalfStep = false;
                    data.Vertices[nIdx] = nv;
                    _modifiedVertices.Add(nIdx);
                }
            }
        }
    }
}
