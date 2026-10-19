# Passagem para outro agente — 10/09/2026

## 0.8.65 carregada e idempotência validada ao vivo — 15/09/2026

Usuário autorizou prosseguir. Revit estava fechado; manifesto foi atualizado via
stage-local-addin.ps1 para `RevitMCP-local-20260915-102619`, e Revit 2026 iniciou com cópia
descartável de Desktop/teste.rvt em `release/live-idempotency-20260915/MCP Idempotency Validation.rvt`.
Health confirmou 0.8.65, 145 comandos e idempotencySupported=true.

VALIDADO AO VIVO: prévia/replay sem commit; conflito da chave de prévia com commit;
duas chamadas simultâneas com um único nível criado; consulta de operação concluída;
rejeição de sessão incompatível e documento implícito; prévia de batch; descarte intencional
da resposta do batch e recuperação por status/replay sem duplicar; cliente TS e alias
`POST /mcp command=batch` em prévia. Não foi simulada queda real de rede/reinício durante commit.

Níveis temporários 316306,316309,316310 lidos com UniqueId e removidos após dryRun confirmar
apenas esses três IDs. Inventário de níveis voltou exatamente ao inicial. Cópia salva,
isModified=false; arquivo original não foi aberto nem alterado. Revit permanece aberto
na cópia de validação; reler documentos antes de qualquer trabalho de modelagem.

Roteiro: scripts/test-idempotency-live.mjs. Evidência completa local (ignorada no Git):
release/live-idempotency-20260915/evidence.json. Resumo/diário: docs/OPERATION_IDEMPOTENCY.md.
Nenhuma DLL carregada foi sobrescrita. As demais integrações seguem propostas, não implementadas.

## Referências baixadas e primeira absorção — 15/09/2026, 0.8.65 preparada

Usuário autorizou integração e download dos repositórios. Seis clones rasos em
`referencias/repos/`, ignorados no Git; URLs/commits/licenças em `referencias/manifest.json`.
Leitura do WebSocketServer de mskim orientou implementação própria de idempotência no HTTP:
chave opcional prefixada pela sessão, reserva antes do dispatch, rejeição de payload diferente,
status autenticado `/operations?key=...`; exige documento explícito. MCP forwarding/batch
aceitam idempotencyKey; receitas não. Sem retry automático; ledger em memória, 1.024 chaves,
64 KiB por resultado, sem expiração. Ver `docs/OPERATION_IDEMPOTENCY.md` para limites.

Validação: 230 testes C#, 47 Node, build TS, Release C# 0 avisos/erros e check-version
(142 ferramentas/145 comandos) passaram. Primeira restauração falhou por ausência de fontes
NuGet; resolvida usando --source https://api.nuget.org/v3/index.json apenas na restauração,
sem alterar configuração global. RTK permanece indisponível nesta sessão.

Staging local: `release/staging-0.8.65-20260915-102301` (14 arquivos conferidos por SHA-256).
NÃO foi alterado manifesto ativo de add-in, nem DLL carregada, nem reiniciado Revit.
Novo comportamento NÃO validado ao vivo. Antes de testar no Revit será preciso ativar
essa build no próximo reinício decidido pelo usuário; staging local sozinho não a ativa.
Recarga de comandos, identidade completa de documento, origem DWG e famílias seguem como
próximas integrações propostas. Diário em `referencias/README.md` e OPERATION_IDEMPOTENCY.

## Pesquisa de ecossistema — 15/09/2026 (sem intervenção no Revit)

Usuário pediu buscar outros MCPs de Revit e avaliar o que absorver. Pesquisa e diário em
`docs/research/REVIT_MCP_ECOSYSTEM_2026-09-15.md`; inventário de 249 candidatos GitHub no JSON
adjacente (não são 249 soluções independentes/validadas). Prioridades: idempotência/sessões,
recarga de comandos (mskim), origem DWG e famílias por receitas (Horizun), contratos/capacidades
(RevitCortex). Código local/package e CHANGELOG estão em 0.8.64; versão carregada NÃO foi
consultada nesta pesquisa. KenLP/RevitMCPServer é o origin local, não concorrente externo.
Nenhum add-in instalado, DLL substituída, código executável alterado ou modelo modificado.
RTK indisponível no terminal; comandos diretos usados. Próxima implementação ainda não executada.

## Estado vigente — 14/09/2026 (0.8.52 preparada) — nuvem de pontos

Usuário tem uma nuvem de pontos (vê no ReCap) duplicada pelo menos 2×; pediu tudo para inserir, gerir e
modelar. Não há MCP de ReCap e o ReCap Pro não tem API além do importador decap.exe (instalado em
C:\Program Files\Autodesk\Autodesk ReCap\decap.exe; SDK AdskRcManaged.dll vem com o Revit). Tudo feito no
add-in Revit: link_point_cloud, inspect_point_cloud (scans, origens, grupos duplicados), set_point_cloud_display
(visibilidade/cor por scan na vista), sample_point_cloud (caixa, histograma z, arquivo). Node pointCloud.ts
(RANSAC em fatia, detecção de cópia deslocada, níveis) + receitas plan_walls_from_point_cloud e convert_scans.
Nada validado ao vivo; falta o caminho/formato do arquivo do usuário. Após reinício: inspect_point_cloud →
link (se ainda não vinculada) → inspect com instanceId (scans duplicados?) → sample summary (histograma → níveis)
→ receita de paredes numa fatia a +1,20 m → revisar com o usuário.

## Estado anterior — 11/09/2026 ~18h20 (0.8.51 carregada; build 0.8.51 com set_parameter BuiltIn preparada)

Ao vivo: merge_walls fachada leste (33,96 m, 3 portas recriadas; porta do vestiário movida 8 cm da junta).
Porta da entrada NÃO alargada: linhas de grade do tipo não podem ser apagadas (removeGridLines agora
explica); painel voltou a vidro (19388201 = Envidraçado). Sentido das portas conferido contra os arcos
(facing = lado de abertura, hand = lado da dobradiça): só 19397694 estava errada, corrigida.
Vestiários revisados por recorte (ok; cabines do vestiário M e louças não modeladas). Lavagem: porta
_ARQPWR_PORTA Dobradiça 2 folhas 140×210 (biblioteca). Avisos 33, salvo.
PREPARADA (RevitMCP-local mais recente): set_parameter aceita BuiltInParameter e recusa nome ambíguo.
Após reinício: duplicar .VIDRAÇA_E=200 cm → "MCP Vidraça sem grade vertical" (SPACING_LAYOUT_VERT=0) →
change_element_type do pano 19388118 → addGridLines 35,36/38,63 + meio do vão? → setPanels porta 9367846
em y 37,0 → conferir. Depois: oficina (recorte), vagas (_ARQPWR_URB Vaga de estacionamento), totem, pátio.

## Estado anterior — 11/09/2026 ~17h45 (0.8.51 carregada; build corrigida da 0.8.51 preparada)

Validado ao vivo na 0.8.51: snap_wall_ends com regra "já conectado" (só sobra 19388871, recusado por
junção com a testeira ACM); create_wall locationLine=WallCenterline; edit_curtain_wall (grades verticais
+ painel de porta) no gerente de vendas (divisória de vidro sul 19397419 + porta de vidro) e na entrada
(painel 19388201, saiu com 1,59 m porque a grade do tipo tinha linhas em y 36,44 e 38,03);
create_floor heightOffset (degraus -0,09/-0,18/-0,27/-0,36 e marquise topo 3,15). merge_walls falhou
(InvalidObjectException: lia o tipo da parede apagada) — corrigido, e edit_curtain_wall ganhou
removeGridLines; RevitMCP-local-20260911-160846 preparado (mesma versão 0.8.51). Avisos 33, salvo.
Após reinício: merge_walls na fachada leste [19388106,19388109,19388107,19388108,19389063] (dryRun) →
entrada: setPanels de volta a vidro, removeGridLines em y 36,44 e 38,03, setPanels porta em y 37,0 →
separadores do showroom + ambientes → sentido das portas → recortes de vestiários, oficina, lavagem.

