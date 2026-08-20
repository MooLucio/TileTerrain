# Inicio Rapido

> **[English](getting-started.md) | Portugues (Brasil)**

Este guia cobre instalacao, configuracao inicial e a configuracao do array de texturas necessaria para o autotiling.

---

## Pre-requisitos

- **Unity 6** ou posterior
- **Universal Render Pipeline (URP) 17** com RenderGraph habilitado
- Seu projeto deve usar o renderer URP (nao o pipeline built-in)

---

## Instalacao

### Opcao A: Importacao Manual

1. Copie a pasta `TileTerrainSystem` para o diretorio `Assets/` do seu projeto.
2. Unity compilara os scripts automaticamente.
3. Os icones personalizados e materiais serao atribuidos automaticamente no primeiro uso.

### Opcao B: Unity Package Manager (UPM)

> Em breve. Por enquanto, use a importacao manual.

---

## Configuracao Inicial

### 1. Criar o Asset de Grid Data

1. Na Hierarchy, crie um GameObject vazio e nomeie `TileTerrain`.
2. Adicione o componente `TileTerrain` a ele.
3. No Inspector, clique em **Create New Grid Data**.
4. Defina as dimensoes da grade:
   - **Internal Width** / **Internal Height**: Numero de quads (nao vertices). Uma grade 64x64 tem 65x65 vertices.
   - **Border Size**: Numero de celulas decorativas por lado (sem collider). Defina como 0 para sem borda.

### 2. Criar a Paleta de Texturas

1. Clique com o botao direito na janela Project: **Create > Tiled terrain > Texture Palette**.
2. Nomeie (ex.: `TerrainPalette`).
3. Atribua ao campo **Palette** do componente TileTerrain.
4. Adicione entradas a paleta — cada entrada e um `Texture2DArray` com um valor de prioridade (menor = prioridade maior, renderiza por cima).

### 3. Criar a Caixa de Props (opcional)

1. Clique com o botao direito: **Create > Tiled terrain > Props Box**.
2. Adicione entradas `TileTerrainProp` para cada tipo de prop.
3. Atribua a Caixa de Props ao componente TileTerrain.

### 4. Atribuir Materiais e Malhas

| Campo | Obrigatorio | Descricao |
|-------|-------------|-----------|
| **Tile Material** | Auto-atribuido | Shader da superficie do terreno (auto-detectado em `TileTerrainShader.mat`) |
| **Water Material** | Sim (para agua) | Shader de superficie de agua translucida |
| **Cliff Mesh Fbx** | Sim (para penhascos) | FBX com 14 sub-malhas de penhasco padrao |
| **Cliff Double Mesh Fbx** | Nao (para altura dupla) | FBX com 14 sub-malhas de penhasco de altura dupla |
| **Cliff Transitional Mesh Fbx** | Nao (para transicoes) | FBX com sub-malhas de penhasco transicional |
| **Ramp Mesh Fbx** | Nao (para rampas) | FBX com 36 sub-malhas de ramp |

### 5. Comecar a Esculpir

1. Selecione o GameObject TileTerrain.
2. Escolha uma aba de ferramenta no Inspector (Altura, Textura, Penhasco, Ramp, Agua ou Props).
3. Pressione `S` para ativar o modo de pintura.
4. Pinte na visualizacao da Cena.

---

## Configuracao do Array de Texturas

O sistema de autotile usa um **Texture2DArray** organizado como um tilemap de **8 colunas x 4 linhas**. Cada tipo de textura que voce quiser pintar precisa do seu proprio Texture2DArray com este layout.

### Layout da Planilha

Cada sprite sheet tem **512 x 256 pixels**, dividido em uma grade de **8 colunas x 4 linhas** (cada celula tem **64 x 64 pixels**).

