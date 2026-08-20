# Tile Terrain System

> **[English](README.md) | Portugues (Brasil)**

Um framework personalizado de terreno para Unity, exclusivo do editor, para jogos baseados em grade que precisam de visuais organicos e suaves combinados com logica de grade discreta. Suporta escultura de heightmap, splatting de texturas baseado em prioridade, um sistema de penhascos de tres niveis com malhas de transicao, rampas de meio-passo, pintura de agua e colocacao de props com entanglement sincronizado com o terreno.

> Fortemente inspirado no [World Editor do Warcraft III](https://en.wikipedia.org/wiki/Warcraft_III_World_Editor).

**Requisitos:** Unity 6+ | URP 17 (Universal Render Pipeline com RenderGraph)

---

## Ferramentas

| # | Ferramenta | Descricao |
|---|------------|-----------|
| 1 | **[Altura](tools-height.pt-BR.md)** | Escultura organica: Elevar, Abaixar, Alvo, Suavizar, Ruido |
| 2 | **[Textura](tools-texture.pt-BR.md)** | Mistura de 3 camadas por prioridade: Pintar, Borrar, Preencher, Apagar |
| 3 | **[Penhasco](tools-cliff.pt-BR.md)** | Elevacao discreta com propagacao BFS: Subir, Descer, Alvo, Borrar, Apagar |
| 4 | **[Ramp](tools-ramp.pt-BR.md)** | Transicoes de meio-passo entre niveis de penhasco: Definir, Apagar |
| 5 | **[Agua](tools-water.pt-BR.md)** | Pintura de agua ciente de tracado com protecao de barragens |
| 6 | **[Props](tools-props.pt-BR.md)** | Objetos decorativos com grupos de entanglement: Colocar, Pintar, Selecionar, Remover, Rotacionar, Escalar |

Atalhos de teclado: `1-5` trocam ferramentas, `S` ativa modo de pintura, `[`/`]` tamanho do pincel, `B` redimensionar arrastando, `M` alternar forma.

---

## Configuracao Rapida

1. Anexe `TileTerrain` a um GameObject.
2. No Inspector, clique em **Create New Grid Data**.
3. Atribua os assets necessarios (veja [Inicio Rapido](getting-started.pt-BR.md) para configuracao completa incluindo array de texturas).
4. Selecione uma aba de ferramenta e ative o modo de pintura (`S`).

---

## Estrutura de Arquivos

```
TileTerrainSystem/
├── Scripts/
│   ├── TileTerrain.cs              # Renderizador principal, geracao de malha, gestao de chunks
│   ├── TileTerrainGridData.cs      # Armazenamento de dados ScriptableObject (vertices, quads, props)
│   ├── TileTerrainBitmask.cs       # Calculo de bitmask autotile + mapeamento de indice de textura
│   ├── TileTerrainCliff.cs         # Carregamento/cache de malhas de penhasco, matriz de ramp
│   ├── TileTerrainConstants.cs     # Constantes compartilhadas (niveis de penhasco, mascaras, sentinelas)
│   ├── TileTerrainPalette.cs       # Paleta de prioridade de texturas ScriptableObject
│   ├── TileTerrainProp.cs          # Definicao de um prop ScriptableObject
│   ├── TileTerrainPropsBox.cs      # Colecao de props ScriptableObject
│   ├── FogOfWarManager.cs          # Singleton de nevoa de guerra (mascara, LOS, BFS flood fill)
│   ├── FogOfWarRevealer.cs         # Componente revelador de nevoa por GameObject
│   └── FogOfWarRenderFeature.cs    # Pass de tela cheia URP 17 RenderGraph para nevoa
├── Editor/
│   ├── TileTerrainEditor.cs              # Inspector personalizado (classe partial)
│   ├── TileTerrainEditor.Height.cs       # Ferramentas de pincel de altura
│   ├── TileTerrainEditor.Texture.cs      # Ferramentas de pintura de textura
│   ├── TileTerrainEditor.Cliff.cs        # Edicao de penhasco + propagacao BFS + ferramentas de ramp
│   ├── TileTerrainEditor.Water.cs        # Ferramentas de pintura de agua
│   ├── TileTerrainEditor.Props.cs        # Ferramentas de colocacao de props
│   ├── TileTerrainEditor.SceneGUI.cs     # Overlay da cena + renderizacao da grade
│   ├── TileTerrainEditor.Safety.cs       # Verificacoes de seguranca (IsSafeToCarve, IsBoundary)
│   ├── TileTerrainIconInitializer.cs     # Atribuicao automatica de icones de ScriptableObject
│   └── TileTerrainOverlay.cs             # Renderizacao do overlay da cena
├── Shaders/
│   ├── TileTerrainShader.shader          # Shader HLSL personalizado URP para terreno
│   ├── TileTerrain.shadergraph           # Variante Shader Graph
│   ├── Sample2DArrayCustom.shadersubgraph # Subgraph personalizado para amostragem de Texture2DArray
│   ├── Water.shadergraph                 # Shader graph da superficie de agua
│   └── FogOfWar.shader                  # Shader de mistura de tela cheia para nevoa de guerra
├── Materials/
│   ├── TileTerrainShader.mat             # Instancia de material do terreno
│   ├── water.mat                         # Instancia de material da agua
│   └── FogOfWar.mat                      # Instancia de material da nevoa de guerra
├── Textures/
│   ├── prototype.png                     # Textura prototipo
│   ├── lowGrass.png, tallGrass.png       # Texturas de grama
│   ├── dirt.png                          # Textura de terra
│   ├── pavement.png                      # Textura de pavimento
│   ├── cliffSide.png                     # Textura de lateral de penhasco
│   └── water.png                         # Textura de agua
├── Models/
│   ├── Cliff/FBX/                        # Malhas de penhasco (padrao, dupla, transicional, rampas)
│   └── Cliff/Blender/                    # Arquivos fonte Blender + definicoes de matriz JSON
├── Icons/                                # Icones de ScriptableObject
├── Examples/
│   ├── Sample_Scene.unity                # Cena de exemplo
│   └── Sample Data/                      # Assets de exemplo (GridData, Palette, PropsBox)
└── Documentation/                        # Este diretorio
```

---

## Documentacao

| Documento | Descricao |
|-----------|-----------|
| **[Inicio Rapido](getting-started.pt-BR.md)** | Instalacao, configuracao, array de texturas |
| **[Arquitetura](architecture.pt-BR.md)** | Design de tres pilares, fluxo de dados, namespace |
| [Ferramenta de Altura](tools-height.pt-BR.md) | Modos de escultura e parametros |
| [Ferramenta de Textura](tools-texture.pt-BR.md) | Pilha de prioridade de 3 camadas, sistema de bitmask autotile |
| [Ferramenta de Penhasco](tools-cliff.pt-BR.md) | Tres tilesets, sistema de paridade, propagacao BFS |
| [Ferramenta de Ramp](tools-ramp.pt-BR.md) | Transicoes de meio-passo, matriz de 36 padroes |
| [Ferramenta de Agua](tools-water.pt-BR.md) | Pintura de agua, protecao de barragens, regras de limite |
| [Ferramenta de Props](tools-props.pt-BR.md) | Grupos de entanglement, sistema de pegada |
| [Nevoa de Guerra](fog-of-war.pt-BR.md) | Referencia completa do sistema (manager, revelador, render feature, shader) |
| [Matriz Autotile](autotile-matrix.pt-BR.md) | Mapeamento bitmask-para-indice de textura |
| [Matriz de Transicao](transition-matrix.pt-BR.md) | Tabela de 36 padroes de penhasco transicional |

---

## Licenca

Este projeto está licenciado sob a [Licença PolyForm Perimeter 1.0.0](LICENSE).