## Estado anterior — 11/09/2026 ~17h15 (0.8.50 carregada, 0.8.51 preparada) — revisão dos escritórios

Revisão com o usuário na vista "MCP - Revisão planta" (19389126, fase Construção nova, recorte por setor):
snap_wall_ends aplicado por pares (13); alinhamento por move_element NÃO funciona (arrasta vizinhos
unidos); regra do usuário: apagar trechos e estender o restante até a perpendicular (feito por
apagar+criar em 4 cadeias; ferramenta merge_walls na 0.8.51). Linhas vermelhas do DWG = portas de
correr (A-DOOR/A-DOOR-GLAZ): trocadas pelas famílias do escritório _ARQPWR_PORTA Deslizante 1/2 folhas
(biblioteca local; load_family usa filePath). WC F.: porta na parede de baixo + 2 portas de cabine;
copa com correr 70. Avisos 33, RVT salvo.
PREPARADA 0.8.51 (133 comandos/128 ferramentas): merge_walls, snap_wall_ends com regra "já conectado".
Pendentes: fachada leste em 5 trechos (19388106-109, 19389063, 3 portas) → merge_walls; gerente vendas
(divisória de vidro sul + porta no vidro); sentido das portas; entrada (porta dupla no vidro com
_ARQPWR_PORTA CORTINA Deslizante…/edit_curtain_wall, degraus, marquise); separadores e ambientes;
revisar vestiários, oficina e lavagem por recorte.

## Estado anterior — 11/09/2026 ~16h30 (0.8.49 carregada, 0.8.50 preparada de novo) — limpeza e revisão

Usuário mandou PDF do modelo (Downloads/teste Model (1).pdf, sem camada de texto: renderizar com
pypdfium2 e girar 90°). Textos lidos: ADM, SALA REUNIÃO, TI, COPA, ESPERA PÓS VENDAS 25, WC F. 12,56,
WC MASC., VESTIÁRIO M. 30,62, VESTIÁRIO FEM 16,90, GERENTE PÓS VENDAS 9,90, GERENTE VENDAS, PROSPECÇÃO,
FORD STORE, BACKLIT GRAPHIC (era o "balcão", agora 2,60), RECEPÇÃO TÉCNICA, PERGOLADO AMADEIRADO
(projeção no lounge), PEÇAS 182,10, boxes GARANTIA/ELÉTRICA/FERRAMENTARIA/MOTORES, LAVAGEM 64,18
(paredes criadas), CHECKLIST, VAGA CLIENTE 5×2,5, VAGA APOIO OFICINA 5,2×2,7.
Criados: parede sul do gerente pós vendas, trecho leste norte do showroom, 8 ambientes (WC F./MASC.
vazaram e foram apagados; PEÇAS revertido por não fechar). Avisos 33. RVT salvo.
Usuário: muitas paredes não se encontram, há paredes erradas e esquadrias faltando/mal colocadas
(portas de correr etc.). 0.8.50 ganhou snap_wall_ends (WallEndSnapper, 5 testes) e
create_room_separators; RevitMCP-local-20260911-154427. 221 testes C#, 37 Node.
Após reinício: snap_wall_ends dryRun (levelName N1-AC, tolerância 0,6) → revisar movimentos → commit →
set_view_crop por setor + imagem, comparar com o PDF → corrigir paredes/esquadrias (portas de correr,
porta dupla da entrada via edit_curtain_wall) → separadores do showroom → ambientes restantes.

## Estado anterior — 11/09/2026 ~16h (0.8.49 carregada, 0.8.50 preparada) — ferramentas da entrada

