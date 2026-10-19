# Plano de implantação — Modelagem e detalhamento de alto nível

> Estado atualizado de implementação, validações e pendências: [AUTONOMIA_STATUS.md](AUTONOMIA_STATUS.md).
> As fases abaixo são propostas; não representam ferramentas já implementadas.

> Documento de partida. Diagnóstico do modelo de referência (Ginásio de Tapurah),
> confronto com o que o MCP faz hoje, e o plano de fases para chegar num MCP capaz de
> **modelar e detalhar no padrão do escritório** — não só editar elemento a elemento.

## 0. Como este diagnóstico foi feito

- Revit 2026 aberto com o modelo `250415_GINASIO_ARQ_TAPURAH_R05.rvt`.
- Varredura **somente leitura** via HTTP direto no addin (`POST /mcp` em `127.0.0.1:7891`),
  usando `get_document_info`, `list_*`, `query_where`, `get_element_info`,
  `get_schedule_data`, `get_views`, `get_model_health`. Nenhuma transação de escrita.
- Durante o scan o addin carregado era a build `0.8.31` (commit `487b396`); o addin já foi
  rebuildado e implantado em `0.8.34` (101 comandos, 94 tools MCP). Os números desta seção
  valem para os dois — nenhum comando de leitura mudou entre as duas versões.

## 1. O modelo de referência

| Item | Valor |
|---|---|
| Projeto | GINÁSIO ESPORTIVO DE TAPURAH — `25-008-TPR-GNS-ARQ` — status **EXECUTIVO** |
| Arquivo | 464,6 MB, não workshared, 2 fases (Existente / Construção nova) |
| Elementos | 66.462 em 157 categorias |
| Saúde | nota **F (55)** — 505 warnings, 43 famílias in-place, 11 CAD **importados** (não vinculados) |
| Níveis | 8 — `N1-ES` (−0,05), `N1-AC` (0,00), `Pé Direito 2,40`, `Pé Direito 2,60`, `CABINE` (4,99), `PD DUPLO 6M`, `COBERTURA 1` (8,00), `COBERTURA 2` (12,00) |

Massa modelada e documentada:

| Categoria | Instâncias | Observação |
|---|---|---|
| Paredes | 467 (54 tipos) | 118 alvenaria 15 cm, **114 revestimento de 1 cm**, 66 `.ACM_Madeira`, 62 divisória granito |
| Pisos | 75 (30 tipos) | acabamento + estrutura + terreno, tudo por tipo nomeado |
| Forros | 30 (1 tipo) | vinculados aos níveis de pé-direito |
| Coberturas | 7 | telha aço zincado |
| Quadro estrutural | 1.813 | Gerdau L/W/U + perfis Ue |
| Pilares estruturais | 72 | mistos: concreto (49x19, 50x19) e metálico W |
| Painéis cortina | 994 + 1.423 montantes | fachada ACM e envidraçados |
| Escadas / guarda-corpos | 12 / 43 | 4 tipos de guarda-corpo |
| Portas / janelas | 83 (18 famílias) / 29 | inclui 47 portas de baia de WC |
| Modelos genéricos | 2.686 | acessibilidade, ferragens, pedras, chapins |
| Marcenaria | 42 peças **in-place** (`Marcenaria1..36`) | + `Arquibancada` também in-place |
| Anotação | 1.600 cotas, 900 textos, 290 notas-chave, 451 itens de detalhe | |
| Vistas | 360 — 191 cortes, 45 tabelas, 39 legendas, 24 plantas, 16 3D, 12 forro | 91 view templates |
| Pranchas | 26 | |

## 2. Soluções adotadas no projeto — o que o MCP precisa saber reproduzir

