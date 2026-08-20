# Arquitetura

> **[English](architecture.md) | Portugues (Brasil)**

O Tile Terrain System e construido sobre tres pilares: **Dados**, **Renderizacao** e **Editor**. Todo o codigo vive no namespace `MooLucio.TileTerrain`.

---

## Tres Pilares

```
┌─────────────────────────────────────────────────────────────┐
│                    EDITOR (Inspector)                        │
│  Altura │ Textura │ Penhasco │ Ramp │ Agua │ Props          │
│  Consultas de pincel, propagacao BFS, desfazer, seguranca   │
└──────────────┬──────────────────────────┬───────────────────┘
               │ le/gravita               │ dispara rebuild
               ▼                          ▼
┌──────────────────────┐    ┌──────────────────────────────────┐
│   DADOS (ScriptableObject) │    │   RENDERIZACAO (MonoBehaviour)      │
│   TileTerrainGridData     │◄───│   TileTerrain                      │
│   • Vertices (posicoes,   │    │   • Divisao em chunks              │
│     alturas, texturas,   │    │   • Geracao de malha                │
│     penhasco, agua, props)│    │   • Instanciacao de malhas         │
│   • Quads (camadas de     │    │     de penhasco/ramp               │
│     autotile + colunas   │    │   • Geracao de superficie de agua   │
│     de bitmask)          │    │   • Gerenciamento de materiais      │
│   • Props e entanglement │    │   • Amostragem de shader URP        │
└──────────────────────────┘    └──────────────────────────────────┘
```

### 1. Dados — `TileTerrainGridData`

Um `ScriptableObject` que persiste todo o estado da grade. Armazena:

| Dados | Descricao |
|-------|-----------|
| **Vertices** | Por vertice: posicao, offset de altura, cor, 3 camadas de IDs de textura + mascaras, nivel de penhasco (`CliffByte`), flag de meio-passo (`CliffHalfStep`), estado de agua (`IsWater`, `WaterLevel`), ID do grupo de entanglement |
| **Quads** | Por quad: 4 IDs de vertices, coordenadas da grade, 3 camadas de resultados autotile (ID de textura + coluna/linha do tile), nivel do piso |
| **Props** | Instancias de props colocados: posicao, rotacao, escala, variante, pegada, ID de entanglement |
| **Grupos de Entanglement** | Grupos de vertices vinculados a um prop — movem juntos quando o prop e realocado |

**Propriedades chave:**
- `Width` / `Height` — Dimensoes totais da grade incluindo borda
- `InternalWidth` / `InternalHeight` area editavel (excluindo borda)
- `BorderSize` — Celulas decorativas por lado (sem collider)
- `Version` — Incrementado em mudancas estruturais; consumidores (ex.: FogOfWarManager) usam para detectar desatualizacao

### 2. Renderizacao — `TileTerrain`

O `MonoBehaviour` principal. E **exclusivo do editor** — nenhum codigo roda em runtime. Os chunks bakeados sao serializados com a cena.

**Fluxo de geracao de malha:**

```
GenerateMesh()
  ├── SyncTexturesFromPalette()
  ├── Inicializar vertices nao preenchidos
  ├── Recalcular todas as bitmasks
  ├── Para cada chunk:
  │     ├── Fase 1: Criar penhascos simples/altura dupla (regra de paridade)
  │     ├── Fase 2: Substituir quads n=3 por malhas transicionais
  │     ├── Fase 3: Substituir por malhas de ramp onde halfStep existe
  │     ├── Construir quads planos do terreno
  │     ├── Construir quads ramp-planos (halfStep sem malha customizada)
  │     ├── Construir instancias de malha de penhasco
  │     ├── Combinar em malha unica por material
  │     ├── Construir malha de agua (vertices mesclados, patches de preenchimento)
  │     └── Atribuir MeshCollider
  ├── SmoothChunkSeams()
  └── RecalculateFloorOffsets()
```

**Sistema de chunks:**
- Grade dividida em chunks (`ChunkSize` configuravel, padrao 16 quads por lado)
- Cada chunk e um GameObject filho com sub-objetos `Terrain` e `Water`
- Chunks marcados estaticos para batching e occlusion culling
- Ocultos na Hierarchy por padrao (`HideChunksInHierarchy`)

