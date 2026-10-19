# Modelagem a partir de DWG

Estado: 11/09/2026, add-in 0.8.48. Piloto: `C:\Users\Mana02\Downloads\teste.dwg` (concessionária
Ford, AutoCAD 2018) no projeto `C:\Users\Mana02\Desktop\teste dwg.rvt` (template do escritório).

## Fluxo

1. `import_cad` em dryRun: conferir `extentsMeters`. O DWG do piloto em "auto" deu 0,08 × 0,11 m
   (desenho em metros com INSUNITS mm); com `unit:"m"` deu 78,5 × 110,8 m. `placement:"center"`
   evita modelar a 3,5 km da origem. Vínculo (link) é o padrão: removível e atualizável.
2. `inspect_cad` summary: camadas por comprimento e extensão. Blocos não aparecem: o Revit
   entrega a geometria do vínculo já explodida (nestingDepth 1). Textos e cotas não são legíveis.
3. Separar camadas: rodar o planejador por camada (script de sondagem) e desenhar as camadas
   em cores. No piloto: A-WALL e AR-ALV = paredes/muros, 0 = divisórias + contornos de vagas,
   A-GLAZ-CURT = vidro (painéis de 2 cm), AR-VAG/AR-ALV-SLD = vagas e boxes, FORD_DIV = divisas.
4. `revit_recipe_plan_walls_from_cad` (ou `scripts/build-cad-walls.mjs --stage plan`): paredes por
   pares de faces, espessura, cantos L/T, vãos; `excludeRegions` para o que não é parede.
5. Regras de classificação (espessura, região, fora do prédio) → tipo + altura, em JSON
   (`scripts/cad/teste-dwg.json`). `--stage create` em dryRun, depois `--commit` por grupo.
6. Conferir avisos, bbox de uma parede, 3D; `save_document`.

## Planejador (`src/McpServer/src/cadWalls.ts`)

- Agrupa segmentos por direção (tolerância angular) e funde linhas colineares.
- Pareia faces da mais próxima para a mais distante, consumindo os trechos usados.
- Faixas sobrepostas da mesma direção viram uma parede só: linhas de acabamento dentro da parede
  e drywall desenhado com 4 linhas (dois pares de 7 cm deslocados 3 cm = 10 cm). Achado no piloto.
- Encaixe de espessura no valor mais próximo dentro de `snapTolerance`.
- Pontes em trechos colineares de mesma espessura; vão sem parede cruzando = abertura; porta quando
  há arco de raio próximo à largura ou bloco/camada de porta perto.
- Pontas estendidas até o eixo da parede encontrada (L/T).
- Peças menores que `minWallLength` saem como `shortPieces` (no piloto, pilares de 25–45 cm).
- Vidro: mesma função com espessura 1–6 cm; montantes viram lacunas pequenas e somem.

## Piloto — segunda etapa (11/09 ~15h, com as fachadas enviadas pelo usuário)

Usuário deu autonomia ("criar como achamos que deve ser") e enviou as fachadas frontal e lateral.
Delas: pele de vidro do piso até 6,00 m (2,80 + 0,35 + 1,65 + 1,20), piso interno 0,45 acima da
calçada, pilares brancos em 5 vãos por fachada, faixa horizontal 2,80–3,15, testeira interna de
gesso, marquise na entrada lateral, bloco de serviço com testeira de ACM e portão de enrolar, totem.

Modelo refeito (teardown da etapa 1: 29 pedidos, 164 excluídos com painéis/montantes), avisos 33:
- 25 paredes (envoltória 25 cm h 6,00; muros base −0,45 h 2,50; internas 3,00; balcão 1,10;
  trecho leste X1 fechado à mão: face externa ausente no DWG).
- 22 panos de vidro: um por vão (painéis param em cada pilar), faixas 0–2,80 e 3,15–6,00.
- 18 divisórias de linha simples: drywall 10 cm (h 3,00) e granito nas cabines (h 1,80).
- 9 pilares: oeste 25×45 (ao longo do vidro), sul 48×25 (aletas 21 cm para fora). Conferidos
  contra os retângulos do DWG ao milímetro. Topo no nível N1-COB (6,00).
- 2 vigas 25×35, topo 3,15 (faixa das elevações).
- 8 portas pelos arcos de 90° (70/80/90 conforme raio). Puladas: 2 cabines (hospedeiro de 1,80),
  porta da sala do gerente (divisória de vidro). 4 arcos sem hospedeiro (portão SE, cabines).
- Piso porcelanato no contorno externo; laje de cobertura 10 cm no nível N1-COB-LAJE (5,50),
  recuada 25 cm. Vista "MCP - Showroom - perspectiva SO".

Roteiro completo: `build-cad-walls.mjs` plan → create (walls, glass, single) → doors;
`build-cad-structure.mjs` setup → create. Piso/laje ainda por chamada avulsa.

Falta: marquise (dimensões na prancha "MARQUISE", não enviada), porta de correr da entrada no
vidro (painel de cortina), testeira interna de gesso, sentido de abertura das portas (não
conferido), oficina/peças e bloco de serviço (sem paredes no DWG além dos muros), totem, pátio.

## Piloto — terceira etapa (11/09 ~15h45, Revit 0.8.49): bloco de serviço

