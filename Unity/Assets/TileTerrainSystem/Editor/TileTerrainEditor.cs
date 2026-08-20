using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MooLucio.TileTerrain
{
    [CustomEditor(typeof(TileTerrain))]
    [InitializeOnLoad]
    public partial class TileTerrainEditor : Editor
    {
        static TileTerrainEditor()
        {
            Selection.selectionChanged -= OnGlobalSelectionChanged;
            Selection.selectionChanged += OnGlobalSelectionChanged;
        }

        private static void OnGlobalSelectionChanged()
        {
            if (Selection.activeGameObject != null)
            {
                var terrain = Selection.activeGameObject.GetComponentInParent<TileTerrain>();
                if (terrain != null && Selection.activeGameObject != terrain.gameObject)
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (terrain != null && Selection.activeGameObject != terrain.gameObject)
                        {
                            Selection.activeGameObject = terrain.gameObject;
                        }
                    };
                }
            }
        }

        // ── Shared ──────────────────────────────────────────────────────────
        internal static TileTerrainEditor ActiveInstance { get; private set; }
        internal int PropsCount => (target as TileTerrain)?.GridData?.Props?.Count ?? 0;
        internal bool paintMode;
        internal float brushRadius = 2f;
        internal float brushStrength = 0.2f;
        internal float textureRandomness = 0.4f;

        private int _paintControlID = -1;
        private static readonly int PaintControlHint = "TileTerrainPaint".GetHashCode();

        // ── Performance: mesh rebuild throttle ─────────────────────────────
        private double _lastMeshRebuildTime = 0;
        private const double MeshRebuildInterval = 1.0 / 30.0;

        // ── Performance: cached grid line arrays ────────────────────────────
        private Vector3[] _cachedGridPositions;
        private Vector3[][] _cachedHLines;
        private Vector3[][] _cachedVLines;
        private bool _gridDirty = true;

        // ── Performance: cached GUIStyles ──────────────────────────────────
        // ── Performance: reusable allocations (avoid per-stroke new) ────────
        private readonly HashSet<int> _paintedVertices = new HashSet<int>();
        private readonly HashSet<int> _modifiedVertices = new HashSet<int>();
        private readonly Queue<(int index, int level, int direction)> _propagationQueue
            = new Queue<(int, int, int)>();
        private readonly HashSet<int> _affectedQuadIndices = new HashSet<int>();

        // ── Performance: falloff LUT (avoid per-vertex sqrt) ────────────────
        private float[] _falloffLut = new float[0];
        private bool _cliffDataChanged = true;

        // ── Performance: dirty chunk tracking for partial rebuild ────────────
        private readonly HashSet<(int, int)> _dirtyChunks = new HashSet<(int, int)>();

        // ── Performance: neighbor-state cache (avoid per-vertex O(8) checks) ──
        private bool[] _touchesWaterCache = new bool[0];
        private bool[] _isBoundaryCache = new bool[0];

        private GUIStyle _tabStyle;
        private GUIStyle _toolBtnStyle;
        private GUIStyle _paintBtnStyle;
        private Vector2 _lastMousePos;
        private bool _isResizingBrush = false;
        private float _brushResizeStartVal = 0f;
        private Vector2 _brushResizeStartMouse = Vector2.zero;

        // ── Mode ────────────────────────────────────────────────────────────
        internal enum BrushShape { Circle, Square }
        internal BrushShape brushShape = BrushShape.Circle;
        internal enum EditorMode { Height, Texture, Cliff, Ramp, Water, Props }
        internal EditorMode editorMode = EditorMode.Height;

        // ── Sub-foldout state ───────────────────────────────────────────────
        private bool _showCoreData = true;
        private bool _showMaterials = false;
        private bool _showCliffMeshes = false;
        private bool _showSceneOverlay = false;
        private bool _showPerformance = false;
        private string SK(string f) => $"TTE_{(target ? target.GetEntityId().GetHashCode() : 0)}_{f}";

        private void OnEnable()
        {
            ActiveInstance = this;
            _paintControlID = -1;

            paintMode = SessionState.GetBool(SK("paintMode"), false);
            editorMode = (EditorMode)SessionState.GetInt(SK("editorMode"), 0);
            brushShape = (BrushShape)SessionState.GetInt(SK("brushShape"), 0);
            heightTool = (HeightTool)SessionState.GetInt(SK("heightTool"), 0);
            selectedTextureIndex = SessionState.GetInt(SK("selTex"), 0);
            brushRadius = SessionState.GetFloat(SK("radius"), 2f);
            brushStrength = SessionState.GetFloat(SK("strength"), 0.2f);
            textureRandomness = SessionState.GetFloat(SK("randtex"), 0.4f);
            targetHeight = SessionState.GetFloat(SK("targetH"), 1f);
            textureTool = (TextureTool)SessionState.GetInt(SK("texTool"), 0);
            cliffTool = (CliffTool)SessionState.GetInt(SK("cliffTool"), (int)CliffTool.Up);
            rampTool = (RampTool)SessionState.GetInt(SK("rampTool"), (int)RampTool.Paint);
            propsTool = (PropsTool)SessionState.GetInt(SK("propsTool"), (int)PropsTool.Place);
            selectedPropIndex = SessionState.GetInt(SK("propsEntry"), 0);
            propsBrushDensity = SessionState.GetFloat(SK("propsDensity"), 0.3f);
            propsSnapToGrid = SessionState.GetBool(SK("propsSnap"), true);
            propsRandomRotate = SessionState.GetBool(SK("propsRandRot"), true);
            _showCoreData = SessionState.GetBool(SK("showCore"), true);
            _showMaterials = SessionState.GetBool(SK("showMat"), false);
            _showCliffMeshes = SessionState.GetBool(SK("showCliff"), false);
            _showSceneOverlay = SessionState.GetBool(SK("showOverlay"), false);
            _showPerformance = SessionState.GetBool(SK("showPerf"), false);

            SceneView.duringSceneGui += OnSceneGUI;

            var terrain = (TileTerrain)target;
            if (terrain != null)
            {
                var icon = EditorGUIUtility.IconContent("d_Terrain Icon");
                if (icon?.image != null)
                    EditorGUIUtility.SetIconForObject(terrain, (Texture2D)icon.image);

                AutoAssignMaterial(terrain);
                terrain.SyncTexturesFromPalette();
                terrain.GenerateMesh();
            }
        }

        private void OnDisable()
        {
            if (ActiveInstance == this) ActiveInstance = null;
            SessionState.SetBool(SK("paintMode"), paintMode);
            SessionState.SetInt(SK("editorMode"), (int)editorMode);
            SessionState.SetInt(SK("brushShape"), (int)brushShape);
            SessionState.SetInt(SK("texTool"), (int)textureTool);
            SessionState.SetInt(SK("cliffTool"), (int)cliffTool);
            SessionState.SetInt(SK("rampTool"), (int)rampTool);
            SessionState.SetInt(SK("propsTool"), (int)propsTool);
            SessionState.SetInt(SK("propsEntry"), selectedPropIndex);
            SessionState.SetFloat(SK("propsDensity"), propsBrushDensity);
            SessionState.SetBool(SK("propsSnap"), propsSnapToGrid);
            SessionState.SetBool(SK("propsRandRot"), propsRandomRotate);
            SessionState.SetInt(SK("heightTool"), (int)heightTool);
            SessionState.SetInt(SK("selTex"), selectedTextureIndex);
            SessionState.SetFloat(SK("radius"), brushRadius);
            SessionState.SetFloat(SK("strength"), brushStrength);
            SessionState.SetFloat(SK("randtex"), textureRandomness);
            SessionState.SetFloat(SK("targetH"), targetHeight);
            SessionState.SetBool(SK("showCore"), _showCoreData);
            SessionState.SetBool(SK("showMat"), _showMaterials);
            SessionState.SetBool(SK("showCliff"), _showCliffMeshes);
            SessionState.SetBool(SK("showOverlay"), _showSceneOverlay);
            SessionState.SetBool(SK("showPerf"), _showPerformance);

            SceneView.duringSceneGui -= OnSceneGUI;
            if (_previewCache != null) _previewCache.Clear();
        }

        // ── Inspector ───────────────────────────────────────────────────────
        private void EnsureStyles()
        {
            if (_toolBtnStyle != null) return;

            _tabStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fixedHeight = 28, fontSize = 12, fontStyle = FontStyle.Bold, padding = new RectOffset(8, 8, 4, 4)
            };

            _paintBtnStyle = new GUIStyle(GUI.skin.button) { fixedHeight = 24, fontStyle = FontStyle.Bold };

            _toolBtnStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fixedHeight = 28,
                alignment = TextAnchor.MiddleLeft,
                margin = new RectOffset(0, 0, 4, 4)
            };
        }

        private void DrawBrushShapeSelector()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Brush Shape");
            var circleIcon = EditorGUIUtility.IconContent("UnlitMode On");
            circleIcon.text = " Circle";
            var squareIcon = EditorGUIUtility.IconContent("d_PlayButton On");
            squareIcon.text = " Square";
            if (GUILayout.Toggle(brushShape == BrushShape.Circle, circleIcon, _toolBtnStyle, GUILayout.ExpandWidth(true)))
                brushShape = BrushShape.Circle;
            if (GUILayout.Toggle(brushShape == BrushShape.Square, squareIcon, _toolBtnStyle, GUILayout.ExpandWidth(true)))
                brushShape = BrushShape.Square;
            EditorGUILayout.EndHorizontal();
        }

        private void AutoAssignMaterial(TileTerrain terrain)
        {
            if (terrain.TileMaterial != null) return;
            string[] guids = AssetDatabase.FindAssets("TileTerrainShader t:Material");
            if (guids.Length > 0)
                terrain.TileMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            serializedObject.Update();

            _showCoreData = EditorGUILayout.BeginFoldoutHeaderGroup(_showCoreData, "  Core Data");
            if (_showCoreData)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("GridData"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Palette"), new GUIContent("Texture Palette"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("PropsBox"), new GUIContent("Props Box"));
                var terrain = (TileTerrain)target;
                if (terrain.GridData != null)
                {
                    var data = terrain.GridData;
                    EditorGUI.BeginChangeCheck();
                    int newIW = EditorGUILayout.IntField(new GUIContent("Internal Width", "Grid width excluding border (quads)."), data.InternalWidth);
                    int newIH = EditorGUILayout.IntField(new GUIContent("Internal Height", "Grid height excluding border (quads)."), data.InternalHeight);
                    int newB = EditorGUILayout.IntField(new GUIContent("Border Size", "Border cells on each side (no collider, auto-added)."), data.BorderSize);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(data, "Change Grid Dimensions");
                        data.InternalWidth = Mathf.Max(1, newIW);
                        data.InternalHeight = Mathf.Max(1, newIH);
                        data.BorderSize = Mathf.Max(0, newB);
                        EditorUtility.SetDirty(data);
                        terrain.GenerateMesh();
                    }
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.TextField("Total Size", $"{data.Width} \u00d7 {data.Height}");
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            _showMaterials = EditorGUILayout.BeginFoldoutHeaderGroup(_showMaterials, "  Materials");
            if (_showMaterials)
            {
                var terrain = (TileTerrain)target;
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField("Terrain Material", terrain.TileMaterial, typeof(Material), false);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("WaterMaterial"), new GUIContent("Water Material"));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            _showCliffMeshes = EditorGUILayout.BeginFoldoutHeaderGroup(_showCliffMeshes, "  Cliff Meshes");
            if (_showCliffMeshes)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("CliffMeshFbx"), new GUIContent("Single Floor", "FBX with single-step cliff meshes for one-level elevation changes."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("CliffDoubleMeshFbx"), new GUIContent("Double Floor", "FBX with double-height cliff meshes for two-level elevation spans."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("CliffTransitionalMeshFbx"), new GUIContent("Transitional", "FBX with transitional meshes for quads containing 3 distinct floor levels."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("RampMeshFbx"), new GUIContent("Ramps", "FBX with ramp meshes for half-step elevation transitions."));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            _showPerformance = EditorGUILayout.BeginFoldoutHeaderGroup(_showPerformance, "  Performance & Occlusion");
            if (_showPerformance)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ChunkSize"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("HideChunksInHierarchy"), new GUIContent("Hide in Hierarchy"));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            _showSceneOverlay = EditorGUILayout.BeginFoldoutHeaderGroup(_showSceneOverlay, "  Scene Overlay");
            if (_showSceneOverlay)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ShowGrid"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("GridColor"), new GUIContent("Small Grid", "Color of the small (1x1) grid lines."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Grid4x4Color"), new GUIContent("Big Grid", "Color of the big (4x4) grid lines for spatial orientation."));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(8);

            // ── Mode Tabs ──
            var heightIcon = EditorGUIUtility.IconContent("TerrainInspector.TerrainToolSculpt On");
            var textureIcon = EditorGUIUtility.IconContent("TerrainInspector.TerrainToolSplat");
            var cliffIcon = EditorGUIUtility.IconContent("TerrainInspector.TerrainToolRaise On");
            heightIcon.text = "  Height";
            textureIcon.text = "  Texture";
            cliffIcon.text = "  Cliff";
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(editorMode == EditorMode.Height, heightIcon, _tabStyle,
                GUILayout.ExpandWidth(true))) editorMode = EditorMode.Height;
            if (GUILayout.Toggle(editorMode == EditorMode.Texture, textureIcon, _tabStyle,
                GUILayout.ExpandWidth(true))) editorMode = EditorMode.Texture;
            if (GUILayout.Toggle(editorMode == EditorMode.Cliff, cliffIcon, _tabStyle,
                GUILayout.ExpandWidth(true))) editorMode = EditorMode.Cliff;
            EditorGUILayout.EndHorizontal();

            var rampIcon = EditorGUIUtility.IconContent("d_DragArrow"); rampIcon.text = "  Ramp";
            var waterIcon = EditorGUIUtility.IconContent("TerrainInspector.TerrainToolSmoothHeight On");
            waterIcon.text = "  Water";
            var propsIcon = EditorGUIUtility.IconContent("d_TerrainInspector.TerrainToolTrees");
            propsIcon.text = "  Props";
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(editorMode == EditorMode.Ramp, rampIcon, _tabStyle,
                GUILayout.ExpandWidth(true))) editorMode = EditorMode.Ramp;
            if (GUILayout.Toggle(editorMode == EditorMode.Water, waterIcon, _tabStyle,
                GUILayout.ExpandWidth(true))) editorMode = EditorMode.Water;
            if (GUILayout.Toggle(editorMode == EditorMode.Props, propsIcon, _tabStyle,
                GUILayout.ExpandWidth(true))) editorMode = EditorMode.Props;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);

            if (editorMode == EditorMode.Height) DrawHeightTools();
            else if (editorMode == EditorMode.Texture) DrawTextureTools();
            else if (editorMode == EditorMode.Cliff) DrawCliffTools();
            else if (editorMode == EditorMode.Ramp) DrawRampTools();
            else if (editorMode == EditorMode.Water) DrawWaterTools();
            else DrawPropsTools();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
