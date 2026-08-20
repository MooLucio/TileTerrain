# Ferramenta de Ramp

> **[English](tools-ramp.md) | Portugues (Brasil)](tools-ramp.pt-BR.md)

Transicoes de meio-passo entre niveis adjacentes de penhasco. Cria geometria de rampa suave entre dois niveis de piso.

**Atalho de teclado:** `4` (compartilha aba de Penhasco, selecione sub-aba Ramp)

---

## Sub-Ferramentas

| Modo | Descricao |
|------|-----------|
| **Definir (Set)** | Alterna a flag `CliffHalfStep` em vertices validos |
| **Apagar (Erase)** | Limpa a flag `CliffHalfStep` dos vertices sob o pincel |

---

## Como as Rampas Funcionam

Uma rampa e uma transicao de **meio-passo** (+0.5 unidades) entre dois niveis adjacentes de penhasco. Quando um vertice tem `CliffHalfStep = true`, sua altura efetiva e `CliffByte + 0.5` em vez de apenas `CliffByte`.

```
Sem rampa:             Com rampa:
                       
Nivel 2 ─────         Nivel 2 ─────
         │                     │╲
         │                     │ ╲  ← malha de rampa
Nivel 1 ─────         Nivel 1 ────
```

---

## Regras de Colocacao

Para um vertice receber a flag de rampa, **todos** os seguintes devem ser verdadeiros:

1. Pelo menos um vizinho cardinal (cima/baixo/esquerda/direita) tem `CliffByte == CliffByteDoVertice + 1` (exatamente 1 piso acima).
2. Todos os 4 quads ao redor do vertice cobrem no maximo 1 diferenca de piso.
3. O vertice ja nao esta sinalizado.

Apos a colocacao, duas passes de limpeza rodam:
1. Se qualquer quad modificado tem halfStep mas cobre >1 piso, todas as flags halfPadrao nesse quad sao limpas.
2. Vertices halfStep isolados (sem vizinho cardinal com halfStep) sao removidos para impedir rampas de canto invalidas.

---

## Matriz de Ramp

O sistema de ramp usa uma **tabela de busca de 36 entradas** mapeando configuracoes de 4 sockets de vertices para IDs de malha FBX (0-35).

### Valores dos Sockets

Cada socket de vertice tem um de 5 valores:

| Valor | Significado |
|-------|-------------|
| `0.0` | Nivel base (baixo) |
| `0.1` | Nivel base com variante-R (parceiro da coluna tem halfStep) |
| `0.5` | Meio-passo (o proprio vertice da rampa) |
| `1.0` | Nivel elevado (alto) |
| `1.1` | Nivel elevado com variante-R (parceiro da coluna tem halfStep) |

A variante-R (`0.1` / `1.1`) indica que o parceiro da coluna (o vertice compartilhando a mesma coluna no quad adjacente) tambem tem halfStep, o que afeta a forma da malha.

### Orientacao do Parceiro

O sistema determina a orientacao da rampa a partir de:
1. **Dois vertices halfStep**: Usa a aresta que compartilham (vertical se v0+v2, horizontal se v1+v3).
2. **Um vertice halfStep**: Verifica qual parceiro da aresta esta no nivel de piso elevado.
3. **Ambiguo**: Fallback para verificar vizinhos da grade fora do quad.

### Codificacao da Chave

Valores dos sockets sao empacotados em uma chave base-5: `code(v0) + 5*code(v1) + 25*code(v2) + 125*code(v3)`

Onde `code` mapeia: `0.0→0, 0.1→1, 0.5→2, 1.0→3, 1.1→4`

---

## Parametros

| Parametro | Intervalo | Padrao | Descricao |
|-----------|-----------|--------|-----------|
| **Raio do Pincel** | 0.1 - 20.0 | 2.0 | Tamanho do pincel em unidades da grade |
| **Forma do Pincel** | Circulo / Quadrado | Circulo | Forma do decaimento do pincel |

---

## Requisitos

- **Ramp Mesh Fbx** deve ser atribuido no componente TileTerrain.
- O vertice deve estar em um penhasco (CliffByte > nivel do piso de pelo menos um vizinho).
- Quads adjacentes nao devem cobrir mais de 1 diferenca de piso.

---

## Visualizacao na Cena

- **Diamantes amarelos**: Alvos validos para colocacao de rampa
- **Diamantes verdes**: Vertices que ja tem rampas

---

## Interacao com Outras Ferramentas

- **Ferramenta de agua** limpa todas as flags de ramp (halfStep) nos vertices afetados e vizinhos.
- **Ferramenta de penhasco** revalida flags de ramp apos cada tracado.
- **Ferramenta de altura** nao modifica flags de ramp diretamente.
