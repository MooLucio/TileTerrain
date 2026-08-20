using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace MooLucio.TileTerrain
{
    [CreateAssetMenu(menuName = "Tiled terrain/Props Box", fileName = "TileTerrainPropsBox")]
    public class TileTerrainPropsBox : ScriptableObject
    {
        [FormerlySerializedAs("props")] public List<TileTerrainProp> Props = new List<TileTerrainProp>();

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
