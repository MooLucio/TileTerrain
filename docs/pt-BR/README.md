# Tile Terrain System

> **[English](../../README.md) | Português (Brasil)**

Um framework de edição de terreno para Unity, exclusivo do editor, para jogos baseados em grade que precisam de visuais orgânicos e suaves combinados com lógica de grade discreta. Oferece escultura de heightmap, splatting de texturas baseado em prioridade, um sistema de penhascos (cliffs) de três níveis com malhas de transição, props e uma névoa de guerra (fog of war) ciente de penhascos.

> Fortemente inspirado no [World Editor do Warcraft III](https://en.wikipedia.org/wiki/Warcraft_III_World_Editor).

**Licença do código:** MIT — veja [`LICENSE.md`](LICENSE.md).
**Licença dos assets:** CC BY 4.0 — veja [`LICENSE.assets.md`](LICENSE.assets.md).

---

## Índice

- [Recursos](#recursos)
- [Requisitos](#requisitos)
- [Instalação](#instalação)
- [Início Rápido](#início-rápido)
- [Arquitetura](#arquitetura)
- [Sistema de Penhascos](#sistema-de-penhascos)
- [Modos de Ferramenta](#modos-de-ferramenta)
- [Estrutura do Repositório](#estrutura-do-repositório)
- [Sistemas Avançados](#sistemas-avançados)
  - [Névoa de Guerra](#névoa-de-guerra)
- [Documentação](#documentação)
- [Licença](#licença)
- [Apoiar](#apoiar)
- [Reconhecimento](#reconhecimento)

---

## Recursos

- **Baseado em grade com visuais orgânicos** — esculpa altura contínua enquanto a lógica do jogo permanece em uma grade discreta limpa.
- **Ferramenta de Altura** — elevar, abaixar, definir alvo, suavizar e esculpir com ruído dentro de um único nível de penhasco.
- **Ferramenta de Textura** — pilha de texturas com prioridade de 3 slots (Over → Mid → Under) com shader de bitmask de auto-tile.
- **Ferramenta de Penhasco** — sistema de penhasco de três níveis (padrão / duplo / transição) com elevação ciente de paridade e suavização por BFS.
- **Ferramenta de Água** — pintura de água ciente de traçado com proteção de barragens e preenchimento de bordas na linha costeira.
- **Props** — objetos decorativos posicionáveis que entrelaçam os vértices que ocupam para permanecerem sincronizados.
- **Névoa de guerra** — névoa baseada em tile e ciente de penhascos usando um passe de tela cheia URP RenderGraph (sem física ou raycasts).
- **Apenas editor** — assa tudo em malhas; nenhum código de runtime no build.

---

## Requisitos

- **Unity 6 (6000.x)** — verificado contra `6000.5.1f1`.
- **URP** (Universal Render Pipeline) — necessário para o shader de terreno e para a render feature de névoa de guerra.
- Recursos de linguagem C# 9.

---

## Instalação

Copie a pasta `TileTerrainSystem` (tudo em [`Unity/Assets/TileTerrainSystem`](../../Unity/Assets/TileTerrainSystem)) para a pasta `Assets/` do seu projeto. O Unity importa os arquivos `.meta`, então as referências de assets permanecem intactas.

> Os arquivos `Data/TileTerrainGridData.asset` e `Data/*.asset` incluídos são dados de exemplo — você pode usá-los para começar ou criar os seus próprios pelo inspector.

---

## Início Rápido

1. Anexe o script `TileTerrain` a um `GameObject`.
2. Clique em **Create New Grid Data** no inspector.
3. Atribua os assets necessários:
   - **Terrain Material** — shader lit compatível com URP
   - **Water Material** — shader de água translúcida
   - **Texture Palette** — `TileTerrainPalette` ScriptableObject
   - **Cliff Meshes** — modelos FBX para penhascos padrão, duplo e de transição
4. Use as ferramentas do inspector para esculpir.

### Tipos de Asset Necessários

| Propriedade | Tipo | Finalidade |
|----------|------|---------|
| `GridData` | `TileTerrainGridData` | Estado da grade em ScriptableObject |
| `TileMaterial` | `Material` | Shader da superfície do terreno |
| `Palette` | `TileTerrainPalette` | Registro de matriz de texturas |
| `CliffMeshFbx` | `GameObject` | FBX com sub-malhas de penhasco padrão |
| `CliffDoubleMeshFbx` | `GameObject` | FBX com sub-malhas de altura dupla |
| `CliffTransitionalMeshFbx` | `GameObject` | FBX com sub-malhas de transição |
| `WaterMaterial` | `Material` | Shader da superfície da água |

---

## Arquitetura

O sistema é dividido em três pilares principais:

### 1. Armazenamento de Dados — `TileTerrainGridData`

Um `ScriptableObject` persistente que contém todo o estado da grade. Dados por vértice incluem:
- Offsets de altura e posições base
- Máscaras de textura (over, mid, under)
- Níveis de penhasco (`CliffByte`)
- Estado da água (`IsWater`, `WaterLevel`)

### 2. Renderização — `TileTerrain`

O `MonoBehaviour` principal. Divide a grade em chunks para batching de draw calls e culling de oclusão. Ele:
- Gera malhas de terreno com altura + offset de penhasco
- Instancia geometria de penhasco via seleção de malha por bitmask
- Desenha superfícies de água dinâmicas com preenchimento de triângulos de borda na linha costeira

Usa um shader HLSL personalizado compatível com URP.

### 3. Interface de Editor — `TileTerrainEditor`

Um inspector personalizado que fornece quatro modos de manipulação baseados em pincel com:
- Consultas de pincel com índice espacial (sem varreduras O(n) de vértices)
- Propagação BFS (Breadth-First Search) para suavização de penhascos
- Reforço de segurança na linha costeira
- UI persistida via `SessionState` entre recargas do inspector

---

## Sistema de Penhascos

### Três Tilesets

| Tileset | Finalidade | Condição de Disparo | Índices |
|---------|---------|-------------------|---------|
| **Padrão** (`cliff_mesh.fbx`) | Penhascos de um passo | Bordas de penhasco normais | 0–15 |
| **Duplo** (`cliff_double_mesh.fbx`) | Penhascos de dois passos | Vértices cobrem ≥2 níveis (ex.: 0→2) | 0–15 |
| **Transição** (`cliff_transitional_mesh.fbx`) | Transições de três níveis | 3 níveis de piso únicos (n, n+1, n+2) | 0–19 |

### Prioridade de Renderização

Ao construir chunks de malha, o sistema verifica nesta ordem:
1. **Transição** — se houver 3 níveis de piso únicos no nível atual
2. **Duplo** — se o nível+1 tiver cobertura de penhasco
3. **Padrão** — fallback de penhasco de um passo

### Sistema de Paridade

O pincel de penhasco usa paridade para mudanças naturais de elevação:

| Nível do Piso | Subir Adiciona | Descer Remove |
|:-----------:|:-------:|:------------:|
| Par (0, 2, 4…) | +2 | –1 |
| Ímpar (1, 3, 5…) | +1 | –2 |

Isso garante:
- Pisos pares disparam malhas de altura dupla ao empilhar
- Pisos ímpares criam transições adequadas entre níveis

---

## Modos de Ferramenta

### 1. Ferramenta de Altura
Escultura orgânica dentro de um único nível de penhasco.
- **Sub-ferramentas**: Elevar (Raise), Abaixar (Lower), Alvo (Target), Suavizar (Smooth), Ruído (Noise)
- **Segurança**: Respeita limites de água via verificação `IsBoundary`
- **Alcance**: –2 a +2 unidades

### 2. Ferramenta de Textura
Mistura de texturas multicamadas baseada em prioridade.
- **Sub-ferramentas**: Pintar (Paint), Borrar (Smudge), Apagar (Erase)
- **Sistema de prioridade**: índice menor da paleta = prioridade maior (renderiza por cima)
- **Pilha de três slots por vértice**: Over → Mid → Under

### 3. Ferramenta de Penhasco
Mudanças discretas de elevação via modificação de `CliffByte`.
- **Sub-ferramentas**: Subir (Up), Descer (Down), Alvo (Target), Borrar (Smudge), Apagar (Erase)
- **Propagação BFS**: elevação em cascata pelos vizinhos (diferença máxima de ±2)
- **Segurança**: `IsSafeToCarve` impede romper barragens que seguram água

### 4. Ferramenta de Água
Pintura de água ciente de traçado com proteção de barragens.
- Captura o nível do piso → define como `WaterLevel`
- Marca o vértice como `IsWater`
- Abaixa o piso em 1 unidade
- Propaga o estado de água via BFS

---

## Estrutura do Repositório

```
TileTerrain/
├── Unity/Assets/TileTerrainSystem/   # Raiz do pacote Unity (copie para Assets/)
│   ├── Scripts/                      # Dados de runtime + baking (restrito ao editor)
│   ├── Editor/                       # Inspector personalizado e modos de ferramenta
│   ├── Shaders/                      # Shaders URP de terreno + névoa de guerra
│   ├── Textures/                     # Texturas de exemplo
│   ├── Models/                       # Fontes de penhascos FBX/Blender
│   ├── Materials/                    # Instâncias de materiais de terreno/água
│   ├── Data/                         # Assets de grade e paleta de exemplo
│   ├── Icons/                        # Ícones de ScriptableObject
│   └── Documentation/                # Documentação do sistema (veja abaixo)
├── LICENSE.md                        # MIT (código, shaders, docs)
├── LICENSE.assets.md                 # CC BY 4.0 (assets)
├── CONTRIBUTING.md
├── CODE_OF_CONDUCT.md
├── docs/pt-BR/                       # Traduções português (Brasil)
│   ├── README.md
│   ├── CONTRIBUTING.md
│   ├── CODE_OF_CONDUCT.md
│   ├── LICENSE.md
│   └── LICENSE.assets.md
└── README.md
```

---

## Sistemas Avançados

### Propagação BFS

As ferramentas de penhasco e água usam uma fila de Breadth-First Search para aplicar mudanças de elevação em cascata pela grade. A propagação respeita:
- **Passo máximo de 2** entre vértices adjacentes (evita rasgos na face do penhasco)
- **Regras de paridade** para transições naturais de elevação
- **Segurança da água** — `IsSafeToCarve` impede romper paredes de barragens

### Detecção de Padrão de Transição

Detecta quando um quad tem 3 níveis de piso únicos e seleciona a malha de transição adequada. Cinco categorias de distribuição cobrem todos os 36 padrões válidos de combinações de altura `(0, 1, 2)`.

### Proteção de Borda e Margem

| Verificação | Finalidade |
|-------|---------|
| `TouchesWater` | Detecta terra seca adjacente à água (verificação de barragem) |
| `IsCliffEdge` | Identifica quedas estruturais entre níveis de penhasco |
| `IsSafeToCarve` | Impede escavar através de penhascos que seguram água |
| `IsBoundary` | Detecta interfaces água/terra para segurança de altura |

### Pilha de Prioridade de Texturas

Cada vértice mantém uma pilha de prioridade de 3 slots:
- **Over** — prioridade mais alta, renderizada por cima
- **Mid** — camada do meio, visível onde a máscara do Over não é sólida (máscara ≠ 15)
- **Under** — camada base, visível somente quando Over e Mid têm lacunas

Quando uma nova textura é pintada:
1. Coleta texturas existentes + textura recebida (máximo 4 candidatas)
2. Ordena por prioridade (índice menor = prioridade maior)
3. As 3 primeiras texturas únicas preenchem Over → Mid → Under
4. Se uma textura já ocupa um slot, ela não é substituída (idempotente)

### Névoa de Guerra

Um sistema de névoa de guerra baseado em tile e ciente de penhascos usando um passe de tela cheia URP RenderGraph. Rastreia estados por célula de **visível / explorado / oculto** com pintura suave baseada em distância.

**Recursos principais**:
- Máscara RGBA8 por célula com valores contínuos em [0, 1] para fade suave de entrada/saída.
- LOS ciente de penhascos via DDA 2D (Amanatides–Woo) — sem física ou raycasts.
- Taxa de subida baseada em distância: células logo abaixo do revelador prendem instantaneamente (taxa = 1), células na borda interpolarizam na taxa do inspector.
- Mistura de 3 estados (Oculto / Explorado / Visível) com cores de névoa e explorado ajustáveis.
- Reveladores baseados em componentes — anexe `FogOfWarRevealer` a qualquer GameObject.

**Componentes**:
- `FogOfWarManager` (singleton, é dono da máscara, esvazia o registro de reveladores a cada `LateUpdate`)
- `FogOfWarRevealer` (por GameObject, auto-registrado)
- `FogOfWarRenderFeature` (URP `ScriptableRendererFeature`, injeta após os transparentes)
- Shader `TileTerrain/FogOfWar` (amostra máscara + profundidade da cena, mistura névoa sobre a cena)

Para referência completa (todos os campos, detalhes de algoritmo, números de performance, exemplos, solução de problemas), veja **[`fog-of-war.pt-BR.md`](../../Unity/Assets/TileTerrainSystem/Documentation/fog-of-war.pt-BR.md)**.

---

## Documentação

A documentação completa do sistema fica em [`Unity/Assets/TileTerrainSystem/Documentation/`](../../Unity/Assets/TileTerrainSystem/Documentation/):

- [`README.pt-BR.md`](../../Unity/Assets/TileTerrainSystem/Documentation/README.pt-BR.md) — visão geral detalhada do sistema
- [`matrix-solution.pt-BR.md`](../../Unity/Assets/TileTerrainSystem/Documentation/matrix-solution.pt-BR.md) — mapeamento de índice de textura para bitmask de autotile
- [`water-solution.pt-BR.md`](../../Unity/Assets/TileTerrainSystem/Documentation/water-solution.pt-BR.md) — especificação do algoritmo da ferramenta de água
- [`water-border-protection.pt-BR.md`](../../Unity/Assets/TileTerrainSystem/Documentation/water-border-protection.pt-BR.md) — regras de segurança dos limites água/terra
- [`transition-matrix.pt-BR.md`](../../Unity/Assets/TileTerrainSystem/Documentation/transition-matrix.pt-BR.md) — tabela de padrões de penhasco de transição
- [`fog-of-war.pt-BR.md`](../../Unity/Assets/TileTerrainSystem/Documentation/fog-of-war.pt-BR.md) — referência do sistema de névoa de guerra

---

## Licença

- **Código, shaders e documentação** são licenciados sob a [Licença MIT](LICENSE.md).
- **Assets** (texturas, ícones, modelos, materiais, dados de exemplo) são licenciados sob [CC BY 4.0](LICENSE.assets.md).

---

## Apoiar

Este projeto é gratuito e open source. Se você achou útil e quer agradecer, uma
doação é bem-vinda, mas totalmente opcional:

- **PayPal** (internacional): [Doar](https://www.paypal.com/donate/?business=FT8LTCL8Z86C4&no_recurring=0&currency_code=BRL)
- **Mercado Pago** (Brasil — PIX/cartão): [Doar](https://link.mercadopago.com.br/moolucio)

Doações são um apoio voluntário e não conferem benefícios, prioridade ou créditos.

---

## Reconhecimento

Este projeto se inspira fortemente no **World Editor do Warcraft III**, da
Blizzard Entertainment. O Tile Terrain System é uma implementação original e
totalmente independente. Não tem afiliação com a Blizzard Entertainment e
não requer nem implica qualquer endosso dela.
