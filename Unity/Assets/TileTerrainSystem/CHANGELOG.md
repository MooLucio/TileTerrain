# Changelog

> **English** | [Portugues (Brasil)](CHANGELOG.pt-BR.md)

All notable changes to the Tile Terrain System will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- MIT License.
- Public documentation for GitHub collaboration.
- Comprehensive getting-started guide with texture array setup instructions.
- Architecture documentation explaining the three-pillar design.
- Per-tool documentation (Height, Texture, Cliff, Ramp, Water, Props).
- Border system documentation.
- Props and entanglement system documentation.
- Ramp system documentation.

### Changed
- Rewritten README as a concise landing page with links to detailed docs.
- File structure in documentation now matches actual project layout.

### Removed
- `water-border-protection.md` (internal development log, not user-facing documentation).

## [1.0.0] - 2026-01-01

### Added
- Core terrain system: grid-based heightmap sculpting with chunk-based rendering.
- Height tool with 5 sub-modes: Raise, Lower, Target, Smooth, Noise.
- Texture tool with 3-layer priority stack (Over/Mid/Under) and autotile bitmask system.
- Cliff system with 3 tilesets: Standard, Double-height, Transitional (36 patterns).
- Ramp system for half-step elevation transitions (36 ramp patterns).
- Water tool with BFS propagation and dam protection.
- Props tool with entanglement groups for terrain-synced placement.
- Border system for decorative-only cells without collider.
- Fog of War system with cliff-aware LOS and URP RenderGraph integration.
- Custom URP HLSL terrain shader with Texture2DArray sampling.
- Bilingual documentation (English and Portuguese).
