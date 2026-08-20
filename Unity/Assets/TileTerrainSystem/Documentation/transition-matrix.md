# Transitional Cliff Mesh Matrix

> **English** | [Português (Brasil)](transition-matrix.pt-BR.md)

Maps 4-vertex height combinations to transitional cliff mesh indices (0–35).

These are used when a quad has **3 unique floor levels** (`n`, `n+1`, `n+2`), requiring a special transitional mesh instead of standard single/double-height cliffs.

## Vertex Order

`[v0, v1, v2, v3]` — natural quad order (NOT the `[v2, v3, v0, v1]` remapping used for texture tiles).

Values are relative offsets from the quad's minimum floor level (0, 1, or 2).

## Distribution Categories

| Distribution | Indices | Pattern |
|-------------|---------|---------|
| **1** | 0–3 | 2 floor, 1 mid, 1 high. High adjacent to both floors. |
| **2** | 4–7 | 1 floor, 2 mid, 1 high. Mids adjacent. |
| **3** | 8–11 | 1 floor, 1 mid, 2 high. Highs adjacent. |
| **4** | 12–15 | 2 floor, 1 mid, 1 high. High diagonal to one floor. |
| **5** | 16–19 | 1 floor, 2 mid, 1 high. Mids diagonal. |
| **6** | 20–23 | (extended) |
| **7** | 24–28 | (extended, higher combinations) |
| **8** | 29–35 | (extended, higher combinations) |

## Full Mapping

| Index | v0 | v1 | v2 | v3 |
|:-----:|:--:|:--:|:--:|:--:|
| 0 | 0 | 0 | 1 | 2 |
| 1 | 0 | 0 | 2 | 1 |
| 2 | 0 | 1 | 0 | 2 |
| 3 | 0 | 1 | 1 | 2 |
| 4 | 0 | 1 | 2 | 0 |
| 5 | 0 | 1 | 2 | 1 |
| 6 | 0 | 1 | 2 | 2 |
| 7 | 0 | 2 | 0 | 1 |
| 8 | 0 | 2 | 1 | 0 |
| 9 | 0 | 2 | 1 | 1 |
| 10 | 0 | 2 | 1 | 2 |
| 11 | 0 | 2 | 2 | 1 |
| 12 | 1 | 0 | 0 | 2 |
| 13 | 1 | 0 | 1 | 2 |
| 14 | 1 | 0 | 2 | 0 |
| 15 | 1 | 0 | 2 | 1 |
| 16 | 1 | 0 | 2 | 2 |
| 17 | 1 | 1 | 0 | 2 |
| 18 | 1 | 1 | 2 | 0 |
| 19 | 1 | 2 | 0 | 0 |
| 20 | 1 | 2 | 0 | 1 |
| 21 | 1 | 2 | 0 | 2 |
| 22 | 1 | 2 | 1 | 0 |
| 23 | 1 | 2 | 2 | 0 |
| 24 | 2 | 0 | 0 | 1 |
| 25 | 2 | 0 | 1 | 0 |
| 26 | 2 | 0 | 1 | 1 |
| 27 | 2 | 0 | 1 | 2 |
| 28 | 2 | 0 | 2 | 1 |
| 29 | 2 | 1 | 0 | 0 |
| 30 | 2 | 1 | 0 | 1 |
| 31 | 2 | 1 | 0 | 2 |
| 32 | 2 | 1 | 1 | 0 |
| 33 | 2 | 1 | 2 | 0 |
| 34 | 2 | 2 | 0 | 1 |
| 35 | 2 | 2 | 1 | 0 |

## Code Reference

See `TileTerrainBitmask.cs`:
- Static constructor builds `HeightToTransitionIndex` lookup table from `MakeKey()`
- `GetTransitionalMeshIndex()` returns the mesh index for a given quad (+ floor offset)
- Transitional meshes are loaded from `cliff_transitional_mesh.fbx` (20 meshes, indices match the first 20 entries above)
