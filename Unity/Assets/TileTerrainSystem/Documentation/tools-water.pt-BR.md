# Ferramenta de Agua

> **[English](tools-water.md) | Portugues (Brasil)](tools-water.pt-BR.md)

Pintura de agua ciente de tracado com propagacao BFS e protecao de barragens. Cria superficies de agua que ficam no nivel de piso capturado.

**Atalho de teclado:** `5`

---

## Sem Sub-Ferramentas

A ferramenta de agua tem um unico modo: pintar agua. Nao ha apagar separado — use a ferramenta Apagar de Penhasco para remover agua elevando o piso acima do nivel de agua.

---

## Parametros

| Parametro | Intervalo | Padrao | Descricao |
|-----------|-----------|--------|-----------|
| **Raio do Pincel** | 0.1 - 20.0 | 2.0 | Tamanho do pincel em unidades da grade |
| **Forma do Pincel** | Circulo / Quadrado | Circulo | Forma do decaimento do pincel |

---

## Como Funciona a Pintura de Agua

1. **Captura nivel do piso**: O `CliffByte` atual do vertice se torna o `WaterLevel`.
2. **Marca como agua**: `IsWater = true`, `height = 0`.
3. **Abaixa o piso**: `CliffByte -= 1` (a agua fica em cima do piso abaixado).
4. **Limpa rampas**: Todas as flags de ramp (halfStep) nos vertices afetados e vizinhos sao limpas.
5. **Propagacao BFS**: O estado de agua se espalha para vertices vizinhos.

### Altura da Superficie de Agua

```
waterY = (WaterLevel - 0.5) * CliffHeight
```

A superficie de agua fica 0.5 unidades abaixo do nivel de piso capturado, criando uma linha costeira natural.

---

## Propagacao BFS

1. Para cada vertice no raio do pincel:
   - Ignora se ja e agua, borda de penhasco, ou nivel do piso nao coincide com o piso inicial.
   - Define estado de agua e abaixa o piso.
   - Enfileira na fila de propagacao.
2. **Loop de propagacao:**
   - Se nivel de penhasco do vizinho e >2 abaixo do alvo → enfileira com nivel abaixado em 1.
   - Se vertice e agua e nivel de penhasco ≥ nivel de agua → drena agua.
3. Recalcula pisos dos quads para todos os quads afetados.

---

## Interacao Agua e Penhasco

### Ferramenta Descer Penhasco (Interacao com Agua)

Se qualquer vertice pintado tem `IsWater == true`:
1. Captura seu `WaterLevel`.
2. Para cada outro vertice pintado:
   - Se `piso - 1 < nivelDeAguaCapturado`: define nivel de agua, marca como agua, executa descida.
   - Se `piso - 1 >= nivelDeAguaCapturado`: executa descida sem alterar estado de agua.

### Ferramenta Subir Penhasco (Interacao com Agua)

Se qualquer vertice pintado tem `IsWater == true`:
1. Captura seu `WaterLevel`.
2. Para cada outro vertice pintado:
   - Se `piso + 1 < nivelDeAguaCapturado`: vertice permanece submerso, executa subida.
   - Se `piso + 1 >= nivelDeAguaCapturado`: limpa agua, executa subida.

---

## Regras de Seguranca

- **Bordas de penhasco sao ignoradas** para preservar a estrutura do terreno.
- O tracado respeita o nivel do piso inicial — apenas vertices no mesmo piso sao modificados.
- Tracados de origem terrestre nao modificam vertices de agua.
- `IsSafeToCarve` impede romper penhascos que seguram agua.
- Vertices de limite de agua sao restritos a `passoMaximo = 1`.

---

## Modelo de Dados

| Campo | Tipo | Padrao | Descricao |
|-------|------|--------|-----------|
| `IsWater` | `bool` | `false` | Se este vertice esta submerso |
| `WaterLevel` | `sbyte` | `0` | O nivel do piso onde a superficie de agua fica |

---

## Requisitos

- **Water Material** deve ser atribuido no componente TileTerrain (shader translucido recomendado).

---

## Renderizacao

A agua e renderizada como uma malha separada por chunk:
- Cada vertice de agua recebe um tile 1x1 subdividido em quatro quads 0.5x0.5 (elimina T-junctions).
- **Patches de preenchimento de 3 cantos de agua**: Quando um quad tem exatamente 3 cantos de agua, um patch de triangulo conecta o centro aos pontos medios das arestas de agua.
- Todos os vertices de agua na mesma posicao XZ sao mesclados para normais consistentes.