### 2.1 Taxonomia de tipos por nome
Tipos seguem `.CATEGORIA_Descrição`: `.ALVENARIA_15CM`, `.ACABAMENTO_Porcelanato 90x90 cm`,
`.ESTRUTURA_Laje 10cm`, `.TERRENO_Gramado`, `.GESSO_Placa de gesso comum (ST)`,
`.PAVIMENTAÇÃO_Blocos de Concreto`. Famílias do escritório usam prefixo `_ARQPWR_`.
O ponto inicial existe para ordenar no topo da lista — qualquer criação de tipo pelo MCP
tem que respeitar isso, senão o tipo "some" no meio da biblioteca.

### 2.2 Parâmetros de tipo governam o orçamento
Nos **tipos** (não nas instâncias) existe uma taxonomia própria:
`Filtro` (ex.: `Acabamento`), `Subfiltro` (ex.: `Pisos de Acabamento`), `Grupo`, `Subgrupo`,
`Código`, `OC_Etapa`, `Por Unidade`, `Por Área do Material`, `Por Volume do Material`,
`Valor Instalação`. Existe até uma legenda `01_Filtros e Subfiltros` no modelo.
**Consequência dura**: um tipo criado pelo MCP sem `Filtro`/`Subfiltro` entra no modelo
mas fica fora de todo o quantitativo. Isso é bug silencioso — o pior tipo.

### 2.3 Revestimento é parede independente de 1 cm
114 paredes `.ACABAMENTO_Revestimento xxCM` (0,0328 ft = 1 cm) e `.ACABAMENTO_Pintura xxCM`
(3 cm) são camadas de acabamento modeladas **por fora** da alvenaria, não como camada do
tipo composto. É o que alimenta `TABELA DE REVESTIMENTO - PAREDES` e `PAREDES - PINTURA`.

### 2.4 Níveis usados como pé-direito
`Pé Direito 2,40`, `Pé Direito 2,60`, `CABINE`, `PD DUPLO 6M` não são pavimentos: são cotas de
forro/altura. Um agente que assuma "nível = pavimento" vai criar parede da laje até o forro errado.

### 2.5 Fachada ACM em parede cortina
`.ACABAMENTO_ACM` e `_ARQPWR_Brise SulMetais SM 84` são **paredes cortina** de 2,5 cm com painéis
`Preto / 45 graus` (287), `Laranja / 45 graus` e `Envidraçado` (99). A fachada é resolvida no eixo
de cortina + painel, não em famílias soltas.

### 2.6 Detalhamento serial — o caso de uso principal
**175 cortes `Det. Painel 001..175`**, todos 1:50, distribuídos em **12 pranchas**
(`PAINEIS EM ACM - FACHADA FRONTAL/LATERAL`) com **179 viewports** — 12 a 21 por prancha.
Mais `PLANTA DE LOCAÇÃO - ELEMENTOS ACM`, cortes de fixação lateral/superior e
`TABELA DE ACM` por fachada. Isso é trabalho de dias, 100% repetitivo, e é exatamente
o que o MCP tem que passar a fazer sozinho.
Padrão análogo em `DETALHAMENTO BANHEIRO MASCULINO/FEMININO` (divisórias em granito, 62 peças)
e `SISTEMA DE VENTILAÇÃO GEOTÉRMICA`.

### 2.7 Documentação por template
91 view templates com prefixo de etapa: `.OC_01.A LEV-*` (levantamento), `.OC_01.B REF-*`
(reforma: construir/demolir), `.OC_01.C LAY-*` (layout), `.OC_01.D ARQ-*` (arquitetônico),
`.OC_02.B HID-*`, `.OC_02.C ELE-*`. Toda vista nova nasce de um template — criar vista
"crua" está fora do padrão.

### 2.8 Passivos que o MCP **não pode** herdar como verdade
- 43 famílias in-place (`Marcenaria1..36`, `Arquibancada`, `Paredes 1..3`, `Pisos 1..2`) —
  geometria sem tipo confiável.
