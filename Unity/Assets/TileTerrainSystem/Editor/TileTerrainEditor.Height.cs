using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MooLucio.TileTerrain
{
    public partial class TileTerrainEditor
    {
        private float targetHeight = 1f;
        internal enum HeightTool { Raise, Lower, Target, Smooth, Noise }
        internal HeightTool heightTool = HeightTool.Raise;

        private void DrawHeightTools()
        {
            var paintIcon = EditorGUIUtility.IconContent(paintMode ? "d_PauseButton" : "d_PlayButton");
            paintIcon.text = paintMode ? "  Disable Paint Mode" : "  Enable Paint Mode";
            _paintBtnStyle.normal.textColor = paintMode ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 1f, 0.6f);
            if (GUILayout.Button(paintIcon, _paintBtnStyle)) paintMode = !paintMode;

            EditorGUILayout.Space(4);
            const float btnMinW = 38f;
            var cUp = EditorGUIUtility.IconContent("d_icon dropdown open@2x"); cUp.text = " Raise";
            var cDown = EditorGUIUtility.IconContent("d_icon dropdown@2x"); cDown.text = " Lower";
            var cTarget = EditorGUIUtility.IconContent("d_SceneLayersToggle"); cTarget.text = " Target";
            var cSmooth = EditorGUIUtility.IconContent("ShadedWireframe On"); cSmooth.text = " Smooth";
            var cNoise = EditorGUIUtility.IconContent("d_ToggleUVOverlay"); cNoise.text = " Noise";

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(heightTool == HeightTool.Raise, cUp, _toolBtnStyle,
                GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true)))
                heightTool = HeightTool.Raise;
            if (GUILayout.Toggle(heightTool == HeightTool.Lower, cDown, _toolBtnStyle,
                GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true)))
                heightTool = HeightTool.Lower;
            if (GUILayout.Toggle(heightTool == HeightTool.Target, cTarget, _toolBtnStyle,
                GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true)))
                heightTool = HeightTool.Target;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(heightTool == HeightTool.Smooth, cSmooth, _toolBtnStyle,
                GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true)))
                heightTool = HeightTool.Smooth;
            if (GUILayout.Toggle(heightTool == HeightTool.Noise, cNoise, _toolBtnStyle,
                GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true)))
                heightTool = HeightTool.Noise;
            EditorGUILayout.EndHorizontal();
            DrawBrushShapeSelector();
            brushRadius = EditorGUILayout.Slider("Brush Radius", brushRadius, 0.1f, 20f);
            brushStrength = EditorGUILayout.Slider("Brush Strength", brushStrength, 0f, 5f);

            if (heightTool == HeightTool.Target)
                targetHeight = EditorGUILayout.Slider("Target Height", targetHeight, -2f, 2f);

            EditorGUILayout.Space(4);
            var regenIcon = EditorGUIUtility.IconContent("Refresh");
            regenIcon.text = "  Regenerate Mesh";
            if (GUILayout.Button(regenIcon, GUILayout.Height(22)))
            {
                TileTerrainCliff.InvalidateCache();
                ((TileTerrain)target).GenerateMesh();
            }
        }

        private void PaintHeight(TileTerrain terrain, Vector3 worldHit)
        {
            var data = terrain.GridData;
            if (data == null) return;
            data.EnsureGridData();
            Undo.RecordObject(data, "Paint Height");

            Vector3 localHit = terrain.transform.InverseTransformPoint(worldHit);

            int w = data.Width;
            int h = data.Height;
            int row = w + 1;
            int xMin = Mathf.Max(0, Mathf.FloorToInt(localHit.x - brushRadius + w * 0.5f));
            int xMax = Mathf.Min(w, Mathf.CeilToInt(localHit.x + brushRadius + w * 0.5f));
            int zMin = Mathf.Max(0, Mathf.FloorToInt(localHit.z - brushRadius + h * 0.5f));
            int zMax = Mathf.Min(h, Mathf.CeilToInt(localHit.z + brushRadius + h * 0.5f));

            PrecomputeNeighborCache(data, w, h, row, xMin, xMax, zMin, zMax);
            int lutW = xMax - xMin + 1;
            int lutH = zMax - zMin + 1;
            int lutSize = lutW * lutH;
            if (_falloffLut.Length < lutSize)
                _falloffLut = new float[lutSize];

            for (int z = zMin; z <= zMax; z++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    int vi = z * row + x;
                    var vv = data.Vertices[vi];
                    float dx = vv.position.x - localHit.x;
                    float dz = vv.position.z - localHit.z;
                    if (brushShape == BrushShape.Square)
                    {
                        float dMax = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
                        _falloffLut[(z - zMin) * lutW + (x - xMin)] =
                            dMax <= brushRadius ? 1f - dMax / brushRadius : 0f;
                    }
                    else
                    {
                        float dSq = dx * dx + dz * dz;
                        _falloffLut[(z - zMin) * lutW + (x - xMin)] =
                            dSq <= brushRadius * brushRadius ? 1f - Mathf.Sqrt(dSq) / brushRadius : 0f;
                    }
                }
            }

            // Cache old heights (expanded for entangled groups)
            var oldHeights = new Dictionary<int, float>();
            for (int z = zMin; z <= zMax; z++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    float inf = _falloffLut[(z - zMin) * lutW + (x - xMin)];
                    if (inf <= 0f) continue;
                    int i = z * row + x;
                    var v = data.Vertices[i];
                    if (v.EntanglementGroupId >= 0)
                    {
                        var group = data.GetEntanglementGroup(v.EntanglementGroupId);
                        for (int gi = 0; gi < group.vertexIds.Count; gi++)
                            if (!oldHeights.ContainsKey(group.vertexIds[gi]))
                                oldHeights[group.vertexIds[gi]] = data.Vertices[group.vertexIds[gi]].height;
                    }
                    else if (!oldHeights.ContainsKey(i))
                    {
                        oldHeights[i] = v.height;
                    }
                }
            }

            for (int z = zMin; z <= zMax; z++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    float inf = _falloffLut[(z - zMin) * lutW + (x - xMin)];
                    if (inf <= 0f) continue;

                    int i = z * row + x;
                    var v = data.Vertices[i];

                    if (v.IsWater || _touchesWaterCache[i])
                    {
                        if (_isBoundaryCache[i]) continue;
                    }

                    float delta = brushStrength * inf;

                    switch (heightTool)
                    {
                        case HeightTool.Raise: v.height = Mathf.Clamp(v.height + delta, -2f, 2f); break;
                        case HeightTool.Lower: v.height = Mathf.Clamp(v.height - delta, -2f, 2f); break;
                        case HeightTool.Target: v.height = Mathf.Clamp(Mathf.Lerp(v.height, targetHeight, inf), -2f, 2f); break;
                        case HeightTool.Smooth:
                            float avg = GetAvgHeight(data, new Vector2(v.position.x, v.position.z), brushRadius);
                            v.height = Mathf.Clamp(Mathf.Lerp(v.height, avg, Mathf.Clamp01(brushStrength * 0.1f * inf)), -2f, 2f);
                            break;
                        case HeightTool.Noise:
                            float noise = Mathf.PerlinNoise(v.position.x * 0.5f + 100f, v.position.z * 0.5f + 100f);
                            float targetN = (noise * 4f) - 2f;
                            v.height = Mathf.Clamp(Mathf.Lerp(v.height, targetN, delta), -2f, 2f);
                            break;
                    }
                    data.Vertices[i] = v;
                }
            }

            // ── Entanglement propagation: sync same-delta to all grouped vertices ──
            var syncedHeightGroups = new HashSet<int>();
            int propXMin = xMin, propXMax = xMax, propZMin = zMin, propZMax = zMax;
            for (int z = zMin; z <= zMax; z++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    float inf = _falloffLut[(z - zMin) * lutW + (x - xMin)];
                    if (inf <= 0f) continue;
                    int i = z * row + x;
                    var v = data.Vertices[i];
                    if (v.EntanglementGroupId >= 0 && syncedHeightGroups.Add(v.EntanglementGroupId))
                    {
                        var group = data.GetEntanglementGroup(v.EntanglementGroupId);
                        if (group.vertexIds == null || group.vertexIds.Count == 0) continue;

                        // Find the modified vertex in this group closest to brush center
                        int reprIdx = group.vertexIds[0];
                        float bestDist = float.MaxValue;
                        for (int gi = 0; gi < group.vertexIds.Count; gi++)
                        {
                            var gv = data.Vertices[group.vertexIds[gi]];
                            float ddx = gv.position.x - localHit.x;
                            float ddz = gv.position.z - localHit.z;
                            float dist = ddx * ddx + ddz * ddz;
                            if (dist < bestDist) { bestDist = dist; reprIdx = group.vertexIds[gi]; }
                        }

                        float oldH;
                        if (!oldHeights.TryGetValue(reprIdx, out oldH)) continue;
                        float newH = data.Vertices[reprIdx].height;
                        float heightDelta = newH - oldH;
                        if (Mathf.Abs(heightDelta) > 0.0001f)
                        {
                            for (int gi = 0; gi < group.vertexIds.Count; gi++)
                            {
                                int gvi = group.vertexIds[gi];
                                var gv = data.Vertices[gvi];
                                gv.height = Mathf.Clamp(oldHeights[gvi] + heightDelta, -2f, 2f);
                                data.Vertices[gvi] = gv;

                                // Expand dirty bounds to cover entangled vertices
                                int vx = gvi % row;
                                int vz = gvi / row;
                                if (vx < propXMin) propXMin = vx; if (vx > propXMax) propXMax = vx;
                                if (vz < propZMin) propZMin = vz; if (vz > propZMax) propZMax = vz;
                            }
                        }
                    }
                }
            }

            MarkDirtyChunks(terrain, propXMin, propXMax, propZMin, propZMax);
            EditorUtility.SetDirty(data);
            RequestMeshRebuild(terrain);
        }

        private float GetAvgHeight(TileTerrainGridData data, Vector2 pos, float radius)
        {
            float total = 0f;
            int count = 0;
            float rSq = radius * radius;
            int w = data.Width;
            int h = data.Height;
            int row = w + 1;
            int xMin = Mathf.Max(0, Mathf.FloorToInt(pos.x - radius + w * 0.5f));
            int xMax = Mathf.Min(w, Mathf.CeilToInt(pos.x + radius + w * 0.5f));
            int zMin = Mathf.Max(0, Mathf.FloorToInt(pos.y - radius + h * 0.5f));
            int zMax = Mathf.Min(h, Mathf.CeilToInt(pos.y + radius + h * 0.5f));

            for (int z = zMin; z <= zMax; z++)
            for (int x = xMin; x <= xMax; x++)
            {
                var v = data.Vertices[z * row + x];
                float dx = v.position.x - pos.x;
                float dz = v.position.z - pos.y;
                if (brushShape == BrushShape.Square)
                {
                    if (Mathf.Abs(dx) <= radius && Mathf.Abs(dz) <= radius) { total += v.height; count++; }
                }
                else
                {
                    if (dx * dx + dz * dz <= rSq) { total += v.height; count++; }
                }
            }
            return count > 0 ? total / count : 0f;
        }
    }
}
