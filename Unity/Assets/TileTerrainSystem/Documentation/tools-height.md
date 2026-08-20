# Height Tool

> **English** | [Portugues (Brasil)](tools-height.pt-BR.md)

Organic heightmap sculpting within a single cliff tier. Modifies vertex height offsets without changing cliff floor levels.

**Keyboard shortcut:** `1`

---

## Sub-Modes

| Mode | Description |
|------|-------------|
| **Raise** | Increases height by `brushStrength * falloff` |
| **Lower** | Decreases height by `brushStrength * falloff` |
| **Target** | Lerps height toward `targetHeight` |
| **Smooth** | Lerps height toward the average of neighbors within brush radius |
| **Noise** | Lerps height toward a Perlin noise value mapped to [-2, 2] |

---

## Parameters

| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| **Brush Radius** | 0.1 - 20.0 | 2.0 | Brush size in grid units |
| **Brush Strength** | 0.0 - 5.0 | 0.2 | Intensity of brush application |
| **Brush Shape** | Circle / Square | Circle | Brush falloff shape |
| **Target Height** | -2 to 2 | 1.0 | Target height (Target mode only) |

---

## Algorithm

1. Compute bounding box of brush in vertex grid coordinates.
2. Precompute neighbor cache (`_touchesWaterCache`, `_isBoundaryCache`) for the affected region.
3. Build falloff LUT:
   - **Circle**: `1 - sqrt(dx² + dz²) / radius`
   - **Square**: `1 - max(|dx|, |dz|) / radius`
4. Cache old heights for all affected vertices (including entangled group members).
5. Apply height modification per vertex:
   - **Raise**: `height + delta`, clamped to [-2, 2]
   - **Lower**: `height - delta`, clamped to [-2, 2]
   - **Target**: `Lerp(height, targetHeight, inf)`
   - **Smooth**: `Lerp(height, avgHeight, Clamp01(brushStrength * 0.1 * inf))`
   - **Noise**: `Lerp(height, (PerlinNoise(x*0.5+100, z*0.5+100)*4)-2, delta)`
6. Water protection: vertices marked as water or touching water at the boundary are skipped.
7. Entanglement propagation: height delta applied to the representative vertex is applied uniformly to all group members.

---

## Safety Rules

- Height is globally clamped to **[-2, 2]**.
- Water vertices are **never** modified by height tools.
- Vertices touching water at the boundary are skipped.
- Entangled vertices receive the same height delta as their representative vertex.

---

## Effects on Other Systems

- After each stroke, `PinPropsToTerrain()` is called to re-snap pinned props.
- Props are respawned to reflect new terrain height.
