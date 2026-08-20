# Getting Started

> **English** | [Portugues (Brasil)](getting-started.pt-BR.md)

This guide covers installation, initial setup, and the texture array configuration required for autotiling.

---

## Prerequisites

- **Unity 6** or later
- **Universal Render Pipeline (URP) 17** with RenderGraph enabled
- Your project must use the URP renderer (not the built-in pipeline)

---

## Installation

### Option A: Manual Import

1. Copy the `TileTerrainSystem` folder into your project's `Assets/` directory.
2. Unity will compile the scripts automatically.
3. The custom icons and materials will be auto-assigned on first use.

### Option B: Unity Package Manager (UPM)

> Coming soon. For now, use manual import.

---

## Initial Setup

### 1. Create the Grid Data Asset

1. In the Hierarchy, create an empty GameObject and name it `TileTerrain`.
2. Add the `TileTerrain` component to it.
3. In the Inspector, click **Create New Grid Data**.
4. Set the grid dimensions:
   - **Internal Width** / **Internal Height**: Number of quads (not vertices). A 64x64 grid has 65x65 vertices.
   - **Border Size**: Number of decorative-only border cells per side (no collider). Set to 0 for no border.

### 2. Create the Texture Palette

1. Right-click in the Project window: **Create > Tiled terrain > Texture Palette**.
2. Name it (e.g., `TerrainPalette`).
3. Assign it to the TileTerrain component's **Palette** field.
4. Add entries to the palette — each entry is a `Texture2DArray` with a priority value (lower = higher priority, renders on top).

### 3. Create the Props Box (optional)

1. Right-click: **Create > Tiled terrain > Props Box**.
2. Add `TileTerrainProp` entries for each prop type.
3. Assign the Props Box to the TileTerrain component.

### 4. Assign Materials and Meshes

| Field | Required | Description |
|-------|----------|-------------|
| **Tile Material** | Auto-assigned | Terrain surface shader (auto-detected from `TileTerrainShader.mat`) |
| **Water Material** | Yes (for water) | Translucent water surface shader |
| **Cliff Mesh Fbx** | Yes (for cliffs) | FBX with 14 standard cliff sub-meshes |
| **Cliff Double Mesh Fbx** | No (for double-height) | FBX with 14 double-height cliff sub-meshes |
| **Cliff Transitional Mesh Fbx** | No (for transitions) | FBX with transitional cliff sub-meshes |
| **Ramp Mesh Fbx** | No (for ramps) | FBX with 36 ramp sub-meshes |

### 5. Start Sculpting

1. Select the TileTerrain GameObject.
2. Choose a tool tab in the Inspector (Height, Texture, Cliff, Ramp, Water, or Props).
3. Press `S` to enable paint mode.
4. Paint in the Scene view.

---

## Texture Array Configuration

The autotile system uses a **Texture2DArray** arranged as an **8-column x 4-row tilemap**. Each texture type you want to paint requires its own Texture2DArray with this layout.

### Sheet Layout

Each sprite sheet is **512 x 256 pixels**, divided into a grid of **8 columns x 4 rows** (each cell is **64 x 64 pixels**).

```
     Col 0   Col 1   Col 2   Col 3   Col 4   Col 5   Col 6   Col 7
    ┌───────┬───────┬───────┬───────┬───────┬───────┬───────┬───────┐
Row │       │       │       │       │       │       │       │       │
 0  │  c0r0 │  c1r0 │  c2r0 │  c3r0 │  c4r0 │  c5r0 │  c6r0 │  c7r0 │
    ├───────┼───────┼───────┼───────┼───────┼───────┼───────┼───────┤
Row │       │       │       │       │       │       │       │       │
 1  │  c0r1 │  c1r1 │  c2r1 │  c3r1 │  c4r1 │  c5r1 │  c6r1 │  c7r1 │
    ├───────┼───────┼───────┼───────┼───────┼───────┼───────┼───────┤
Row │       │       │       │       │       │       │       │       │
 2  │  c0r2 │  c1r2 │  c2r2 │  c3r2 │  c4r2 │  c5r2 │  c6r2 │  c7r2 │
    ├───────┼───────┼───────┼───────┼───────┼───────┼───────┼───────┤
Row │       │       │       │       │       │       │       │       │
 3  │  c0r3 │  c1r3 │  c2r3 │  c3r3 │  c4r3 │  c5r3 │  c6r3 │  c7r3 │
    └───────┴───────┴───────┴───────┴───────┴───────┴───────┴───────┘
     ◄──── Connectors / Corners ────►  ◄──── Random Center Tiles ────►
```

### Column Groups

| Columns | Purpose | Description |
|---------|---------|-------------|
| **0-3** | **Connector / Corner tiles** | Bitmask-driven tiles. Each of the 14 non-trivial bitmask patterns maps to exactly one of these 16 cells (4 columns x 4 rows). These tiles show the edge/corner transitions between this texture and the surrounding textures. |
| **4-7** | **Random Center tiles** | Used when a tile is fully surrounded (all 4 corners match) or completely isolated (no corners match). The system randomly selects from these 16 cells for visual variety. |

### Row Mapping

| Row | Bitmask Range | Description |
|-----|---------------|-------------|
| **0** | Masks 12-15 | Top row: tiles where the top two vertices (v2, v3) dominate |
| **1** | Masks 4-7 | Second row: tiles where bottom-left vertex (v0) is active |
| **2** | Masks 8-11 | Third row: tiles where bottom-right vertex (v1) is active |
| **3** | Masks 1-3 | Bottom row: remaining corner combinations |

