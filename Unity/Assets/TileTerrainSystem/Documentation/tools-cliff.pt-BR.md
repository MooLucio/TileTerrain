# Ferramenta de Penhasco

> **[English](tools-cliff.md) | Portugues (Brasil)**

Mudancas discretas de elevacao via niveis de piso do penhasco com propagacao BFS. Suporta tres tilesets (Padrao, Duplo, Transicional) e um sistema de paridade para transicoes naturais.

**Atalho de teclado:** `3`

---

## Sub-Ferramentas

| Modo | Descricao |
|------|-----------|
| **Subir (Up)** | Eleva o piso do penhasco em +1 por vertice |
| **Descer (Down)** | Abaixa o piso do penhasco em -1 por vertice |
| **Alvo (Target)** | Define o piso do penhasco para um valor especifico |
| **Borrar (Smudge)** | Copia o nivel de penhasco de um vertice vizinho aleatorio com probabilidade `forcaDoPincel * 0.2` |
| **Apagar (Erase)** | Redefine o piso do penhasco para 0 (remove penhasco) |

---

## Parametros

| Parametro | Intervalo | Padrao | Descricao |
|-----------|-----------|--------|-----------|
| **Nivel Alvo do Penhasco** | -3 a 11 | 0 | Nivel de piso alvo (apenas no modo Alvo) |
| **Raio do Pincel** | 0.1 - 20.0 | 2.0 | Tamanho do pincel em unidades da grade |
| **Forca do Pincel** | 0.0 - 5.0 | 0.2 | Intensidade de mistura do Smudge (apenas no modo Smudge) |
| **Forma do Pincel** | Circulo / Quadrado | Circulo | Forma do decaimento do pincel |

---

## Tres Tilesets

| Tileset | Arquivo FBX | Malhas | Condicao de Disparo |
|---------|-------------|--------|---------------------|
| **Padrao** | `cliff_mesh.fbx` | 14 (indices 0-13) | Bordas normais de penhasco (um passo) |
| **Duplo** | `cliff_double_mesh.fbx` | 14 (indices 0-13) | Vertices cobrem ≥2 niveis (ex.: 0→2) |
| **Transicional** | `cliff_transitional_mesh.fbx` | 36 (indices 0-35) | 3 niveis de piso unicos (n, n+1, n+2) |

**Prioridade de renderizacao:** Transicional > Duplo > Padrao

---

## Sistema de Paridade

O pincel de penhasco usa paridade para transicoes naturais de elevacao:

| Nivel do Piso | Subir Adiciona | Descer Remove |
|:-------------:|:--------------:|:-------------:|
| **Par** (0, 2, 4…) | +2 | –1 |
| **Impar** (1, 3, 5…) | +1 | –2 |

Isso garante:
- Pisos pares disparam malhas de altura dupla ao empilhar
- Pisos impares criam transicoes adequadas entre niveis

---

## Algoritmo de Propagacao BFS

1. Remove props no raio do pincel (incluindo grupos de entanglement).
2. Para cada vertice no raio do pincel:
   - Rastreia estado da agua (captura nivel de agua se o tracado comecou na agua).
   - Enfileira vertice com nivel alvo e direcao (+1, -1 ou 0).
3. **Loop de propagacao:**
   - Desenfileira vertice, aplica nivel alvo.
   - Se nivel de penhasco ≥ nivel de agua → drena agua.
   - Verifica 8 vizinhos conectados: se diferenca > `passoMaximo`, enfileira vizinho com alvo ajustado.
   - Vertices de limite de agua: `passoMaximo = 1` (impede romper barragens).
4. **Passo de reparo** (ate 10 iteracoes): Corrige incompatibilidades restantes.
5. Recalcula pisos dos quads: `quad.floor = min(4 CliffBytes dos vertices)`.
6. Revalida flags halfStep (ramp).

---

## Como as Malhas de Penhasco Sao Selecionadas

Para cada quad, o sistema calcula uma mascara de 4 cantos onde o bit `i` e setado quando o vertice `i` tem penhasco no nivel atual:

| Mascara | Padrao da Malha |
|---------|-----------------|
| 0 | Sem malha de penhasco (quad plano) |
| 1-14 | Malha de canto/borda (mapeada via tabela de busca) |
| 15 | Todos os cantos com penhasco → quad plano elevado por 1 nivel (sem malha necessaria) |

A mascara e convertida para um ID de malha via `CliffMaskToMeshID()` que mapeia para o nome da malha filho no FBX.

---

## Regras de Seguranca

- Pintura de penhasco **remove automaticamente props** que sobrepoe o pincel.
- Tracados de origem terrestre nao modificam vertices de agua (excecao: ferramenta Up pode drenar agua).
- Vertices de limite de agua sao restritos a `passoMaximo = 1`.
- `IsSafeToCarve` impede romper penhascos que seguram agua.
