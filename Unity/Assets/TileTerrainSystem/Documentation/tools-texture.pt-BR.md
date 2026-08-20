# Ferramenta de Textura

> **[English](tools-texture.md) | Portugues (Brasil)**

Mistura de texturas multicamadas baseada em prioridade com selecao de bitmask autotile.

**Atalho de teclado:** `2`

---

## Sub-Ferramentas

| Modo | Descricao |
|------|-----------|
| **Pintar (Paint)** | Pinta a textura selecionada nos vertices dentro do raio do pincel |
| **Borrar (Smudge)** | Copia os dados de textura de um vertice vizinho aleatorio com probabilidade `forcaDoPincel * 0.2` |
| **Preencher (Fill)** | Preenche por BFS uma area continua da mesma textura e nivel de penhasco (apenas clique, sem arrastar) |
| **Apagar (Erase)** | Redefine a textura do vertice para o padrao (indice 0), limpa camadas mid/under |

---

## Parametros

| Parametro | Intervalo | Padrao | Descricao |
|-----------|-----------|--------|-----------|
| **Textura Selecionada** | -- | -- | Indice em `terrain.RegisteredTextures` (UI da paleta) |
| **Randomizacao de Textura** | 0.0 - 1.0 | 0.4 | Probabilidade de selecao de tile centro aleatorio para tiles totalmente cercados/isolados |
| **Raio do Pincel** | 0.1 - 20.0 | 2.0 | Tamanho do pincel em unidades da grade |
| **Forca do Pincel** | 0.0 - 5.0 | 0.2 | Intensidade de mistura do Smudge (apenas no modo Smudge) |
| **Forma do Pincel** | Circulo / Quadrado | Circulo | Forma do decaimento do pincel |

---

## Pilha de Prioridade de 3 Camadas

Cada vertice mantem uma pilha de 3 slots de textura:

| Camada | Regra de Visibilidade |
|--------|----------------------|
| **Over** | Sempre visivel (prioridade mais alta) |
| **Mid** | Visivel onde a bitmask do Over nao e solida (mascara < 15) |
| **Under** | Visivel apenas quando Over e Mid tem lacunas (mascara < 15) |

Quando uma nova textura e pintada:
1. Coleta texturas existentes + textura recebida (maximo 4 candidatas).
2. Remove duplicatas e ordena por prioridade (indice menor da paleta = prioridade maior).
3. As 3 primeiras texturas unicas preenchem Over → Mid → Under.
4. Se uma textura ja ocupa um slot, ela nao e substituida (idempotente).

---

## Sistema de Bitmask Autotile

Apos pintar, bitmasks sao recalculadas por quad. Veja [getting-started.pt-BR.md](getting-started.pt-BR.md#configuracao-do-array-de-texturas) para o layout completo do tilemap 4x8.

**Ordem dos vertices:** `[v2, v3, v0, v1]`

```
v2 ─── v3
│  quad  │
v0 ─── v1
```

Cada vertice sinaliza 1 bit (mesma textura = 1, diferente = 0). A mascara de 4 bits (0-15) seleciona o tile do array de textura:

- **Mascaras 1-14**: Tiles de conector/canto (colunas 0-3)
- **Mascara 0**: Tile isolado → centro aleatorio (colunas 4-7)
- **Mascara 15**: Totalmente cercado → centro aleatorio (colunas 4-7)

O controle deslizante `TextureRandomness` controla a probabilidade de usar variacoes aleatorias vs. o tile centro base.

---

## Detalhes da Ferramenta Preencher

- Dispara apenas no clique do mouse (sem arrastar).
- Encontra o vertice mais proximo ao ponto de clique.
- Preenchimento BFS: expande para vizinhos 4-conectados compartilhando o mesmo `overTextureId` e o mesmo `CliffByte`.
- Aplica a textura selecionada a todos os vertices visitados.
- Recalcula bitmasks em lote.

---

## UI da Paleta

Um quadro de visualizacoes de textura (64x64 pixels) renderizado a partir de visualizacoes de `Texture2DArray`. Cada entrada mostra:
- Miniatura de visualizacao da textura
- Valor de prioridade
- Nome do array de textura
- Selecao indicada com destaque verde
