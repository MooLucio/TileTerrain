# Cliff Tool

> **English** | [Portugues (Brasil)](tools-cliff.pt-BR.md)

Discrete elevation changes via cliff floor levels with BFS propagation. Supports three tilesets (Standard, Double, Transitional) and a parity system for natural transitions.

**Keyboard shortcut:** `3`

---

## Sub-Modes

| Mode | Description |
|------|-------------|
| **Up** | Raises cliff floor by +1 per vertex |
| **Down** | Lowers cliff floor by -1 per vertex |
| **Target** | Sets cliff floor to a specific value |
| **Smudge** | Copies a random neighboring vertex's cliff level with probability `brushStrength * 0.2` |
| **Erase** | Resets cliff floor to 0 (removes cliff) |

---

## Parameters

| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| **Target Cliff Level** | -3 to 11 | 0 | Target floor level (Target mode only) |
| **Brush Radius** | 0.1 - 20.0 | 2.0 | Brush size in grid units |
| **Brush Strength** | 0.0 - 5.0 | 0.2 | Smudge blend intensity (Smudge mode only) |
| **Brush Shape** | Circle / Square | Circle | Brush falloff shape |

---

## Three Tilesets

| Tileset | FBX File | Meshes | Trigger |
|---------|----------|--------|---------|
| **Standard** | `cliff_mesh.fbx` | 14 (indices 0-13) | Normal cliff edges (single-step) |
| **Double** | `cliff_double_mesh.fbx` | 14 (indices 0-13) | Vertices span ≥2 levels (e.g., 0→2) |
| **Transitional** | `cliff_transitional_mesh.fbx` | 36 (indices 0-35) | 3 unique floor levels (n, n+1, n+2) |

**Rendering priority:** Transitional > Double > Standard

---

## Parity System

The cliff brush uses parity for natural elevation transitions:

| Floor Level | Up Adds | Down Removes |
|:-----------:|:-------:|:------------:|
| **Even** (0, 2, 4…) | +2 | –1 |
| **Odd** (1, 3, 5…) | +1 | –2 |

This ensures:
- Even floors trigger double-height meshes when stacking
- Odd floors create proper transitions between levels

---

## BFS Propagation Algorithm

1. Remove any props in the brush radius (including entanglement groups).
2. For each vertex in brush radius:
   - Track water state (capture water level if stroke started on water).
   - Enqueue vertex with target level and direction (+1, -1, or 0).
3. **Propagation loop:**
   - Dequeue vertex, apply target level.
   - If cliff level ≥ water level → drain water.
   - Check 8-connected neighbors: if difference > `maxStep`, enqueue neighbor with adjusted target.
   - Water boundary vertices: `maxStep = 1` (prevents breaching dams).
4. **Repair pass** (up to 10 iterations): Fix remaining cliff mismatches.
5. Recalculate quad floors: `quad.floor = min(all 4 vertex CliffBytes)`.
6. Revalidate halfStep (ramp) flags.

---

## How Cliff Meshes Are Selected

For each quad, the system calculates a 4-bit corner mask where bit `i` is set when vertex `i` has cliff at the current level:

| Mask | Mesh Pattern |
|------|-------------|
| 0 | No cliff mesh (flat quad) |
| 1-14 | Corner/edge cliff mesh (mapped via lookup table) |
| 15 | All corners cliffed → flat quad raised by 1 tier (no mesh needed) |

The mask is converted to a mesh ID via `CliffMaskToMeshID()` which maps to the FBX child mesh name.

---

## Safety Rules

- Cliff painting **automatically removes props** that overlap the brush.
- Land-origin strokes never modify water vertices (exception: Up tool can drain water).
- Water boundary vertices are constrained to `maxStep = 1`.
- `IsSafeToCarve` prevents breaching water-holding cliffs.

---

## Keyboard Shortcuts (Cliff Mode)

| Key | Action |
|-----|--------|
| `Click` | Apply cliff at cursor |
| `Drag` | Continuous cliff painting |
| `Shift+Click` | Cliff Down |