```
     Col 0   Col 1   Col 2   Col 3   Col 4   Col 5   Col 6   Col 7
    ┌───────┬───────┬───────┬───────┬───────┬───────┬───────┬───────┐
Row │       │       │       │       │       │       │       │       │
 0  │  c0r0 │  c1r0 │  c2r0 │  c3r0 │  c4r0 │  c5r0 │  c6r0 │  c7r0 │
    ├───────┼───────┼───────┼───────┼───────┼───────┼───────┼───────┤
Row │       │       │       │       │       │       │       │       │
 1  │  c0r1 │  c1r1 │  c2r1 │  c3r1 │  c4r1 │  c5r1 │  c6r1 │  c7r1 │
    ├───────┼───────┼───────┼───────┼───────┼───────┼───────┼───────┤
Row │       │       │       │       │       │       │       │       │
 2  │  c0r2 │  c1r2 │  c2r2 │  c3r2 │  c4r2 │  c5r2 │  c6r2 │  c7r2 │
    ├───────┼───────┼───────┼───────┼───────┼───────┼───────┼───────┤
Row │       │       │       │       │       │       │       │       │
 3  │  c0r3 │  c1r3 │  c2r3 │  c3r3 │  c4r3 │  c5r3 │  c6r3 │  c7r3 │
    └───────┴───────┴───────┴───────┴───────┴───────┴───────┴───────┘
     ◄─── Conectores / Cantos ────►  ◄─── Tiles Centro Aleatorios ────►
```

### Grupos de Colunas

| Colunas | Finalidade | Descricao |
|---------|------------|-----------|
| **0-3** | **Tiles de Conector / Canto** | Tiles direcionados por bitmask. Cada uma das 14 padroes nao triviais mapeia para exatamente uma dessas 16 celulas (4 colunas x 4 linhas). Estes tiles mostram as transicoes de borda/canto entre esta textura e as texturas circundantes. |
| **4-7** | **Tiles Centro Aleatorios** | Usados quando um tile esta completamente cercado (todas as 4 cantos coincidem) ou completamente isolado (nenhum canto coincide). O sistema seleciona aleatoriamente uma dessas 16 celulas para variedade visual. |

### Mapeamento de Linhas

| Linha | Intervalo de Bitmask | Descricao |
|:-----:|----------------------|-----------|
| **0** | Masks 12-15 | Linha superior: tiles onde os dois vertices superiores (v2, v3) dominam |
| **1** | Masks 4-7 | Segunda linha: tiles onde o vertice inferior esquerdo (v0) esta ativo |
| **2** | Masks 8-11 | Terceira linha: tiles onde o vertice inferior direito (v1) esta ativo |
| **3** | Masks 1-3 | Linha inferior: combinacoes restantes de cantos |

### Mapeamento Bitmask-para-Celula

Cada vertice em um quad tem um sinalizador de 1 bit indicando se coincide com a textura alvo. Os 4 bits formam uma mascara (0-15) que seleciona o tile:

| Mascara | Coluna | Linha | Padrao de Vertices `[v2, v3, v0, v1]` |
|:-------:|:------:|:-----:|---------------------------------------|
| 0 | 4-7 | aleatorio | `0, 0, 0, 0` — isolado (centro aleatorio) |
| 1 | 2 | 3 | `0, 0, 0, 1` |
| 2 | 1 | 3 | `0, 0, 1, 0` |
| 3 | 3 | 3 | `0, 0, 1, 1` |
| 4 | 0 | 1 | `0, 1, 0, 0` |
| 5 | 2 | 1 | `0, 1, 0, 1` |
| 6 | 1 | 1 | `0, 1, 1, 0` |
| 7 | 3 | 1 | `0, 1, 1, 1` |
| 8 | 0 | 2 | `1, 0, 0, 0` |
| 9 | 2 | 2 | `1, 0, 0, 1` |
| 10 | 1 | 2 | `1, 0, 1, 0` |
| 11 | 3 | 2 | `1, 0, 1, 1` |
| 12 | 0 | 0 | `1, 1, 0, 0` |
| 13 | 2 | 0 | `1, 1, 0, 1` |
| 14 | 1 | 0 | `1, 1, 1, 0` |
| 15 | 4-7 | aleatorio | `1, 1, 1, 1` — totalmente cercado (centro aleatorio) |

### Ordem dos Vertices

A bitmask usa esta ordem de vertices: `[v2, v3, v0, v1]`

