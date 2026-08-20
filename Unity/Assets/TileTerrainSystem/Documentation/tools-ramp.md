# Ramp Tool

> **English** | [Portugues (Brasil)](tools-ramp.pt-BR.md)

Half-step elevation transitions between adjacent cliff levels. Creates smooth ramp geometry between two floor tiers.

**Keyboard shortcut:** `4` (shares Cliff tab, select Ramp sub-tab)

---

## Sub-Modes

| Mode | Description |
|------|-------------|
| **Set** | Toggles the `CliffHalfStep` flag on valid vertices |
| **Erase** | Clears the `CliffHalfStep` flag from vertices under the brush |

---

## How Ramps Work

A ramp is a **half-step** (+0.5 units) transition between two adjacent cliff levels. When a vertex has `CliffHalfStep = true`, its effective height is `CliffByte + 0.5` instead of just `CliffByte`.

```
Without ramp:          With ramp:
                      
Level 2 ─────         Level 2 ─────
         │                     │╲
         │                     │ ╲  ← ramp mesh
Level 1 ─────         Level 1 ────
```

---

## Placement Rules

For a vertex to receive a ramp flag, **all** of the following must be true:

1. At least one cardinal neighbor (up/down/left/right) has `CliffByte == thisVertex.CliffByte + 1` (exactly 1 floor higher).
2. All 4 quads around the vertex span at most 1 floor difference.
3. The vertex is not already flagged.

After placement, two cleanup passes run:
1. If any modified quad has a halfStep but spans >1 floor, all halfStep flags in that quad are cleared.
2. Isolated halfStep vertices (no cardinal neighbor with halfStep) are removed to prevent invalid corner ramps.

---

## Ramp Matrix

The ramp system uses a **36-entry lookup table** mapping 4-vertex socket configurations to FBX mesh IDs (0-35).

### Socket Values

Each vertex socket has one of 5 values:

| Value | Meaning |
|-------|---------|
| `0.0` | Base level (low) |
| `0.1` | Base level with R-variant (column partner has halfStep) |
| `0.5` | Half step (the ramp vertex itself) |
| `1.0` | Elevated level (high) |
| `1.1` | Elevated level with R-variant (column partner has halfStep) |

The R-variant (`0.1` / `1.1`) indicates that the column partner (the vertex sharing the same column in the adjacent quad) also has a halfStep, which affects the mesh shape.

### Partner Orientation

The system determines ramp orientation from:
1. **Two halfStep vertices**: Uses the edge they share (vertical if v0+v2, horizontal if v1+v3).
2. **One halfStep vertex**: Checks which edge-partner sits at the elevated floor level.
3. **Ambiguous**: Falls back to checking grid neighbors outside the quad.

### Key Encoding

Socket values are packed into a base-5 key: `code(v0) + 5*code(v1) + 25*code(v2) + 125*code(v3)`

Where `code` maps: `0.0→0, 0.1→1, 0.5→2, 1.0→3, 1.1→4`

---

## Parameters

| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| **Brush Radius** | 0.1 - 20.0 | 2.0 | Brush size in grid units |
| **Brush Shape** | Circle / Square | Circle | Brush falloff shape |

---

## Requirements

- **Ramp Mesh Fbx** must be assigned on the TileTerrain component.
- The vertex must be on a cliff (CliffByte > floor level of at least one neighbor).
- Adjacent quads must not span more than 1 floor difference.

---

## Scene Visualization

- **Yellow diamonds**: Valid ramp placement targets
- **Green diamonds**: Vertices that already have ramps

---

## Interaction with Other Tools

- **Water tool** clears all ramp (halfStep) flags at affected vertices and neighbors.
- **Cliff tool** revalidates ramp flags after each stroke.
- **Height tool** does not modify ramp flags directly.
