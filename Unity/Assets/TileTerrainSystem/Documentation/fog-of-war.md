# Fog of War

> **English** | [Português (Brasil)](fog-of-war.pt-BR.md)

A tile-based, cliff-aware fog of war system for the Tile Terrain framework. Designed for Unity 6 + URP 17 using the RenderGraph API.

---

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Mask Channels](#mask-channels)
- [Setup](#setup)
- [`FogOfWarManager` Reference](#fogofwar-manager-reference)
- [`FogOfWarRevealer` Reference](#fogofwarrevealer-reference)
- [`FogOfWarRenderFeature` Reference](#fogofwarrenderfeature-reference)
- [Shader Reference](#shader-reference)
- [Line of Sight](#line-of-sight)
- [Smoothing & Painting](#smoothing--painting)
- [Distance-Based Rise](#distance-based-rise)
- [Public API](#public-api)
- [Performance](#performance)
- [Known Limitations](#known-limitations)
- [Examples](#examples)
- [Troubleshooting](#troubleshooting)

---

## Overview

The system masks the world view through a per-cell **RGBA8 texture** that tracks:

| Channel | Meaning |
|---|---|
| **R** | Currently visible — lerps toward 1 inside revealers, toward 0 outside |
| **G** | Explored — lerps toward 1 inside revealers; never decays on its own (Persistent) or mirrors R (Flashlight) |
| **B/A** | Unused |

A full-screen URP render pass samples this mask with bilinear filtering, reconstructs world position from scene depth, and blends fog over the scene color. The result is a smooth, animated fog edge that tracks the player's vision in real time.

**Key features:**
- Cliff-aware LOS via 2D DDA (Amanatides–Woo) against the grid's per-vertex heights — no physics or raycasts.
- 3-state visibility: **Hidden → Explored → Visible** with continuous [0,1] values for soft transitions.
- Per-revealer **Persistent** (RTS-style "memory" stays) or **Flashlight** (G mirrors R) modes.
- **Distance-based rise rate** — cells right under the revealer snap to 1, cells at the edge lerp gradually.
- **Smoothing** for both rise and fall, with per-frame lerp factors.
- Component-based revealers — attach `FogOfWarRevealer` to any GameObject; it auto-registers.
- Works in both edit mode (gizmos) and play mode.

---

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│ Scene                                                        │
│                                                              │
│  ┌──────────────────────┐                                    │
│  │ FogOfWarManager      │  singleton, owns _mask texture     │
│  │ (on FogOfWar GO)     │  drains static revealer registry   │
│  └─────────┬────────────┘  each LateUpdate                   │
│            │                                                 │
│            │  reads                                          │
│            ▼                                                 │
│  ┌──────────────────────┐                                    │
│  │ FogOfWarRevealer #1  │  registers in OnEnable             │
│  │ (on Main Camera)     │  → radius, eyeHeight, occluded,    │
│  └──────────────────────┘    persistence                     │
│                                                              │
│  ┌──────────────────────┐                                    │
│  │ FogOfWarRevealer #2  │  any other GO (NPC, torch, etc)   │
│  │ (on enemy)           │                                    │
│  └──────────────────────┘                                    │
└──────────────────────────────────────────────────────────────┘
              │
              │  CPU writes RGBA32 mask texture
              ▼
┌──────────────────────────────────────────────────────────────┐
│ URP Render Pipeline                                          │
│                                                              │
│  ┌──────────────────────────────────────┐                    │
│  │ FogOfWarRenderFeature                │  ScriptableRendererFeature
│  │ (added to URP-HighFidelity-Renderer) │                    │
│  └─────────┬────────────────────────────┘                    │
│            │  AfterRenderingTransparents                     │
│            ▼                                                 │
│  ┌──────────────────────────────────────┐                    │
│  │ FogOfWarPass  (RecordRenderGraph)    │  src → tmp (material)
│  │                                       │  tmp → src (blit)
│  └─────────┬────────────────────────────┘                    │
│            │                                                 │
│            ▼                                                 │
│  ┌──────────────────────────────────────┐                    │
│  │ FogOfWar.shader  (TileTerrain/...)     │  samples _MaskTex
│  │                                       │  reconstructs world pos
│  │                                       │  blends fog over scene
│  └──────────────────────────────────────┘                    │
└──────────────────────────────────────────────────────────────┘
```

**File layout:**

| File | Role |
|---|---|
| `Scripts/FogOfWarManager.cs` | Singleton, mask lifecycle, LOS, smoothing, gizmos |
| `Scripts/FogOfWarRevealer.cs` | Per-GameObject vision source; self-registers |
| `Scripts/FogOfWarRenderFeature.cs` | URP `ScriptableRendererFeature` + RenderGraph pass |
| `Shaders/FogOfWar.shader` | Full-screen blend: scene + mask → fogged output |
| `Materials/FogOfWar.mat` | Material wrapping the shader, referenced by the feature |
| `Materials/FogOfWar.mat.meta` | (auto-generated) |

---

## Mask Channels

The mask is an `RGBA32` `Texture2D` of size `(gridWidth × upscale) × (gridHeight × upscale)`, with `wrapMode = Clamp` and `filterMode = Bilinear`. Each grid cell occupies an `upscale × upscale` block of texels. Bilinear filtering at cell boundaries produces soft edges for free.

```
Cell (0,0)  Cell (1,0)  Cell (2,0)
┌────┬────┬────┬────┬────┬────┐
│ RG │ RG │ RG │ RG │ RG │ RG │
│ BA │ BA │ BA │ BA │ BA │ BA │
├────┼────┼────┼────┼────┼────┤
│ RG │ RG │ RG │ RG │ RG │ RG │
│ BA │ BA │ BA │ BA │ BA │ BA │
├────┼────┼────┼────┼────┼────┤
│ RG │ RG │ RG │ RG │ RG │ RG │
│ BA │ BA │ BA │ BA │ BA │ BA │
└────┴────┴────┴────┴────┴────┘
  Cell (0,1)  Cell (1,1)  Cell (2,1)
```

The channels are written and read as continuous floats in `[0, 1]`:

| Channel | Writer | Reader | Behavior |
|---|---|---|---|
| **R** | Revealers (lerp toward 1), fall pass (lerp toward 0) | Shader: `visible` | Smooth rise + decay |
| **G** | Revealers (lerp toward 1 for Persistent; snapshot R for Flashlight) | Shader: `explored` | Persistent memory / flashlight mirror |
| **B** | Unused | Unused | — |
| **A** | Unused | Unused | — |

---

## Setup

The system is already wired in `TestScene.unity` and the `URP-HighFidelity-Renderer.asset`. To re-add from scratch:

1. **Create a `FogOfWar` GameObject** in the scene.
2. **Add `FogOfWarManager` component**. Assign the `TileTerrainGridData` asset.
3. **Create a Material** that uses `TileTerrain/FogOfWar` shader, name it `FogOfWar`.
4. **Add `FogOfWarRenderFeature`** to your URP renderer asset (e.g. `URP-HighFidelity-Renderer`):
   - In Project window, select the URP renderer asset.
   - **Add Renderer Feature → Fog Of War Render Feature**.
   - Drag the `FogOfWar` material into the **Fog Material** slot.
5. **Attach `FogOfWarRevealer`** to any GameObject that should reveal fog (typically the Main Camera). It auto-registers on `OnEnable`.
6. **Position the camera** so it looks at the grid (the gizmo only draws in Scene view; the Game view needs to be inside the grid for fog to be visible).

That's it — play the scene and the fog will start painting.

---

## `FogOfWarManager` Reference

Add this to a dedicated GameObject in the scene. It is a singleton; the second instance in the scene is destroyed with a warning.

### Grid
| Field | Default | Description |
|---|---|---|
| `GridData` | _(required)_ | The `TileTerrainGridData` the fog covers. Mask resolution = `grid × maskUpscale`. |

### Fog Appearance
| Field | Default | Description |
|---|---|---|
| `fogColor` | `(0.02, 0.02, 0.04, 1)` | Color for **Hidden** cells. Alpha is ignored (assumed 1). |
| `exploredColor` | `(0.35, 0.35, 0.4, 0.55)` | Tint for **Explored** (but not currently visible) cells. `RGB` is the tinted scene, `A` is the visibility strength (0 = invisible, 1 = full). |
| `OutsideGridFog` | `1` | `0` = render scene normally outside grid bounds. `1` = fog everything outside. |

### LOS
| Field | Default | Description |
|---|---|---|
| `KneeOffset` | `0.25` | Vertical knee (world units) added to the LOS eye height to avoid 1-cell self-occlusion when standing on a slope. |

### Performance
| Field | Default | Description |
|---|---|---|
| `UpdateInterval` | `0` | Minimum seconds between mask recomputes. `0` = every `LateUpdate`. Use `0.016` (or similar) to throttle. |
| `maskUpscale` | `4` | Mask resolution multiplier. `1` = 1 px/cell (blocky). `4` = 16 px/cell (recommended). `8` = 64 px/cell (very smooth, more CPU). |
| `MaskBlur` | `0.025` | Soft-blur radius in normalised grid UV. `0` = sharp bilinear only. `0.02` = soft. `0.05` = wide gradient. At upscale 4: `0.01` ≈ 1 cell, `0.02` ≈ 1.5 cells. |

### Smoothing
| Field | Default | Description |
|---|---|---|
| `VisibleRiseRate` | `0.35` | Per-frame lerp factor for the **R** channel toward 1, **at the edge of a revealer**. The actual rate per cell is distance-based (see [Distance-Based Rise](#distance-based-rise)). `0` = snap, `1` = lerp fully in one frame. |
| `VisibleFallRate` | `0.10` | Per-frame lerp factor for the **R** channel toward 0 when not revealed. `0.10` = ~20 frames to fade (~0.33 s @60fps). `0.30` = ~5 frames. |
| `ExploredRiseRate` | `0.10` | Per-frame lerp factor for the **G** channel toward 1, **at the edge of a Persistent revealer**. Should be slower than `VisibleRiseRate` so "remembered" areas build gradually. Ignored by Flashlight revealers. |

### Debug
| Field | Default | Description |
|---|---|---|
| `debugDrawMask` | `true` | Draw the fog mask in the Scene view (gizmos). Green = visible, yellow = explored, red = partial. |
| `debugDrawHeight` | `5` | Vertical offset for the debug mask quad (world units above grid origin). |
| `debugDrawScale` | `1` | Scale for the debug mask (1 = matches the grid in world units). |

### Events
| Member | Description |
|---|---|
| `event System.Action FogUpdated` | Fires after every `UpdateMask`. Subscribe for custom reactions (audio, AI alerts, etc). |
| `Texture MaskTexture` | Read-only handle to the live mask texture. Bind it elsewhere if needed. |

---

## `FogOfWarRevealer` Reference

Attach to any GameObject that should reveal fog. It self-registers in `OnEnable`, self-unregisters in `OnDisable`. Put it on:
- The Main Camera (for the player's vision)
- Enemy AI (so the player can "see" through an enemy's eyes for a moment)
- Placeable torches (Flashlight mode)
- A static light fixture (Persistent mode for a guard post)

### Reveal
| Field | Default | Description |
|---|---|---|
| `Radius` | `8` | Reveal radius in **grid cells**. The revealer affects every cell within this radius (clamped to LOS if `Occluded` is on). |
| `EyeHeight` | `1.8` | Vertical offset (world units) above the GameObject pivot for the LOS eye. The cell under the pivot is sampled at this height, not the pivot itself. |

### Line of Sight
| Field | Default | Description |
|---|---|---|
| `Occluded` | `true` | Run cliff-aware LOS check. When `false`, every cell within `Radius` is revealed (cheaper, but no cliff occlusion). |
| `Persistence` | `Persistent` | `Persistent` = explored cells stay explored forever (cleared only by `HideAll`). `Flashlight` = explored mirrors current visibility (G = R). |

### Debug
| Field | Default | Description |
|---|---|---|
| `debugDraw` | `false` | Draw a wire sphere at the eye position when the GameObject is selected. |

### Runtime
| Member | Description |
|---|---|
| `Vector2Int GridCell` | The cell the revealer currently occupies (set each `LateUpdate`). Read-only from outside. |
| `float EyeHeight` | (Serialized, see Inspector table) Vertical offset of the LOS eye. The eye itself is computed internally, not exposed. |

---

## `FogOfWarRenderFeature` Reference

A URP `ScriptableRendererFeature`. Add it to your URP renderer asset; it injects one full-screen pass at `RenderPassEvent.AfterRenderingTransparents`.

### Inspector
| Field | Default | Description |
|---|---|---|
| `FogMaterial` | _(required)_ | Material using `TileTerrain/FogOfWar` shader. |
| `InjectionPoint` | `AfterRenderingTransparents` | When in the URP frame the pass runs. Earlier = fog below transparents. Later = fog above. Default keeps the look correct. |

### Pass internals
- **Allocates** an intermediate color texture sized to the camera target, `msaaSamples = 1` (forced), `depthBufferBits = 0`.
- **Pass 1**: blits `src → tmp` with the fog material bound. The material samples `_MaskTex` (pushed by the feature from `FogOfWarManager.MaskTexture`) and scene depth.
- **Pass 2**: blits `tmp → src` (no material) so the camera's color attachment gets the fogged result. Uses `AddBlitPass` (not `AddCopyPass`) to handle MSAA mismatches via the RenderGraph blit helpers.
- **Per-frame uniforms pushed**: `_MaskTex`, `_FogColor`, `_ExploredColor`, `_OutsideGridFog`, `_FogBlur`, `_GridOffset`, `_GridWorldSize`.

---

## Shader Reference

Shader path: `TileTerrain/FogOfWar` (`Shaders/FogOfWar.shader`).

### Properties
| Property | Type | Default | Description |
|---|---|---|---|
| `_MaskTex` | 2D | `black` | The fog mask from `FogOfWarManager`. Sampled with a 13-tap circular blur. |
| `_FogColor` | Color | `(0.02, 0.02, 0.04, 1)` | Hidden-cell color. |
| `_ExploredColor` | Color | `(0.35, 0.35, 0.4, 0.55)` | Explored-cell tint. `A` = visibility strength. |
| `_OutsideGridFog` | Range(0,1) | `1` | How much to fog pixels outside the grid bounds. |
| `_FogBlur` | Range(0,0.1) | `0.025` | UV-space radius for the 13-tap circular blur. |
| `_GridOffset` | Vector | `(0,0,0,0)` | World XZ origin of cell (0,0). |
| `_GridWorldSize` | Vector | `(1,1,0,0)` | Full grid size in world units. |

### Algorithm
1. Sample scene depth, reconstruct world position via `ComputeWorldSpacePosition`.
2. Convert world XZ → grid UV using `_GridOffset` and `_GridWorldSize`.
3. Sample `_MaskTex` with `SampleMaskBlurred(uv, _FogBlur)` — 1 center + 12 taps on a circle, averaged. Gives soft fog edge.
4. Combine states: `vis = max(visible, explored * _ExploredColor.a)`.
5. Lerp scene color toward `_FogColor` by `1 - vis`.
6. Tint "explored but not visible" pixels toward `_ExploredColor.rgb`.
7. Outside the grid: optionally apply `_OutsideGridFog` as a full-screen fog.

---

## Line of Sight

The LOS check is the most performance-sensitive part of the CPU loop. It runs **once per cell, per revealer, per frame**.

### Algorithm: 2D DDA (Amanatides–Woo)
A 2D grid traversal that visits exactly the cells a line from `from` to `to` passes through, in the correct order. The traversal is in normalised ray parameter `t ∈ [0, 1]`, where the line height is `lerp(eyeY, targetY, t) + kneeOffset`.

For each cell along the ray (except the origin):
1. Look up the cell's `max(4 corner vertex heights)` — this is the **blocker** (cliff top).
2. Compare to the line height at this `t`.
3. If `cellMax > lineY`, the cell occludes the view → return `false`.

If the ray exits the grid before being blocked, return `true` (the cell is visible).

### Cliff handling
- The **target cell** is sampled at its **center height** (average of 4 corners) — this is the height we want to see *into*.
- The **intermediate cells** along the ray use their **max corner height** as the blocker — this is the cliff top.
- This means a unit standing in a low area can see a high plateau, but a unit on a high plateau looking into a low area is blocked by the plateau's cliff.

### `KneeOffset`
A small vertical offset added to the LOS eye height to avoid the common "1-cell self-occlusion" artifact where a unit standing on a slope immediately occludes the cell right next to them because the corner height is slightly higher than the eye. `0.25` is usually correct; raise it if you see black rings around revealers on sloped terrain.

### Performance characteristics
| Grid | Revealer radius | Cells checked | Approx. cost (μs) |
|---|---|---|---|
| 64×64 | 8 | ~200 | ~30 |
| 64×64 | 20 | ~1,250 | ~190 |
| 256×256 | 12 | ~450 | ~70 |

---

## Smoothing & Painting

The mask channels are written as continuous values in `[0, 1]`, not binary 0/1. This lets the painting process animate smoothly.

### Per-frame update (3 phases)

#### Phase 1 — Fall pass
For **every** pixel, `R *= (1 - visibleFallRate)`. This is the visible fade-out. Pixels below `1e-4` snap to exactly 0 (so they don't stay as ghost values forever). G is **not** touched in the fall pass for Persistent revealers.

#### Phase 2 — Reveal pass (per revealer)
For every visible cell (within `Radius` and passing LOS), the cell's `upscale × upscale` block is updated:

```csharp
// R: lerp toward 1 at this cell's distance-based rate (see below)
if (c.r < 1f) c.r += (1f - c.r) * vRise;

// G: 
//   Persistent: lerp toward 1 at the cell's rate
//   Flashlight: snapshot of R (G does NOT track R's decay this frame)
if (persistent) {
    if (c.g < 1f) c.g += (1f - c.g) * eRise;
} else {
    c.g = c.r;
}
```

#### Phase 3 — GPU upload
`Texture2D.SetPixels` + `Apply(false)` to push the mask to the GPU.

### First-update snap
The very first `UpdateMask` after mask (re)allocation uses `vRise = eRise = 1`, so the first reveal is instant (no fade-in). After that frame, the inspector rates take over. This avoids a "fog slowly appearing" effect at game start.

The snap is also re-armed whenever `EnsureMask` rebuilds the mask (grid resize, upscale change).

---

## Distance-Based Rise

Within a single revealer's footprint, the rise rate is **distance-based**:

```
t = sqrt(dx² + dy²) / radius   // 0 at centre, 1 at edge
vRise(cell) = lerp(1, visibleRiseRate, t)
eRise(cell) = lerp(1, exploredRiseRate, t)
```

| Cell position | t | vRise (default) | Effect |
|---|---|---|---|
| Centre (under revealer) | 0 | `1.0` | Instant — the cell right under the revealer is always at full visibility |
| Mid | 0.5 | `0.675` | Lerps fast |
| Edge | 1.0 | `0.35` (= public) | Lerps slow |

**Why this pattern?** The cell right under the camera should be exactly what the player sees — no lag. The periphery can lag because peripheral vision doesn't need pixel-perfect tracking; a soft "trailing" edge looks natural.

**Customising**:
- Set `visibleRiseRate = 1` for instant reveal across the whole disc (no smoothing).
- Set `visibleRiseRate = 0.7` for a snappier feel.
- Set `visibleRiseRate = 0.1` for a very trailing "cinematic" fog.

---

## Public API

### `FogOfWarManager`

```csharp
public static FogOfWarManager Instance { get; }                  // singleton
public static IReadOnlyCollection<FogOfWarRevealer> Revealers;  // current revealers
public Texture MaskTexture { get; }                              // live mask texture
public event System.Action FogUpdated;                          // fires after each update

public void HideAll();   // clears R and G, fires FogUpdated
public void RevealAll(); // sets R=1, G=1 everywhere, fires FogUpdated
```

### `FogOfWarRevealer`

```csharp
[NonSerialized] public Vector2Int GridCell;   // cell the revealer is in (set each frame)
```

The revealer self-registers in `OnEnable` and self-unregisters in `OnDisable`. No manual API needed.

### `FogOfWarRenderFeature`

No public API. Configure `FogMaterial` and `InjectionPoint` in the inspector.

### Typical use from gameplay code

```csharp
// React when the mask updates
FogOfWarManager.Instance.FogUpdated += () => {
    // e.g. update minimap, trigger AI alerts
};

// Force a full re-clear
FogOfWarManager.Instance.HideAll();

// Reveal the whole map (debug, intro cutscene)
FogOfWarManager.Instance.RevealAll();
```

---

## Performance

### CPU
| Operation | Cost (64×64 grid, upscale 4) |
|---|---|
| Fall pass (R decay for 65,536 texels) | ~0.3 ms |
| Reveal pass (per revealer, ~200 cells) | ~30 μs |
| Reveal pass (8 revealers, ~200 cells each) | ~0.25 ms |
| GPU upload (`SetPixels` + `Apply`) | ~0.5 ms |
| **Total per frame** | **~1 ms** |

Throttling with `UpdateInterval` (e.g. `0.033` for 30 Hz) cuts the cost roughly in half.

### GPU
- **Mask sampling**: 13 taps per fragment for the blur = ~27M samples/frame on 1920×1080. ~0.3 ms on a desktop GPU.
- **Scene depth read**: 1 tap. Free with the URP depth texture.
- **World position reconstruction**: ALU only, no extra reads.
- **Output**: 1 RT write per fragment. Free.

Total GPU cost: **< 0.5 ms** on typical hardware.

### Memory
- Mask texture: `(64 × 4) × (64 × 4) × 4 bytes = 256 × 256 × 4 = 256 KB`. Trivial.
- CPU pixel buffer: same = 256 KB. Trivial.

---

## Known Limitations

1. **Flashlight `G` doesn't track `R` per-frame.** In Flashlight mode, `G` is set to `R` at the moment the revealer writes the cell. If `R` then decays (no revealers touching the cell), `G` does **not** decay with it. For a true `G = R` mirror, the manager would need a per-pixel mode flag and a post-decay sync pass. This is a known compromise; the common case is "all revealers are the same mode".

2. **Mixed Persistent + Flashlight revealers lose the persistent memory.** If you have both types and a cell is touched by a flashlight, the snapshot `G = R` may overwrite a previously persistent `G = 1`. Use a single global mode for now.

3. **LOS is 2D.** The cliff check looks at the **max of 4 corner heights** of the blocker cell. This is conservative — it always assumes the cell is fully occluded at its highest corner. A more accurate approach would be to interpolate the height along the cell edges, but the corner-max approach is fast and visually correct for "blocky" terrain.

4. **The mask is CPU-side and uploaded each frame.** For very large grids (512×512) or 60+ revealers, consider:
   - Increasing `UpdateInterval` to throttle to 30 Hz.
   - Using `job-system` parallelization for the per-cell block (TODO).
   - Using a compute shader to update the mask on the GPU (major refactor).

5. **Outside-grid fog is binary.** It's controlled by a single `OutsideGridFog` float. There's no per-region fog of war for areas beyond the grid.

---

## Examples

### RTS-style player vision (Persistent)

```csharp
// On Main Camera:
public class PlayerVision : MonoBehaviour {
    void Start() {
        var revealer = gameObject.AddComponent<FogOfWarRevealer>();
        revealer.Radius = 15f;
        revealer.EyeHeight = 2f;
        revealer.Occluded = true;
        revealer.Persistence = FogRevealPersistence.Persistent;
    }
}
```

Place a single such revealer on the player camera. They will see a 15-cell radius that updates as they move, and previously-seen areas stay dimly visible forever.

### Stealth-game flashlight (Flashlight)

```csharp
// On a torch GameObject:
var torch = gameObject.AddComponent<FogOfWarRevealer>();
torch.Radius = 8f;
torch.EyeHeight = 1f;
torch.Occluded = true;
torch.Persistence = FogRevealPersistence.Flashlight;
```

When the torch is enabled, the cells in its radius are visible. When disabled, they fade out (after the next fall pass, R decays; G snapshots are stale — see Limitation 1).

### Multiple player scouts

```csharp
// On each scout GameObject:
var scout = gameObject.AddComponent<FogOfWarRevealer>();
scout.Radius = 6f;
scout.EyeHeight = 1.5f;
scout.Occluded = true;
scout.Persistence = FogRevealPersistence.Persistent;
```

Attach to any number of units. The mask is the **union** of all revealers' visible cells (any cell within any revealer's radius is visible).

### Fully-fogged map with periodic reveal (boss room)

```csharp
// On the boss-room trigger:
public class BossRoomFog : MonoBehaviour {
    FogOfWarRevealer revealer;
    void OnTriggerEnter(Collider other) {
        if (other.CompareTag("Player")) {
            if (revealer == null) revealer = gameObject.AddComponent<FogOfWarRevealer>();
            revealer.Radius = 30f;
            revealer.Persistence = FogRevealPersistence.Persistent;
        }
    }
}
```

### Listen for fog changes (for AI / audio)

```csharp
void Start() {
    FogOfWarManager.Instance.FogUpdated += OnFogChanged;
}

void OnFogChanged() {
    // Check if any enemies are now in the visible set
    foreach (var enemy in enemies) {
        Vector2Int cell = WorldToCell(enemy.transform.position);
        var sample = SampleCell(cell);
        if (sample.r > 0.5f) enemy.OnSeenByPlayer();
    }
}
```

---

## Troubleshooting

### "I see no fog at all"
- **Camera looks outside the grid.** The gizmo only draws in Scene view; the Game view needs to be inside `(-gw/2, -gh/2)` to `(gw/2, gh/2)`. Move the camera.
- **`outsideGridFog = 0`** with the camera outside the grid. Set to `1` to fog everything outside, or move the camera in.
- **`GridData` is null** on the `FogOfWarManager`. Assign it in the inspector.
- **`FogMaterial` is null** on the `FogOfWarRenderFeature`. Assign the `FogOfWar.mat`.
- **`FogOfWarRenderFeature` is not added** to your URP renderer asset. Add it.

### "Fog appears but is a hard binary 0/1 (no smooth edge)"
- **`maskBlur = 0`** on the manager. Set to `0.025` (or higher) for a soft edge.
- **`maskUpscale = 1`** on the manager. The block size is 1 px; even with blur, the cell-to-cell step is visible. Set to `4` or higher.
- **Camera is far away** so the fog occupies a small screen area. Move closer.

### "Game view is fully black / fully bright"
- **Fully black**: fog is correct but no scene is being rendered behind it. Check that the URP renderer is set up correctly. The `AddBlitPass` copy-back should restore the scene; if not, the source texture might be invalid.
- **Fully bright**: the fog mask is all 1 (everything visible). You might have called `RevealAll()` or have too many revealers.
- **MSAA error**: the temp texture is forced to `msaaSamples = 1`; the camera color's `msaaSamples` is preserved. If you see "MSAA samples from source and destination texture doesn't match", make sure the render feature is using `AddBlitPass` (not `AddCopyPass`) for the copy-back.

### "Fog lags behind the camera"
- **`updateInterval > 0`** is throttling the mask updates. Set to `0` for every-frame updates.
- **`VisibleRiseRate` is too low.** The cell under the camera is at `rate = 1` regardless, so it should be instant. If the cell is visibly lagging, the camera's pivot is not at the centre of the cell — check `EyeHeight` and the transform's `position.y`.

### "Cells flicker on the edges of the revealer"
- **`MaskBlur` is too low** for the upscale. Try `maskBlur = 0.05`.
- **Revealer is moving fast.** Consider smoothing the revealer position (lerp the transform) or use a high `VisibleRiseRate` so the lerp catches up.

### "First frame is fully fogged, then snaps in"
- That's the **first-update snap** working as designed. The very first `UpdateMask` uses `vRise = 1`, which (combined with the distance-based lerp) snaps the entire disc to full visibility. To disable: clear `_snapNextRise` to `false` before the first update, or set the manager's `VisibleRiseRate` to `1` and accept no smoothing at all.

---

## See Also

- [`README.md`](README.md) — main Tile Terrain system documentation
- `TileTerrainGridData` — the grid data the fog covers
- `Shaders/FogOfWar.shader` — shader source
- URP RenderGraph documentation — for understanding the render pass
