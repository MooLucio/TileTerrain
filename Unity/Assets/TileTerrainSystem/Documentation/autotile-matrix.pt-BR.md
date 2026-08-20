# Solução da Matriz de Autotile

> **[English](autotile-matrix.md) | Portugues (Brasil)**

Mapeia bitmasks de 4 bits por vértice para índices de fatias de textura para auto-tiling.

## Ordem dos Vértices

A ordem dos vértices do quad usada para o cálculo do bitmask é:

```
[v2, v3, v0, v1]
```

- `v2`, `v3` = metade superior do quad (topo)
- `v0`, `v1` = metade inferior do quad (base)

## Caso Especial: Totalmente Cercado (1,1,1,1)

Quando todos os quatro vértices coincidem, o tile é randomizado para variedade visual.
Candidatos à randomização: `0, 4, 5, 7, 12, 13, 14, 15, 20, 21, 22, 23, 27, 28, 29, 30, 31`

## Bitmask para Índice de Textura

| Vértices `[v2, v3, v0, v1]` | Índice de Textura |
|----------------------------|:-------------:|
| `0, 0, 0, 1` | 1 |
| `0, 0, 1, 0` | 2 |
| `0, 0, 1, 1` | 3 |
| `0, 1, 0, 0` | 8 |
| `0, 1, 0, 1` | 9 |
| `0, 1, 1, 0` | 10 |
| `0, 1, 1, 1` | 11 |
| `1, 0, 0, 0` | 16 |
| `1, 0, 0, 1` | 17 |
| `1, 0, 1, 0` | 18 |
| `1, 0, 1, 1` | 19 |
| `1, 1, 0, 0` | 24 |
| `1, 1, 0, 1` | 25 |
| `1, 1, 1, 0` | 26 |

### Máscara Completa (15) — Texturas Centrais Randomizadas

Quando a máscara `== 15` (todos os vértices coincidem), o sistema seleciona uma textura aleatória das colunas 4–7 (tiles centrais). Veja `TileTerrainBitmask.GetTextureIndex()` para a lógica de seleção ponderada.
