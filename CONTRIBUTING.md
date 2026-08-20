# Contributing

> **English** | [Português (Brasil)](docs/pt-BR/CONTRIBUTING.md)

Thanks for your interest in the Tile Terrain System! This is a small, focused
tool and contributions are welcome — but please keep the scope in mind.

## Ground rules

- **Editor-only, data-driven.** The system bakes meshes in the editor; there
  is no runtime code. New features should follow that model unless there is a
  strong reason not to.
- **Backward compatibility.** Renaming a public field breaks saved scenes and
  grid assets. If you must rename a serialized field, add
  `[FormerlySerializedAs("oldName")]`.
- **Keep the grid data serialization stable.** `TileTerrainGridData` is a
  persistence format; changes to it should migrate data, not break it.
- **Match the existing style.** PascalCase public members, `_camelCase`
  private fields, XML doc comments on public APIs, and constants instead of
  magic numbers (see `TileTerrainConstants.cs`).

## Workflow

1. Open an issue to discuss the change before opening a pull request.
2. Branch from `main`.
3. Make your change and add/adjust documentation in
   `Unity/Assets/TileTerrainSystem/Documentation/` when relevant.
4. Verify it compiles. The project is developed against Unity 6 (6000.x) with
   URP; keep it compiling there.
5. Open a pull request describing the change, why, and what you tested.

## Licensing

By contributing you agree that your contributions are licensed under the
repository's licenses:

- Code, shaders and documentation → [MIT](LICENSE.md)
- Assets → [CC BY 4.0](LICENSE.assets.md)

Do not submit third-party assets unless you own them or they are licensed
compatibly (and say so in the PR).

Donations (PayPal / Mercado Pago) are voluntary support and grant no special
access, priority or credits in this project.
