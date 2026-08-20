using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace MooLucio.TileTerrain
{
    [Overlay(typeof(SceneView), "Tile Terrain", "Tile Terrain Info")]
    public class TileTerrainOverlay : Overlay
    {
        private static readonly GUIContent[] ModeIcons =
        {
            EditorGUIUtility.IconContent("TerrainInspector.TerrainToolSculpt On"),
            EditorGUIUtility.IconContent("TerrainInspector.TerrainToolSplat"),
            EditorGUIUtility.IconContent("TerrainInspector.TerrainToolRaise On"),
            EditorGUIUtility.IconContent("d_ScaleTool"),
            EditorGUIUtility.IconContent("TerrainInspector.TerrainToolSmoothHeight On"),
            EditorGUIUtility.IconContent("d_TerrainInspector.TerrainToolTrees"),
        };

        private static GUIContent _playIcon;
        private static GUIContent _pauseIcon;
        private static readonly string[] ModeLabels = { "Height", "Texture", "Cliff", "Ramp", "Water", "Props" };

        private static GUIContent _iconRaising;
        private static GUIContent _iconLowering;
        private static GUIContent _iconTargeting;
        private static GUIContent _iconSmoothing;
        private static GUIContent _iconNoise;
        private static GUIContent _iconPainting;
        private static GUIContent _iconSmudging;
        private static GUIContent _iconErasing;
        private static GUIContent _iconFlooding;
        private static GUIContent _iconRotating;
        private static GUIContent _iconScaling;
        private static GUIContent _iconSelecting;

        private static GUIContent Icon(string name, string text)
        {
            var c = EditorGUIUtility.IconContent(name);
            if (c.image != null)
            {
                var r = new GUIContent(c.image, text);
                r.text = text;
                return r;
            }
            return new GUIContent(text);
        }

        private static void EnsureIcons()
        {
            if (_iconRaising != null) return;
            _iconRaising = Icon("d_icon dropdown open@2x", "Raising");
            _iconLowering = Icon("d_icon dropdown@2x", "Lowering");
            _iconTargeting = Icon("d_SceneLayersToggle", "Targeting");
            _iconSmoothing = Icon("ShadedWireframe On", "Smoothing");
            _iconNoise = Icon("d_ToggleUVOverlay", "Noise");
            _iconPainting = Icon("d_Grid.PaintTool", "Painting");
            _iconSmudging = Icon("d_scenepicking_pickable_hover", "Smudging");
            _iconErasing = Icon("d_Grid.EraserTool", "Erasing");
            _iconFlooding = Icon("d_Grid.FillTool", "Flooding");
            _iconRotating = Icon("d_RotateTool", "Rotating");
            _iconScaling = Icon("d_ScaleTool", "Scaling");
            _iconSelecting = Icon("d_scenepicking_pickable_hover", "Selecting");
            _playIcon = Icon("d_PlayButton", "");
            if (_playIcon.image == null) _playIcon = Icon("PlayButton", "");
            _pauseIcon = Icon("d_PauseButton", "");
            if (_pauseIcon.image == null) _pauseIcon = Icon("PauseButton", "");
        }

        private static GUIContent ActionIcon(TileTerrainEditor e)
        {
            EnsureIcons();
            return e.editorMode switch
            {
                TileTerrainEditor.EditorMode.Height => e.heightTool switch
                {
                    TileTerrainEditor.HeightTool.Raise => _iconRaising,
                    TileTerrainEditor.HeightTool.Lower => _iconLowering,
                    TileTerrainEditor.HeightTool.Target => _iconTargeting,
                    TileTerrainEditor.HeightTool.Smooth => _iconSmoothing,
                    TileTerrainEditor.HeightTool.Noise => _iconNoise,
                    _ => null,
                },
                TileTerrainEditor.EditorMode.Texture => e.textureTool switch
                {
                    TileTerrainEditor.TextureTool.Paint => _iconPainting,
                    TileTerrainEditor.TextureTool.Smudge => _iconSmudging,
                    TileTerrainEditor.TextureTool.Erase => _iconErasing,
                    TileTerrainEditor.TextureTool.Fill => _iconFlooding,
                    _ => null,
                },
                TileTerrainEditor.EditorMode.Cliff => e.cliffTool switch
                {
                    TileTerrainEditor.CliffTool.Up => _iconRaising,
                    TileTerrainEditor.CliffTool.Down => _iconLowering,
                    TileTerrainEditor.CliffTool.Target => _iconTargeting,
                    TileTerrainEditor.CliffTool.Smudge => _iconSmudging,
                    TileTerrainEditor.CliffTool.Erase => _iconErasing,
                    _ => null,
                },
                TileTerrainEditor.EditorMode.Ramp => e.rampTool switch
                {
                    TileTerrainEditor.RampTool.Paint => _iconPainting,
                    TileTerrainEditor.RampTool.Erase => _iconErasing,
                    _ => null,
                },
                TileTerrainEditor.EditorMode.Water => _iconFlooding,
                TileTerrainEditor.EditorMode.Props => e.propsTool switch
                {
                    TileTerrainEditor.PropsTool.Place => _iconPainting,
                    TileTerrainEditor.PropsTool.Paint => _iconPainting,
                    TileTerrainEditor.PropsTool.Select => _iconSelecting,
                    TileTerrainEditor.PropsTool.Remove => _iconErasing,
                    TileTerrainEditor.PropsTool.Rotate => _iconRotating,
                    TileTerrainEditor.PropsTool.Scale => _iconScaling,
                    TileTerrainEditor.PropsTool.Erase => _iconErasing,
                    _ => null,
                },
                _ => null,
            };
        }

        public override VisualElement CreatePanelContent()
        {
            var container = new IMGUIContainer(DrawInfoBar)
            {
                style =
                {
                    minWidth = 430,
                    minHeight = 24,
                    marginLeft = 0,
                    marginRight = 0,
                    marginTop = 0,
                    marginBottom = 0,
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 2,
                    paddingBottom = 2,
                    borderLeftWidth = 0,
                    borderRightWidth = 0,
                    borderTopWidth = 0,
                    borderBottomWidth = 0,
                }
            };
            return container;
        }

        private GUIContent PaintIndicator
        {
            get
            {
                EnsureIcons();
                bool paint = editor != null && editor.paintMode;
                var icon = paint ? _playIcon : _pauseIcon;
                if (icon != null && icon.image != null) return icon;
                return new GUIContent(paint ? "\u25B6" : "\u25A0");
            }
        }

        private GUIStyle _actionStyle;

        private GUIStyle ActionStyle
        {
            get
            {
                if (_actionStyle == null)
                {
                    _actionStyle = new GUIStyle(EditorStyles.miniLabel) { imagePosition = ImagePosition.ImageLeft };
                }
                return _actionStyle;
            }
        }

        private TileTerrainEditor _cachedEditor;

        private TileTerrainEditor editor
        {
            get
            {
                if (_cachedEditor == null || _cachedEditor != TileTerrainEditor.ActiveInstance)
                    _cachedEditor = TileTerrainEditor.ActiveInstance;
                return _cachedEditor;
            }
        }

        private void DrawInfoBar()
        {
            if (editor == null)
            {
                GUILayout.Label("Select a TileTerrain", EditorStyles.miniLabel);
                return;
            }

            var origColor = GUI.color;
            var origContent = GUI.contentColor;

            GUILayout.BeginHorizontal();

            int modeIndex = (int)editor.editorMode;
            GUIContent modeIcon = ModeIcons[modeIndex];
            GUIContent cleanModeIcon = (modeIcon != null && modeIcon.image != null)
                ? new GUIContent(modeIcon.image, modeIcon.tooltip)
                : modeIcon;
            string modeLabel = ModeLabels[modeIndex];

            Color modeColor = editor.editorMode switch
            {
                TileTerrainEditor.EditorMode.Height    => new Color(0.20f, 0.60f, 1.00f),
                TileTerrainEditor.EditorMode.Texture   => new Color(1.00f, 0.70f, 0.15f),
                TileTerrainEditor.EditorMode.Cliff     => new Color(0.75f, 0.55f, 0.30f),
                TileTerrainEditor.EditorMode.Ramp      => new Color(0.40f, 0.85f, 0.40f),
                TileTerrainEditor.EditorMode.Water     => new Color(0.15f, 0.65f, 0.90f),
                TileTerrainEditor.EditorMode.Props     => new Color(0.50f, 0.80f, 0.30f),
                _ => Color.gray,
            };

            GUI.color = modeColor;
            GUILayout.Label(cleanModeIcon, GUILayout.Width(20), GUILayout.Height(20));
            GUI.color = origColor;
            GUI.contentColor = modeColor;
            GUILayout.Label(modeLabel, EditorStyles.miniBoldLabel, GUILayout.Width(56), GUILayout.Height(20));
            GUI.contentColor = origContent;

            GUILayout.Space(4);

            EditorGUI.DrawRect(
                GUILayoutUtility.GetRect(1, 16, GUILayout.Width(1), GUILayout.Height(16)),
                new Color(0.55f, 0.55f, 0.55f, 0.40f));

            GUILayout.Space(6);

            GUI.color = editor.paintMode ? new Color(0.20f, 1.00f, 0.35f) : new Color(0.55f, 0.55f, 0.55f);
            GUILayout.Label(PaintIndicator, GUILayout.Width(20), GUILayout.Height(20));
            GUI.color = origColor;
            GUILayout.Label(editor.paintMode ? "Paint" : "Paused", EditorStyles.miniLabel, GUILayout.Width(42), GUILayout.Height(20));

            GUILayout.Space(4);

            EditorGUI.DrawRect(
                GUILayoutUtility.GetRect(1, 16, GUILayout.Width(1), GUILayout.Height(16)),
                new Color(0.55f, 0.55f, 0.55f, 0.40f));

            GUILayout.Space(6);

            GUIContent action = ActionIcon(editor);
            if (action != null)
                GUILayout.Label(action, ActionStyle, GUILayout.Height(20));
            else
                GUILayout.Space(4);

            GUILayout.FlexibleSpace();

            if (editor.editorMode == TileTerrainEditor.EditorMode.Props)
            {
                string toolName = editor.propsTool switch
                {
                    TileTerrainEditor.PropsTool.Place => "Place",
                    TileTerrainEditor.PropsTool.Paint => "Paint",
                    TileTerrainEditor.PropsTool.Select => "Select",
                    TileTerrainEditor.PropsTool.Remove => "Remove",
                    TileTerrainEditor.PropsTool.Rotate => "Rotate",
                    TileTerrainEditor.PropsTool.Scale => "Scale",
                    TileTerrainEditor.PropsTool.Erase => "Erase",
                    _ => ""
                };
                GUILayout.Label($"{toolName} ({editor.PropsCount})", EditorStyles.miniLabel, GUILayout.Width(85), GUILayout.Height(20));
            }
            else
            {
                string shapeStr = editor.brushShape == TileTerrainEditor.BrushShape.Square ? "Sq" : "Cir";
                GUILayout.Label($"{shapeStr} R:{editor.brushRadius:F1}", EditorStyles.miniLabel, GUILayout.Width(85), GUILayout.Height(20));
            }

            if (editor.editorMode == TileTerrainEditor.EditorMode.Height)
                GUILayout.Label($"Strength: {editor.brushStrength:F2}", EditorStyles.miniLabel, GUILayout.Width(85), GUILayout.Height(20));

            if (editor.editorMode == TileTerrainEditor.EditorMode.Props && editor.propsTool == TileTerrainEditor.PropsTool.Paint)
                GUILayout.Label($"D:{editor.propsBrushDensity:F2}", EditorStyles.miniLabel, GUILayout.Width(85), GUILayout.Height(20));

            GUILayout.EndHorizontal();

            GUI.color = origColor;
            GUI.contentColor = origContent;
        }
    }
}
