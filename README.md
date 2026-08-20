# Tile Terrain System

> **English** | [Português (Brasil)](docs/pt-BR/README.md)

A custom, editor-only Unity terrain editing framework for grid-based games that need smooth, organic visuals combined with discrete grid logic. It provides heightmap sculpting, priority-based texture splatting, a three-tier cliff system with transitional meshes, props, and a cliff-aware fog of war.

> Heavily inspired by the [Warcraft III World Editor](https://en.wikipedia.org/wiki/Warcraft_III_World_Editor).

**Code license:** MIT — see [`LICENSE.md`](LICENSE.md).
**Asset license:** CC BY 4.0 — see [`LICENSE.assets.md`](LICENSE.assets.md).

---

## Table of Contents

- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Architecture](#architecture)
- [Cliff System](#cliff-system)
- [Tool Modes](#tool-modes)
- [Repository Layout](#repository-layout)
- [Advanced Systems](#advanced-systems)
  - [Fog of War](#fog-of-war)
- [License](#license)
- [Support](#support)
- [Acknowledgments](#acknowledgments)

---

## Features

- **Grid-based with organic visuals** — sculpt continuous height while the game logic stays on a clean discrete grid.
- **Height tool** — raise, lower, target, smooth and noise sculpting within a single cliff tier.
- **Texture tool** — 3-slot priority texture stack (Over → Mid → Under) with auto-tiling bitmask shader.
- **Cliff tool** — three-tier cliff system (standard / double / transitional) with parity-aware elevation and BFS smoothing.
- **Water tool** — stroke-aware water painting with dam protection and shoreline edge-filling.
- **Props** — placeable decorative objects that entangle the vertices they occupy so they stay synced.
- **Fog of war** — tile-based, cliff-aware fog using a URP RenderGraph full-screen pass (no physics or raycasts).
- **Editor only** — bakes everything to meshes; no runtime code in the build.

---

## Requirements

- **Unity 6 (6000.x)** — verified against `6000.5.1f1`.
- **URP** (Universal Render Pipeline) — required by the terrain shader and the fog-of-war render feature.
- C# 9 language features.

---

## Installation

Copy the `TileTerrainSystem` folder (everything under [`Unity/Assets/TileTerrainSystem`](Unity/Assets/TileTerrainSystem)) into your project's `Assets/` folder. Unity imports the `.meta` files, so asset references stay intact.

> The included `Data/TileTerrainGridData.asset` and `Data/*.asset` files are sample data — you can use them to get started or create your own from the inspector.

---

## Quick Start

1. Attach the `TileTerrain` script to a `GameObject`.
2. Click **Create New Grid Data** in the inspector.
3. Assign the required assets:
   - **Terrain Material** — URP-compatible lit shader
   - **Water Material** — translucent water shader
   - **Texture Palette** — `TileTerrainPalette` ScriptableObject
   - **Cliff Meshes** — FBX models for standard, double and transitional cliffs
4. Use the inspector tools to sculpt.

### Required Asset Types

| Property | Type | Purpose |
|----------|------|---------|
| `GridData` | `TileTerrainGridData` | ScriptableObject grid state |
| `TileMaterial` | `Material` | Terrain surface shader |
| `Palette` | `TileTerrainPalette` | Texture array registry |
| `CliffMeshFbx` | `GameObject` | FBX with standard cliff sub-meshes |
| `CliffDoubleMeshFbx` | `GameObject` | FBX with double-height sub-meshes |
| `CliffTransitionalMeshFbx` | `GameObject` | FBX with transitional sub-meshes |
| `WaterMaterial` | `Material` | Water surface shader |

---

## Architecture

The system is divided into three main pillars:

### 1. Data Storage — `TileTerrainGridData`

A persistent `ScriptableObject` holding the entire grid state. Per-vertex data includes:
- Height offsets and base positions
- Texture masks (over, mid, under)
- Cliff tier levels (`CliffByte`)
- Water state (`IsWater`, `WaterLevel`)

### 2. Rendering — `TileTerrain`

The main `MonoBehaviour`. Divides the grid into chunks for draw-call batching and occlusion culling. It:
- Generates terrain meshes with height + cliff offset
- Instantiates cliff geometry via bitmask mesh selection
- Draws dynamic water surfaces with shoreline edge-triangle filling

Uses a custom URP-compatible HLSL shader.

### 3. Editor Interface — `TileTerrainEditor`

A custom inspector providing four brush-based manipulation modes with:
- Spatial-indexed brush queries (no O(n) vertex scans)
- BFS (Breadth-First Search) propagation for cliff smoothing
- Water shoreline safety enforcement
- `SessionState`-persisted UI across inspector reloads

---

## Cliff System

### Three Tilesets

| Tileset | Purpose | Trigger Condition | Indices |
|---------|---------|-------------------|---------|
| **Standard** (`cliff_mesh.fbx`) | Single-step cliffs | Normal cliff edges | 0–15 |
| **Double** (`cliff_double_mesh.fbx`) | Two-step cliffs | Vertices span ≥2 levels (e.g., 0→2) | 0–15 |
| **Transitional** (`cliff_transitional_mesh.fbx`) | Three-level transitions | 3 unique floor levels (n, n+1, n+2) | 0–19 |

### Rendering Priority

When building mesh chunks, the system checks in this order:
1. **Transitional** — if 3 unique floor levels at current level
2. **Double** — if level+1 has cliff coverage
3. **Standard** — fallback single-step cliff

### Parity System

The cliff brush uses parity for natural elevation changes:

| Floor Level | Up Adds | Down Removes |
|:-----------:|:-------:|:------------:|
| Even (0, 2, 4…) | +2 | –1 |
| Odd (1, 3, 5…) | +1 | –2 |

This ensures:
- Even floors trigger double-height meshes when stacking
- Odd floors create proper transitions between levels

---

## Tool Modes

### 1. Height Tool
Organic sculpting within a single cliff tier.
- **Sub-tools**: Raise, Lower, Target, Smooth, Noise
- **Safety**: Respects water boundaries via `IsBoundary` check
- **Range**: –2 to +2 units

### 2. Texture Tool
Multi-layer priority-based texture blending.
- **Sub-tools**: Paint, Smudge, Erase
- **Priority system**: Lower palette index = higher priority (renders on top)
- **Three-slot stack per vertex**: Over → Mid → Under

### 3. Cliff Tool
Discrete elevation changes via `CliffByte` modification.
- **Sub-tools**: Up, Down, Target, Smudge, Erase
- **BFS propagation**: Cascading elevation across neighbors (max ±2 difference)
- **Safety**: `IsSafeToCarve` prevents breaching water-holding dams

### 4. Water Tool
Stroke-aware water painting with dam protection.
- Captures floor level → sets as `WaterLevel`
- Marks vertex as `IsWater`
- Lowers floor by 1 unit
- Propagates water state through BFS

---

## Repository Layout

```
TileTerrain/
├── Unity/Assets/TileTerrainSystem/   # Unity package root (copy into Assets/)
│   ├── Scripts/                      # Runtime data + baking (editor-gated)
│   ├── Editor/                       # Custom inspector & tool modes
│   ├── Shaders/                      # URP terrain + fog-of-war shaders
│   ├── Textures/                     # Sample textures
│   ├── Models/                       # Cliff FBX/Blender sources
│   ├── Materials/                    # Terrain/water material instances
│   ├── Data/                         # Sample grid + palette assets
│   ├── Icons/                        # ScriptableObject icons
│   └── Documentation/                # System documentation (see below)
├── LICENSE.md                        # MIT (code, shaders, docs)
├── LICENSE.assets.md                 # CC BY 4.0 (assets)
├── CONTRIBUTING.md
├── CODE_OF_CONDUCT.md
├── docs/pt-BR/                       # Portuguese (Brazil) translations
│   ├── README.md
│   ├── CONTRIBUTING.md
│   ├── CODE_OF_CONDUCT.md
│   ├── LICENSE.md
│   └── LICENSE.assets.md
└── README.md
```

---

## Advanced Systems

### BFS Propagation

Cliff and water tools use a Breadth-First Search queue to cascade elevation changes across the grid. The propagation respects:
- **Max step of 2** between adjacent vertices (prevents cliff-face tearing)
- **Parity rules** for natural elevation transitions
- **Water safety** — `IsSafeToCarve` prevents breaching dam walls

### Transitional Pattern Detection

Detects when a quad has 3 unique floor levels and selects the appropriate transitional mesh. Five distribution categories handle all 36 valid patterns of `(0, 1, 2)` height combinations.

### Edge & Shore Protection

| Check | Purpose |
|-------|---------|
| `TouchesWater` | Detects dry land adjacent to water (dam check) |
| `IsCliffEdge` | Identifies structural drops between cliff levels |
| `IsSafeToCarve` | Prevents carving through water-holding cliffs |
| `IsBoundary` | Detects water/land interfaces for height safety |

### Texture Priority Stack

Each vertex maintains a 3-slot priority stack:
- **Over** — highest priority, rendered on top
- **Mid** — middle layer, visible where Over's mask isn't solid (mask ≠ 15)
- **Under** — base layer, visible only when Over and Mid have gaps

When a new texture is painted:
1. Collect existing textures + incoming texture (max 4 candidates)
2. Sort by priority (lower index = higher priority)
3. Top 3 unique textures fill Over → Mid → Under
4. If a texture already occupies a slot, it is not replaced (idempotent)

### Fog of War

A tile-based, cliff-aware fog of war system using a URP RenderGraph full-screen pass. Tracks per-cell **visible / explored / hidden** states with smooth, distance-based painting.

**Key features**:
- Per-cell RGBA8 mask with continuous [0, 1] values for soft fade in/out.
- Cliff-aware LOS via 2D DDA (Amanatides–Woo) — no physics or raycasts.
- Distance-based rise rate: cells right under the revealer snap (rate = 1), cells at the edge lerp at the inspector rate.
- 3-state blend (Hidden / Explored / Visible) with adjustable fog and explored colors.
- Component-based revealers — attach `FogOfWarRevealer` to any GameObject.

**Components**:
- `FogOfWarManager` (singleton, owns mask, drains revealer registry each `LateUpdate`)
- `FogOfWarRevealer` (per-GameObject, self-registers)
- `FogOfWarRenderFeature` (URP `ScriptableRendererFeature`, injects after transparents)
- `TileTerrain/FogOfWar` shader (samples mask + scene depth, blends fog over scene)

For full reference (every field, algorithm details, performance numbers, examples, troubleshooting), see **[`fog-of-war.md`](Unity/Assets/TileTerrainSystem/Documentation/fog-of-war.md)**.

---

## Documentation

The full system documentation lives in [`Unity/Assets/TileTerrainSystem/Documentation/`](Unity/Assets/TileTerrainSystem/Documentation/):

- [`README.md`](Unity/Assets/TileTerrainSystem/Documentation/README.md) — detailed system overview
- [`matrix-solution.md`](Unity/Assets/TileTerrainSystem/Documentation/matrix-solution.md) — Autotile bitmask-to-texture index mapping
- [`water-solution.md`](Unity/Assets/TileTerrainSystem/Documentation/water-solution.md) — Water tool algorithm specification
- [`water-border-protection.md`](Unity/Assets/TileTerrainSystem/Documentation/water-border-protection.md) — Water/land boundary safety rules
- [`transition-matrix.md`](Unity/Assets/TileTerrainSystem/Documentation/transition-matrix.md) — Transitional cliff pattern table
- [`fog-of-war.md`](Unity/Assets/TileTerrainSystem/Documentation/fog-of-war.md) — Fog of war system reference

---

## License

- **Code, shaders and documentation** are licensed under the [MIT License](LICENSE.md).
- **Assets** (textures, icons, models, materials, sample data) are licensed under [CC BY 4.0](LICENSE.assets.md).

---

## Support

This project is free and open source. If you find it useful and want to say
thanks, a donation is appreciated but completely optional:

- **PayPal** (international): [Donate](https://www.paypal.com/donate/?business=FT8LTCL8Z86C4&no_recurring=0&currency_code=BRL)
- **Mercado Pago** (Brazil — PIX/card): [Donate](https://link.mercadopago.com.br/moolucio)
- **Gumroad** (other projects): [Mool Studio](https://moolstudio.gumroad.com)

Donations are voluntary support and grant no benefits, priority or credits.

---

## Acknowledgments

This project draws heavy inspiration from the **Warcraft III World Editor**
by Blizzard Entertainment. Tile Terrain System is an original, fully
independent implementation. It is not affiliated with Blizzard
Entertainment, and it neither requires nor implies any endorsement from
them.
