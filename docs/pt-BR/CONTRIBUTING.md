# Contribuindo

> **[English](../../CONTRIBUTING.md) | Português (Brasil)**

Obrigado pelo seu interesse no Tile Terrain System! Esta é uma ferramenta pequena e
focada, e contribuições são bem-vindas — mas por favor, mantenha o escopo em mente.

## Regras básicas

- **Exclusivo do editor, orientado a dados.** O sistema assa malhas no editor; não
  há código de runtime. Novos recursos devem seguir esse modelo, a menos que haja um
  motivo forte para não fazê-lo.
- **Compatibilidade retroativa.** Renomear um campo público quebra cenas salvas e
  assets de grade. Se você precisar renomear um campo serializado, adicione
  `[FormerlySerializedAs("nomeAntigo")]`.
- **Mantenha a serialização dos dados da grade estável.** `TileTerrainGridData` é um
  formato de persistência; mudanças nele devem migrar dados, não quebrá-los.
- **Siga o estilo existente.** Membros públicos em PascalCase, campos privados em
  `_camelCase`, comentários de documentação XML em APIs públicas e constantes em vez
  de números mágicos (veja `TileTerrainConstants.cs`).

## Fluxo de trabalho

1. Abra uma issue para discutir a mudança antes de abrir um pull request.
2. Crie um branch a partir de `main`.
3. Faça a sua mudança e adicione/ajuste documentação em
   `Unity/Assets/TileTerrainSystem/Documentation/` quando relevante.
4. Verifique se compila. O projeto é desenvolvido contra Unity 6 (6000.x) com URP;
   mantenha-o compilando lá.
5. Abra um pull request descrevendo a mudança, o porquê e o que você testou.

## Licenciamento

Ao contribuir, você concorda que suas contribuições são licenciadas sob as
licenças do repositório:

- Código, shaders e documentação → [MIT](LICENSE.md)
- Assets → [CC BY 4.0](LICENSE.assets.md)

Não envie assets de terceiros a menos que você os possua ou eles sejam licenciados
de forma compatível (e mencione isso no PR).

Doações (PayPal / Mercado Pago) são um apoio voluntário e não conferem acesso,
prioridade ou créditos especiais neste projeto.
