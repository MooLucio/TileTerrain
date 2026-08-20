# Water Tool

> **English** | [Portugues (Brasil)](tools-water.pt-BR.md)

Stroke-aware water painting with BFS propagation and dam protection. Creates water surfaces that sit at the captured floor level.

**Keyboard shortcut:** `5`

---

## No Sub-Modes

The water tool has a single mode: paint water. There is no separate erase — use the Cliff Erase tool to remove water by raising the floor above the water level.

---

## Parameters

| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| **Brush Radius** | 0.1 - 20.0 | 2.0 | Brush size in grid units |
| **Brush Shape** | Circle / Square | Circle | Brush falloff shape |

---

## How Water Painting Works

1. **Capture floor level**: The vertex's current `CliffByte` becomes the `WaterLevel`.
2. **Mark as water**: `IsWater = true`, `height = 0`.
3. **Lower floor**: `CliffByte -= 1` (the water sits on top of the lowered floor).
4. **Clear ramps**: All ramp (halfStep) flags at affected vertices and neighbors are cleared.
5. **BFS propagation**: Water state cascades to neighboring vertices.

### Water Surface Height

```
waterY = (WaterLevel - 0.5) * CliffHeight
```

The water surface sits 0.5 units below the captured floor level, creating a natural shoreline.

---

## BFS Propagation

1. For each vertex in brush radius:
   - Skip if already water, cliff edge, or floor level doesn't match starting floor.
   - Set water state and lower floor.
   - Enqueue into propagation queue.
2. **Propagation loop:**
   - If neighbor's cliff level is >2 below target → enqueue with level lowered by 1.
   - If vertex is water and cliff level ≥ water level → drain water.
3. Recalculate quad floors for all affected quads.

---

## Water and Cliff Interaction

### Cliff Down Tool (Water Interaction)

If any brushed vertex has `IsWater == true`:
1. Capture its `WaterLevel`.
2. For each other brushed vertex:
   - If `floor - 1 < capturedWaterLevel`: set water level, mark as water, run cliff-down.
   - If `floor - 1 >= capturedWaterLevel`: run cliff-down without changing water state.

### Cliff Up Tool (Water Interaction)

If any brushed vertex has `IsWater == true`:
1. Capture its `WaterLevel`.
2. For each other brushed vertex:
   - If `floor + 1 < capturedWaterLevel`: vertex stays submerged, run cliff-up.
   - If `floor + 1 >= capturedWaterLevel`: clear water, run cliff-up.

---

## Safety Rules

- **Cliff edges are skipped** to preserve terrain structure.
- The stroke respects the starting floor level — only vertices at the same floor are modified.
- Land-origin strokes never modify water vertices.
- `IsSafeToCarve` prevents breaching water-holding cliffs.
- Water boundary vertices are constrained to `maxStep = 1`.

---

## Data Model

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `IsWater` | `bool` | `false` | Whether this vertex is submerged |
| `WaterLevel` | `sbyte` | `0` | The floor level at which the water surface sits |

---

## Requirements

- **Water Material** must be assigned on the TileTerrain component (translucent shader recommended).

---

## Rendering

Water is rendered as a separate mesh per chunk:
- Each water vertex gets a 1x1 tile subdivided into four 0.5x0.5 quads (eliminates T-junctions).
- **3-water-corner fill patches**: When a quad has exactly 3 water corners, a triangle fill patch connects the center to the midpoints of the water edges.
- All water vertices at the same XZ position are merged for consistent normals.
