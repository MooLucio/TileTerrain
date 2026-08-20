using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Linq;

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

            string baseDir = FindBaseDir();
            if (string.IsNullOrEmpty(baseDir)) return;

            string iconDir = $"{baseDir}/Icons";
            if (!Directory.Exists(iconDir)) return;

            string[] iconFiles = Directory.GetFiles(iconDir, "*.png")
                .Where(f => !f.EndsWith(".meta"))
                .ToArray();

            string[] allScriptGuids = AssetDatabase.FindAssets("t:MonoScript");
            foreach (string guid in allScriptGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs")) continue;

                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null) continue;

                Type scriptType = script.GetClass();
                if (scriptType == null) continue;

                if (typeof(ScriptableObject).IsAssignableFrom(scriptType))
                {
                    Texture2D icon = FindMatchingIcon(scriptType.Name, iconFiles, iconDir);
                    if (icon != null)
                        ApplyIcon(script, icon);
                }
            }

            // Native icons for non-SO scripts
            SetNativeIconForScript("TileTerrain", "Terrain Icon");
            SetNativeIconForScript("FogOfWarManager", "Services-Selected-Focused@2x");
            SetNativeIconForScript("FogOfWarRevealer", "d_toggle_searcher_preview_on_hover");
        }

        private static Texture2D FindMatchingIcon(string className, string[] iconFiles, string iconDir)
        {
            foreach (string iconFile in iconFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(iconFile);

                if (fileName == className || fileName == $"{className} Icon")
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(iconFile);
            }
            return null;
        }

        private static void ApplyIcon(MonoScript script, Texture2D icon)
        {
            EditorGUIUtility.SetIconForObject(script, icon);
            EditorUtility.SetDirty(script);
        }

        private static void SetNativeIconForScript(string scriptName, string nativeIconName)
        {
            MonoScript script = FindScript(scriptName);
            Texture2D icon = EditorGUIUtility.IconContent(nativeIconName).image as Texture2D;

            if (script != null && icon != null)
                ApplyIcon(script, icon);
        }

        private static MonoScript FindScript(string scriptName)
        {
            string[] scriptGuids = AssetDatabase.FindAssets($"{scriptName} t:MonoScript");
            foreach (string guid in scriptGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith($"{scriptName}.cs"))
                    return AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            }
            return null;
        }

        private static string FindBaseDir()
        {
            string[] guids = AssetDatabase.FindAssets("t:MonoScript TileTerrainIconInitializer");
            if (guids.Length == 0) return null;

            string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            return Path.GetDirectoryName(Path.GetDirectoryName(scriptPath));
        }
    }
}
