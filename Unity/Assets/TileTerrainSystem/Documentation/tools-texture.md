# Texture Tool

> **English** | [Portugues (Brasil)](tools-texture.pt-BR.md)

Multi-layer priority-based texture blending with autotile bitmask selection.

**Keyboard shortcut:** `2`

---

## Sub-Modes

| Mode | Description |
|------|-------------|
| **Paint** | Paints the selected texture onto vertices within brush radius |
| **Smudge** | Copies a random neighboring vertex's texture data with probability `brushStrength * 0.2` |
| **Fill** | Flood-fills contiguous area of the same texture and cliff level (click-only, no drag) |
| **Erase** | Resets vertex texture to default (index 0), clears mid/under layers |

---

## Parameters

| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| **Selected Texture** | -- | -- | Index into `terrain.RegisteredTextures` (palette UI) |
| **Texture Randomness** | 0.0 - 1.0 | 0.4 | Probability of random center tile selection for fully surrounded/isolated tiles |
| **Brush Radius** | 0.1 - 20.0 | 2.0 | Brush size in grid units (Paint, Smudge, Erase) |
| **Brush Strength** | 0.0 - 5.0 | 0.2 | Smudge blend intensity (Smudge mode only) |
| **Brush Shape** | Circle / Square | Circle | Brush falloff shape |

---

## 3-Layer Priority Stack

Each vertex maintains a 3-slot texture stack:

| Layer | Visibility Rule |
|-------|-----------------|
| **Over** | Always visible (highest priority) |
| **Mid** | Visible where Over's bitmask is not solid (mask < 15) |
| **Under** | Visible only when both Over and Mid have gaps (mask < 15) |

When a new texture is painted:
1. Collect existing textures + incoming texture (max 4 candidates).
2. Deduplicate and sort by priority (lower palette index = higher priority).
3. Top 3 unique textures fill Over → Mid → Under.
4. If a texture already occupies a slot, it is not replaced (idempotent).

---

## Autotile Bitmask System

After painting, bitmasks are recalculated per quad. See [getting-started.md](getting-started.md#texture-array-configuration) for the full 4x8 tilemap layout.

**Vertex order:** `[v2, v3, v0, v1]`

```
v2 ─── v3
│  quad  │
v0 ─── v1
```

Each vertex flags 1 bit (same texture = 1, different = 0). The 4-bit mask (0-15) selects the tile from the texture array:

- **Masks 1-14**: Connector/corner tiles (columns 0-3)
- **Mask 0**: Isolated tile → random center (columns 4-7)
- **Mask 15**: Fully surrounded → random center (columns 4-7)

The `TextureRandomness` slider controls the probability of using random variations vs. the base center tile.

---

## Fill Tool Details

- Only triggers on mouse click (no drag).
- Finds nearest vertex to click point.
- BFS flood fill: expands to 4-connected neighbors sharing the same `overTextureId` and same `CliffByte`.
- Applies the selected texture to all visited vertices.
- Batch-recalculates bitmasks.

---

## Palette UI

A grid of texture previews (64x64 pixels) rendered from `Texture2DArray` previews. Each entry shows:
- Texture preview thumbnail
- Priority value
- Texture array name
- Selection indicated with green highlight

---

## Keyboard Shortcuts (Texture Mode)

| Key | Action |
|-----|--------|
| `1-4` | Select texture slot (palette index) |
| `Click` | Paint/Fill/Erase at cursor |
| `Drag` | Continuous paint/smudge |