- Nome de tipo mentindo sobre a espessura (`.VIDRAÇA_E=200 cm`, `Revestimento xxCM`).
- Tipos duplicados por cópia: `.ESTRUTURA_Laje 10cm` 2/3/4/5, `.ALVENARIA_15CM 2`.
- 11 DWG **importados** (não vinculados) e 12 imagens/PDF embutidos.
- 505 warnings, sendo 35 de `Marca de tipo` duplicada e 15 de pisos sobrepostos.

**Regra de projeto derivada disso:** todo comando novo decide por **classe + geometria +
parâmetro**, nunca por nome de tipo. Nome serve para *sugerir*, não para *concluir*.

## 3. Cobertura atual do MCP

| Frente | Hoje | Falta |
|---|---|---|
| Leitura / introspecção | 33 comandos, paginação, `query_where`, geometria, imagem de vista | schema de parâmetros por categoria; camadas do tipo composto; leitura de curtain grid; material/acabamento por face |
| Modelagem | parede, piso, forro, pilar, viga, ambiente, nível, eixo, abertura, instância de família | **tipo composto** (criar/editar camadas), **parede cortina** (grid/montante/painel), **telhado**, **escada**, **guarda-corpo**, **rampa**, **DirectShape**, **material**, pintura de face, terreno |
| Edição | set/update param, batch, mover/girar/copiar/espelhar/array, trocar tipo, agrupar | juntar/desjuntar paredes, editar perfil de parede, editar croqui de piso, `purge_unused` |
| Vistas | planta, corte, 3D, perspectiva, duplicar, template, filtro, section box, PDF | **callout / chamada de detalhe**, vista de detalhe (drafting), criar view template, vistas dependentes |
| Prancha | criar prancha, posicionar vista, tabela, configurar tabela | **layout em grade** de N viewports, numeração/renumeração, revisões, índice de pranchas, carimbo em lote |
| Anotação | cota alinhada, texto, linha de detalhe, região preenchida, tag, tag em vista | **cadeia de cotas** (multi-referência), cota por eixo/face, **nota-chave**, símbolo de anotação, legenda, cota de nível (`create_spot_elevation` segue oculto) |
| Análise | clash, clearance, headroom, path of travel, saúde do modelo | conferência de quantitativo, auditoria de padrão (nome / Filtro / Subfiltro), verificação NBR 9050 |

## 4. Plano de implantação

Cada fase entrega valor sozinha. A ordem é por dependência, não por vontade.
As fases F0–F5 constroem o **braço** (o que o MCP sabe executar). A seção 8 define o
**cérebro** — como isso vira um agente que projeta sozinho — e acrescenta requisitos
que mudam a forma de F2 e F3, então leia as duas antes de começar a codar.

### F0 — Fundamentos de leitura (bloqueia todo o resto)
1. `get_category_schema` — para uma categoria: parâmetros válidos, grupo, storage type,
   unidade, instância vs tipo, shared vs project. Sem isso o agente chuta `set_parameter`.
2. `get_type_structure` — camadas do `CompoundStructure` (função, material, espessura, core).
   É o que permite concluir "isto é revestimento de 1 cm" sem ler nome.
3. Saneamento do `get_element_info`: hoje devolve **chaves duplicadas** (`Categoria` 2×,
   `Nível` 2×), **IDs crus** onde caberia nome (`Nível=3711007`) e **valores em pés** sem rótulo.
   Passar a devolver `{name, value, displayValue, unit, storageType, isType, isShared}`.
4. `get_curtain_grid` — eixos, painéis e montantes de uma parede cortina.

*Aceite*: rodar os quatro contra o Ginásio e reconstruir as camadas de `.ALVENARIA_15CM`,
o `Filtro`/`Subfiltro` dos 30 tipos de piso e o grid da fachada ACM.

### F1 — Vocabulário do escritório
5. `office_standard.json` versionado no repo: prefixos (`.CATEGORIA_`, `_ARQPWR_`),
   parâmetros de tipo obrigatórios (`Filtro`, `Subfiltro`, `Grupo`, `Subgrupo`, `Código`,
   `OC_Etapa`), mapa de view templates por etapa, escalas por tipo de vista.