### 3. Editor — `TileTerrainEditor`

Um inspector personalizado dividido em 7 arquivos de classe partial. Fornece 6 modos de ferramenta baseados em pincel com:

- Consultas de pincel com indice espacial (sem varreduras O(n) de vertices)
- Propagacao BFS para suavizacao de penhascos
- Reforco de seguranca na linha costeira da agua
- UI persistida via `SessionState` entre recargas do inspector
- Desfazer baseado em tracado (`Undo.CollapseUndoOperations`)
- Rebuild de malha limitado a 30 Hz e respawn de props a 15 Hz

---

## Fluxo de Dados

### Pintando uma Textura

```
Usuario pinta tracado do pincel
  → Coleta vertices no raio do pincel
  → Aplica textura a cada vertice (ordenacao por prioridade em over/mid/under)
  → BatchRecalculateVertices()
    → Para cada quad afetado: RecalculateQuad()
      → Coleta IDs de textura unicos dos 4 cantos
      → Ordena por prioridade (indice menor = prioridade maior)
      → Atribui as camadas over/mid/under
      → Computa bitmask → coluna/linha no tilemap
  → Marca chunks sujos
  → Solicita rebuild de malha (limitado)
```

### Elevando um Penhasco

```
Usuario eleva penhasco
  → Remove props sobrepostos
  → Enfileira vertices com nivel alvo
  → Loop de propagacao BFS:
    → Desenfileira vertice, aplica nivel
    → Verifica 8 vizinhos: se diferenca > passo maximo (2), enfileira vizinho
    → Vertices de limite de agua: passo maximo = 1
  → Passo de reparo (ate 10 iteracoes)
  → Recalcula pisos dos quads
  → Revalida flags halfStep (ramp)
  → Marca chunks sujos
```

---

## Constantes Principais

| Constante | Valor | Finalidade |
|-----------|-------|------------|
| `HeightMin` | -2 | Altura minima do offset do vertice |
| `HeightMax` | 2 | Altura maxima do offset do vertice |
| `CliffHeight` | 1 | Altura no mundo por nivel de penhasco |
| `WaterOffset` | 0.5 | Superficie da agua fica 0.5 unidades abaixo do nivel de agua |
| `FullQuadMask` | 15 | Todos os 4 cantos com penhasco = quad plano elevado |
| `SolidTextureMask` | 0xFF | Mascara de textura totalmente opaca |
| `NoCliffLevel` | -128 | Sentinela para sem penhasco |
| `MinEditableCliff` | -3 | Nivel de penhasco editavel mais baixo |
| `MaxEditableCliff` | 11 | Nivel de penhasco editavel mais alto |
| `TilemapColumns` | 8 | Colunas no tilemap de textura |

---

## Interacoes Entre Ferramentas

| Interacao | Efeito |
|-----------|--------|
| Penhasco → Props | Pintura de penhasco remove todos os props sobrepostos e seus grupos de entanglement |
| Altura/Penhasco/Ramp → Props | Apos cada tracado, `PinPropsToTerrain()` realinha props fixos |
| Agua → Penhasco | Penhasco respeita limites de agua; ferramenta Up pode drenar agua |
| Agua → Rampas | Pintura de agua limpa todas as flags de ramp (halfStep) |
| Props → Vertices | Grupos de entanglement sincronizam modificacoes de vertices nos membros do grupo |
| Textura → Quads | Recalculo de bitmask dirige a selecao de malha autotile |

---

## Nevoa de Guerra (Sistema Separado)

A nevoa de guerra e um subsistema independente com seus proprios componentes:

- `FogOfWarManager` — Singleton, owns a mascara RGBA8, roda LOS e flood fill
- `FogOfWarRevealer` — Componente por GameObject que se registra com o manager
- `FogOfWarRenderFeature` — URP `ScriptableRendererFeature` que injeta um pass de tela cheia para nevoa

Veja [fog-of-war.pt-BR.md](fog-of-war.pt-BR.md) para referencia completa.