```
v2 ─── v3
│  quad  │
v0 ─── v1
```

- **v0** = Inferior Esquerdo
- **v1** = Inferior Direito
- **v2** = Superior Esquerdo
- **v3** = Superior Direito

### Formula

O indice da textura dentro do array e calculado como:

```
indice = (mask % 4) + (mask / 4) * 8
```

Isso mapeia a grade 4x4 de conectores para as posicoes corretas dentro da planilha de 8 colunas.

### Randomizacao

Quando a mascara e 0 (isolado) ou 15 (totalmente cercado), o sistema usa as **colunas 4-7** (tiles centro aleatorios) em vez dos tiles de conector. O controle deslizante `TextureRandomness` (0-1) na ferramenta de Textura controla a probabilidade:

- **0.0** = sempre usar o tile centro base (coluna 4)
- **0.4** = 40% de chance de uma variacao aleatoria (padrao)
- **1.0** = sempre usar uma variacao aleatoria

Candidatos de randomizacao para mascara 15: `0, 4, 5, 7, 12, 13, 14, 15, 20, 21, 22, 23, 28, 29, 30, 31`

---

## Criando um Texture2DArray

### No Unity

1. Importe sua sprite sheet como **Texture** (nao Sprite). Defina **Texture Type** como `Default`.
2. Defina **Wrap Mode** como `Clamp`.
3. Nas configuracoes de importacao, mantenha como Default. Use um script ou a abordagem abaixo:

### Fluxo de Trabalho Recomendado

1. Crie um PNG de 512x256 com todos os 32 tiles organizados na grade 8x4.
2. Importe no Unity como **Texture** (nao Sprite).
3. Use um script para converter para `Texture2DArray`:

```csharp
using UnityEngine;

public static class TextureArrayBuilder
{
    public static Texture2DArray CreateFromSheet(Texture2D sheet, int cellSize = 64)
    {
        int cols = sheet.width / cellSize;   // 8
        int rows = sheet.height / cellSize;  // 4
        int slices = cols * rows;            // 32

        var arr = new Texture2DArray(cellSize, cellSize, slices,
            TextureFormat.RGBA32, mipChain: true);

        Color[] pixels = new Color[cellSize * cellSize];

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int slice = r * cols + c;
                Rect rect = new Rect(c * cellSize, r * cellSize, cellSize, cellSize);
                Texture2D tile = new Texture2D(cellSize, cellSize);
                var colors = sheet.GetPixels((int)rect.x, (int)rect.y,
                    (int)rect.width, (int)rect.height);
                tile.SetPixels(colors);
                tile.Apply();

                Graphics.CopyTexture(tile, arr, slice);
                Object.DestroyImmediate(tile);
            }
        }

        arr.Apply();
        return arr;
    }
}
```

### Configuracao da Paleta

Apos criar o Texture2DArray, adicione a TileTerrainPalette:

1. Selecione seu asset de Paleta.
2. Adicione uma nova entrada.
3. Atribua o Texture2DArray.
4. Defina a **Prioridade** (numero menor = prioridade maior, renderiza por cima):
   - Grama: 0 (prioridade mais alta)
   - Terra: 1
   - Pavimento: 2
   - Agua: 3 (prioridade mais baixa)

---

## Configuracao da Grade

| Configuracao | Descricao | Recomendado |
|-------------|-----------|-------------|
| **Internal Width** | Quads ao longo do X (vertices totais = Largura + 1 + Borda*2) | 32-128 |
| **Internal Height** | Quads ao longo do Z | 32-128 |
| **Border Size** | Celulas de borda decorativas por lado (sem collider) | 0-4 |
| **Chunk Size** | Quads por lado do chunk (menos chunks = menos draw calls, culling mais grosseiro) | 16-32 |

---

## Proximos Passos

- Leia o documento de [Arquitetura](architecture.pt-BR.md) para entender o design de tres pilares.
- Explore a [Documentacao das Ferramentas](#ferramentas) para uso detalhado.
- Confira o `Examples/Sample_Scene.unity` para uma demo funcional.