6. `audit_standard` — varre o modelo e lista o que foge do padrão (tipo sem Filtro,
   nome fora do padrão, duplicata `... 2/3/4`, marca duplicada). Read-only, com severidade.
7. Guard-rail nos comandos de criação: criar tipo ou vista sem os campos obrigatórios **falha**
   com erro tipado (`missing_standard_parameter`) em vez de criar lixo silencioso.

*Aceite*: `audit_standard` reproduz os achados da seção 2.8 sem nada hardcoded.

### F2 — Modelagem que falta
8. `create_wall_type` / `edit_type_structure` — tipo composto por camadas, com
   `Filtro`/`Subfiltro` obrigatórios (F1).
9. `create_curtain_wall` + `set_curtain_grid` + `set_panel_type` — a fachada ACM em comandos.
10. `create_roof` (footprint e extrusion) e `create_ramp`.
11. `create_stairs` (por corrida) e `create_railing` (por path + tipo).
12. `create_direct_shape` — para o que não tem categoria própria, avisando que DirectShape
    não entra em quantitativo por tipo.
13. `create_material` / `set_element_material` / `paint_face`.

*Aceite*: reconstruir, em modelo vazio, um trecho de 3 painéis da fachada ACM e uma escada
com guarda-corpo, batendo a geometria com o original dentro de 1 mm.

### F3 — Detalhamento serial (o carro-chefe)
14. `create_callout` — chamada de detalhe em planta, corte ou elevação.
15. `create_dimension_chain` — cadeia de cotas por várias referências (faces, eixos, aberturas)
    numa tirada só; hoje só existe cota alinhada de duas referências.
16. `create_keynote_tag` e `place_annotation_symbol`.
17. `recipe_serial_details` — receita: dado um conjunto de elementos (ex.: os 175 painéis ACM),
    gerar corte/callout por elemento, aplicar template, nomear por padrão (`Det. Painel NNN`),
    cotar, taguear e devolver a lista de vistas criadas. Dry-run obrigatório.
18. `layout_views_on_sheets` — distribuir N vistas em pranchas numa grade (linhas × colunas,
    margens), criando as pranchas conforme necessário e numerando na sequência.

*Aceite*: refazer as pranchas 11–22 (175 vistas, 179 viewports) a partir dos painéis do
modelo, em uma execução, com resultado visualmente equivalente ao que foi feito à mão.

### F4 — Documentação em lote
19. `create_view_template` e `apply_template_by_stage` (usa o mapa da F1).
20. `renumber_sheets`, `create_sheet_index`, `manage_revisions`.
21. `batch_export` (PDF/DWG por conjunto de pranchas, com nomenclatura padronizada).

*Aceite*: emitir o caderno executivo inteiro do Ginásio por comando.

### F5 — QA e quantitativo
22. `verify_quantities` — compara tabela × modelo e aponta elemento fora de filtro.
23. `check_accessibility` (NBR 9050) em cima do pack spatial-QC existente — largura de porta,
    área de giro, barra de apoio, piso tátil (o modelo já traz as famílias `_ARQPWR_ACS`).
24. `purge_unused` com dry-run.

## 5. Marcos sugeridos

| Marco | Conteúdo | Versão alvo |
|---|---|---|
| M1 | F0 completo + `audit_standard` | 0.9.x |
| M2 | F2 (modelagem) | 0.10.x |
| M3 | F3 (detalhamento serial + layout em prancha) | 0.11.x |
| M4 | F4 + F5 | 1.0 |

## 6. Decisões em aberto

1. **Padrão do escritório**: `office_standard.json` no repo, ou lido de um template `.rte`
   do escritório? Ler do template é mais fiel, mas exige o arquivo presente.