### Bitmask-to-Cell Mapping

Each vertex in a quad has a 1-bit flag indicating whether it matches the target texture. The 4 bits form a mask (0-15) that selects the tile:

| Mask | Column | Row | Vertex Pattern `[v2, v3, v0, v1]` |
|:----:|:------:|:---:|-----------------------------------|
| 0 | 4-7 | random | `0, 0, 0, 0` — isolated (random center) |
| 1 | 2 | 3 | `0, 0, 0, 1` |
| 2 | 1 | 3 | `0, 0, 1, 0` |
| 3 | 3 | 3 | `0, 0, 1, 1` |
| 4 | 0 | 1 | `0, 1, 0, 0` |
| 5 | 2 | 1 | `0, 1, 0, 1` |
| 6 | 1 | 1 | `0, 1, 1, 0` |
| 7 | 3 | 1 | `0, 1, 1, 1` |
| 8 | 0 | 2 | `1, 0, 0, 0` |
| 9 | 2 | 2 | `1, 0, 0, 1` |
| 10 | 1 | 2 | `1, 0, 1, 0` |
| 11 | 3 | 2 | `1, 0, 1, 1` |
| 12 | 0 | 0 | `1, 1, 0, 0` |
| 13 | 2 | 0 | `1, 1, 0, 1` |
| 14 | 1 | 0 | `1, 1, 1, 0` |
| 15 | 4-7 | random | `1, 1, 1, 1` — fully surrounded (random center) |

### Vertex Order

The bitmask uses this vertex order: `[v2, v3, v0, v1]`

```
v2 ─── v3
│  quad  │
v0 ─── v1
```

- **v0** = Bottom-Left
- **v1** = Bottom-Right
- **v2** = Top-Left
- **v3** = Top-Right

### Formula

The texture index within the array is calculated as:

```
index = (mask % 4) + (mask / 4) * 8
```

This maps the 4x4 connector grid into the correct positions within the 8-column sheet.

### Randomization

When mask is 0 (isolated) or 15 (fully surrounded), the system uses **columns 4-7** (random center tiles) instead of the connector tiles. The `TextureRandomness` slider (0-1) in the Texture tool controls the probability:

- **0.0** = always use the base center tile (column 4)
- **0.4** = 40% chance of a random variation (default)
- **1.0** = always use a random variation

Randomization candidates for mask 15: `0, 4, 5, 7, 12, 13, 14, 15, 20, 21, 22, 23, 28, 29, 30, 31`

---

## Creating a Texture2DArray

### In Unity

1. Import your sprite sheet as a **Texture** (not Sprite). Set **Texture Type** to `Default`.
2. Set **Wrap Mode** to `Clamp`.
3. In the texture import settings, set **Type** to `Sprite (2D and UI)` — no, actually keep it as Default. Use a script or the following approach:

### Recommended Workflow

1. Create a 512x256 PNG with all 32 tiles arranged in the 8x4 grid.
2. Import it into Unity as a **Texture** (not Sprite).
3. Use a script to convert it to a `Texture2DArray`:

```csharp
using UnityEngine;

public static class TextureArrayBuilder
{
    public static Texture2DArray CreateFromSheet(Texture2D sheet, int cellSize = 64)
    {
        int cols = sheet.width / cellSize;   // 8
        int rows = sheet.height / cellSize;  // 4
        int slices = cols * rows;            // 32

        var arr = new Texture2DArray(cellSize, cellSize, slices,
            TextureFormat.RGBA32, mipChain: true);

        Color[] pixels = new Color[cellSize * cellSize];

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int slice = r * cols + c;
                Rect rect = new Rect(c * cellSize, r * cellSize, cellSize, cellSize);
                Texture2D tile = new Texture2D(cellSize, cellSize);
                var colors = sheet.GetPixels((int)rect.x, (int)rect.y,
                    (int)rect.width, (int)rect.height);
                tile.SetPixels(colors);
                tile.Apply();

                Graphics.CopyTexture(tile, arr, slice);
                Object.DestroyImmediate(tile);
            }
        }

        arr.Apply();
        return arr;
    }
}
```

### Palette Setup

After creating the Texture2DArray, add it to the TileTerrainPalette:

1. Select your Palette asset.
2. Add a new entry.
3. Assign the Texture2DArray.
4. Set the **Priority** (lower number = higher priority, renders on top):
   - Grass: 0 (highest priority)
   - Dirt: 1
   - Pavement: 2
   - Water: 3 (lowest priority)

---

## Grid Configuration

| Setting | Description | Recommended |
|---------|-------------|-------------|
| **Internal Width** | Quads along X (total vertices = Width + 1 + Border*2) | 32-128 |
| **Internal Height** | Quads along Z | 32-128 |
| **Border Size** | Decorative border cells per side (no collider) | 0-4 |
| **Chunk Size** | Quads per chunk side (fewer chunks = fewer draw calls, coarser culling) | 16-32 |

---

## Next Steps

- Read the [Architecture](architecture.md) document to understand the three-pillar design.
- Explore the individual [Tool Documentation](#tools) for detailed usage.
- Check out the `Examples/Sample_Scene.unity` for a working demo.
