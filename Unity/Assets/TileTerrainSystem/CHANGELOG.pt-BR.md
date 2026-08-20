# Historico de Alteracoes

> **[English](CHANGELOG.md) | Portugues (Brasil)**

Todas as mudancas notaveis no Tile Terrain System serao documentadas neste arquivo.

O formato e baseado no [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/),
e este projeto segue o [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Adicionado
- Licenca MIT.
- Documentacao publica para colaboracao no GitHub.
- Guia completo de inicio rapido com instrucoes de configuracao do array de texturas.
- Documentacao de arquitetura explicando o design de tres pilares.
- Documentacao por ferramenta (Altura, Textura, Penhasco, Ramp, Agua, Props).
- Documentacao do sistema de borda.
- Documentacao do sistema de props e entanglement.
- Documentacao do sistema de ramp.

### Alterado
- README reescrito como pagina inicial concisa com links para docs detalhados.
- Estrutura de arquivos na documentacao agora reflete o layout real do projeto.

### Removido
- `water-border-protection.pt-BR.md` (log interno de desenvolvimento, nao e documentacao de usuario).

## [1.0.0] - 2026-01-01

### Adicionado
- Sistema de terreno core: escultura de heightmap baseada em grade com renderizacao por chunks.
- Ferramenta de altura com 5 sub-modos: Elevar, Abaixar, Alvo, Suavizar, Ruido.
- Ferramenta de textura com pilha de prioridade de 3 camadas (Over/Mid/Under) e sistema de bitmask autotile.
- Sistema de penhascos com 3 tilesets: Padrao, Altura Dupla, Transicao (36 padroes).
- Sistema de ramp para transicoes de elevacao meio-passo (36 padroes de ramp).
- Ferramenta de agua com propagacao BFS e protecao de barragens.
- Ferramenta de props com grupos de entanglement para colocacao sincronizada com o terreno.
- Sistema de borda para celulas decorativas sem collider.
- Sistema de Nevoa de Guerra com LOS ciente de penhascos e URP RenderGraph.
- Shader HLSL personalizado para URP com amostragem de Texture2DArray.
- Documentacao bilingue (Ingles e Portugues).