2. **In-place**: o MCP tenta *interpretar* famílias in-place (arquibancada, marcenaria) ou
   apenas reporta "não gerenciável" e propõe substituição por família de projeto?
3. **Cotas**: cadeia de cotas depende de `Reference` estável. Definir se referenciamos por
   face de elemento, eixo (grid) ou plano de referência — muda a robustez do resultado.
4. **Escopo de escrita**: `recipe_serial_details` pode gerar 175 vistas numa transação.
   Confirmar o limite de batch (hoje 200 passos) e a política de rollback parcial.
5. **Unidades**: manter `units` opt-in ou inferir de `Document.GetUnits()`
   (já está no backlog do ROADMAP) — com 66 mil elementos, erro de unidade sai caro.

## 7. Validação

O Ginásio de Tapurah vira o modelo-fixture oficial:
- **Leitura**: reconstruir por comando os números da seção 1 (54 tipos de parede, 175 detalhes,
  91 templates, 26 pranchas).
- **Escrita**: sempre em cópia do arquivo, com `dryRun` antes, e comparação de fingerprint
  (`scripts/smoke-test.ps1` já faz isso para o pack atual).
- **Regressão de padrão**: `audit_standard` antes e depois de qualquer receita — a receita não
  pode aumentar a contagem de desvios.


## 8. Camada de autonomia — o MCP como arquiteto

Objetivo declarado: **receber briefing e entregar projeto**, com Lucas (engenharia) e
Thaís (arquitetura) atuando por briefing e conferência, não por comando.
Isso não é "mais comandos". É uma camada acima, e ela impõe requisitos que mudam o
desenho de F2 e F3.

### 8.1 Três faixas de autonomia (e por que não são iguais)

| Faixa | Trabalho | Autonomia realista | Por quê |
|---|---|---|---|
| **A — Determinístico** | detalhamento serial, documentação, pranchas, quantitativo, conferência normativa, auditoria de padrão | **Total.** Roda sem humano, só relatório no fim | A resposta certa é verificável por regra. 175 cortes de ACM não têm criatividade nenhuma — têm repetição |
| **B — Dimensionamento** | traçar paredes a partir do programa, alturas, forros, esquadrias por vão, escada por desnível, acessibilidade | **Agente propõe, humano escolhe.** 2–3 alternativas, conferência, depois execução automática | A regra existe (NBR, código de obras, padrão do escritório) mas a combinação é aberta. Erra em escala se ninguém olhar |
| **C — Partido** | implantação, volumetria, linguagem, o que faz o projeto ser *aquele* projeto | **Humana.** O agente vira ferramenta de estudo rápido | Não é falta de tecnologia — é onde vocês dois agregam valor. Automatizar aqui entrega projeto genérico |

Consequência prática: **a ordem de implantação é A → B → C**, não a ordem em que a
ideia é empolgante. A faixa A já vale meses de trabalho por ano (só as pranchas
11–22 do Ginásio são 175 vistas e 179 viewports feitas à mão).

### 8.2 As quatro peças que faltam além dos comandos

**1. Briefing como dado, não como prosa.**
Um `BRIEFING.yaml` / `PROGRAMA.yaml` por projeto: ambientes, área mínima, pé-direito,
adjacências obrigatórias e proibidas, orientação solar desejada, público, norma aplicável,
nível de detalhamento alvo. Prosa vira modelo errado de forma silenciosa; tabela vira
modelo conferível. **É daqui que sai a conferência**: "programa pedia 4 vestiários de 25 m²,
o modelo tem 3 de 22 m²" é uma verificação automática, não uma leitura humana.

**2. Conhecimento normativo e do escritório como regra executável.**
NBR 9050 (circulação, giro, barra de apoio, piso tátil — as famílias `_ARQPWR_ACS` já
existem no modelo), saídas de emergência, código de obras do município, e o padrão do
escritório da seção 2. Isso vive em arquivo versionado, não no prompt: regra em prompt
não é auditável nem testável.

