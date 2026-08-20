# Architecture

> **English** | [Portugues (Brasil)](architecture.pt-BR.md)

The Tile Terrain System is built on three pillars: **Data**, **Rendering**, and **Editor**. All code lives in the `MooLucio.TileTerrain` namespace.

---

## Three Pillars

```
┌─────────────────────────────────────────────────────────────┐
│                    EDITOR (Inspector)                        │
│  Height │ Texture │ Cliff │ Ramp │ Water │ Props            │
│  Brush queries, BFS propagation, undo, safety checks        │
└──────────────┬──────────────────────────┬───────────────────┘
               │ reads/writes             │ triggers rebuild
               ▼                          ▼
┌──────────────────────┐    ┌──────────────────────────────────┐
│   DATA (ScriptableObject) │    │   RENDERING (MonoBehaviour)         │
│   TileTerrainGridData     │◄───│   TileTerrain                      │
│   • Vertices (positions,  │    │   • Chunk splitting                │
│     height, textures,    │    │   • Mesh generation                 │
│     cliff, water, props) │    │   • Cliff/ramp mesh instantiation   │
│   • Quads (autotile layers│    │   • Water surface generation        │
│     + bitmask columns)   │    │   • Material management             │
│   • Props & entanglement │    │   • URP shader sampling             │
└──────────────────────────┘    └──────────────────────────────────┘
```

### 1. Data — `TileTerrainGridData`

A `ScriptableObject` that persists the entire grid state. It stores:

| Data | Description |
|------|-------------|
| **Vertices** | Per-vertex: position, height offset, color, 3-layer texture IDs + masks, cliff level (`CliffByte`), half-step flag (`CliffHalfStep`), water state (`IsWater`, `WaterLevel`), entanglement group ID |
| **Quads** | Per-quad: 4 vertex IDs, grid coordinates, 3-layer autotile results (texture ID + tile column/row), floor level |
| **Props** | Placed prop instances: position, rotation, scale, variant, footprint, entanglement ID |
| **Entanglement Groups** | Groups of vertices linked to a prop — move together when the prop is relocated |

**Key properties:**
- `Width` / `Height` — Total grid dimensions including border
- `InternalWidth` / `InternalHeight` — Editable area (excluding border)
- `BorderSize` — Decorative cells per side (no collider)
- `Version` — Bumped on structural changes; consumers (e.g. FogOfWarManager) cache this to detect staleness

### 2. Rendering — `TileTerrain`

The main `MonoBehaviour`. It is **editor-only** — no code runs at runtime. Baked chunks are serialized with the scene.

**Mesh generation flow:**

```
GenerateMesh()
  ├── SyncTexturesFromPalette()
  ├── Seed uninitialized vertices
  ├── Recalculate all bitmasks
  ├── For each chunk:
  │     ├── Phase 1: Spawn single/double-height cliffs (parity rule)
  │     ├── Phase 2: Replace n=3 quads with transitional meshes
  │     ├── Phase 3: Replace with ramp meshes where halfStep exists
  │     ├── Build flat terrain quads
  │     ├── Build ramp-flat quads (halfStep without custom mesh)
  │     ├── Build cliff mesh instances
  │     ├── Combine into single mesh per material
  │     ├── Build water mesh (merged vertices, fill patches)
  │     └── Assign MeshCollider
  ├── SmoothChunkSeams()
  └── RecalculateFloorOffsets()
```

**Chunk system:**
- Grid is divided into chunks (configurable `ChunkSize`, default 16 quads per side)
- Each chunk is a child GameObject with `Terrain` and `Water` sub-objects
- Chunks are marked static for batching and occlusion culling
- Hidden in Hierarchy by default (`HideChunksInHierarchy`)

### 3. Editor — `TileTerrainEditor`

A custom inspector split across 7 partial class files. Provides 6 brush-based tool modes with:

- Spatial-indexed brush queries (no O(n) vertex scans)
- BFS propagation for cliff smoothing
- Water shoreline safety enforcement
- `SessionState`-persisted UI across inspector reloads
- Stroke-based undo (`Undo.CollapseUndoOperations`)
- Throttled mesh rebuilds (30 Hz) and prop respawns (15 Hz)

---

## Data Flow

### Painting a Texture

```
User paints brush stroke
  → Collect vertices in brush radius
  → Apply texture to each vertex (priority sort into over/mid/under)
  → BatchRecalculateVertices()
    → For each affected quad: RecalculateQuad()
      → Collect unique texture IDs from 4 corners
      → Sort by priority (lower index = higher priority)
      → Assign to over/mid/under layers
      → Compute bitmask → column/row in tilemap
  → Mark dirty chunks
  → Request mesh rebuild (throttled)
```

### Raising a Cliff

```
User raises cliff
  → Remove overlapping props
  → Enqueue vertices with target level
  → BFS propagation loop:
    → Dequeue vertex, apply level
    → Check 8 neighbors: if difference > maxStep (2), enqueue neighbor
    → Water boundary vertices: maxStep = 1
  → Repair pass (up to 10 iterations)
  → Recalculate quad floors
  → Revalidate halfStep (ramp) flags
  → Mark dirty chunks
```

---

## Key Constants

| Constant | Value | Purpose |
|----------|-------|---------|
| `HeightMin` | -2 | Minimum vertex height offset |
| `HeightMax` | 2 | Maximum vertex height offset |
| `CliffHeight` | 1 | World-space height per cliff tier |
| `WaterOffset` | 0.5 | Water surface sits 0.5 units below water level |
| `FullQuadMask` | 15 | All 4 corners cliffed = flat raised quad |
| `SolidTextureMask` | 0xFF | Fully-occluding texture mask |
| `NoCliffLevel` | -128 | Sentinel for no cliff |
| `MinEditableCliff` | -3 | Lowest editable cliff level |
| `MaxEditableCliff` | 11 | Highest editable cliff level |
| `TilemapColumns` | 8 | Columns in the texture tilemap |

---

## Cross-Tool Interactions

| Interaction | Effect |
|-------------|--------|
| Cliff → Props | Cliff painting removes all overlapping props and their entanglement groups |
| Height/Cliff/Ramp → Props | After each stroke, `PinPropsToTerrain()` re-snaps pinned props |
| Water → Cliff | Cliff respects water boundaries; Up tool can drain water |
| Water → Ramps | Water painting clears all ramp (halfStep) flags |
| Props → Vertices | Entanglement groups sync vertex modifications across group members |
| Texture → Quads | Bitmask recalculation drives autotile mesh selection |

---

## Fog of War (Separate System)

The fog of war is an independent subsystem with its own components:

- `FogOfWarManager` — Singleton, owns the RGBA8 mask texture, runs LOS and flood fill
- `FogOfWarRevealer` — Per-GameObject component that registers with the manager
- `FogOfWarRenderFeature` — URP `ScriptableRendererFeature` that injects a full-screen fog pass

See [fog-of-war.md](fog-of-war.md) for complete reference.
