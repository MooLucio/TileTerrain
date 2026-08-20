# Props Tool

> **English** | [Portugues (Brasil)](tools-props.pt-BR.md)

Placement and management of decorative objects (trees, rocks, etc.) with entanglement groups for terrain-synced movement.

**Keyboard shortcut:** `6`

---

## Sub-Modes

| Mode | Shortcut | Description |
|------|----------|-------------|
| **Place** | `Q` | Single-click placement of the selected prop |
| **Paint** | `W` | Brush-based scattering of props |
| **Select** | `E` | Click to select an existing prop for editing |
| **Remove** | `D` | Click to remove a single prop |
| **Erase** | `F` | Brush-based bulk removal of props |
| **Rotate** | `R` | Drag to rotate the selected prop |
| **Scale** | `T` | Drag to scale the selected prop |

---

## Parameters

| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| **Selected Prop** | -- | -- | Index into the PropsBox palette |
| **Props Brush Density** | 0.0 - 1.0 | 0.3 | Probability of placing a prop at each vertex (Paint mode) |
| **Snap to Grid** | bool | `true` | Snap prop placement to quad centers |
| **Random Rotate** | bool | `true` | Random Y rotation on placement |
| **Brush Radius** | 0.1 - 20.0 | 2.0 | Brush size (Paint and Erase modes only) |
| **Brush Shape** | Circle / Square | Circle | Brush shape (Paint and Erase modes only) |

---

## Prop Definition (TileTerrainProp)

Each prop type is defined as a `TileTerrainProp` ScriptableObject:

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Label` | string | -- | Display name in the palette |
| `Prefabs` | List\<GameObject\> | -- | Prefab variants (one randomly chosen at placement) |
| `MinScale` | float | 0.5 | Minimum random scale |
| `MaxScale` | float | 1.5 | Maximum random scale |
| `RandomRotation` | bool | `true` | Enable random rotation |
| `CanRotate` | bool | `true` | Allow manual rotation |
| `CanScale` | bool | `true` | Allow manual scaling |
| `OccupyWidth` | int | 1 | Horizontal footprint (quads) |
| `OccupyHeight` | int | 1 | Vertical footprint (quads) |
| `CanPlaceInWater` | bool | `true` | Allow placement on water vertices |

---

## Entanglement System

When a prop is placed, it creates an **entanglement group** linking the prop to all vertices within its footprint. This provides:

1. **Overlap prevention**: No two props can share the same vertex.
2. **Terrain sync**: When the height/cliff tool modifies an entangled vertex, all vertices in the group receive the same modification.
3. **Auto-removal**: Cliff painting automatically removes props whose footprint overlaps the brush.

### How It Works

1. Prop placement computes occupied quads from `OccupyWidth` × `OccupyHeight`.
2. All vertices in the footprint are validated (same floor, no water if restricted, no existing entanglement).
3. Footprint vertices are leveled to the same height and cliff level.
4. An `EntanglementGroup` is created, tagging each vertex with the group ID.
5. When any entangled vertex is modified by height/cliff tools, the delta is applied uniformly to all group members.

---

## Placement Algorithm

1. If snap-to-grid: position = center of the quad under the cursor.
2. Calculate Y from terrain height at placement point.
3. Assign random rotation (0-360) and random scale (`MinScale` to `MaxScale`).
4. Pick a random prefab variant from the `Prefabs` list.
5. **Validation checks** (all must pass):
   - All footprint quads on the **same floor**.
   - No footprint vertex is water (if `CanPlaceInWater` is false).
   - No footprint vertex has an existing entanglement group.
6. **Level rounding**: All footprint vertices set to same height and CliffByte as center vertex; CliffHalfStep cleared.
7. Create `PropInstance` and `EntanglementGroup`.

---

## Scene Visualization

- **Place mode**: Green footprint rectangle (valid) or red (invalid), wireframe bounding box, crosshair at placement point.
- **Select mode**: Cyan wire disc, connection line to ground, position/rotation/scale/pin fields in inspector.

---

## Selection and Modification

- **Select**: Finds nearest prop within distance 4. Shows editing fields in inspector.
- **Rotate**: Horizontal drag adjusts `rotationY`.
- **Scale**: Vertical drag adjusts `scale` (minimum 0.1).
- **Delete key**: Removes the selected prop.

---

## Cross-Tool Interactions

- **Cliff tool** automatically removes all props within the brush radius.
- **Height/Cliff/Ramp tools** call `PinPropsToTerrain()` after each stroke to re-snap pinned props.
- **Props Box** is the palette container referenced by the TileTerrain component.
