using System.Collections.Generic;
using UnityEngine;

namespace MooLucio.TileTerrain
{
    [CreateAssetMenu(menuName = "Tiled terrain/Texture Palette", fileName = "TileTerrainTexturePalette")]
    public class TileTerrainPalette : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public Texture2DArray Texture;
            public float Priority;
        }

        public Texture2D CliffTexture;
        public List<Entry> Entries = new List<Entry>();

        public float GetPriority(Texture2DArray tex)
        {
            foreach (var e in Entries)
            {
                if (e.Texture == tex) return e.Priority;
            }
            return float.MaxValue;
        }

        public int GetIndex(Texture2DArray tex)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Texture == tex) return i;
            }
            return -1;
        }
    }
}