PREPARADA 0.8.50 (130 comandos/125 ferramentas; RevitMCP-local-20260911-153604; 216 C#, 37 Node):
set_view_crop, edit_curtain_wall (grade + painel de porta), create_floor heightOffset/structural,
create_wall locationLine/wallTypeId (verificado), list_families offset, receita plan_walls com
singleLineRegions/doorLayers. Nada disso validado ao vivo.
Entrada oeste lida no DWG: porta dupla de abrir no vidro y 35,36–38,63 (AR-POR), contorno de marquise
A-DETL x −18,38..−16,40 / y 33,11..40,83 com grelha Layer0 (estrutura), linhas de degrau.
Após reinício: create_wall dryRun (conferir locationLine=WallCenterline e curva no eixo) → set_view_crop
na planta 6465305 → edit_curtain_wall no pano inferior do vão y 34,85–39,62 (grade vertical nas
ombreiras, painel "OC_Porta Parede Cortina-Vidro Duplo" 9367846) → marquise create_floor heightOffset
→ degraus → sentido das 8 portas (facing/hand vs openSide do arco, conferir com recorte).

## Estado anterior — 11/09/2026 ~15h45 (0.8.49 carregada) — DWG, bloco de serviço

create_column validado. Oficina (galpão aberto com testeira ACM) e peças criados por
scripts/cad/teste-dwg-servico.mjs; avisos 33; salvo. Ver docs/CAD_TRACING.md (terceira etapa).
Pendentes: marquise, porta de correr da entrada, sentido das portas, totem, pátio/calçadas.

## Estado anterior — 11/09/2026 ~15h15 (0.8.48 carregada, 0.8.49 preparada) — DWG, segunda etapa

Usuário deu autonomia e enviou as fachadas. `teste dwg` refeito e salvo: paredes, vidro em faixas por
vão, divisórias de linha simples, pilares e vigas da fachada, portas, piso e laje de cobertura; avisos
33. Detalhes e pendências em docs/CAD_TRACING.md (seção "segunda etapa"). Ids em .cad-build/created.json.
PREPARADA 0.8.49 (RevitMCP-local-20260911-151441): só a correção do create_column (categorias).
Próximo: marquise, porta de correr da entrada, sentido das portas, bloco de serviço/oficina.

## Estado anterior — 11/09/2026 ~14h45 (0.8.48 carregada) — modelagem por DWG, primeira etapa

`teste dwg.rvt` (Desktop, salvo por save_document). DWG vinculado 19387657 (unit m, center, só na
planta N1-AC). Criadas 24 paredes + 5 panos de vidro por `scripts/build-cad-walls.mjs` com
`scripts/cad/teste-dwg.json`; avisos 33 (iguais). Detalhes, premissas de altura e o que ficou de
fora em docs/CAD_TRACING.md. Cliente MCP ainda sem as ferramentas 0.8.47/0.8.48: usar scripts/rv.mjs.
Próximo: confirmar premissas com o usuário; portas nos vãos; divisórias de linha simples;
create_wall com linha de centro explícita.

## Estado anterior — 11/09/2026 ~14h05 (0.8.47 carregada, 0.8.48 preparada) — nova modelagem por DWG

Usuário pediu nova modelagem a partir de C:\Users\Mana02\Downloads\teste.dwg (AC1032 = AutoCAD 2018),
sem outras informações do projeto. Revit reiniciado com 0.8.47; `teste mcp` salvo (13:46); aberto
documento novo `Projeto1` (nunca salvo). list_recent_documents validado ao vivo. MCP client ainda
sem as ferramentas 0.8.47: usar `node scripts/rv.mjs <comando> '<json>' [--dry]` (HTTP direto).
PREPARADA 0.8.48 (128 comandos/123 ferramentas; RevitMCP-local-20260911-140423; 216 C#, 32 Node):
import_cad, inspect_cad, create_wall_type_variant, receita revit_recipe_plan_walls_from_cad
(planejador puro cadWalls.ts; ajustar não exige reiniciar o Revit, só o cliente MCP).
Roteiro após reinício: salvar Projeto1 (save_document saveAsPath) → import_cad dryRun/commit na
planta do térreo → inspect_cad summary (conferir extents vs. tamanho plausível; unidade) →
get_view_image → escolher camadas de parede/portas → plan_walls_from_cad → revisar com o usuário
(espessuras, não pareadas) → create_wall_type_variant → create_wall (dryRun por lote) → portas.
Riscos: textos/cotas do DWG não são legíveis pela API; várias pranchas/pavimentos no mesmo DWG
exigem bboxMeters; importação pode gerar aviso e ser revertida pela captura de falhas.

## Estado anterior — 11/09/2026 ~13h40 (0.8.46 carregada, 0.8.47 preparada)

Código 0.8.35–0.8.46 versionado no commit 2ba09fe (familias/ e .codex/ ignorados). RVT `teste mcp`
com isModified=true: toda a cobertura só existe na sessão — salvar ao fechar o Revit.
PREPARADA 0.8.47 (125 comandos/119 ferramentas; RevitMCP-local-20260911-134013; 216 testes C#, 26 Node):
- save_document (SelfManagedWrite): salva no próprio arquivo ou saveAsPath; confere isModified e gravação.
- list_recent_documents: lista de recentes do Revit.ini (UTF-16), filtro por nome sem acento.
- open_document (UiAction): por path ou name falado resolvido na lista de recentes; recusa versão
  mais nova, pede allowUpgrade/detachFromCentral; activate:false abre sem janela.
Após reinício (Revit e cliente MCP): verify-local-addin.ps1 → list_recent_documents → open_document
name "teste mcp" (testar com Revit na tela inicial) → save_document dryRun → commit → conferir
isModified=false. Testar ambiguidade com name "showroom" (2 candidatos) sem abrir.
Pendentes da cobertura: calha 5 cm além das testeiras, corte a prumo do caibro, faixa de 5 cm da
alvenaria atrás do frechal. Depois: bacia/cuba/torneiras, piso rebaixado × laje.

## Estado anterior — 11/09/2026 ~13h45 (0.8.46 carregada e validada; beiral refeito)

Revit 0.8.46 (122), `teste mcp`, avisos 34. Roteiro build-purlin-roof.mjs executado inteiro
(dryRun → commit em cada estágio). Validado ao vivo:
- set_parameter em metros para Largura/Altura (tipos 19409404 Bloqueio 2,5×19,47; 19409406
  Testeira 2,5×16,97; 19409408 Testeira lateral 2,5×12). Mapa em .roof-build/types.json.
- teardown: 83 pedidos → 145 excluídos; dependentes classificados (esboços dos oitões por
  perfil, planos/linhas/cotas de croqui, 6 aberturas). Roteiro agora bloqueia classes fora da lista.
- covering: base 2,4497 (lambril na cumeeira 6,0497).
- framing: 81 peças; join_geometry recortou 32/32 (30 bloqueios + 2 testeiras) com redução de volume.
- gables: oitões 19409700 (sul) e 19409761 (norte), 2,80..6,0347, 3 aberturas cada.
- gutters: offsets descobertos por prévia (horizontal −0,05 = para dentro; vertical +0,03 = para
  cima, aresta inferior do beiral); calhas 19409811 (leste) e 19409812 (oeste), face de trás na
  testeira (x 6,55 / −0,55), topo 2,4797 = lambril −2 cm; telha pinga 5 cm dentro. Antigas removidas.
Corte 19401073 conferido. Cumeeira 19409188 seguiu o telhado (hospedada).
Observações: calha 9,20 m passa 5 cm além das testeiras nas pontas; corte a prumo do caibro não
modelado; interior mostra faixa de 5 cm do topo da alvenaria atrás do frechal.

## Estado anterior — 11/09/2026 ~13h25 (0.8.45 carregada, 0.8.46 preparada)

Revisão do usuário no corte do beiral: calha presa na borda da telha, testeira abaixo dela e
11,5 cm abaixo do caibro, bloqueio no eixo com cunha de 2,7 cm e frechal/topo da alvenaria
expostos. Decisões: bloqueio na face externa da parede (frechal rente à face externa),
testeira recuada 5 cm com altura = prumo do caibro, calha fixada na testeira.
0.8.46 (122 comandos/116 ferramentas; RevitMCP-local-20260911-132215; 210 testes C#, 26 Node):
plan_purlin_roof plateAlignment/fasciaRecess/fasciaAutoHeight e cutByCovering; join_geometry;
offsets/flip em create_roof_edge_sweep. Roteiro build-purlin-roof.mjs com estágios types,
teardown, framing+join, gutters (descobre sinal dos offsets em dryRun).
Após reinício, em ordem (cada um dryRun e depois --commit, conferindo corte 19401073):
plan → types → teardown → covering → framing → gables → gutters → verify.
Limitação: corte a prumo do caibro não modelado (topo do caibro termina na face interna da testeira).

## Estado anterior — 11/09/2026 ~12h30 (0.8.45 carregada e validada)

Revit com 0.8.45 (121 comandos), só `teste mcp` aberto, avisos 34. Nada preparado pendente
de reinício. Validado AO VIVO nesta etapa (commit + faces/imagem):
- create_profile_family (SelfManagedWrite): família "MCP Perfil cumeeira 45 graus" (19408821,
  tipo 19409184), ridgeCap 45°, abas 0,20 m, espessura 1,2 cm; salva em %TEMP%\...\RevitMCP\profiles.
- duplicate_family_type com FasciaType: "MCP Cumeeira 45 graus" 19409186; set_parameter Perfil {id}.
- Cumeeira metálica errada 19408760 excluída (dryRun mostrou 1 dependente) e substituída por
  19409188 (UniqueId ...-01282924): abas a 45°, pontas z 6,124 sobre a telha, alcance 0,20 m.
- create_roof_edge_sweep slopeCheck: nova 45/45 matchesRoofPitch true; tipo do escritório
  (dryRun) 20° vs 45° com aviso. Verificação agora por sólido (bbox inflada).
Estado da cobertura: estrutura 81 peças, oitões por perfil com aberturas, calhas 19408765/66,
cumeeira 19409188, sem rufos (usuário: rufo só com platibanda/encontro com parede).
Banheiro: forro 19408776 com junta de 2 cm e 4 tabicas. Detalhes em ROOF_STRUCTURE.md.

Gaps abertos: classificação de aresta de encontro com parede/platibanda (onde vai rufo);
cumeeira cerâmica real (perfil é casca em V, não telha curva); set_view_detail_level falha em
vistas controladas por modelo; corte de detalhe 19408813 fica coberto pela marca do corte
longitudinal; bbox de famílias de perfil/borda não serve para conferência.

## Estado anterior — 11/09/2026 ~12h10 (0.8.44 carregada, 0.8.45 preparada)

Carregada 0.8.44 (120). Abertos: teste mcp (ativo) e _ARQPWR_Showroom R01_2026 (IDs dos tipos
iguais aos do R01_2025). Regressão de documento corrigida e validada (batch + UI action).
Validado ao vivo (commit + faces/imagens, avisos 34):
- copy_elements_from_document: 19407741 Rufo, 19407742 Cumeeira metálica, 19407743 Calha
  30×20, 19407744 Tabica tipo 1, 19407745 Tabica tipo 2.
- create_roof_edge_sweep com tipos do escritório: calhas 19408765/66 nos beirais (perfil
  30×20 conferido por faces e corte). Rufos laterais 19408761-64 REMOVIDOS: usuário apontou
  que rufo sem platibanda não faz sentido (classificação "abutment" ainda não existe).
  Cumeeira metálica 19408760 MANTIDA mas ERRADA: perfil fixo 20°, telhado 45°, abas ~17 cm
  acima da telha (faces normal ±0,342/0,940). Substituir após 0.8.45.
- Banheiro: forro 19394071 excluído (11 elementos = forro + esboço; peças do banheiro
  conferidas) e recriado como 19408776 com perimeterGap 2 cm (3.62..5.78 × 5.42..7.78, h 2.60).
  Tabica tipo 2 hospedada na face inferior nas 4 bordas: 19408786 S, 19408797 L, 19408800 N,
  19408802 O; perfil L de 2 cm preenchendo a folga até a parede, cantos a 45°. Sentido
  anti-horário (perfil à direita da linha). Corte 19408804 "MCP - Banheiro - junta perimetral
  forro" e corte 19408813 "MCP - Cobertura - detalhe cumeeira" criados.
- Bounding boxes de bordas/tabicas são infladas: conferir por faces/sólido.
- Lição: delete_elements em dryRun antes, para ver dependentes.

PREPARADA 0.8.45 (121 comandos/115 ferramentas; RevitMCP-local-20260911-120548; 207 testes C#,
26 Node). Pasta RevitMCP-local-20260911-120529 é build antiga copiada após falha de compilação
— ignorar (manifesto aponta para 120548). stage-local-addin.ps1 agora recusa build desatualizada.
Novas: create_profile_family (ridgeCap por inclinação; SelfManagedWrite), duplicate_family_type
para qualquer tipo, slopeCheck e extensão real em create_roof_edge_sweep, profileSide em
place_curve_family_instance.
Após reinício: create_profile_family ridgeCap 45° (asa ~0,20, espessura ~0,01) → duplicar tipo
19407742 → set_parameter Perfil {id} → excluir 19408760 → create_roof_edge_sweep ridge →
slopeCheck + corte 19408813. Salvar RVT antes de fechar.

## Estado anterior — 11/09/2026 ~12h (0.8.43 carregada, 0.8.44 preparada)

Carregada 0.8.43 (120 comandos). Só `teste mcp` aberto. REGRESSÃO 0.8.43: documentos
comparados por referência → todo batch de escrita com 2+ passos falha ("single document"),
toda ação de UI é recusada, list_open_documents marca ativo como inativo. Usar comandos
únicos até carregar 0.8.44 (RevitMCP-local-20260911-115141; corrigido com Document.Equals;
activate_document ganhou allowOpen para abrir o showroom). 202 testes C#, 26 Node.

Validado ao vivo nesta etapa (teste mcp, commit + leitura, avisos 34):
- create_profile_wall: oitões 19402931 (sul) e 19402963 (norte), 2.80..6.0097 m, faces
  y 0/.20 e 7.80/8.00 (perfil no plano central). Causa da falha anterior: Wall.Create com
  perfil nasce com topo no próximo nível; normalização de base/altura resolve. flipped=true.
- create_opening_in_wall: 3 aberturas por oitão (19402960-62, 19402992-94) para terças/cumeeira.
- create_roof_edge_sweep: cumeeira (tipo padrão Frontão 19885) e calhas nos 2 beirais (Calha
  19886), 9.20 m cada, removidos após conferência (perfis padrão ~0,9 m).
- documentTitle e carimbo "document" nas respostas.
Imagens: cortes 19401073/19401082 e VISTA 3D 01 mostram estrutura, oitões e cobertura.

Pendente: salvar RVT (isModified); abrir showroom (activate_document allowOpen na 0.8.44) e
copiar Cumeeira metálica 2666882, Rufo metálico 2666157, Calha 30×20 2667839, Tabica tipo 1
3463734; aplicar bordas; forro do banheiro 19394071 (UniqueId ...-0127ee17, tipo 3078072,
3.6..5.8 × 5.4..7.8, h 2.60, espessura 1,2 cm) recriar com perimeterGap e tabica na face inferior.

## Estado anterior — 11/09/2026 ~11h50 (validação 0.8.42, preparo 0.8.43)

Objetivo reafirmado pelo usuário: validar e criar ferramentas; madeira genérica; bitolas OK;
fidelidade à realidade não é o foco. Banheiro MANTÉM forro e precisa de junta perimetral.
Usuário autorizou alternar documento ativo. Exemplos do escritório estão no showroom
(E:\Dropbox\...\_ARQPWR_Showroom R01_2025.rvt): telhados EST Terças/Caibros/Ripas, bordas
Cumeeira metálica (tipo 2666882), Rufo metálico (2666157), Calha 30×20 (2667839), famílias
Tabica tipo 1 (tipo 3463734) / tipo 2 (3465249), sancas. IDs do showroom, conferir ao vivo.

Validado AO VIVO na 0.8.42 (commit + leitura), documento teste mcp:
- analyze_wall_roof_interface (oitão triângulo exato; cunha .20 no beiral), inspect_scope_boxes.
- plan_purlin_roof (81 peças; base 2.4247), create_framing_member (caibro teste: faces
  inferior/superior nos planos previstos; removido e recriado no lote).
- detach_wall_top: 4 paredes externas voltaram a 2.80.
- create_roof_type_variant: tipo 19402587 "MCP Cobertura capa-canal sobre lambril", telhado
  19401056 com base 2.4247, intradorso = topo dos caibros (z = x + 3.0247).
- Lote de 81 peças MCP-COBERTURA:* (ids em .roof-build/framing-created.json). Avisos 34.
FALHOU: create_profile_wall (dryRun) — parede nasce 0..2.9 m (nível a nível) ignorando o
perfil 2.8..6.0. Oitões NÃO criados: cabana está com oitões abertos acima de 2.80.

Durante a sessão o usuário abriu o showroom; comandos passaram a ler o showroom (sem escrita).
Gap corrigido na 0.8.43: documentTitle/documentPath em todos os comandos.

PREPARADO 0.8.43 (120 comandos/114 ferramentas, RevitMCP-local-20260911-114555, manifesto
atualizado; 202 testes C#, 26 Node): list_open_documents, activate_document,
copy_elements_from_document, create_roof_edge_sweep, place_curve_family_instance,
create_profile_wall com normalização/diagnóstico/verification "sketch", roteiro exige 0.8.43.
ANTES DE FECHAR O REVIT: salvar teste mcp (isModified true, estrutura não salva).

Próximo: health 0.8.43; list_open_documents; `build-purlin-roof.mjs --stage gables`
(diagnóstico); copiar Cumeeira/Rufo/Calha e Tabica do showroom; cumeeira na aresta ridge,
rufos nas verge, calhas nas eave; forro do banheiro: localizar (id antigo 19394071 lido no
showroom — reler em teste mcp), recriar com perimeterGap e tabica hospedada na face inferior.

## Estado anterior — 11/09/2026 ~11h30 (estrutura da cobertura)

Decisões dos usuários: terças apoiadas nos oitões; lambril sobre caibros (sem forro,
estrutura aparente); telha cerâmica capa-canal; junta perimetral lambril × alvenaria;
oitões e beirais resolvidos pela estrutura. Detalhes e geometria: [ROOF_STRUCTURE.md](ROOF_STRUCTURE.md).

Revit CARREGADO ainda 0.8.40/109. PREPARADO 0.8.42, 115 comandos/109 ferramentas, pasta
RevitMCP-local-20260911-112837 (manifesto aponta para ela). 196 testes C#, 26 Node,
check-version OK. Novas: create_framing_member, create_roof_type_variant, plan_purlin_roof,
correção de unidades de seção no set_parameter. Não validadas no Revit.

Modelo nesta etapa (commit conferido, avisos 34): carregadas famílias `_ARQPWR_Viga perfil
retangular` e `_ARQPWR_TELHA Cerâmica Canal` (overwrite false); criados 7 tipos de madeira
19402551..19402563 com Largura/Altura conferidas (lista em ROOF_STRUCTURE.md). Nenhuma peça
estrutural criada. Paredes continuam anexadas ao telhado (serão desanexadas no roteiro).

Após reinício: `node scripts/build-purlin-roof.mjs --stage plan`, depois cada estágio em
dryRun e só então `--commit`, conferindo cortes/3D entre estágios. Validar primeiro
create_framing_member com UMA peça (justificação de caibro inclinado) antes do lote.
Estrutura modelada ≠ estrutura dimensionada: bitolas são provisórias.

## Estado anterior — 11/09/2026 ~11h (sessão encontro paredes/telhado)

Revit CARREGADO: 0.8.40, 109 comandos, build 2026-09-11T13:43:48Z. Documento
`teste mcp` com alterações NÃO salvas no RVT (isModified true).
PREPARADO (não carregado): 0.8.41, 112 comandos C#/106 ferramentas MCP, pasta
C:\Users\Mana02\AppData\Roaming\Autodesk\Revit\Addins\2026\RevitMCP-local-20260911-105507,
manifesto já aponta para ela. Build C#/Node, 187 testes C# (17 novos), 26 Node e
check-version passaram. Nada da 0.8.41 validado dentro do Revit.

Alterações no MODELO nesta sessão (todas conferidas após commit, avisos 34):
1. Paredes externas 19389460/61/62/63 anexadas ao telhado 19401056 (nativo).
   Beirais: topo 2.80 ext./3.00 int., cunha fechada. Oitões sul/norte até 5.80.
   ATENÇÃO: gravado por engano de dryRun (flag em params, ignorado pelo servidor).
   Mantido por seguir a regra "anexação nativa preferida" e geometria conferida,
   mas oitões por anexação NÃO foram decisão deliberada: pedir conferência dos
   usuários; reverter com detach_wall_top (0.8.41) se preferirem oitão independente.
2. Caixa de escopo ESCOPO-INT 16795380 movida (-.2,+1.5,0) m e Altura 7.20: antes
   cortava a cabana em y=7.28 e z=3.41 em 43 vistas e nos níveis. Detalhes SCOPE_BOX.md.
3. Cama 19394063 +.494 m Y e mesas 19401031/32 +.575 m Y: estavam alinhadas à borda
   da caixa de escopo, ~50 cm longe da parede norte; agora encostadas em y=7.80.

Falhas/descobertas: create_profile_wall 0.8.40 falha sempre em verification_failed
(bbox); corrigido na 0.8.41. dryRun em params gravava; MCP floor_type_variant e
set_family_orientation tinham o mesmo defeito — corrigidos. Sem ferramenta para
desanexar — criada detach_wall_top. Sem cálculo de perfil do intradorso — criada
analyze_wall_roof_interface. Sem leitura de caixa de escopo — inspect_scope_boxes.
Ambiente: não há fontes NuGet configuradas; se o restore dos testes quebrar use
`dotnet restore src/RevitAddin.Tests/RevitMCPAddin.Tests.csproj -p:RevitVersion=2026 --source %USERPROFILE%\.nuget\packages`.
Clientes HTTP diretos: passar dryRun como 3º argumento de callRevit/callRevitBatch.

Próximos passos após o usuário reiniciar: health 0.8.41/112; analyze nas 4 paredes
(beirais devem mostrar diferença .20 através da espessura); profile wall isolado
(dryRun, commit em y=-15, leitura, remoção); inspect_scope_boxes deve dar 0 fora;
dryRun de detach/attach via MCP. Depois: estrutura da cobertura (frechal, terças,
cumeeira, caibros, cinta), beiral lateral em balanço, forro sala/quarto e junta
perimetral do forro do banheiro — decisões construtivas com os usuários.
Contrato e evidências: [WALL_ROOF_CONNECTION.md](WALL_ROOF_CONNECTION.md).

## Estado anterior — parede por perfil, 11/09/2026

Preparada0.8.40,109 comandos C#/103 ferramentas MCP. Implementado
create_profile_wall: parede nativa independente com perfil vertical estritamente
convexo, validações e conferência das elevações. Incluídos attach_wall_top e
create_profile_wall no perfil architecture do MCP. Procedimento e limitações:
[WALL_ROOF_CONNECTION.md](WALL_ROOF_CONNECTION.md).

Builds passaram,170 testes C# e26 Node passaram; consistência de versão passou.
NÃO houve validação geométrica ao vivo nem alteração do modelo nesta etapa.
Health ainda0.8.38/107 comandos,build2026-09-11T13:23:32Z.
Manifesto aponta agora para
C:\Users\Mana02\AppData\Roaming\Autodesk\Revit\Addins\2026\RevitMCP-local-20260911-094350.
Recarregar Revit quando o usuário decidir; integração MCP também precisa reler
o catálogo para expor novas ferramentas. Dados históricos abaixo não substituem
este estado. Próxima etapa: validar anexação e perfil no encontro paredes/telhado.

## Oitões: regra definida pelo usuário em 11/09

Preferir anexação nativa quando possível e construtivamente adequada. Quando não
for possível, usar alternativa: nova parede acima da parede existente, com perfil
editado conforme a face inferior do telhado. Não insistir na anexação nem simular
o fechamento com parede retangular atravessando cobertura. Considerar espessura,
folgas e apoios; não assumir que linha de cumeeira externa é o limite da alvenaria.
Ferramenta implementada na0.8.40; validação no modelo ainda pendente.

Preparada versão0.8.39: attach_wall_top (Revit2026), verificação de vínculo e
rejeição de conflito sem substituir anexos existentes; correção tan(ângulo) para
inclinação da cobertura. Builds C#/Node e consistência102 ferramentas/108 comandos
passaram, mas anexação nativa ainda não validada ao vivo.
Cortes criados19401073 transversal e19401082 longitudinal: confirmam oitões abertos
e ausência de estrutura entre paredes e telhado. Nenhuma parede anexada nesta etapa.

## Telhado criado após reinício — 11/09

Health confirmou0.8.38/107 comandos,build2026-09-11T13:23:32Z; RVT salvo antes
da sessão. Criado telhado19401056, tipo genérico9970621 de15.24 cm, footprint
(-.6,-.6)..(6.6,8.6),cumeeiraY,base2.2 m. Inclinação REAL45° confirmada pelas
normais das faces (+/-0.7071,0,+0.7071); topo da cumeeira6.015526 m, intradorso
da cumeeira5.8 m. Ainda sem madeira, material cerâmico ou empenas.

BUG identificado: FootPrintRoof.SlopeAngle recebe rise/run, NÃO radianos.
Confirmado na documentação local RevitAPI.xml, linha210313. Versão carregada
0.8.38 usou entrada compensada57.29577951308232 para gravar slope=1 (45°).
O campo slopeDegrees retornado dessa chamada NÃO é o ângulo físico.
Código agora usa Math.Tan(angle*pi/180) tanto na escrita quanto na verificação.
NÃO repetir entrada compensada após carregar a correção. Nova build preparada
mantém versão0.8.38; distinguir pelo timestamp. Validar nova conversão ao vivo.
inspect_planar_faces funcionou no telhado (8 faces); outras geometrias pendentes.

## Prioridade atual — cobertura e forros, 11/09

Usuário mudou prioridade para macro: telhado, estrutura da cobertura, forro e junta
perimetral. Parar de priorizar acessórios. Autorizou corrigir pacote e depois fará
reinício/recarregamento. Versão preparada agora0.8.38; conferir health após reinício.
CreateGableRoof: inicialização do mapping, validação de4 bordas, tipos e inclinações;
falha original ainda exige reteste real. Não declarar telhado corrigido ao vivo.
CreateCeiling: perimeterGap opcional, somente retângulos alinhados aos eixos,
inset geométrico; não cria perfil de acabamento nem sistema completo de junta.
Forro existente não é atualizado automaticamente. Estrutura de madeira, anexação
das paredes/empenas e materiais da cobertura ainda pendentes. Builds passaram.
Plano de teste: dryRun de telhado, commit controlado com conferência de geometria;
forro retangular com gap2 cm, conferir distâncias até faces acabadas e cortes.
Não tomar2 cm como especificação definitiva do escritório.

## Atualização em 11/09/2026

Health ainda0.8.35/104 comandos. Auditoria dos4 itens passou. Acrescentadas duas
mesas de apoio ao quarto:19401031 e19401032, tipo novo19401029 derivado de
_ARQPWR_MBL Mesa retangular. Medidas45x45x55 cm, origens(.85,7,0) e(3.1,7,0).
Tipo de jantar preservado. Bounding boxes confirmaram dimensões e separação da
cama; captura da vista principal mostra o conjunto, com um apoio parcialmente
oculto pela parede do primeiro plano. Não houve reinício nem gravação do RVT.

## Objetivo e autorização

Lucas é engenheiro; sua esposa é arquiteta. Querem agente + MCP capazes de desenvolver
modelagem e detalhamento a partir do briefing, com conferências dos profissionais.
O trabalho inclui desenvolver o repositório e modelar um projeto piloto no Revit.
Não há autonomia completa implementada. Usuário pediu registrar tudo para continuidade.
Preferência atual: seguir modelando com ferramentas carregadas, acumular correções
do MCP e reiniciar somente depois. Não interromper repetidamente para pedir reinício.
Não houve solicitação de commit/push; alterações de código permanecem locais.

## Ambiente e versões — conferidos ao vivo

- Repo: E:\LUCAS\3-Codes\RevitMCPServer; PowerShell; comandos prefixados com rtk.
- Biblioteca: pasta familias na raiz, aproximadamente450 RFA. Não sobrescrever
  famílias existentes automaticamente; usar overwrite:false ao carregar.
- Revit2026, documento `teste mcp`, C:\Users\Mana02\Desktop\teste mcp.rvt.
- Em10/09 o documento retornou isModified:true. Alterações do modelo ainda não
  estavam todas salvas no RVT. Snapshot JSON não é backup do arquivo Revit.
- Health HTTP127.0.0.1:7891: versão carregada0.8.35,104 comandos,
  build2026-09-10T13:11:50Z. Código e pacote preparado0.8.37:107 comandos C#,
  101 ferramentas MCP,10 comandos ocultos e3 receitas.
- DLL preparada mais recente:
  C:\Users\Mana02\AppData\Roaming\Autodesk\Revit\Addins\2026\RevitMCP-local-20260910-193834.
  Manifesto RevitMCPAddin.addin aponta para ela. Conferir novamente antes de usar.
- Token local em %APPDATA%/Autodesk/Revit/Addins/2026/revit-mcp-token.txt.
  Não imprimir/copiar token para documentação. Cliente Node já sabe lê-lo.
- Integração MCP pode estar com esquemas antigos; revit_batch chama comandos C#
  pelo nome. Isso NÃO carrega comandos de DLL que ainda não foi reiniciada.

## Briefing da cabana

Cabana colonial6x8 m; cozinha, sala e quarto integrados; banheiro privativo.
Cobertura prevista de duas águas, alta inclinação45°, eixo da cumeeira Y,
beiral60 cm. Ainda NÃO há telhado criado. Acabamento colonial/madeira pendente.
Banheiro interno x3.6..5.8,y5.4..7.8 m. Box ocupa faixa norte a partir de y6.7.
Unidades deste documento: metros, exceto indicação explícita. API Double e bbox
retornam frequentemente pés; NÃO confundir com os metros usados na entrada.

## Decisões construtivas do usuário

- Nível de referência não é necessariamente piso acabado: acabamento pode estar
  acima ou abaixo. Alturas de equipamentos devem referenciar o piso local.
- Paredes devem descer até apoio; nesta cabana base-.05, topo2.80 m.
- Acabamento integrado não deve atravessar paredes do banheiro.
- Banheiro pelo menos1 cm abaixo da cabana, com soleira na transição.
- Bancada/armário devem chegar à face da parede; editar medidas da família.
- Rodamãos e saias devem ser habilitados conforme cada encontro, não indiscriminadamente.
- Frente real das famílias e pingadeiras exigem imagem/semântica, não apenas vetor.

## Inventário essencial

| Elemento | ID | Estado/posição relevante |
|---|---:|---|
| N1-AC |3711007| elevação0 |
| N1-ES |3664300| elevação-.05 |
| Paredes sul/leste/norte/oeste |19389460/61/62/63| IDs completos19389460..19389463 |
| Paredes banheiro sul/oeste |19389464/65|15 cm |
| Laje |19389468|topo-.05,base-.15 |
| Acabamento banheiro |19389484|topo-.01,base-.06; sobrepõe laje1 cm |
| Acabamento integrado |19394209|perfil L,topo0 |
| Soleira |19394218|granito,topo0 |
| Portas entrada/banheiro |19389512/19389547|preservar vãos |
| Janelas |19389574/82/84/86|IDs19389574,19389582,19389584,19389586; pingadeiras ainda invertidas |
| Cama/sofá/mesa |19394063/64/66|IDs19394063,19394064,19394066 |
| Bancada cozinha |19394065|tipo19394040,Largura2.35,tampo.90,rodamão1.00 |
| Armário inferior |19396967|2.35x.55x.80; origem(5.8,5.25,0),rotação-90° |
| Refrigerador |19394313|origem(5.02,1.65,0),rotação270° após correção |
| Cadeiras |19394314/15|origens(3.9,1.05,0)/(3.9,2.95,0); rotações180°/0° |
| Cooktop |19394316|origem(5.5,3.35,.90),rotação90°; recorte pendente |
| Forro banheiro |19394071|altura2.60 |
| Chuveiro |19394309|parede leste,ponto(5.8,7.15,2.09) |
| Comando chuveiro |19397980|(5.8,7.4,1.09),afastado da mangueira |
| Espelho |19397967|centro(5.8,6,1.49),diâmetro.75 |
| Lavatório |19399925|inserção(5.8,6,.04),borda~.84=.85 acima do piso |
| Toalheiro |19399942|(5.15,5.4,1.19),parede sul banheiro |
| Box |19400983|tipo19400968 renomeado220 x210 cm; centro(4.7,6.7,-.01) |

Snapshot `snapshots/cabana-2026-09-10.json` contém leitura atual dos principais
elementos, UniqueIds, parâmetros e os34 avisos. Sempre conferir existência e
identidade antes de alterar. O usuário já desfez intervenções acidentalmente;
correções foram reaplicadas após consulta real, sem repetir deslocamentos preservados.

## Não confundir tentativas com elementos existentes

Bacia19394310 e dependentes foram removidos. Cuba Farm foi carregada mas não instalada.
Misturador19399940 foi removido por orientação incorreta. Cópias dos testes de falha
19394246/19394265 foram revertidas; cópia válida19394284 foi removida.
Não há bacia, torneira de lavatório, cuba de cozinha nem telhado concluídos.

## Aprendizados e gaps

- MaxZ da bancada inclui rodamão: cooktop estava a1 m, corrigido para.90.
- FacingOrientation não é frente física. Geladeira e cadeiras precisaram180°.
- Referência interior/exterior da parede não equivale ao interior físico da cabana.
  Nas paredes leste e sul do banheiro, foi usada face API exterior para o interior real.
- Lavatório tem borda cerca de.80 m acima da origem. A inserção a.84 ficou alta;
  mover origem para.04 corrigiu. Não reutilizar esse offset para outras famílias.
- Famílias WorkPlaneBased em plano horizontal rejeitam/ignoram Z sem hospedagem
  adequada. Hospedagem por face de piso/tampo ainda não implementada.
- Janela permite CanFlipFacing, mas versão carregada só oferece flip na criação.
  Espelhamento de janela sem host foi rejeitado e revertido. Não espelhar a parede.
- DryRun NÃO testa commit. Espelhamento passou dryRun e falhou no commit.
- Imagens são essenciais. Exportação3D com recorte/isolate ainda inclui linhas
  distantes. Perspectivas podem cortar objetos e ocultação de host pode não ocultar
  todos os componentes aparentes. Não declarar enquadramento/abertura validados.
- Box: largura externa conferida, abertura útil e puxadores NÃO aprovados.
- Ausência de warnings não prova inexistência de colisões, recortes ou boa circulação.

## Código implementado, ainda aguardando carregamento

0.8.36: create_floor_type_variant copia camadas/materiais, ajusta espessuras e
atribui somente pisos indicados; plano banheiro em FLOOR_TYPE_VARIANT.md.
MoveElement detecta LocationPoint que não obedece translação. SetParameter
regenera antes de devolver texto. CreateGableRoof inicializa ModelCurveArray,
correção candidata de ArgumentNullException, ainda sem reteste real.

0.8.37: inspect_planar_faces com normais/áreas/material e interceptação vertical
no XY; índices transitórios, só faces planas, NÃO são referências para hospedar.
set_family_orientation define facingFlipped/handFlipped, idempotente; valida host
e ponto preservados. Falta validação real após reinício. Corrigido fator do retorno
eye de CreatePerspectiveView (a câmera real já estava correta).

Catálogo semântico consultável, regras genéricas de encontro, superfícies de apoio
automaticamente classificadas e memória universal de projeto AINDA NÃO implementados.
Os documentos são calibrações de trabalho, não um sistema completo de aprendizado.

## Testes e operação

Build C# e Node passaram;170 testes C# e26 Node passaram. São testes existentes,
não validam novas APIs geométricas dentro do Revit. check-version passou101/107.
Auditoria read-only `rtk node scripts/audit-cabana.mjs` passou4 verificações na
última leitura: cooktop, largura armário, rotações das duas cadeiras. Não é QA total.
0.8.35: warnings/rollback testados ao vivo individualmente e batch, sem intervenção
manual; IDs temporários ausentes depois. Política atual reverte TODO aviso de escrita.
34 avisos existentes:33 marcas de tipo duplicadas e1 sobreposição piso/laje.

Comandos a partir da raiz:
```powershell
rtk proxy curl -s --max-time 5 http://127.0.0.1:7891/health
rtk node scripts/check-version.mjs
rtk node scripts/audit-cabana.mjs
rtk dotnet build src/RevitAddin/RevitMCPAddin.csproj -c Release -p:RevitVersion=2026 -p:DeployToRevit=false
rtk dotnet test src/RevitAddin.Tests/RevitMCPAddin.Tests.csproj -c Release --no-restore -p:DeployToRevit=false
rtk proxy powershell -NoProfile -File scripts/stage-local-addin.ps1
```
Em src/McpServer: `rtk npm run build` e `rtk npm test`.
Staging copia DLLs para pasta nova e atualiza manifesto; NÃO substitui assembly
carregada. Nunca executar build com deploy padrão contra DLLs em uso.
No npm ci anterior foram reportadas13 vulnerabilidades; não foi feita correção
automática de dependências. Tratar separadamente, sem npm audit fix --force indiscriminado.

## Próxima retomada

1. Ler este arquivo, confirmar documento/health e consultar elementos antes de agir.
2. Se ainda sem reinício: usar só104 comandos carregados; continuar modelagem e
   inspeção útil, não repetir tentativas de hospedagem já comprovadamente inadequadas.
3. Após reinício escolhido pelo usuário: validar107 comandos e novas ferramentas
   com leituras de face da bancada; conferir inversão de janela por imagem e
   idempotência; ajustar piso banheiro para4 cm mantendo topo-.01 e soleira.
4. Completar banheiro/cozinha, resolver recorte de pedra e drenagem; conferir layout
   da bacia com a nova faixa do box antes de instalar. Retomar telhado e empenas.
5. Implementar catálogo familiar estruturado e regras de apoio/orientação com testes
   executáveis. Atualizar diário e handoff a cada sessão, distinguindo planejado,
   compilado, carregado, validado e pendente.

## Vistas de revisão existentes

19397970 acessórios,19399930 lavatório,19399946 toalheiro,19400986 box próximo,
19401005 revisão3D com enquadramento inadequado,19401016 box frontal.
Alguns elementos estão ocultos apenas nessas vistas; não confundir com exclusão.
Vista principal {3D}=10677109. Não alterar visibilidade global para mascarar conflitos.

## Atualização vigente — 14/09/2026, continuação Codex

- Revit 0.8.59 confirmado carregado; documento projeto Raphael, caminho conferido. RTK indisponível no PATH desta sessão; shell direto utilizado.
- Seis guarda-corpos preservam IDs/UniqueIds e usam tipo 19396700. Todos corrigidos para fase Existente (antes estavam Construção nova).
- IMPORTANTE: o usuário rejeitou a malha geométrica por ficar extremamente pesada. Foi REMOVIDA: não reutilizar scripts/guarda_corpo_tela.mjs nem guardas-continuacao.mjs para reconstruir os fios.
- Estado salvo: apenas tubos inferior/superior 40x40 mm (0,10/1,00 m) e montantes 25 mm, sem tela. Malha horizontal removida; padrão fino com espaçamento 100 m e offsets que anulam sua altura, sem repetição densa. Configuração atual em dados/guardas-config-leve.json. Conferência visual: renders_revit/guardas-leve-3d.png. Arquivo RVT salvo às 20:32:43 UTC.
- Prioridade do usuário: painel leve com TEXTURA de tela e transparência, sem fios individuais. Ainda NÃO implementado. Exige recurso de material/aparência com recorte/transparência e painel acompanhando lances. Não confundir simples transparência uniforme com textura de tela.
- Falhas registradas: corrimão superior contínuo causou aviso de descontinuidade (rollback), inclusive com Transições=0. Solução validada: tubo superior como trilho não contínuo nomeado, usado como referência dos montantes. Material-only variants ainda NÃO validadas no Revit.
- Tipos auxiliares: balaústre fino 19396687 (não usar para malha densa), perfil tubo 19396691, perfil fio 19396695 (não usado), corrimão contínuo 19396698 (não usado), guarda-corpo 19396700.
- 49 avisos antes/depois da aplicação original. Dados com UniqueIds em guardas-verificacao-final.json; este snapshot antecede a remoção da malha, mas IDs/tipo/fase foram preservados.
- Preparada ampliação 0.8.60 de create_wall_type_variant: layerMaterialIds por camada, null preserva material, dispensa mudar espessuras. Não é ferramenta de pintura de faces nem de textura/aparência. A textura da tela continua exigindo ampliação adicional; ideal acumular antes de outro reinício.
- Build C# sem avisos/erros, 224 testes C# e 44 Node passaram; build Node e check-version passaram. Testes não equivalem a validação de materiais ao vivo.
- Demais pendências 2–6 continuam abertas. Não declarar modelagem finalizada. Conferir bordas abertas do patamar L3 ao complementar os painéis leves.

## Atualização vigente — 14/09/2026 16:40, hachura autorizada

- Usuário autorizou prosseguir e esclareceu: a tela pode ser uma HACHURA CRUZADA. Essa é a solução prioritária; não precisa de imagem externa, não reconstruir fios.
- Preparada 0.8.61: create_material com hachura de modelo (duas direções perpendiculares), cor/transparência gráfica; create_direct_shape com painel planar convexo; inspect_planar_faces com includeEdgeLoops para contornos reais. Incorpora materiais por camada da 0.8.60.
- Build C# sem avisos/erros, 224 testes C# e 44 Node, build Node, check-version e syntax check do roteiro passaram. 139 ferramentas MCP, 142 comandos C#.
- AINDA NÃO CARREGADA/VALIDADA NO REVIT. Modelo permanece salvo com tubos e montantes, sem painéis; documento correto conferido isModified:false nesta etapa.
- Próximo: reiniciar Revit com 0.8.61 e seguir docs/RAILING_HATCH.md no repositório. Script scripts/prepare-railing-hatch.mjs faz dryRun do material e exporta bordas dos tubos; --apply-material cria apenas o material, não painéis. Rodar da cópia do repositório (import relativo).
- Criar primeiro painel piloto entre tubos reais; validar hachura cruzada 5 cm a 45°, cinza e transparência gráfica 80% (ajustáveis) antes de replicar. Corrigir fase para Existente. Hachura é gráfica, não vazado físico nem textura para render; usuário aceitou essa representação.
- Materiais e pendências 3–6 seguem abertos. Não declarar modelagem concluída. Pequena tentativa read-only get_version retornou unknown_command; nome correto para versão do produto é get_revit_version, versão do add-in via health.

## Atualização vigente — 16/09/2026, pranchas Raphael
Revisão gravada no RVT com add-in 0.8.65. Quatro A1: LEV-01 subsolo/térreo (19413786), LEV-02 P1/P2 (19417434), LEV-03 cobertura/AA/BB (19429040), LEV-04 fachadas (19429046). Plantas 1:50; cortes/fachadas 1:100. Detalhados pilares do subsolo e corte BB; chamadas/cotas das fachadas e AA reorganizadas. PDFs de revisão em HANDOFF_AGENTE/REVISAO_PRANCHAS_2026-09-16. 33 avisos preexistentes ao salvar.
PENDENTE: marcas AA/BB não aparecem nas plantas baixas, apenas na cobertura; notas nas LEV-01/02 encaminham à LEV-03. Sobreposição preexistente de etiquetas no térreo 19416503/19421225 preservada para revisão pontual. Não declarar documentação finalizada. Ver diário RAPHAEL_PRANCHAS_2026-09-16.md para IDs, validações e tentativas que não resolveram. Roteiros não idempotentes: não executar cegamente.

## Refino gráfico — 16/09/2026, correção após crítica do usuário
REGRA EXPLÍCITA: sempre tirar print e revisar. Não considerar encaixe na prancha ou relatório de caixas como aprovação gráfica. Conferir vista ampliada e folha inteira após mudanças, corrigir e repetir.
Chamadas de AA/BB e fachadas refeitas com joelhos ortogonais, trechos horizontais, colunas alinhadas (direita alinhada à direita). Altura do joelho depende do número de linhas e do lado de ligação; lado direito da nota liga na última linha, esquerdo na primeira. Preview fornece altura da caixa. Evitar que leaders atravessem valores de cotas, níveis e etiquetas, além de impedir caixas de texto sobrepostas.
Aplicada região .TERRENO 18619419 (Earth 45 Small) sob pisos em AA (19429157/66/75) e BB (19429184). Representação gráfica de solo, não camada geotécnica levantada nem fundação dimensionada. Etiqueta Garagem AA afastada da linha de nível. Chamada piso BB transferida à direita após print revelar linha sobre área da garagem. Pontas de chamadas AA corrigidas para tocar guarda-corpo/escada. Faixa esquerda de texto da fachada frontal afastada das cotas.
Capturas revistas: E:/lidar/trabalho/revisao-visual-*.png, refino-19413769.png e refino-LEV-03/04.png. Caixas das quatro vistas sem pares sobrepostos na última consulta; não substitui conferência dos leaders por imagem. PDFs LEV-03/04_REVISAO atualizados, RVT salvo, sem exigir reinício. Pendências anteriores de marcas AA/BB nas plantas continuam.
Scripts: refino_grafico_0916.mjs, solo_cortes_0916.mjs, refino_pontas_0916.mjs, exportar_refino_0916.mjs. Não reexecutar cegamente: criam/substituem elementos.

## Preparação 0.8.66 — tabelas nativas e implantação (18/09/2026)

- A tentativa anterior de representar o quadro de esquadrias com TextNotes foi removida. Regra: quadro quantitativo deve ser `ViewSchedule` nativo, nunca texto desenhado.
- Tabelas nativas existentes no RVT: portas 19429657, janelas 19429664 e portão 19429671. O portão é DirectShape e, por isso, permanece em uma tabela nativa separada por instância.
- Preparada 0.8.66: `configure_schedule` ganhou agrupamento, estilos e formatação de colunas; `place_view_on_sheet` aceita `ScheduleSheetInstance`; novos comandos `create_drafting_view` e `place_image_in_view` criam a vista de implantação e vinculam a base raster.
- Base obtida do Google Maps para o endereço Av. Presidente Marques, 1658, com créditos Google/Airbus preservados: `E:/lidar/trabalho/google-maps-implantacao-limpa.png`.
- O roteiro inicial com imagem foi removido após a correção do usuário. Usar somente `scripts/model-raphael-site-plan.mjs` como registro do processo vetorial; ele não é idempotente.

### Correção após validação do usuário — implantação modelada

- A implantação **não deve conter a imagem do Google Maps**. A tentativa com DraftingView/imagem e sua LEV-05 foram apagadas integralmente. Google Maps serve somente como base para orientação, escala aparente, lote e sistema viário.
- Implantação final usa a vista de modelo 18670676, renomeada `AS-BUILT - PLANTA DE IMPLANTAÇÃO`, escala 1:100, sem nuvem e sem marcas de corte. Rua, passeio, meio-fios e áreas externas são elementos do modelo; limite aparente do lote, eixo viário, notas e norte são vetores/anotações do Revit.
- LEV-05 final 19429810, viewport 19429816. Quadros nativos já existentes e vinculados ao modelo: portas 18131869 (instância 19429817) e janelas 18131858 (instância 19429818). Não foram desenhados com TextNotes.
- Print final revisto: `E:/lidar/trabalho/lev05-final.png`. PDF: `HANDOFF_AGENTE/REVISAO_PRANCHAS_2026-09-18/LEV-05_IMPLANTACAO_E_ESQUADRIAS_REVISAO.pdf`.
- Script válido: `scripts/model-raphael-site-plan.mjs`. Não reexecutar: cria anotações e uma nova LEV-05.

## Implantação calibrada pelo KMZ — 18/09/2026

- Fonte recebida: `C:/Users/Mana02/Downloads/terreno.kmz`, com 7 LineStrings: lote, contorno auxiliar da edificação, uma divisão interna e quatro alinhamentos da via.
- O lote do KMZ mede aproximadamente 42 m de profundidade. Por instrução do usuário, as divisas laterais foram mantidas paralelas às laterais do prédio e a largura foi calibrada pela edificação modelada: 9,70 m.
- Contorno adotado na vista: (-4,291; 5,670), (37,395; 5,670), (34,285; -4,030), (-7,837; -4,030) m. A testada e o fundo permanecem inclinados conforme o KMZ.
- Rua modelada novamente como pisos poligonais reais: passeio próximo com faixa gramada/acesso, pista em asfalto 15 cm e passeio oposto. Material 19429882; tipo de piso 19429900. Pisos novos 19430015/22/31/38/47/55, todos em Existente.
- Meios-fios inclinados modelados em DirectShape 19430077/78.
- Muros de divisa modelados em alvenaria 15 cm: lateral norte 19430073, lateral sul 19430074, fundo inclinado 19430075 e complemento frontal sul 19430076. Base N1-QUINTAL, altura 3,35 m, fase Existente.
- LEV-05 preserva tabelas nativas de portas e janelas. Viewport 19429816 reposicionado após revisão por print; não cruza as tabelas e o título está dentro da margem.
- Prints finais revistos: `E:/lidar/trabalho/implantacao-kmz-muros.png` e `E:/lidar/trabalho/lev05-kmz-muros.png`.
- PDF: `REVISAO_PRANCHAS_2026-09-18/LEV-05_IMPLANTACAO_KMZ_MODELADA_REVISAO.pdf`.
- RVT salvo como `Lev-01-260916.rvt`. Após as alterações resta 1 aviso preexistente: elementos 19387843/19390004 unidos sem interseção.
- Scripts não idempotentes: `update-raphael-site-from-kmz.mjs`, `model-raphael-site-surfaces-kmz.mjs`, `model-raphael-boundary-walls-kmz.mjs`. Não reexecutar cegamente.

### Correção da testada — alinhamento de calçada, acesso e muros

- Após revisão por print, a linha do lote e o piso externo estavam alguns centímetros fora do mesmo alinhamento. A testada foi recalculada diretamente na linha do passeio do KMZ: x=-7,731 em y=-4,03 e x=-4,344 em y=5,67.
- A faixa de acesso externo agora usa exatamente os limites do piso interno: y=-3,80..3,00. O meio-fio próximo foi dividido em dois trechos, deixando o vão do acesso sem ressalto.
- Foi criado um piso trapezoidal de ligação entre a calçada/rampa interna em x=-4,50 e a divisa inclinada. Não há mais vazio triangular entre piso interno e passeio público.
- Muros frontais norte e sul foram recriados no mesmo alinhamento das faces externas das divisas. IDs: norte 19430175, sul 19430176.
- Novos pisos da faixa externa e ligação: 19430179, 19430187, 19430194, 19430203. Meios-fios divididos: 19430208/19430210. Todos em fase Existente.
- Limites gráficos novos: 19430167/172/173/174, coincidentes com a geometria modelada.
- Prints revistos: E:/lidar/trabalho/implantacao-kmz-alinhada.png e E:/lidar/trabalho/lev05-kmz-alinhada.png.
- PDF: REVISAO_PRANCHAS_2026-09-18/LEV-05_IMPLANTACAO_KMZ_ALINHADA_REVISAO.pdf.
- Script não idempotente: scripts/align-raphael-frontage-kmz.mjs.
