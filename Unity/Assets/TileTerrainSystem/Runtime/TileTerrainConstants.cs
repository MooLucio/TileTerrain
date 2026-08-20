namespace MooLucio.TileTerrain
{
    /// <summary>
    /// Named constants shared across the tile terrain system.
    /// Keeps the magic numbers that define cliff levels, floor accumulation
    /// and auto-tiling in one place so they can be tuned consistently.
    /// </summary>
    public static class TileTerrainConstants
    {
        /// <summary>Sentinel "no cliff / no floor" level. Sbyte can't go below this.</summary>
        public const sbyte NoCliffLevel = -128;

        /// <summary>Sentinel ceiling level. Sbyte can't go above this.</summary>
        public const sbyte MaxCliffLevel = 127;

        /// <summary>Lowest cliff level the editor allows while painting.</summary>
        public const sbyte MinEditableCliff = -3;

        /// <summary>Highest cliff level the editor allows while painting.</summary>
        public const sbyte MaxEditableCliff = 11;

        /// <summary>Sentinel used while accumulating floor offsets from cliff tiers.</summary>
        public const float NoFloorOffset = -100f;

        /// <summary>4-bit mask with all corners cliffed → flat quad raised to the floor tier.</summary>
        public const int FullQuadMask = 15;

        /// <summary>Fully-occluding texture mask byte (all 8 bits set).</summary>
        public const byte SolidTextureMask = 0xFF;

        /// <summary>Texture array index of the solid base tile used for fully-cliffed quads.</summary>
        public const int SolidBaseTile = 27;
    }
}
