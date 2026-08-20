# Autotile Matrix Solution

> **English** | [Portugues (Brasil)](autotile-matrix.pt-BR.md)

Maps 4-bit vertex bitmasks to texture-slice indices for auto-tiling.

## Vertex Order

The quad vertex order used for bitmask calculation is:

```
[v2, v3, v0, v1]
```

- `v2`, `v3` = upper half of the quad (top)
- `v0`, `v1` = lower half of the quad (bottom)

## Special Case: Fully Surrounded (1,1,1,1)

When all four vertices match, the tile is randomized for visual variety.
Randomization candidates: `0, 4, 5, 7, 12, 13, 14, 15, 20, 21, 22, 23, 27, 28, 29, 30, 31`

## Bitmask to Texture Index

| Vertices `[v2, v3, v0, v1]` | Texture Index |
|----------------------------|:-------------:|
| `0, 0, 0, 1` | 1 |
| `0, 0, 1, 0` | 2 |
| `0, 0, 1, 1` | 3 |
| `0, 1, 0, 0` | 8 |
| `0, 1, 0, 1` | 9 |
| `0, 1, 1, 0` | 10 |
| `0, 1, 1, 1` | 11 |
| `1, 0, 0, 0` | 16 |
| `1, 0, 0, 1` | 17 |
| `1, 0, 1, 0` | 18 |
| `1, 0, 1, 1` | 19 |
| `1, 1, 0, 0` | 24 |
| `1, 1, 0, 1` | 25 |
| `1, 1, 1, 0` | 26 |

### Full Mask (15) — Randomized Center Textures

When mask `== 15` (all vertices match), the system selects a random texture from columns 4–7 (center tiles). See `TileTerrainBitmask.GetTextureIndex()` for the weighted selection logic.
