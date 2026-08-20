using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MooLucio.TileTerrain
{
    public partial class TileTerrainEditor
    {
        internal int selectedTextureIndex = 0;
        internal enum TextureTool { Paint, Smudge, Erase, Fill }
        internal TextureTool textureTool = TextureTool.Paint;
        internal bool eraseMode => textureTool == TextureTool.Erase;

        private Dictionary<Texture2DArray, Texture2D> _previewCache = new Dictionary<Texture2DArray, Texture2D>();

        private void DrawTextureTools()
        {
            var paintIcon = EditorGUIUtility.IconContent(paintMode ? "d_PauseButton" : "d_PlayButton");
            paintIcon.text = paintMode ? "  Disable Paint Mode" : "  Enable Paint Mode";
            var paintBtnStyle = new GUIStyle(GUI.skin.button) { fixedHeight = 24, fontStyle = FontStyle.Bold };
            paintBtnStyle.normal.textColor = paintMode ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 1f, 0.6f);
            if (GUILayout.Button(paintIcon, paintBtnStyle)) paintMode = !paintMode;

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var terrain = (TileTerrain)target;
            if (terrain.Palette == null)
            {
                EditorGUILayout.HelpBox("Assign a TileTerrainPalette in System Setup.", MessageType.Warning);
            }
            else
            {
                if (GUILayout.Button("Full Sync & Recalculate All", GUILayout.Height(28)))
                {
                    Undo.RecordObject(terrain, "Sync Palette");
                    terrain.FullSyncAndRegenerate();
                    EditorUtility.SetDirty(terrain);
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);
            int texCount = terrain.Palette != null ? terrain.Palette.Entries.Count : 0;

            if (texCount > 0)
            {
                EditorGUILayout.LabelField("Select Texture to Paint", EditorStyles.boldLabel);
                DrawTexturePicker(terrain, texCount);
            }
            else
            {
                EditorGUILayout.HelpBox("Add at least one Texture2DArray to paint with.", MessageType.Warning);
            }

            if (textureTool != TextureTool.Fill)
            {
                DrawBrushShapeSelector();
                brushRadius = EditorGUILayout.Slider("Brush Radius", brushRadius, 0.5f, 20f);
            }

            if (textureTool == TextureTool.Smudge)
                brushStrength = EditorGUILayout.Slider("Brush Strength", brushStrength, 0f, 5f);

            if (textureTool == TextureTool.Paint || textureTool == TextureTool.Fill)
                textureRandomness = EditorGUILayout.Slider("Texture Randomness", textureRandomness, 0f, 1f);

            const float btnMinW = 38f;
            EditorGUILayout.BeginHorizontal();
            {
                var cPaint = EditorGUIUtility.IconContent("d_Grid.PaintTool"); cPaint.text = " Painting";
                if (GUILayout.Toggle(textureTool == TextureTool.Paint, cPaint, _toolBtnStyle,
                    GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true), GUILayout.Height(32)))
                    textureTool = TextureTool.Paint;

                var cSmudge = EditorGUIUtility.IconContent("d_scenepicking_pickable_hover"); cSmudge.text = " Smudging";
                if (GUILayout.Toggle(textureTool == TextureTool.Smudge, cSmudge, _toolBtnStyle,
                    GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true), GUILayout.Height(32)))
                    textureTool = TextureTool.Smudge;

                var cFill = EditorGUIUtility.IconContent("d_Grid.FillTool"); cFill.text = " Filling";
                if (GUILayout.Toggle(textureTool == TextureTool.Fill, cFill, _toolBtnStyle,
                    GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true), GUILayout.Height(32)))
                    textureTool = TextureTool.Fill;

                var cErase = EditorGUIUtility.IconContent("d_Grid.EraserTool"); cErase.text = " Erasing";
                if (GUILayout.Toggle(textureTool == TextureTool.Erase, cErase, _toolBtnStyle,
                    GUILayout.MinWidth(btnMinW), GUILayout.ExpandWidth(true), GUILayout.Height(32)))
                    textureTool = TextureTool.Erase;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            var recalcIcon = EditorGUIUtility.IconContent("Refresh");
            recalcIcon.text = "  Recalculate All Bitmasks";
            if (GUILayout.Button(recalcIcon, GUILayout.Height(22)) && terrain.GridData != null)
            {
                Undo.RecordObject(terrain.GridData, "Recalculate Bitmasks");
                TileTerrainBitmask.RecalculateAll(terrain);
                EditorUtility.SetDirty(terrain.GridData);
                terrain.GenerateMesh();
            }
        }

        private void DrawTexturePicker(TileTerrain terrain, int texCount)
        {
            int columns = Mathf.Max(1, (int)(EditorGUIUtility.currentViewWidth - 32) / 72);
            int previewSize = 64;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            int col = 0;
            EditorGUILayout.BeginHorizontal();

            for (int i = 0; i < texCount; i++)
            {
                var entry = terrain.Palette.Entries[i];
                var tex2DA = entry.Texture;

                int runtimeIdx = terrain.RegisteredTextures.IndexOf(tex2DA);
                if (runtimeIdx == -1) continue;

                bool isSelected = (runtimeIdx == selectedTextureIndex);

                Texture2D preview = GetTextureArrayPreview(tex2DA);

                var rect = EditorGUILayout.BeginVertical(GUILayout.Width(previewSize + 8));
                if (isSelected)
                {
                    var selectionRect = new Rect(rect.x - 2, rect.y - 2, rect.xMax - rect.x + 4, rect.yMax - rect.y + 4);
                    EditorGUI.DrawRect(selectionRect, new Color(0.2f, 0.8f, 0.4f, 0.3f));
                    Handles.DrawSolidRectangleWithOutline(selectionRect, new Color(0, 0, 0, 0), new Color(0.2f, 1f, 0.5f, 1f));
                }

                GUIContent btnContent = preview != null
                    ? new GUIContent(preview)
                    : new GUIContent(tex2DA != null ? "Loading..." : "(null)");

                if (GUILayout.Button(btnContent, GUILayout.Width(previewSize + 4), GUILayout.Height(previewSize + 4)))
                {
                    selectedTextureIndex = runtimeIdx;
                    GUI.FocusControl(null);
                }

                string label = $"P:{entry.Priority} {(tex2DA != null ? tex2DA.name : "null")}";
                EditorGUILayout.LabelField(label,
                    isSelected ? EditorStyles.boldLabel : EditorStyles.miniLabel,
                    GUILayout.Width(previewSize + 4));

                EditorGUILayout.EndVertical();

                col++;
                if (col >= columns && i < texCount - 1)
                {
                    col = 0;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            selectedTextureIndex = Mathf.Clamp(selectedTextureIndex, 0, texCount - 1);
        }

        private void PaintTexture(TileTerrain terrain, Vector3 worldHit)
        {
            var data = terrain.GridData;
            if (data == null || data.Vertices == null || data.Vertices.Count == 0) return;

            Vector3 localHit = terrain.transform.InverseTransformPoint(worldHit);

            Undo.RecordObject(data, eraseMode ? "Erase Texture" : "Paint Texture");

            _paintedVertices.Clear();
            int incoming = selectedTextureIndex;

            int w = data.Width;
            int h = data.Height;
            int row = w + 1;
            int xMin = Mathf.Max(0, Mathf.FloorToInt(localHit.x - brushRadius + w * 0.5f));
            int xMax = Mathf.Min(w, Mathf.CeilToInt(localHit.x + brushRadius + w * 0.5f));
            int zMin = Mathf.Max(0, Mathf.FloorToInt(localHit.z - brushRadius + h * 0.5f));
            int zMax = Mathf.Min(h, Mathf.CeilToInt(localHit.z + brushRadius + h * 0.5f));

            for (int z = zMin; z <= zMax; z++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    int i = z * row + x;
                    var vertex = data.Vertices[i];

                    float dx = vertex.position.x - localHit.x;
                    float dz = vertex.position.z - localHit.z;
                    if (brushShape == BrushShape.Square)
                    {
                        if (Mathf.Abs(dx) > brushRadius || Mathf.Abs(dz) > brushRadius) continue;
                    }
                    else
                    {
                        if (dx * dx + dz * dz > brushRadius * brushRadius) continue;
                    }

                    if (textureTool == TextureTool.Erase)
                    {
                        int defaultTex = (terrain.RegisteredTextures != null && terrain.RegisteredTextures.Count > 0) ? 0 : -1;
                        if (defaultTex >= 0)
                        {
                            vertex.overTextureId = defaultTex;
                            vertex.overMask = 0xFF;
                            vertex.midTextureId = -1;
                            vertex.midMask = 0;
                            vertex.underTextureId = -1;
                            vertex.underMask = 0;
                        }
                    }
                    else if (textureTool == TextureTool.Smudge)
                    {
                        int nx = Mathf.Clamp(x + Random.Range(-1, 2), 0, w);
                        int nz = Mathf.Clamp(z + Random.Range(-1, 2), 0, h);
                        var neighbor = data.Vertices[nz * row + nx];
                        if (Random.value < brushStrength * 0.2f)
                        {
                            vertex.overMask = neighbor.overMask;
                            vertex.overTextureId = neighbor.overTextureId;
                            vertex.midMask = neighbor.midMask;
                            vertex.midTextureId = neighbor.midTextureId;
                        }
                    }
                    else
                    {
                        ApplyTextureToVertex(terrain, ref vertex, incoming);
                    }

                    data.Vertices[i] = vertex;
                    _paintedVertices.Add(i);
                }
            }

            if (_paintedVertices.Count > 0)
            {
                TileTerrainBitmask.TextureRandomness = textureRandomness;
                TileTerrainBitmask.BatchRecalculateVertices(terrain, _paintedVertices);
                MarkDirtyChunks(terrain, xMin, xMax, zMin, zMax);
                EditorUtility.SetDirty(data);
                RequestMeshRebuild(terrain);
            }
        }

        private void FillTexture(TileTerrain terrain, Vector3 worldHit)
        {
            if (Event.current.type != EventType.MouseDown) return;

            var data = terrain.GridData;
            if (data == null || data.Vertices == null || data.Vertices.Count == 0) return;

            Vector3 localHit = terrain.transform.InverseTransformPoint(worldHit);

            int w = data.Width;
            int h = data.Height;
            int row = w + 1;

            int startIdx = FindNearestVertexIndex(data, localHit);
            if (startIdx < 0) return;

            var startVertex = data.Vertices[startIdx];
            int sourceId = startVertex.overTextureId;
            int incoming = selectedTextureIndex;

            if (sourceId < 0) return;

            sbyte floor = startVertex.CliffByte;

            Undo.RecordObject(data, "Fill Texture");

            HashSet<int> visited = new HashSet<int>();
            Queue<int> queue = new Queue<int>();

            queue.Enqueue(startIdx);
            visited.Add(startIdx);

            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int vx = idx % row;
                int vz = idx / row;

                FillCheckNeighbor(data, queue, visited, w, h, row, vx - 1, vz, sourceId, floor);
                FillCheckNeighbor(data, queue, visited, w, h, row, vx + 1, vz, sourceId, floor);
                FillCheckNeighbor(data, queue, visited, w, h, row, vx, vz - 1, sourceId, floor);
                FillCheckNeighbor(data, queue, visited, w, h, row, vx, vz + 1, sourceId, floor);
            }

            _paintedVertices.Clear();
            int xMin = int.MaxValue, xMax = int.MinValue, zMin = int.MaxValue, zMax = int.MinValue;
            bool sameTex = sourceId == incoming;
            foreach (int idx in visited)
            {
                if (!sameTex)
                {
                    var vertex = data.Vertices[idx];
                    ApplyTextureToVertex(terrain, ref vertex, incoming);
                    data.Vertices[idx] = vertex;
                }
                _paintedVertices.Add(idx);

                int vx = idx % row;
                int vz = idx / row;
                if (vx < xMin) xMin = vx;
                if (vx > xMax) xMax = vx;
                if (vz < zMin) zMin = vz;
                if (vz > zMax) zMax = vz;
            }

            if (_paintedVertices.Count > 0)
            {
                TileTerrainBitmask.TextureRandomness = textureRandomness;
                TileTerrainBitmask.BatchRecalculateVertices(terrain, _paintedVertices);
                MarkDirtyChunks(terrain, xMin, xMax, zMin, zMax);
                EditorUtility.SetDirty(data);
                RequestMeshRebuild(terrain);
            }
        }

        private static void FillCheckNeighbor(TileTerrainGridData data, Queue<int> queue, HashSet<int> visited,
            int w, int h, int row, int nx, int nz, int sourceId, sbyte floor)
        {
            if (nx < 0 || nz < 0 || nx > w || nz > h) return;
            int idx = nz * row + nx;
            if (visited.Contains(idx)) return;

            var v = data.Vertices[idx];
            if (v.overTextureId != sourceId) return;
            if (v.CliffByte != floor) return;

            visited.Add(idx);
            queue.Enqueue(idx);
        }

        private static readonly int[] _candScratch = new int[4];

        private static void ApplyTextureToVertex(TileTerrain terrain, ref VertexData v, int textureId)
        {
            if (textureId < 0) return;

            if (textureId == v.overTextureId) return;

            float pT = terrain.GetPriority(textureId);
            float pOver = terrain.GetPriority(v.overTextureId);

            var cands = _candScratch;
            int count = 0;
            void TryAdd(int id)
            {
                if (id < 0) return;
                if (terrain.GetPriority(id) < pT) return;
                for (int k = 0; k < count; k++) if (cands[k] == id) return;
                cands[count++] = id;
            }

            TryAdd(textureId);
            TryAdd(v.overTextureId);
            TryAdd(v.midTextureId);
            TryAdd(v.underTextureId);

            for (int i = 1; i < count; i++)
            {
                int key = cands[i];
                float keyP = terrain.GetPriority(key);
                int j = i - 1;
                while (j >= 0 && terrain.GetPriority(cands[j]) > keyP)
                {
                    cands[j + 1] = cands[j];
                    j--;
                }
                cands[j + 1] = key;
            }

            v.overTextureId = count >= 1 ? cands[0] : -1;
            v.midTextureId = count >= 3 ? cands[1] : -1;
            v.underTextureId = count >= 2 ? cands[count >= 3 ? 2 : 1] : -1;

            v.overMask = (v.overTextureId >= 0) ? (byte)0xFF : (byte)0;
            v.midMask = (v.midTextureId >= 0) ? (byte)0xFF : (byte)0;
            v.underMask = (v.underTextureId >= 0) ? (byte)0xFF : (byte)0;
        }

        private Texture2D GetTextureArrayPreview(Texture2DArray texArray)
        {
            if (texArray == null) return null;
            if (_previewCache.TryGetValue(texArray, out Texture2D cached) && cached != null)
                return cached;

            int size = 128;
            Texture2D preview = new Texture2D(size, size, TextureFormat.RGBA32, false);
            preview.name = $"Preview_{texArray.name}";
            preview.hideFlags = HideFlags.HideAndDontSave;

            RenderTexture tempRT = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = tempRT;

            Graphics.Blit(texArray, tempRT);

            preview.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            preview.Apply();

            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(tempRT);

            _previewCache[texArray] = preview;
            return preview;
        }
    }
}
