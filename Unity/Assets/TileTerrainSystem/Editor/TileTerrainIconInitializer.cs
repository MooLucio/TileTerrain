using UnityEditor;
using UnityEngine;
using System.IO;

namespace MooLucio.TileTerrain
{
    [InitializeOnLoad]
    public static class TileTerrainIconInitializer
    {
        private static bool _initialized;

        static TileTerrainIconInitializer()
        {
            if (_initialized || EditorApplication.isUpdating) return;
            _initialized = true;
            EditorApplication.delayCall += AssignIcons;
        }

        [MenuItem("Tools/Tile Terrain/Assign Terrain Icons")]
        public static void AssignIcons()
        {
            if (EditorApplication.isUpdating) return;

            string[] scriptGuids = AssetDatabase.FindAssets("t:MonoScript TileTerrainIconInitializer");
            string baseDir = "";
            if (scriptGuids.Length > 0)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(scriptGuids[0]);
                baseDir = Path.GetDirectoryName(Path.GetDirectoryName(scriptPath));
            }
            string iconDir = $"{baseDir}/Icons";

            // Custom icons for ScriptableObjects
            SetIconForScript("TileTerrainGridData", $"{iconDir}/TileTerrainGridData Icon.png");
            SetIconForScript("TileTerrainPalette", $"{iconDir}/TileTerrainPalette Icon.png");

            // Native icon for the main TileTerrain component
            SetNativeIconForScript("TileTerrain", "Terrain Icon");

            // Native icons for the fog of war scripts
            SetNativeIconForScript("FogOfWarManager", "Services-Selected-Focused@2x");
            SetNativeIconForScript("FogOfWarRevealer", "d_toggle_searcher_preview_on_hover");
        }

        private static void SetIconForScript(string scriptName, string iconPath)
        {
            MonoScript script = FindScript(scriptName);
            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);

            if (script != null && icon != null)
            {
                EditorGUIUtility.SetIconForObject(script, icon);
                EditorUtility.SetDirty(script);
                AssetDatabase.SaveAssetIfDirty(script);
            }
        }

        private static void SetNativeIconForScript(string scriptName, string nativeIconName)
        {
            MonoScript script = FindScript(scriptName);
            // Load the native Unity icon
            Texture2D icon = EditorGUIUtility.IconContent(nativeIconName).image as Texture2D;

            if (script != null && icon != null)
            {
                EditorGUIUtility.SetIconForObject(script, icon);
                EditorUtility.SetDirty(script);
                AssetDatabase.SaveAssetIfDirty(script);
            }
        }

        private static MonoScript FindScript(string scriptName)
        {
            string[] scriptGuids = AssetDatabase.FindAssets($"{scriptName} t:MonoScript");
            if (scriptGuids.Length == 0) return null;

            foreach (var guid in scriptGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith($"{scriptName}.cs"))
                {
                    return AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                }
            }
            return null;
        }
    }
}
