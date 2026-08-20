# Ferramenta de Props

> **[English](tools-props.md) | Portugues (Brasil)](tools-props.pt-BR.md)

Colocacao e gerenciamento de objetos decorativos (arvores, pedras, etc.) com grupos de entanglement para movimento sincronizado com o terreno.

**Atalho de teclado:** `6`

---

## Sub-Ferramentas

| Modo | Atalho | Descricao |
|------|--------|-----------|
| **Colocar (Place)** | `Q` | Clique unico para colocar o prop selecionado |
| **Pintar (Paint)** | `W` | Dispersao de props baseada em pincel |
| **Selecionar (Select)** | `E` | Clique para selecionar um prop existente para edicao |
| **Remover (Remove)** | `D` | Clique para remover um unico prop |
| **Apagar (Erase)** | `F` | Remocao em massa de props baseada em pincel |
| **Rotacionar (Rotate)** | `R` | Arraste para rotacionar o prop selecionado |
| **Escalar (Scale)** | `T` | Arraste para escalar o prop selecionado |

---

## Parametros

| Parametro | Intervalo | Padrao | Descricao |
|-----------|-----------|--------|-----------|
| **Prop Selecionado** | -- | -- | Indice na paleta do PropsBox |
| **Densidade do Pincel de Props** | 0.0 - 1.0 | 0.3 | Probabilidade de colocar um prop em cada vertice (modo Pintar) |
| **Snap no Grid** | bool | `true` | Alinhar colocacao de props aos centros dos quads |
| **Rotacao Aleatoria** | bool | `true` | Rotacao Y aleatoria na colocacao |
| **Raio do Pincel** | 0.1 - 20.0 | 2.0 | Tamanho do pincel (apenas nos modos Pintar e Apagar) |
| **Forma do Pincel** | Circulo / Quadrado | Circulo | Forma do pincel (apenas nos modos Pintar e Apagar) |

---

## Definicao de Prop (TileTerrainProp)

Cada tipo de prop e definido como um ScriptableObject `TileTerrainProp`:

| Campo | Tipo | Padrao | Descricao |
|-------|------|--------|-----------|
| `Label` | string | -- | Nome de exibicao na paleta |
| `Prefabs` | List\<GameObject\> | -- | Variantes de prefab (uma escolhida aleatoriamente na colocacao) |
| `MinScale` | float | 0.5 | Escala aleatoria minima |
| `MaxScale` | float | 1.5 | Escala aleatoria maxima |
| `RandomRotation` | bool | `true` | Habilitar rotacao aleatoria |
| `CanRotate` | bool | `true` | Permitir rotacao manual |
| `CanScale` | bool | `true` | Permitir escala manual |
| `OccupyWidth` | int | 1 | Pegada horizontal (quads) |
| `OccupyHeight` | int | 1 | Pegada vertical (quads) |
| `CanPlaceInWater` | bool | `true` | Permitir colocacao em vertices de agua |

---

## Sistema de Entanglement

Quando um prop e colocado, ele cria um **grupo de entanglement** vinculando o prop a todos os vertices dentro de sua pegada. Isso fornece:

1. **Prevencao de sobreposicao**: Dois props nao podem compartilhar o mesmo vertice.
2. **Sincronizacao com o terreno**: Quando a ferramenta de altura/penhasco modifica um vertice entangled, todos os vertices do grupo recebem a mesma modificacao.
3. **Remocao automatica**: Pintura de penhasco remove automaticamente props cuja pegada sobrepoe o pincel.

### Como Funciona

1. Colocacao do prop calcula quads ocupados a partir de `OccupyWidth` × `OccupyHeight`.
2. Todos os vertices da pegada sao validados (mesmo nivel, sem agua se restrito, sem grupo de entanglement existente).
3. Vertices da pegada sao nivelados para a mesma altura e nivel de penhasco.
4. Um `EntanglementGroup` e criado, sinalizando cada vertice com o ID do grupo.
5. Quando qualquer vertice entangled e modificado pelas ferramentas de altura/penhasco, o delta e aplicado uniformemente a todos os membros do grupo.

---

## Algoritmo de Colocacao

1. Se snap no grid: posicao = centro do quad sob o cursor.
2. Calcula Y a partir da altura do terreno no ponto de colocacao.
3. Atribui rotacao aleatoria (0-360) e escala aleatoria (`MinScale` a `MaxScale`).
4. Escolhe uma variante de prefab aleatoria da lista `Prefabs`.
5. **Verificacoes de validacao** (todas devem passar):
   - Todos os quads da pegada no **mesmo nivel**.
   - Nenhum vertice da pegada e agua (se `CanPlaceInWater` e falso).
   - Nenhum vertice da pegada tem grupo de entanglement existente.
6. **Nivelamento**: Todos os vertices da pegada definidos para a mesma altura e CliffByte do vertice central; CliffHalfStep limpo.
7. Cria `PropInstance` e `EntanglementGroup`.

---

## Visualizacao na Cena

- **Modo Colocar**: Retangulo de pegada verde (valido) ou vermelho (invalido), wireframe da bounding box, crosshair no ponto de colocacao.
- **Modo Selecionar**: Disco wire ciano, linha de conexao com o chao, campos de posicao/rotacao/escala/fixar no inspector.

---

## Selecao e Modificacao

- **Selecionar**: Encontra o prop mais proximo dentro da distancia 4. Mostra campos de edicao no inspector.
- **Rotacionar**: Arrasto horizontal ajusta `rotationY`.
- **Escalar**: Arrasto vertical ajusta `escala` (minimo 0.1).
- **Tecla Delete**: Remove o prop selecionado.

---

## Interacoes Entre Ferramentas

- **Ferramenta de penhasco** remove automaticamente todos os props no raio do pincel.
- **Ferramentas de altura/penhasco/ramp** chamam `PinPropsToTerrain()` apos cada tracado para realinhar props fixos.
- **Caixa de Props** e o container de paleta referenciado pelo componente TileTerrain.