`create_column` corrigido e validado ao vivo (tipo estrutural encontrado). Roteiro
`scripts/cad/teste-dwg-servico.mjs`, geometria calculada das linhas do DWG:
- Galpão da oficina sobre os 7 boxes (4,0 × 7,0 m, −16,5°): parede NO 25 cm do DWG, fundos e leste
  20 cm para dentro das divisas FORD_DIV (muros continuam separados), frente aberta alinhada à frente
  dos boxes com 6 pilares 25×45 nas divisões e testeira `.ACABAMENTO_ACM` 4,50–6,00 (fachada frontal:
  faixa de ACM de 1,50 m). Piso no nível N0-PATIO (−0,45), laje de cobertura a 5,50.
- Peças: parede sul 15 cm na linha simples do DWG, fundos 25 cm junto ao muro norte, fechada pelo
  showroom e pelo galpão; piso −0,45, laje 5,50. Área do polígono ~135 m² (DWG anota 182,10 m²).
- Avisos 33. Vista "MCP - Oficina - perspectiva do patio". Ids em created.json (servico).

Decisões nossas (sem dados): frente aberta em vez do portão de enrolar de 6,0 × 4,95 da elevação
(a elevação mostra 20 m de fachada e a frente da oficina tem ~31 m; não há como casar sem a prancha),
piso da oficina no nível do pátio, alturas iguais ao showroom.

## Piloto — revisão com o usuário (11/09 ~16h–18h, Revit 0.8.50–0.8.51)

Fontes: PDF do modelo (textos das salas, áreas) e prints do usuário. Regras aprendidas:
- Trechos colineares com degraus de 1–6 cm: apagar os trechos e estender o restante até a
  perpendicular (merge_walls). Mover paredes unidas arrasta as vizinhas; não resolve.
- Linhas vermelhas do DWG (A-DOOR / A-DOOR-GLAZ) são portas de correr; usar as famílias do
  escritório `_ARQPWR_PORTA Deslizante 1 folha` / `2 folhas` (biblioteca local), não a multi-painéis.
- Cabines de WC não delimitam ambiente ("Delimitação de ambientes" = Não); borda de bancada não é parede.
- Ambiente em nível com paredes de base diferente: criar no nível onde todas as paredes existem.
- Vista de revisão precisa da fase "Construção nova" (a planta do template está em "Existente").

Ambientes e áreas (Revit × PDF): SHOWROOM 720,12 (inclui lounge/prospecção/circulação); ESPERA PÓS
VENDAS 23,72 × 25; VESTIÁRIO FEM 14,21 × 16,90; VESTIÁRIO M. 14,09 × 30,62 (PDF parece somar WC);
GERENTE PÓS VENDAS 13,76 × 9,90; WC F. 8,62 × 12,56; WC MASC. 16,33; GERENTE VENDAS 9,94; ADM 13,79;
SALA REUNIÃO 16,81; TI 11,25; COPA 10,54; PEÇAS 132,78 × 182,10.

## Piloto — primeira etapa (substituída) (teste dwg, N1-AC)

24 paredes + 5 panos de vidro, avisos 33 antes e depois. Ids em `.cad-build/created.json`.

| Regra | Tipo | Altura | Qtde |
|---|---|---|---|
| muro (15 cm fora do prédio) | .ALVENARIA_15CM | 2,50 | 5 |
| envoltória leste/norte (25 cm) | .ALVENARIA_25CM | 6,45 | 4 |
| internas 25/20/15 | .ALVENARIA_* | 3,00 | 5 |
| drywall 10 | .GESSO_10cm (ST+ST) | 3,00 | 9 |
| balcão (15 cm no showroom) | .ALVENARIA_15CM | 1,10 | 1 |
| fachada de vidro | .VIDRAÇA_E=200 cm | 6,45 | 3 |
| vidro interno (gerente) | .VIDRAÇA_E=200 cm | 3,00 | 2 |

**Premissas a confirmar com os usuários:** 6,45 m foi medido na geometria das fachadas (topo da
platibanda); 3,00 m das internas, 2,50 m dos muros e 1,10 m do balcão são suposições. Nível único
N1-AC (0,00). Tipos escolhidos só pela espessura.

**Fora desta etapa (excluídos ou não detectados):** divisórias de linha simples (WCs, TI, copa),
divisores dos boxes da oficina, cabine no canto SE, divisor do pátio, trecho de 5 m da parede leste
(y 33,4–38,4, sem par de faces), pilares, portas (a porta de entrada do vidro foi detectada como vão
de 0,83 m), piso, cobertura/marquise, galpão da oficina/peças, totem.

## Gaps de ferramenta

- `create_column` só achava pilares arquitetônicos (corrigido na 0.8.49, falta validar ao vivo).
- `create_wall` deixa "Linha de localização" do template (Face de acabamento: Externa) embora a
  geometria fique no eixo; definir linha de centro explicitamente e aceitar `wallTypeId`.
- Divisórias de linha simples precisam de outro detector (linha + espessura informada).
- Portas: sentido de abertura (facing/hand) ainda não é aplicado a partir do arco.
- `create_perspective_view` usa `viewName` (passar `name` é ignorado sem erro); `rename_element` usa `name`.
- Textos do DWG (nomes de ambientes, áreas) não chegam pela API; ler pela imagem ou pedir DXF.