**3. Idempotência — o requisito não óbvio.**
Se o agente roda "gerar paredes do pavimento térreo" duas vezes, o modelo tem que ficar
igual, não com 400 paredes duplicadas. Exige: um parâmetro compartilhado de origem
(ex.: `AI_ORIGIN_ID`, chave estável derivada do programa) gravado em todo elemento gerado,
e comandos que façam **upsert** (achou a chave → atualiza; não achou → cria).
Sem isso, "autônomo" significa "acumula lixo a cada tentativa" — e a segunda rodada é
sempre pior que a primeira. **Isto precisa entrar em F2 desde o primeiro comando de
criação, não depois.**

**4. Proposta inspecionável antes do commit.**
`dryRun` de hoje é por chamada e some no rollback. Para revisar uma geração de 500
elementos vocês precisam **ver**. Opções, da melhor para a pior:
- **Design Options do Revit** — nativo, comparação A/B, descarte limpo, é como arquiteto
  já compara alternativa. Recomendado.
- Fase dedicada (`Proposta IA`) — simples, mas polui as fases do projeto.
- Cópia do arquivo — seguro e burro; perde a comparação lado a lado.

### 8.3 Onde mora o cérebro

Três lugares possíveis; a escolha define o custo de manutenção:

| Opção | Prós | Contras |
|---|---|---|
| **(a) No agente** — skills + arquivos de projeto orquestrando os comandos MCP | Regra muda sem recompilar nem reiniciar o Revit; versionável; testável em texto | Depende de o agente seguir o processo |
| (b) Em recipes C# no addin | Determinístico, rápido, uma transação só | Toda regra de projeto vira recompilação + restart do Revit |
| (c) Serviço externo | Isolado | Mais uma peça para manter, sem ganho claro |

**Recomendação: (a) para decisão, (b) para execução.** O agente decide *o quê*; o addin
executa em bloco *o como* (uma transação, um undo). É exatamente a divisão que a
arquitetura atual já defende — o servidor Node é burro de propósito.

### 8.4 O ciclo de trabalho

```
briefing (vocês)
  └─> programa estruturado ......... PROGRAMA.yaml         [gate 1: vocês validam a tabela]
      └─> estudo / partido ......... 2-3 alternativas 3D   [gate 2: vocês escolhem]
          └─> modelagem ............ faixa B, upsert por AI_ORIGIN_ID
              └─> conferência ...... automática (programa, norma, clash, padrão)
                  └─> detalhamento . faixa A, serial        [gate 3: revisão de prancha]
                      └─> emissão .. pranchas + quantitativo
```

Os três gates não são burocracia: são os pontos onde errar barato. Um erro de programa
que passa para o detalhamento custa 175 cortes refeitos.

### 8.5 O que isso acrescenta ao plano

| Fase | Acréscimo vindo da autonomia |
|---|---|
| F1 | `PROGRAMA.yaml` + `BRIEFING.yaml` como esquema versionado, ao lado do `office_standard.json` |
| F2 | **Todo comando de criação grava `AI_ORIGIN_ID` e faz upsert.** Requisito, não opcional |
| F2 | `create_design_option` / `set_active_design_option` — para proposta inspecionável |
| F3 | Nada muda: a faixa A já é o carro-chefe |
| F5 | `verify_program` — modelo × `PROGRAMA.yaml`, ambiente a ambiente |
| F5 | `check_accessibility` sobe de "bom ter" para **bloqueante** — é a conferência que substitui olho humano em edificação pública |
| Novo **F6** | `propose_layout` — dado o programa e um contorno, gerar N alternativas de planta com ambientes, paredes e circulação; sempre em Design Option, sempre com relatório de aderência ao programa |

O F6 é a faixa B de verdade e só faz sentido depois de F2 completo com idempotência.
Antes disso é demo bonita que suja o modelo.
