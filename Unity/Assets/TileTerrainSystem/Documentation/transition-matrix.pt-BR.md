# Matriz de Malha de Penhasco de Transição

> **[English](transition-matrix.md) | Português (Brasil)**

Mapeia combinações de altura de 4 vértices para índices de malha de penhasco de transição (0–35).

Elas são usadas quando um quad tem **3 níveis de piso únicos** (`n`, `n+1`, `n+2`), exigindo uma malha de transição especial em vez de penhascos padrão de altura simples/dupla.

## Ordem dos Vértices

`[v0, v1, v2, v3]` — ordem natural do quad (NÃO o remapeamento `[v2, v3, v0, v1]` usado para tiles de textura).

Os valores são offsets relativos ao nível mínimo de piso do quad (0, 1 ou 2).

## Categorias de Distribuição

| Distribuição | Índices | Padrão |
|-------------|---------|---------|
| **1** | 0–3 | 2 piso, 1 médio, 1 alto. Alto adjacente a ambos os pisos. |
| **2** | 4–7 | 1 piso, 2 médios, 1 alto. Médios adjacentes. |
| **3** | 8–11 | 1 piso, 1 médio, 2 altos. Altos adjacentes. |
| **4** | 12–15 | 2 piso, 1 médio, 1 alto. Alto diagonal a um piso. |
| **5** | 16–19 | 1 piso, 2 médios, 1 alto. Médios diagonais. |
| **6** | 20–23 | (estendida) |
| **7** | 24–28 | (estendida, combinações mais altas) |
| **8** | 29–35 | (estendida, combinações mais altas) |

## Mapeamento Completo

| Índice | v0 | v1 | v2 | v3 |
|:-----:|:--:|:--:|:--:|:--:|
| 0 | 0 | 0 | 1 | 2 |
| 1 | 0 | 0 | 2 | 1 |
| 2 | 0 | 1 | 0 | 2 |
| 3 | 0 | 1 | 1 | 2 |
| 4 | 0 | 1 | 2 | 0 |
| 5 | 0 | 1 | 2 | 1 |
| 6 | 0 | 1 | 2 | 2 |
| 7 | 0 | 2 | 0 | 1 |
| 8 | 0 | 2 | 1 | 0 |
| 9 | 0 | 2 | 1 | 1 |
| 10 | 0 | 2 | 1 | 2 |
| 11 | 0 | 2 | 2 | 1 |
| 12 | 1 | 0 | 0 | 2 |
| 13 | 1 | 0 | 1 | 2 |
| 14 | 1 | 0 | 2 | 0 |
| 15 | 1 | 0 | 2 | 1 |
| 16 | 1 | 0 | 2 | 2 |
| 17 | 1 | 1 | 0 | 2 |
| 18 | 1 | 1 | 2 | 0 |
| 19 | 1 | 2 | 0 | 0 |
| 20 | 1 | 2 | 0 | 1 |
| 21 | 1 | 2 | 0 | 2 |
| 22 | 1 | 2 | 1 | 0 |
| 23 | 1 | 2 | 2 | 0 |
| 24 | 2 | 0 | 0 | 1 |
| 25 | 2 | 0 | 1 | 0 |
| 26 | 2 | 0 | 1 | 1 |
| 27 | 2 | 0 | 1 | 2 |
| 28 | 2 | 0 | 2 | 1 |
| 29 | 2 | 1 | 0 | 0 |
| 30 | 2 | 1 | 0 | 1 |
| 31 | 2 | 1 | 0 | 2 |
| 32 | 2 | 1 | 1 | 0 |
| 33 | 2 | 1 | 2 | 0 |
| 34 | 2 | 2 | 0 | 1 |
| 35 | 2 | 2 | 1 | 0 |

## Referência de Código

Veja `TileTerrainBitmask.cs`:
- O construtor estático constrói a tabela de consulta `HeightToTransitionIndex` a partir de `MakeKey()`
- `GetTransitionalMeshIndex()` retorna o índice de malha para um quad determinado (+ offset de piso)
- As malhas de transição são carregadas de `cliff_transitional_mesh.fbx` (20 malhas, índices correspondem às primeiras 20 entradas acima)
