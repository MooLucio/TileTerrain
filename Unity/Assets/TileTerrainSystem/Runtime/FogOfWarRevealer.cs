using UnityEngine;
using UnityEngine.Serialization;

namespace MooLucio.TileTerrain
{
    public enum FogRevealPersistence
    {
        Persistent,
        Flashlight
    }

    [DisallowMultipleComponent]
    public class FogOfWarRevealer : MonoBehaviour
    {
        [Header("Reveal")]
        [Tooltip("Reveal Radius in grid cells.")]
        [FormerlySerializedAs("radius")]
        [Min(0f)] public float Radius = 8f;

        [Tooltip("Height offset above the GameObject pivot for the LOS eye position (world units).")]
        [FormerlySerializedAs("eyeHeight")]
        public float EyeHeight = 1.8f;

        [Header("Line of Sight")]
        [Tooltip("Run cliff-aware line-of-sight check. When false, every cell within Radius is revealed.")]
        [FormerlySerializedAs("occluded")]
        public bool Occluded = true;

        [Tooltip("Use a 4-connected flood fill from the revealer (O(r²)) instead of the per-cell DDA " +
                 "raycast (O(r³)). A cell whose max height exceeds the eye blocks the fill from spreading " +
                 "past it (it is still painted). Cheaper for large radii; gives 'shadow casting' rather " +
                 "than true line of sight.")]
        [FormerlySerializedAs("useFloodFill")]
        public bool UseFloodFill = false;

        [Tooltip("If true, explored cells stay explored forever. If false (Flashlight), explored mirrors visible.")]
        [FormerlySerializedAs("persistence")]
        public FogRevealPersistence Persistence = FogRevealPersistence.Persistent;

        [Header("Debug")]
        [Tooltip("Draw LOS debug gizmos in the Scene view.")]
        [FormerlySerializedAs("debugDraw")]
        public bool DebugDraw = false;

        [System.NonSerialized] public Vector2Int GridCell;

        void OnEnable()
        {
            FogOfWarManager.Register(this);
        }

        void OnDisable()
        {
            FogOfWarManager.Unregister(this);
        }

        void OnDrawGizmosSelected()
        {
            if (!DebugDraw) return;
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * EyeHeight, 0.2f);
        }
    }
}
