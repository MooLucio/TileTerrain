# Tile Terrain System

> **English** | [Portugues (Brasil)](README.pt-BR.md)

A custom, editor-only Unity terrain framework for grid-based games requiring smooth organic visuals combined with discrete grid logic. Supports heightmap sculpting, priority-based texture splatting, a three-tier cliff system with transitional meshes, half-step ramps, water painting, and props placement with terrain-synced entanglement.

> Heavily inspired by the [Warcraft III World Editor](https://en.wikipedia.org/wiki/Warcraft_III_World_Editor).

**Requirements:** Unity 6+ | URP 17 (Universal Render Pipeline with RenderGraph)

---

## Tools

| # | Tool | Description |
|---|------|-------------|
| 1 | **[Height](tools-height.md)** | Organic sculpting: Raise, Lower, Target, Smooth, Noise |
| 2 | **[Texture](tools-texture.md)** | 3-layer priority blending: Paint, Smudge, Fill, Erase |
| 3 | **[Cliff](tools-cliff.md)** | Discrete elevation with BFS propagation: Up, Down, Target, Smudge, Erase |
| 4 | **[Ramp](tools-ramp.md)** | Half-step transitions between cliff levels: Set, Erase |
| 5 | **[Water](tools-water.md)** | Stroke-aware water painting with dam protection |
| 6 | **[Props](tools-props.md)** | Decorative objects with entanglement groups: Place, Paint, Select, Remove, Rotate, Scale |

Keyboard shortcuts: `1-5` switch tools, `S` toggles paint mode, `[`/`]` brush size, `B` drag-resize, `M` shape toggle.

---

## Quick Setup

1. Attach `TileTerrain` to a GameObject.
2. In the Inspector, click **Create New Grid Data**.
3. Assign required assets (see [Getting Started](getting-started.md) for full setup including texture array configuration).
4. Select a tool tab and enable paint mode (`S`).

---

## File Structure

```
TileTerrainSystem/
├── Scripts/
│   ├── TileTerrain.cs              # Main renderer, mesh generation, chunk management
│   ├── TileTerrainGridData.cs      # ScriptableObject data store (vertices, quads, props)
│   ├── TileTerrainBitmask.cs       # Autotile bitmask calculation + texture index mapping
│   ├── TileTerrainCliff.cs         # Cliff mesh loading, caching, ramp matrix
│   ├── TileTerrainConstants.cs     # Shared constants (cliff levels, masks, sentinels)
│   ├── TileTerrainPalette.cs       # Texture priority palette ScriptableObject
│   ├── TileTerrainProp.cs          # Single prop definition ScriptableObject
│   ├── TileTerrainPropsBox.cs      # Collection of props ScriptableObject
│   ├── FogOfWarManager.cs          # Fog of war singleton (mask, LOS, BFS flood fill)
│   ├── FogOfWarRevealer.cs         # Per-GameObject fog revealer component
│   └── FogOfWarRenderFeature.cs    # URP 17 RenderGraph full-screen fog pass
├── Editor/
│   ├── TileTerrainEditor.cs              # Main custom inspector (partial class)
│   ├── TileTerrainEditor.Height.cs       # Height brush tools
│   ├── TileTerrainEditor.Texture.cs      # Texture painting tools
│   ├── TileTerrainEditor.Cliff.cs        # Cliff editing + BFS propagation + Ramp tools
│   ├── TileTerrainEditor.Water.cs        # Water painting tools
│   ├── TileTerrainEditor.Props.cs        # Props placement tools
│   ├── TileTerrainEditor.SceneGUI.cs     # Scene view overlay + grid rendering
│   ├── TileTerrainEditor.Safety.cs       # Safety checks (IsSafeToCarve, IsBoundary)
│   ├── TileTerrainIconInitializer.cs     # ScriptableObject icon auto-assignment
│   └── TileTerrainOverlay.cs             # Scene overlay rendering
├── Shaders/
│   ├── TileTerrainShader.shader          # Custom URP HLSL terrain shader
│   ├── TileTerrain.shadergraph           # Shader Graph variant
│   ├── Sample2DArrayCustom.shadersubgraph # Custom subgraph for 2D texture array sampling
│   ├── Water.shadergraph                 # Water surface shader
│   └── FogOfWar.shader                  # Fog of war full-screen blend shader
├── Materials/
│   ├── TileTerrainShader.mat             # Terrain material instance
│   ├── water.mat                         # Water material instance
│   └── FogOfWar.mat                      # Fog of war material instance
├── Textures/
│   ├── prototype.png                     # Prototype texture
│   ├── lowGrass.png, tallGrass.png       # Grass textures
│   ├── dirt.png                          # Dirt texture
│   ├── pavement.png                      # Pavement texture
│   ├── cliffSide.png                     # Cliff side texture
│   └── water.png                         # Water texture
├── Models/
│   └── Cliff/FBX/                        # Cliff meshes (standard, double, transitional, ramps)
├── Icons/                                # ScriptableObject icons
├── Examples/
│   ├── Sample_Scene.unity                # Sample scene
│   └── Sample Data/                      # Example GridData, Palette, PropsBox assets
└── Documentation/                        # This directory
```

---

## Documentation

| Document | Description |
|----------|-------------|
| **[Getting Started](getting-started.md)** | Installation, setup, texture array configuration |
| **[Architecture](architecture.md)** | Three-pillar design, data flow, namespace |
| [Height Tool](tools-height.md) | Sculpting modes and parameters |
| [Texture Tool](tools-texture.md) | 3-layer priority stack, autotile bitmask system |
| [Cliff Tool](tools-cliff.md) | Three tilesets, parity system, BFS propagation |
| [Ramp Tool](tools-ramp.md) | Half-step transitions, 36-pattern matrix |
| [Water Tool](tools-water.md) | Water painting, dam protection, boundary rules |
| [Props Tool](tools-props.md) | Entanglement groups, footprint system |
| [Fog of War](fog-of-war.md) | Full system reference (manager, revealer, render feature, shader) |
| [Autotile Matrix](autotile-matrix.md) | Bitmask-to-texture-index mapping |
| [Transition Matrix](transition-matrix.md) | 36 transitional cliff pattern table |

---

## License

This project is licensed under the [MIT License](LICENSE).
