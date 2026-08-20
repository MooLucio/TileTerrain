# Névoa de Guerra (Fog of War)

> **[English](fog-of-war.md) | Português (Brasil)**

Um sistema de névoa de guerra baseado em tile e ciente de penhascos para o framework Tile Terrain. Projetado para Unity 6 + URP 17 usando a API RenderGraph.

---

## Índice

- [Visão Geral](#visão-geral)
- [Arquitetura](#arquitetura)
- [Canais da Máscara](#canais-da-máscara)
- [Configuração](#configuração)
- [Referência do `FogOfWarManager`](#referência-do-fogofwarmanager)
- [Referência do `FogOfWarRevealer`](#referência-do-fogofwarrevealer)
- [Referência do `FogOfWarRenderFeature`](#referência-do-fogofwarrenderfeature)
- [Referência do Shader](#referência-do-shader)
- [Linha de Visão (LOS)](#linha-de-visão-los)
- [Suavização e Pintura](#suavização-e-pintura)
- [Subida Baseada em Distância](#subida-baseada-em-distância)
- [API Pública](#api-pública)
- [Performance](#performance)
- [Limitações Conhecidas](#limitações-conhecidas)
- [Exemplos](#exemplos)
- [Solução de Problemas](#solução-de-problemas)

---

## Visão Geral

O sistema mascara a visão do mundo através de uma **textura RGBA8 por célula** que rastreia:

| Canal | Significado |
|---|---|
| **R** | Atualmente visível — interpola em direção a 1 dentro dos reveladores, em direção a 0 fora |
| **G** | Explorado — interpola em direção a 1 dentro dos reveladores; nunca decai sozinho (Persistent) ou espelha R (Flashlight) |
| **B/A** | Não usados |

Um passe de renderização URP de tela cheia amostra esta máscara com filtragem bilinear, reconstrói a posição mundial a partir da profundidade da cena e mistura a névoa sobre a cor da cena. O resultado é uma borda de névoa suave e animada que acompanha a visão do jogador em tempo real.

**Recursos principais:**
- LOS ciente de penhascos via DDA 2D (Amanatides–Woo) contra as alturas por vértice da grade — sem física ou raycasts.
- Visibilidade de 3 estados: **Oculto → Explorado → Visível** com valores contínuos em [0,1] para transições suaves.
- Modos por revelador **Persistent** (a "memória" estilo RTS permanece) ou **Flashlight** (G espelha R).
- **Taxa de subida baseada em distância** — células logo abaixo do revelador prendem a 1, células na borda interpolarizam gradualmente.
- **Suavização** tanto na subida quanto na queda, com fatores de interpolação por frame.
- Reveladores baseados em componentes — anexe `FogOfWarRevealer` a qualquer GameObject; ele se auto-registra.
- Funciona tanto no modo edição (gizmos) quanto no modo play.

---

## Arquitetura

```
┌──────────────────────────────────────────────────────────────┐
│ Cena                                                         │
│                                                              │
│  ┌──────────────────────┐                                    │
│  │ FogOfWarManager      │  singleton, dono da textura _mask  │
│  │ (no GO FogOfWar)     │  esvazia o registro estático de    │
│  └─────────┬────────────┘  reveladores a cada LateUpdate     │
│            │                                                 │
│            │  lê                                             │
│            ▼                                                 │
│  ┌──────────────────────┐                                    │
│  │ FogOfWarRevealer #1  │  registra-se em OnEnable           │
│  │ (na câmera principal)│  → raio, altura dos olhos,         │
│  └──────────────────────┘    oclusão, persistência           │
│                                                              │
│  ┌──────────────────────┐                                    │
│  │ FogOfWarRevealer #2  │  qualquer outro GO (NPC, tocha...) │
│  │ (no inimigo)         │                                    │
│  └──────────────────────┘                                    │
└──────────────────────────────────────────────────────────────┘
              │
              │  CPU grava textura de máscara RGBA32
              ▼
┌──────────────────────────────────────────────────────────────┐
│ URP Render Pipeline                                          │
│                                                              │
│  ┌──────────────────────────────────────┐                    │
│  │ FogOfWarRenderFeature                │  ScriptableRendererFeature
│  │ (adicionado a URP-HighFidelity-      │                    │
│  │  Renderer)                           │                    │
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
│  │ FogOfWar.shader  (TileTerrain/...)     │  amostra _MaskTex
│  │                                       │  reconstrói posição mundial
│  │                                       │  mistura névoa sobre a cena
│  └──────────────────────────────────────┘                    │
└──────────────────────────────────────────────────────────────┘
```

**Estrutura de arquivos:**

| Arquivo | Função |
|---|---|
| `Scripts/FogOfWarManager.cs` | Singleton, ciclo de vida da máscara, LOS, suavização, gizmos |
| `Scripts/FogOfWarRevealer.cs` | Fonte de visão por GameObject; auto-registra |
| `Scripts/FogOfWarRenderFeature.cs` | URP `ScriptableRendererFeature` + passe RenderGraph |
| `Shaders/FogOfWar.shader` | Mistura de tela cheia: cena + máscara → saída com névoa |
| `Materials/FogOfWar.mat` | Material que envolve o shader, referenciado pela feature |
| `Materials/FogOfWar.mat.meta` | (gerado automaticamente) |

---

## Canais da Máscara

A máscara é uma `Texture2D` `RGBA32` de tamanho `(larguraDaGrade × upscale) × (alturaDaGrade × upscale)`, com `wrapMode = Clamp` e `filterMode = Bilinear`. Cada célula da grade ocupa um bloco de texels de `upscale × upscale`. A filtragem bilinear nos limites das células produz bordas suaves de graça.

```
Célula (0,0)  Célula (1,0)  Célula (2,0)
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
  Célula (0,1)  Célula (1,1)  Célula (2,1)
```

Os canais são escritos e lidos como floats contínuos em `[0, 1]`:

| Canal | Escritor | Leitor | Comportamento |
|---|---|---|---|
| **R** | Reveladores (interpola em direção a 1), passe de queda (interpola em direção a 0) | Shader: `visible` | Subida + decaimento suaves |
| **G** | Reveladores (interpola em direção a 1 para Persistent; snapshot de R para Flashlight) | Shader: `explored` | Memória persistente / espelho de lanterna |
| **B** | Não usado | Não usado | — |
| **A** | Não usado | Não usado | — |

---

## Configuração

O sistema já está conectado em `TestScene.unity` e no `URP-HighFidelity-Renderer.asset`. Para readicionar do zero:

1. **Crie um GameObject `FogOfWar`** na cena.
2. **Adicione o componente `FogOfWarManager`**. Atribua o asset `TileTerrainGridData`.
3. **Crie um Material** que use o shader `TileTerrain/FogOfWar`, nomeie-o como `FogOfWar`.
4. **Adicione `FogOfWarRenderFeature`** ao seu asset de renderer URP (ex.: `URP-HighFidelity-Renderer`):
   - Na janela Project, selecione o asset de renderer URP.
   - **Add Renderer Feature → Fog Of War Render Feature**.
   - Arraste o material `FogOfWar` para o slot **Fog Material**.
5. **Anexe `FogOfWarRevealer`** a qualquer GameObject que deva revelar névoa (normalmente a Main Camera). Ele se auto-registra em `OnEnable`.
6. **Posicione a câmera** para que ela olhe para a grade (o gizmo só desenha na Scene view; a Game view precisa estar dentro da grade para a névoa ficar visível).

É só isso — rode a cena e a névoa começará a pintar.

---

## Referência do `FogOfWarManager`

Adicione isto a um GameObject dedicado na cena. É um singleton; a segunda instância na cena é destruída com um aviso.

### Grade
| Campo | Padrão | Descrição |
|---|---|---|
| `GridData` | _(obrigatório)_ | O `TileTerrainGridData` que a névoa cobre. Resolução da máscara = `grade × maskUpscale`. |

### Aparência da Névoa
| Campo | Padrão | Descrição |
|---|---|---|
| `fogColor` | `(0.02, 0.02, 0.04, 1)` | Cor para células **Ocultas**. Alpha é ignorado (assumido como 1). |
| `exploredColor` | `(0.35, 0.35, 0.4, 0.55)` | Tinta para células **Exploradas** (mas não visíveis no momento). `RGB` é a cena tingida, `A` é a força da visibilidade (0 = invisível, 1 = completo). |
| `OutsideGridFog` | `1` | `0` = renderiza a cena normalmente fora dos limites da grade. `1` = aplica névoa em tudo fora. |

### LOS
| Campo | Padrão | Descrição |
|---|---|---|
| `KneeOffset` | `0.25` | Joelho vertical (unidades do mundo) adicionado à altura dos olhos do LOS para evitar auto-oclusão de 1 célula ao ficar em uma rampa. |

### Performance
| Campo | Padrão | Descrição |
|---|---|---|
| `UpdateInterval` | `0` | Segundos mínimos entre recomputes da máscara. `0` = a cada `LateUpdate`. Use `0.016` (ou similar) para limitar. |
| `maskUpscale` | `4` | Multiplicador de resolução da máscara. `1` = 1 px/célula (blocado). `4` = 16 px/célula (recomendado). `8` = 64 px/célula (muito suave, mais CPU). |
| `MaskBlur` | `0.025` | Raio de desfoque suave em UV normalizado da grade. `0` = apenas bilinear nítida. `0.02` = suave. `0.05` = gradiente largo. No upscale 4: `0.01` ≈ 1 célula, `0.02` ≈ 1,5 células. |

### Suavização
| Campo | Padrão | Descrição |
|---|---|---|
| `VisibleRiseRate` | `0.35` | Fator de interpolação por frame para o canal **R** em direção a 1, **na borda de um revelador**. A taxa real por célula é baseada em distância (veja [Subida Baseada em Distância](#subida-baseada-em-distância)). `0` = fixar, `1` = interpolar totalmente em um frame. |
| `VisibleFallRate` | `0.10` | Fator de interpolação por frame para o canal **R** em direção a 0 quando não revelado. `0.10` = ~20 frames para desaparecer (~0,33 s @60fps). `0.30` = ~5 frames. |
| `ExploredRiseRate` | `0.10` | Fator de interpolação por frame para o canal **G** em direção a 1, **na borda de um revelador Persistent**. Deve ser mais lento que `VisibleRiseRate` para que áreas "lembradas" se construam gradualmente. Ignorado por reveladores Flashlight. |

### Depuração
| Campo | Padrão | Descrição |
|---|---|---|
| `debugDrawMask` | `true` | Desenha a máscara de névoa na Scene view (gizmos). Verde = visível, amarelo = explorado, vermelho = parcial. |
| `debugDrawHeight` | `5` | Offset vertical para o quad da máscara de depuração (unidades do mundo acima da origem da grade). |
| `debugDrawScale` | `1` | Escala para a máscara de depuração (1 = corresponde à grade em unidades do mundo). |

### Eventos
| Membro | Descrição |
|---|---|
| `event System.Action FogUpdated` | Dispara após cada `UpdateMask`. Assine para reações personalizadas (áudio, alertas de IA, etc.). |
| `Texture MaskTexture` | Handle somente leitura para a textura da máscara ao vivo. Vincule em outro lugar se necessário. |

---

## Referência do `FogOfWarRevealer`

Anexe a qualquer GameObject que deva revelar névoa. Auto-registra em `OnEnable`, auto-desregistra em `OnDisable`. Coloque em:
- A Main Camera (para a visão do jogador)
- IA inimiga (para que o jogador possa "ver" pelos olhos de um inimigo por um momento)
- Tochas posicionáveis (modo Flashlight)
- Uma luminária estática (modo Persistent para um posto de guarda)

### Revelar
| Campo | Padrão | Descrição |
|---|---|---|
| `Radius` | `8` | Raio de revelação em **células da grade**. O revelador afeta cada célula dentro deste raio (limitado ao LOS se `Occluded` estiver ativo). |
| `EyeHeight` | `1.8` | Offset vertical (unidades do mundo) acima do pivô do GameObject para o olho do LOS. A célula sob o pivô é amostrada nesta altura, não no próprio pivô. |

### Linha de Visão
| Campo | Padrão | Descrição |
|---|---|---|
| `Occluded` | `true` | Executa a verificação de LOS ciente de penhascos. Quando `false`, cada célula dentro de `Radius` é revelada (mais barato, mas sem oclusão de penhascos). |
| `Persistence` | `Persistent` | `Persistent` = células exploradas permanecem exploradas para sempre (limpas apenas por `HideAll`). `Flashlight` = explorado espelha a visibilidade atual (G = R). |

### Depuração
| Campo | Padrão | Descrição |
|---|---|---|
| `debugDraw` | `false` | Desenha uma esfera de arame na posição do olho quando o GameObject está selecionado. |

### Runtime
| Membro | Descrição |
|---|---|
| `Vector2Int GridCell` | A célula que o revelador ocupa atualmente (definida a cada `LateUpdate`). Somente leitura externamente. |
| `float EyeHeight` | (Serializado, veja a tabela do Inspector) Offset vertical do olho do LOS. O próprio olho é calculado internamente, não exposto. |

---

## Referência do `FogOfWarRenderFeature`

Uma `ScriptableRendererFeature` URP. Adicione-a ao seu asset de renderer URP; ela injeta um passe de tela cheia em `RenderPassEvent.AfterRenderingTransparents`.

### Inspector
| Campo | Padrão | Descrição |
|---|---|---|
| `FogMaterial` | _(obrigatório)_ | Material usando o shader `TileTerrain/FogOfWar`. |
| `InjectionPoint` | `AfterRenderingTransparents` | Quando no frame URP o passe roda. Mais cedo = névoa abaixo dos transparentes. Mais tarde = névoa acima. O padrão mantém a aparência correta. |

### Internals do passe
- **Aloca** uma textura de cor intermediária do tamanho do alvo da câmera, `msaaSamples = 1` (forçado), `depthBufferBits = 0`.
- **Passe 1**: blita `src → tmp` com o material de névoa vinculado. O material amostra `_MaskTex` (enviado pela feature a partir de `FogOfWarManager.MaskTexture`) e a profundidade da cena.
- **Passe 2**: blita `tmp → src` (sem material) para que o anexo de cor da câmera receba o resultado com névoa. Usa `AddBlitPass` (não `AddCopyPass`) para lidar com incompatibilidades de MSAA via helpers de blit do RenderGraph.
- **Uniforms enviados por frame**: `_MaskTex`, `_FogColor`, `_ExploredColor`, `_OutsideGridFog`, `_FogBlur`, `_GridOffset`, `_GridWorldSize`.

---

## Referência do Shader

Caminho do shader: `TileTerrain/FogOfWar` (`Shaders/FogOfWar.shader`).

### Propriedades
| Propriedade | Tipo | Padrão | Descrição |
|---|---|---|---|
| `_MaskTex` | 2D | `black` | A máscara de névoa de `FogOfWarManager`. Amostrada com um desfoque circular de 13 taps. |
| `_FogColor` | Color | `(0.02, 0.02, 0.04, 1)` | Cor de célula oculta. |
| `_ExploredColor` | Color | `(0.35, 0.35, 0.4, 0.55)` | Tinta de célula explorada. `A` = força da visibilidade. |
| `_OutsideGridFog` | Range(0,1) | `1` | Quanto aplicar névoa a pixels fora dos limites da grade. |
| `_FogBlur` | Range(0,0.1) | `0.025` | Raio em espaço UV para o desfoque circular de 13 taps. |
| `_GridOffset` | Vector | `(0,0,0,0)` | Origem mundial XZ da célula (0,0). |
| `_GridWorldSize` | Vector | `(1,1,0,0)` | Tamanho total da grade em unidades do mundo. |

### Algoritmo
1. Amostra a profundidade da cena, reconstrói a posição mundial via `ComputeWorldSpacePosition`.
2. Converte o XZ mundial → UV da grade usando `_GridOffset` e `_GridWorldSize`.
3. Amostra `_MaskTex` com `SampleMaskBlurred(uv, _FogBlur)` — 1 centro + 12 taps em um círculo, média. Dá borda de névoa suave.
4. Combina estados: `vis = max(visible, explored * _ExploredColor.a)`.
5. Interpola a cor da cena em direção a `_FogColor` por `1 - vis`.
6. Tingir pixels "explorados mas não visíveis" em direção a `_ExploredColor.rgb`.
7. Fora da grade: opcionalmente aplicar `_OutsideGridFog` como névoa de tela cheia.

---

## Linha de Visão (LOS)

A verificação de LOS é a parte mais sensível a performance do loop de CPU. Roda **uma vez por célula, por revelador, por frame**.

### Algoritmo: DDA 2D (Amanatides–Woo)
Uma travessia de grade 2D que visita exatamente as células pelas quais uma linha de `from` até `to` passa, na ordem correta. A travessia está no parâmetro de raio normalizado `t ∈ [0, 1]`, onde a altura da linha é `lerp(eyeY, targetY, t) + kneeOffset`.

Para cada célula ao longo do raio (exceto a origem):
1. Consulta o `max(4 alturas de vértices de canto)` da célula — este é o **bloqueador** (topo do penhasco).
2. Compara com a altura da linha neste `t`.
3. Se `cellMax > lineY`, a célula oclui a visão → retorna `false`.

Se o raio sair da grade antes de ser bloqueado, retorna `true` (a célula é visível).

### Tratamento de penhascos
- A **célula alvo** é amostrada na sua **altura central** (média dos 4 cantos) — esta é a altura na qual queremos ver *para dentro*.
- As **células intermediárias** ao longo do raio usam a sua **altura máxima de canto** como bloqueador — este é o topo do penhasco.
- Isso significa que uma unidade em uma área baixa pode ver um planalto alto, mas uma unidade em um planalto alto olhando para uma área baixa é bloqueada pelo penhasco do planalto.

### `KneeOffset`
Um pequeno offset vertical adicionado à altura dos olhos do LOS para evitar o artefato comum de "auto-oclusão de 1 célula", em que uma unidade em uma rampa oclui imediatamente a célula ao lado porque a altura do canto é ligeiramente maior que o olho. `0.25` geralmente é o correto; aumente se você vir anéis pretos ao redor de reveladores em terrenos inclinados.

### Características de performance
| Grade | Raio do revelador | Células verificadas | Custo aproximado (μs) |
|---|---|---|---|
| 64×64 | 8 | ~200 | ~30 |
| 64×64 | 20 | ~1.250 | ~190 |
| 256×256 | 12 | ~450 | ~70 |

---

## Suavização e Pintura

Os canais da máscara são escritos como valores contínuos em `[0, 1]`, não binários 0/1. Isso permite que o processo de pintura anime suavemente.

### Atualização por frame (3 fases)

#### Fase 1 — Passe de queda
Para **cada** pixel, `R *= (1 - visibleFallRate)`. Este é o fade-out visível. Pixels abaixo de `1e-4` fixam em exatamente 0 (para não permanecerem como valores fantasma para sempre). G **não** é tocado no passe de queda para reveladores Persistent.

#### Fase 2 — Passe de revelação (por revelador)
Para cada célula visível (dentro de `Radius` e passando no LOS), o bloco `upscale × upscale` da célula é atualizado:

```csharp
// R: interpola em direção a 1 na taxa baseada em distância desta célula (veja abaixo)
if (c.r < 1f) c.r += (1f - c.r) * vRise;

// G:
//   Persistent: interpola em direção a 1 na taxa da célula
//   Flashlight: snapshot de R (G NÃO acompanha o decaimento de R neste frame)
if (persistent) {
    if (c.g < 1f) c.g += (1f - c.g) * eRise;
} else {
    c.g = c.r;
}
```

#### Fase 3 — Upload para a GPU
`Texture2D.SetPixels` + `Apply(false)` para enviar a máscara à GPU.

### Fixação da primeira atualização
O primeiro `UpdateMask` após a (re)alocação da máscara usa `vRise = eRise = 1`, para que a primeira revelação seja instantânea (sem fade-in). Após esse frame, as taxas do inspector assumem. Isso evita um efeito de "névoa aparecendo lentamente" no início do jogo.

A fixação é re-armada sempre que `EnsureMask` reconstrói a máscara (redimensionamento da grade, mudança de upscale).

---

## Subida Baseada em Distância

Dentro do footprint de um único revelador, a taxa de subida é **baseada em distância**:

```
t = sqrt(dx² + dy²) / radius   // 0 no centro, 1 na borda
vRise(cell) = lerp(1, visibleRiseRate, t)
eRise(cell) = lerp(1, exploredRiseRate, t)
```

| Posição da célula | t | vRise (padrão) | Efeito |
|---|---|---|---|
| Centro (sob o revelador) | 0 | `1.0` | Instantâneo — a célula logo abaixo do revelador está sempre na visibilidade total |
| Meio | 0.5 | `0.675` | Interpola rápido |
| Borda | 1.0 | `0.35` (= público) | Interpola lento |

**Por que esse padrão?** A célula logo abaixo da câmera deve ser exatamente o que o jogador vê — sem lag. A periferia pode ter lag porque a visão periférica não precisa de rastreamento pixel-perfeito; uma borda suave "de rastro" parece natural.

**Personalização**:
- Defina `visibleRiseRate = 1` para revelação instantânea em todo o disco (sem suavização).
- Defina `visibleRiseRate = 0.7` para uma sensação mais rápida.
- Defina `visibleRiseRate = 0.1` para uma névoa "cinematográfica" com muito rastro.

---

## API Pública

### `FogOfWarManager`

```csharp
public static FogOfWarManager Instance { get; }                  // singleton
public static IReadOnlyCollection<FogOfWarRevealer> Revealers;  // reveladores atuais
public Texture MaskTexture { get; }                              // textura da máscara ao vivo
public event System.Action FogUpdated;                          // dispara após cada atualização

public void HideAll();   // limpa R e G, dispara FogUpdated
public void RevealAll(); // define R=1, G=1 em todos os lugares, dispara FogUpdated
```

### `FogOfWarRevealer`

```csharp
[NonSerialized] public Vector2Int GridCell;   // célula em que o revelador está (definida a cada frame)
```

O revelador se auto-registra em `OnEnable` e se auto-desregistra em `OnDisable`. Nenhuma API manual necessária.

### `FogOfWarRenderFeature`

Sem API pública. Configure `FogMaterial` e `InjectionPoint` no inspector.

### Uso típico a partir do código do jogo

```csharp
// Reaja quando a máscara atualizar
FogOfWarManager.Instance.FogUpdated += () => {
    // ex.: atualizar minimapa, disparar alertas de IA
};

// Force uma limpeza total
FogOfWarManager.Instance.HideAll();

// Revele o mapa inteiro (debug, cutscene de introdução)
FogOfWarManager.Instance.RevealAll();
```

---

## Performance

### CPU
| Operação | Custo (grade 64×64, upscale 4) |
|---|---|
| Passe de queda (decaimento de R para 65.536 texels) | ~0,3 ms |
| Passe de revelação (por revelador, ~200 células) | ~30 μs |
| Passe de revelação (8 reveladores, ~200 células cada) | ~0,25 ms |
| Upload para a GPU (`SetPixels` + `Apply`) | ~0,5 ms |
| **Total por frame** | **~1 ms** |

Limitar com `UpdateInterval` (ex.: `0.033` para 30 Hz) corta o custo aproximadamente pela metade.

### GPU
- **Amostragem da máscara**: 13 taps por fragmento para o desfoque = ~27M amostras/frame em 1920×1080. ~0,3 ms em uma GPU de desktop.
- **Leitura da profundidade da cena**: 1 tap. De graça com a textura de profundidade URP.
- **Reconstrução da posição mundial**: apenas ALU, sem leituras extras.
- **Saída**: 1 escrita em RT por fragmento. De graça.

Custo total de GPU: **< 0,5 ms** em hardware típico.

### Memória
- Textura da máscara: `(64 × 4) × (64 × 4) × 4 bytes = 256 × 256 × 4 = 256 KB`. Irrisório.
- Buffer de pixels na CPU: o mesmo = 256 KB. Irrisório.

---

## Limitações Conhecidas

1. **O `G` da lanterna não acompanha `R` por frame.** No modo Flashlight, `G` é definido para `R` no momento em que o revelador escreve a célula. Se `R` então decai (nenhum revelador tocando a célula), `G` **não** decai junto. Para um espelho verdadeiro `G = R`, o manager precisaria de um sinalizador de modo por pixel e um passe de sincronização pós-decaimento. Este é um compromisso conhecido; o caso comum é "todos os reveladores estão no mesmo modo".

2. **Reveladores Persistent + Flashlight misturados perdem a memória persistente.** Se você tem ambos os tipos e uma célula é tocada por uma lanterna, o snapshot `G = R` pode sobrescrever um `G = 1` anteriormente persistente. Use um modo global único por enquanto.

3. **O LOS é 2D.** A verificação de penhasco olha o **máximo das 4 alturas de canto** da célula bloqueadora. Isso é conservador — sempre assume que a célula está totalmente ocluída em seu canto mais alto. Uma abordagem mais precisa interpolaria a altura ao longo das bordas da célula, mas a abordagem de canto-máximo é rápida e visualmente correta para terrenos "blocados".

4. **A máscara fica no lado da CPU e é enviada a cada frame.** Para grades muito grandes (512×512) ou 60+ reveladores, considere:
   - Aumentar `UpdateInterval` para limitar a 30 Hz.
   - Usar paralelização por `job-system` para o bloco por célula (TODO).
   - Usar um compute shader para atualizar a máscara na GPU (refatoração grande).

5. **A névoa fora da grade é binária.** É controlada por um único float `OutsideGridFog`. Não há névoa de guerra por região para áreas além da grade.

---

## Exemplos

### Visão de jogador estilo RTS (Persistent)

```csharp
// Na Main Camera:
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

Coloque um único revelador desses na câmera do jogador. Eles verão um raio de 15 células que atualiza conforme se movem, e áreas já vistas permanecem visíveis de forma esmaecida para sempre.

### Lanterna de jogo de furtividade (Flashlight)

```csharp
// Em um GameObject de tocha:
var torch = gameObject.AddComponent<FogOfWarRevealer>();
torch.Radius = 8f;
torch.EyeHeight = 1f;
torch.Occluded = true;
torch.Persistence = FogRevealPersistence.Flashlight;
```

Quando a tocha está habilitada, as células em seu raio ficam visíveis. Quando desabilitada, elas desaparecem (após o próximo passe de queda, R decai; snapshots de G ficam obsoletos — veja a Limitação 1).

### Vários batedores do jogador

```csharp
// Em cada GameObject de batedor:
var scout = gameObject.AddComponent<FogOfWarRevealer>();
scout.Radius = 6f;
scout.EyeHeight = 1.5f;
scout.Occluded = true;
scout.Persistence = FogRevealPersistence.Persistent;
```

Anexe a qualquer número de unidades. A máscara é a **união** das células visíveis de todos os reveladores (qualquer célula dentro do raio de qualquer revelador é visível).

### Mapa totalmente coberto de névoa com revelação periódica (sala do boss)

```csharp
// No trigger da sala do boss:
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

### Ouvir mudanças de névoa (para IA / áudio)

```csharp
void Start() {
    FogOfWarManager.Instance.FogUpdated += OnFogChanged;
}

void OnFogChanged() {
    // Verifique se algum inimigo está agora no conjunto visível
    foreach (var enemy in enemies) {
        Vector2Int cell = WorldToCell(enemy.transform.position);
        var sample = SampleCell(cell);
        if (sample.r > 0.5f) enemy.OnSeenByPlayer();
    }
}
```

---

## Solução de Problemas

### "Não vejo névoa alguma"
- **A câmera olha para fora da grade.** O gizmo só desenha na Scene view; a Game view precisa estar dentro de `(-gw/2, -gh/2)` até `(gw/2, gh/2)`. Mova a câmera.
- **`outsideGridFog = 0`** com a câmera fora da grade. Defina para `1` para aplicar névoa em tudo fora, ou mova a câmera para dentro.
- **`GridData` é nulo** no `FogOfWarManager`. Atribua no inspector.
- **`FogMaterial` é nulo** no `FogOfWarRenderFeature`. Atribua o `FogOfWar.mat`.
- **`FogOfWarRenderFeature` não foi adicionada** ao seu asset de renderer URP. Adicione-a.

### "A névoa aparece mas é um binário 0/1 duro (sem borda suave)"
- **`maskBlur = 0`** no manager. Defina para `0.025` (ou maior) para uma borda suave.
- **`maskUpscale = 1`** no manager. O tamanho do bloco é 1 px; mesmo com desfoque, o passo de célula a célula é visível. Defina para `4` ou maior.
- **A câmera está longe** então a névoa ocupa uma pequena área da tela. Aproxime-se.

### "A Game view está totalmente preta / totalmente clara"
- **Totalmente preta**: a névoa está correta mas nenhuma cena está sendo renderizada atrás. Verifique se o renderer URP está configurado corretamente. O copy-back de `AddBlitPass` deve restaurar a cena; se não, a textura de origem pode estar inválida.
- **Totalmente clara**: a máscara de névoa está toda em 1 (tudo visível). Você pode ter chamado `RevealAll()` ou ter muitos reveladores.
- **Erro de MSAA**: a textura temporária é forçada a `msaaSamples = 1`; o `msaaSamples` da cor da câmera é preservado. Se você vir "MSAA samples from source and destination texture doesn't match", garanta que a render feature está usando `AddBlitPass` (não `AddCopyPass`) para o copy-back.

### "A névoa fica para trás da câmera"
- **`updateInterval > 0`** está limitando as atualizações da máscara. Defina para `0` para atualizações a cada frame.
- **`VisibleRiseRate` é muito baixo.** A célula sob a câmera está em `rate = 1` independentemente, então deveria ser instantânea. Se a célula está visivelmente atrasada, o pivô da câmera não está no centro da célula — verifique `EyeHeight` e o `position.y` do transform.

### "As células piscam nas bordas do revelador"
- **`MaskBlur` é muito baixo** para o upscale. Tente `maskBlur = 0.05`.
- **O revelador está se movendo rápido.** Considere suavizar a posição do revelador (interpolar o transform) ou usar um `VisibleRiseRate` alto para que a interpolação alcance.

### "O primeiro frame está totalmente coberto de névoa e depois aparece de repente"
- Isso é a **fixação da primeira atualização** funcionando como projetado. O primeiro `UpdateMask` usa `vRise = 1`, que (combinado com a interpolação baseada em distância) prende o disco inteiro à visibilidade total. Para desabilitar: limpe `_snapNextRise` para `false` antes da primeira atualização, ou defina o `VisibleRiseRate` do manager para `1` e aceite nenhuma suavização.

---

## Veja Também

- [`README.pt-BR.md`](README.pt-BR.md) — documentação principal do sistema Tile Terrain
- `TileTerrainGridData` — os dados da grade que a névoa cobre
- `Shaders/FogOfWar.shader` — código-fonte do shader
- Documentação do URP RenderGraph — para entender o passe de renderização
