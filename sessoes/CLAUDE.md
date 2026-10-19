# Regras - Revit + pyRevit (Claude Code) - TMS Projetos

Usuário: Toshio Sasaki (TMS Projetos e Consultoria de Engenharia).
Consolidado em 08/10/2026 a partir dos trabalhos feitos no Cowork (Project "Pyrevit"): 26-094, 26-100, 26-101, 1924 SENAC e teste viga/pilar.
Complementa o `~/.claude/CLAUDE.md` global (idioma, travessão, mudança mínima, Word nativo).

---

## 1. Início de toda sessão (obrigatório)
1. `get_revit_status`: dizer qual documento está ativo nesta porta. Se não for o esperado, parar e perguntar.
2. Identificar o projeto e ler o .md dele em `..\..\docs\` antes de mexer.
3. Confirmar o arquivo de trabalho: o **arquivo oficial do usuário** (não cópias de estudo), salvo quando o usuário mandar trabalhar numa cópia.
4. Nada de alteração no Revit sem pedido. Primeiro `get_*` e conferência; só depois modificar.
5. **OBRIGATÓRIO EM TODOS OS CHATS, NAS DUAS PORTAS (48884 e 48885) - regra do usuário de 17/10/2026:** logo no início, **pedir a permissão para assumir o controle do Revit** (computer-use: `request_access` com "Revit 2026"). Com ela, toda janela/diálogo que aparecer (aviso, "OK", "Cancelar", erro, salvar) é tratada na hora por mim, olhando a tela e clicando, sem ficar parado esperando o usuário ver a janela. **O controle é de reserva:** o usuário continua usando o computador; só olhar/clicar no Revit quando o conector travar (suspeita de janela aberta). Não tirar prints nem mexer na tela fora disso. Ao suspeitar de travamento (conector sem resposta por mais de 30 s), primeiro tirar um print do Revit e resolver a janela. Avisos benignos: OK; erro que não pode ser ignorado: Cancelar (a transação é desfeita) e corrigir o script. Nunca usar `open_application` no Revit (abre outra instância); só clicar nas janelas do Revit já aberto. Motivo: em 17/10/2026 perdemos cerca de 1 h porque uma janela do Revit esperava "OK" e ninguém viu.

## 2. Regras gerais de trabalho
1. Português do Brasil. Nunca usar travessão longo.
2. Unidades padrão Eberick: kN, kN/m, kN/m² (não kPa), kN.m; peças e seções em cm; aço em mm; comprimentos gerais em m; tensões em MPa.
3. Citar a fonte de toda afirmação técnica (norma + seção). Não inventar valores de tabela. Hipóteses sempre declaradas como hipóteses.
4. Mudança mínima: alterar só o necessário; reaproveitar scripts e dados existentes.
5. Confirmar antes de: excluir elementos do usuário, apagar/recriar em massa, sobrescrever .rvt, enviar algo para fora do computador.
6A. **Scripts longos salvam aos poucos (regra do usuário de 17/10/2026):** gerador de folhas/vistas grava o modelo (`doc.Save()`) **a cada prancha concluída** (ou a cada 2 vistas) e escreve um arquivo de progresso; se o Revit travar, perde-se no máximo a prancha em andamento. Nada de lotes que só gravam no fim. **Calma com o Revit:** em projeto pesado, comandos pequenos (dividir por região/prancha), um de cada vez, esperar terminar e conferir antes do próximo; nunca dois processos no conector ao mesmo tempo; exportar imagem só quando preciso e em tamanho moderado.
6. Salvar: só quando o usuário pedir ou concordar. **Salvar ANTES de alterar** um modelo que já tem trabalho; backup (cópia do .rvt) antes de fases grandes.
7. Ao terminar cada fase: revisar o próprio trabalho lendo de volta do modelo, listar o que corrigiria (lista objetiva) e só então salvar.
8. Arquivo recebido de terceiros fica intacto: trabalhar em "Salvar como" ao lado (sufixo, ex. `-ANALITICO`).
9. Decisões padrão tomadas sem resposta do usuário: registrar como tal no .md do projeto.
10. Ao concluir uma etapa, atualizar o .md do projeto em `docs\` (decisões, status, pendências).
11. Memorial em Word: equações nativas (OMML), subscrito/sobrescrito nativos, sem pendências no texto.

## 3. Portas e instâncias do Revit
1. 1º Revit aberto = porta 48884 (projeto PYREVIT-48884); 2º = 48885 (PYREVIT-48885). A ORDEM de abertura define a porta.
2. Nunca abrir o mesmo .rvt nas duas instâncias.
3. Não usar conectores KoreOS (`revit-2026`/`revit-2025`, portas 7891/7890) com 2 Revit abertos.
4. Não abrir aplicativos que criem outra instância do Revit (ex.: "Revit Viewer 2026").

## 4. Conector pyRevit (`execute_revit_code`) - lições técnicas
1. IronPython 2.7: sem f-strings, sem pathlib, sem type hints. `"{}".format()`.
2. **Não abre transação sozinho**: abrir `DB.Transaction` no script (Start, Commit; RollBack no except). Usar pré-processador de falhas silencioso.
3. `import System` para `DB.ElementId(System.Int64(n))`. ElementId: `.Value`.
4. Nome: `DB.Element.Name.GetValue(e)` ou parâmetro ALL_MODEL_TYPE_NAME.
5. Acentos: prefixo `u""`, `.encode("utf-8")` no print, `unichr()`/`\u00ed` no código; arquivos com acento no caminho: localizar por prefixo com IO.Directory.
6. `get_revit_view` falha com nome de vista acentuado: renomear temporariamente ou `doc.ExportImage`.
7. Avisos e diálogos travam o conector: IFailuresPreprocessor. Lotes grandes passam de 30 s mas terminam; conferir depois. Tarefa longa sem transação (ex.: ler centenas de .rfa): rodar em segundo plano com `UIApplication.Idling` + `DialogBoxShowing` (OverrideResult(1)) e arquivo de progresso, modelo em `estudos\_catalogo_familias\extrair_idling.py`.
8. Funciona sem documento aberto: criar projeto do template com `app.NewProjectDocument` + `SaveAs` + `__revit__.OpenAndActivateDocument`.
9. **Derrubam o Revit:** `Beam.AddFeature(BeamShortening)`; mudar BoltLocation + `Update()` de junta Advance Steel; trocar ligação parafusada por solda pela API (fazer pela interface).
10. Não iterar `GetFilters()` em todas as vistas. Linhas de detalhe são categoria de modelo (excluir `OwnedByView` ao isolar).
11. Viewport: apagar e recriar para trocar de folha. Tabela na folha: `ScheduleSheetInstance.Point`.
12. Ambientes: `NewRoom(fase)` + `NewRoom(room, circuito)` (NewRooms2 cria na "Fase 1"); linhas de separação com PHASE_CREATED correto.
13. Rotinas auxiliares: `C:\Users\toshi\claude_*_lib.py` e `MCP REVIT\estudos\<projeto>\scripts\`. Carregar com `g=dict(globals()); exec(open(path).read(), g)`.
14. **Famílias (regra de 12/10/2026):** antes de criar qualquer família, consultar (pelo catálogo da regra 15) as bibliotecas `C:\Users\toshi\TMS PROJ\TMS PROJETOS E CONSULTORIA DE ENGENHARIA - 1-PROJETOS\Famílias Revit` e `C:\Users\toshi\OneDrive\1-ARQUIVOS EMPRESARIAIS\2-Arquivos de apoio\5-Famílias Revit` e reaproveitar o que existir. **Toda família criada precisa ser paramétrica**: planos de referência, cotas rotuladas com parâmetros (dimensões que mudam a geometria) e tipos editáveis; nada de geometria fixa com parâmetros só informativos. Salvar a família criada em `MCP REVIT\estudos\<projeto>\familias\`.
15. **Catálogo de famílias (regra de 13/10/2026, vale para 48884 e 48885):** toda busca de família começa pelo índice compacto `..\docs\CATALOGO-FAMILIAS-TMS-INDICE.md` (uma linha por família, por categoria; Grep por palavra-chave), depois `CATALOGO-FAMILIAS-TMS.md` (Grep `| id |`: tipos com dimensões, materiais, fabricante, se é paramétrica) e, para detalhe completo, `CATALOGO-FAMILIAS-TMS.json` (todos os parâmetros de todos os tipos; Grep por `"id": NNN`). Não varrer as pastas das bibliotecas nem abrir .rfa só para ver o conteúdo. Se a biblioteca mudar, atualizar o catálogo (`estudos\_catalogo_familias\README.md`).

## 5. Modelagem física (concreto)
1. Template: `...\2-Arquivos de apoio\5-Famílias Revit\0-Templates\Concreto Armdo - Alvenaria\Template Concreto.rte`.
2. Tipo "25 CA-50" do template tem Ø32 mm (erro): não usar sem corrigir para 25 mm.
3. Cobrimento: tipo "II. (Viga/Coluna)" = 30 mm (CAA II, NBR 6118:2023 Tab. 7.2). Material "Concreto, Moldado in loco - C30" (ou a classe do projeto).
4. Armadura: depois de lançar, conferir por código que nenhuma barra sai do concreto (ganchos para dentro).
5. Armadura reconstruída ou aproximada (sem detalhamento real) deve ficar identificada como tal (marca/comentário) e nunca ser apresentada como detalhamento do Eberick.
6. Armadura mínima (referência NBR 6118:2023): vigas ρmín Tab. 17.3 (17.3.5.2.1), estribos ρsw,mín = 0,2·fct,m/fywk (17.4.1.1.1), smáx 18.3.3.2, pele só h > 60 cm (17.3.5.2.3); pilares As,mín = 0,15·Nd/fyd ≥ 0,4 %·Ac (17.3.5.3.1), Øl ≥ 10 mm (18.4.2.1), estribos 18.4.3, proteção contra flambagem 20·Øt (18.2.4).

## 6. Modelo analítico (para solver)
1. **O analítico se encontra com o analítico:** sem gaps; pontas levadas ao eixo do apoio (tolerância 0,5 a 0,6 m) ou estendidas até o primeiro apoio. Sem rigid links; excentricidades não modeladas, salvo pedido.
2. Físico não é mexido para ajustar o analítico.
3. Hierarquia de apoio (metálica): viga de concreto > perfil principal > secundário > travamento.
4. Barras inteiriças do início ao fim da peça, exceto onde o usuário mandar fragmentar; pontas no corpo de outra barra: avisar que o solver precisa gerar os nós de interseção.
5. Perfis metálicos com pontas rotuladas (aço-aço e aço-concreto), salvo pedido.
6. Seção, forma da seção e material obrigatórios em toda barra; material de concreto com Ecs e propriedades da NBR 6118 (8.2).
7. Pisos que só distribuem carga: painel marcado "SÓ PORTADOR DE CARGA"; avisar para converter em distribuição de carga no solver.
8. Cargas: caso, natureza e valores característicos declarados; listar o que NÃO foi incluído (peso próprio, vento, combinações etc.).
9. Revisão final lida de volta do Revit: pontas soltas, sobreposições, barras sem seção/material, grupos desconectados, apoios.
10. **Envio ao Robot (regra de 16/10/2026, padrão TMS):** o vínculo Revit -> Robot não atribui seção (nem pelo nome da base, nem pela "Chave do nome do corte") e manda painel como painel. Depois de CADA envio: (a) gerar no Revit o CSV das barras analíticas (pontas em m; id do tipo; id da barra); (b) rodar no Robot, por COM, o script de ajustes (modelo: `estudos\26-102-isometal\scripts\robot_ajustes_v2.py`), que identifica cada barra pela geometria e atribui **seção da base com geometria e forma** (BRPFF para U/Ue/L de chapa dobrada; BrPro para laminados, L por medida e peso); montantes e diagonais em par de cantoneiras (treliça dupla) entram como **UMA cantoneira simples da base com ÁREA mais próxima da SOMA das duas, normalmente inferior** (o Robot não calcula barra dupla; procurar nas duas bases a maior área que não passe da soma); se o perfil real não existir na base, usar o **perfil INFERIOR** ("se o inferior passa, o real passa"); só sem equivalente razoável usar seção paramétrica com a medida real (também tem forma); barras de contravento e tirantes ficam NORMAIS (tração e compressão), sem "só tração", salvo pedido; dividir as barras nos nós internos (tolerância de 5 mm: nós a 2 a 5 mm do eixo da barra, como as terças, também são divididos) mantendo as liberações nas pontas; **antes de calcular, rodar `robot_varredura.py` (conectividade, nós sobre barra sem ligação, nós quase coincidentes) e `estabilidade.py` (autovalores da rigidez: modo < 1e-7 = mecanismo)**; **depois da divisão, fundir os "tocos" de banzo** (nó só de diagonal -> nó do montante mais próximo, até 150 mm; pares de até 10 mm entre juntas; 25 mm junto a apoio) com `estudos-102-isometal\scripts
obot_fundir_tocos.py` (modos plano / teste / aplicar; religa por StartNode/EndNode e transfere as forças nodais; salva em nome novo); painéis "SÓ PORTADOR DE CARGA" = **revestimento** (cobertura: direção das terças; laterais e tetos: direção dos pilares/tesouras; oitões: duas direções). (c) Antes: perguntar se o usuário salvou o que fez no Robot; salvar cópia do envio cru; gravar o resultado com nome novo (nunca sobrescrever .rtd sem confirmar).

## 7. Pranchas e documentação (regras definidas no 1924; usar como padrão TMS)
1. Notas e legendas acima do carimbo, na largura da tabela de revisão (~179 mm), empilhadas do topo; se não couber, nova coluna à esquerda. Nada de nota solta na folha.
2. **REGRA UNIVERSAL - ZERO SOBREPOSIÇÃO** (vale para toda vista, detalhe, corte, planta, 3D e prancha): nenhum texto, nota, indicação, tag/identificador, cota, nível, bolha, linha de chamada (leader), título de vista ou tabela passa por cima de outro, nem cruza outra linha de chamada, nem fica sobre o carimbo ou a moldura. Como cumprir:
   - Notas de detalhe em colunas laterais empilhadas pela altura real do texto (TextNote.Height), com folga entre elas; pontas das chamadas de cada coluna alinhadas na MESMA vertical e na MESMA ordem das camadas (assim as linhas nunca se cruzam). Ponta escalonada só se for provado que não cruza.
   - Camadas finas (mm) em escala que as separe (detalhe 1:5 ou maior); cotas fora do caminho das chamadas.
   - Identificador que não cabe no ambiente vai para fora com linha de chamada; cotas fora dos identificadores e da linha de corte.
   - Título de vista curto (uma linha); conferir o rótulo da viewport (LabelOffset) e a distância para a tabela/vista vizinha.
   - Conferência obrigatória: exportar a imagem da vista E da prancha em alta resolução, ampliar as regiões densas e só então entregar. Achou sobreposição, corrige antes de relatar.
   Nada dentro do carimbo.
3. Formato padrão A1 (841x594); A0 (1189x841) só se não couber.
4. Folhas numeradas em sequência, sem sufixo; "NÚMERO DE FOLHAS" atualizado; revisão aplicada em todas.
5. Cores de filtros: sem repetir, escuras e de alto contraste, sem amarelo, verde só escuro. Toda vista com perfis mostra as cores da legenda.
6. Títulos de vista curtos. Tabelas de quantidades numa folha só.
7. Ao editar notas, manter a formatação do próprio texto (FormattedText).
7A. **SÓ O QUE INTERESSA EM CADA VISTA DE DETALHE (regra do usuário de 17/10/2026, 48884 e 48885):**
   - detalhe de **fabricação** de peça: só a peça e o que é soldado nela;
   - detalhe de **treliça**: só as barras da treliça;
   - detalhe de **ligação/conexão**: só as barras conectadas e a conexão em si.
   - Todo o resto fica oculto. Cortes e vistas de **forma** e plantas de **locação** de ligações podem mostrar tudo.
8. **Padrão TMS de detalhamento (todos os projetos):** `..\docs\PADRAO-DETALHAMENTO-TMS.md` (o QUÊ: folhas, nomenclatura, conteúdo por ligação, notas, legendas, solda, verificação), com o "como fazer" em `..\docs\ROTEIRO-DETALHAMENTO-ACO.md` (técnicas, scripts, erros e acertos). Skill `padrao-detalhamento-tms`.
9. **REGRA PERMANENTE - VERIFICAÇÃO APÓS EDIÇÃO:** depois de qualquer edição em vistas, folhas, notas, tabelas, cotas, símbolos ou indicações, rodar o agente **`verificador-revit`** (informando a porta e as folhas alteradas) e corrigir tudo o que ele apontar antes de relatar ao usuário. Ele usa `C:\Users\toshi\claude_verif_lib.py` (referências detalhe/folha, sobreposições, chamadas cruzadas, margens, acentuação, total de folhas, revisões) e confere por imagem em alta resolução.
10. Tabelas e textos que citam detalhe/folha devem ser **gerados a partir do modelo** (números lidos das viewports), nunca digitados; após renumerar folhas, regenerar e conferir (ex.: `estudos\26-083\scripts\tabela_ligacoes.py`).
11. **COMPATIBILIZAÇÃO SIMULTÂNEA (regra de 13/10/2026, 48884 e 48885):** cada projeto tem `docs\<proj>-DECISOES-VIGENTES.md` (decisões em vigor, regras externas, termos obsoletos; atualizar NA HORA de cada decisão) e `docs\<proj>-REGISTRO-ALTERACOES.md` (uma linha por ação). Ao fim de cada etapa: gerar o retrato do modelo (`dump_estado.py`, modelo em `estudos\26-100-apto-tipo\scripts\`) e disparar em segundo plano o agente **`compatibilizador-revit`** (só arquivos, não usa o conector, roda junto com o trabalho); corrigir o que ele gravar em `docs\<proj>-PENDENCIAS-COMPAT.md`. Texto criado ou editado é checado contra sobreposição na hora (`anot_lib.py`: empilhar + checar; `trocar_textos.py`: troca com FormattedText + checagem); o que a biblioteca não enxerga (carimbo, tabela de revisões, desenho) vai por imagem.

## 8. Projetos e documentos
Pasta: `C:\Users\toshi\OneDrive\1-ARQUIVOS EMPRESARIAIS\1-Projetos\CLAUDE\MCP REVIT\docs\`

| Projeto | Documento | Regras específicas |
|---|---|---|
| 26-094 Silo Pedra Preta (CAS) | `26-094-HANDOFF-REVIT-CLAUDE-PESSOAL.md` | Seção 9 do handoff: decisões que não podem ser desfeitas |
| 26-083 Laje alveolar / pré-moldado (André Rachid) | `26-083-LAJE-ALVEOLAR-PRE-MOLDADO.md` | Trabalhar no R02 oficial (sem worksharing) |
| 26-100 Apto tipo | `26-100-APTO-TIPO-ESTUDO-DXF.md` | Trabalhar no R00 oficial |
| 26-101 Bunge Rio Grande, peletização | `26-101-PELETIZACAO-ANALITICO.md` | Decisões de 08/10 (cantoneiras fragmentadas, demais inteiriças) |
| 26-103 Mezanino Helber (estudo de pé-direito) | `26-103-MEZANINO-HELBER.md` | Piso a piso 3,50 m; modelo pelo MD R03 (laje de vigotas sobre as vigas, pilar central); PB e chumbadores são hipótese |
| 26-055 Tombador COFCO (CAS), cobertura metálica | `26-055-TOMBADOR-COFCO.md` | Vistas/filtros "MEM -" no modelo; pré-memorial antes x depois; pasta de estudo compartilhada com a sessão do Robot |
| 26-088 Porta Roll (loja RG Cell, Várzea Grande Shopping) | `26-088-PORTA-ROLL.md` | Base colada sem chumbador (Caderno VGSH); chapa 300x300x10; não alterar o carimbo sem ordem |
| 25-112 Casa Thyago (Granatti), cobertura metálica | `25-112-CASA-THYAGO-COBERTURA-METALICA.md` | Arquivo oficial na TMS PROJ (R01); levantamento de 16/10 com pendências |
| 26-090 LEM (CAS/Bunge), armazém graneleiro 48 x 170 m | `26-090-LEM-CAS.md` | Oficial: `26-090-LEM-CAS_ R1.rvt` na TMS PROJ; `LEM.rvt` em 01-ARQ DE REFER fica intacto |
| 1924 SENAC Tangará, metálica | `1924-SNC-TGA-LEVANTAMENTO-MODELO.md`, `1924-SNC-TGA-PLANO-FABRICACAO.md` | Regras 0 a 13; ligações para solda pela interface |

Scripts e dados: `C:\Users\toshi\OneDrive\1-ARQUIVOS EMPRESARIAIS\1-Projetos\CLAUDE\MCP REVIT\estudos\`.
Projeto novo: criar `docs\<código>-<nome>.md` com pedido, arquivo oficial, decisões, status e pendências.
