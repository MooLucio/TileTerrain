using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace MooLucio.TileTerrain
{
    [CreateAssetMenu(menuName = "Tiled terrain/Prop", fileName = "TileTerrainProp")]
    public class TileTerrainProp : ScriptableObject
    {
        [FormerlySerializedAs("label")] public string Label;
        [FormerlySerializedAs("prefabs")] public List<GameObject> Prefabs;
        [FormerlySerializedAs("minScale")] public float MinScale = 0.5f;
        [FormerlySerializedAs("maxScale")] public float MaxScale = 1.5f;
        [FormerlySerializedAs("randomRotation")] public bool RandomRotation = true;
        [FormerlySerializedAs("canRotate")] public bool CanRotate = true;
        [FormerlySerializedAs("canScale")] public bool CanScale = true;
        [Tooltip("How many quads (cells) this prop occupies horizontally.")]
        [FormerlySerializedAs("occupyWidth")] public int OccupyWidth = 1;
        [Tooltip("How many quads (cells) this prop occupies vertically.")]
        [FormerlySerializedAs("occupyHeight")] public int OccupyHeight = 1;
        [Tooltip("Allow placing this prop on water terrain.")]
        [FormerlySerializedAs("canPlaceInWater")] public bool CanPlaceInWater = true;

#if UNITY_EDITOR
        [System.NonSerialized] private bool _repaintQueued;
        private void OnValidate()
        {
            if (_repaintQueued) return;
            _repaintQueued = true;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                _repaintQueued = false;
                UnityEditor.SceneView.RepaintAll();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            };
        }
#endif
    }
}
