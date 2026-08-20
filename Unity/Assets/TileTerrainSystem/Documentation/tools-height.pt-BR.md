# Ferramenta de Altura

> **[English](tools-height.md) | Portugues (Brasil)**

Escultura organica de heightmap dentro de um unico nivel de penhasco. Modifica offsets de altura dos vertices sem alterar niveis de piso do penhasco.

**Atalho de teclado:** `1`

---

## Sub-Ferramentas

| Modo | Descricao |
|------|-----------|
| **Elevar (Raise)** | Aumenta a altura em `forcaDoPincel * decaimento` |
| **Abaixar (Lower)** | Diminui a altura em `forcaDoPincel * decaimento` |
| **Alvo (Target)** | Interpola a altura em direcao a `alturaAlvo` |
| **Suavizar (Smooth)** | Interpola a altura em direcao a media dos vizinhos dentro do raio do pincel |
| **Ruido (Noise)** | Interpola a altura em direcao a um valor de Perlin Noise mapeado para [-2, 2] |

---

## Parametros

| Parametro | Intervalo | Padrao | Descricao |
|-----------|-----------|--------|-----------|
| **Raio do Pincel** | 0.1 - 20.0 | 2.0 | Tamanho do pincel em unidades da grade |
| **Forca do Pincel** | 0.0 - 5.0 | 0.2 | Intensidade da aplicacao do pincel |
| **Forma do Pincel** | Circulo / Quadrado | Circulo | Forma do decaimento do pincel |
| **Altura Alvo** | -2 a 2 | 1.0 | Altura alvo (apenas no modo Alvo) |

---

## Algoritmo

1. Calcula bounding box do pincel nas coordenadas de grade dos vertices.
2. Pre-computa cache de vizinhos (`_touchesWaterCache`, `_isBoundaryCache`) para a regiao afetada.
3. Constroi LUT de decaimento:
   - **Circulo**: `1 - sqrt(dx² + dz²) / raio`
   - **Quadrado**: `1 - max(|dx|, |dz|) / raio`
4. Armazena alturas antigas para todos os vertices afetados (incluindo membros do grupo entangled).
5. Aplica modificacao de altura por vertice:
   - **Elevar**: `altura + delta`, limitado a [-2, 2]
   - **Abaixar**: `altura - delta`, limitado a [-2, 2]
   - **Alvo**: `Interpolar(altura, alturaAlvo, inf)`
   - **Suavizar**: `Interpolar(altura, alturaMedia, Clamp01(forcaDoPincel * 0.1 * inf))`
   - **Ruido**: `Interpolar(altura, (PerlinNoise(x*0.5+100, z*0.5+100)*4)-2, delta)`
6. Protecao de agua: vertices marcados como agua ou tocando agua no limite sao ignorados.
7. Propagacao de entanglement: o delta de altura aplicado ao vertice representante e aplicado uniformemente a todos os membros do grupo.

---

## Regras de Seguranca

- Altura globalmente limitada a **[-2, 2]**.
- Vertices de agua **nunca** sao modificados pela ferramenta de altura.
- Vertices tocando agua no limite sao ignorados.
- Vertices entangled recebem o mesmo delta de altura que seu representante.

---

## Efeitos em Outros Sistemas

- Apos cada tracado, `PinPropsToTerrain()` e chamado para realinhar props fixos.
- Props sao respawnados para refletir a nova altura do terreno.
