# Roteiro e regras de detalhamento de peças de aço (Revit) - TMS Projetos

Versão 1, 09/10/2026. Base: detalhamento das ligações metálicas do 26-083 (folhas 12, 13 e 14, sessões de 04 e 05/10/2026 e de 09/10/2026), mais as regras do 1924 SENAC.
Usar como documento inicial sempre que for detalhar uma peça de aço. Complementa `sessoes\CLAUDE.md` (seção 7, pranchas) e a regra universal de zero sobreposição.

---

## 1. Histórico do 26-083: o que foi pedido, o que foi proposto, erros e acertos

### 1.1 Concepção da ligação (04 e 05/10, sessão de cálculo)
| O usuário pediu | O Claude propôs | Resultado |
|---|---|---|
| Melhorar ψA,V de uma placa com 8 chumbadores D19 (programa de placa base) | Mostrou que o programa modelava uma borda fictícia; a verificação real é a ruptura da borda lateral | Aceito; levou a mudar a solução |
| Alternativas: assento, cantoneira, console, pilarete | Ligação articulada; barras passantes verificadas ao efeito pino e ao corte (NBR 8800, 6.3.3.2) | Pilarete recusado ("não tem como"); barras passantes aceitas após discussão |
| Conjunto 2x5, no máximo 2x6 se A36 atender | 10 barras Ø20 A36 em 2x5, lisas no pilar, rosca só nas pontas, taxa 0,70; gabarito 80, passo vertical ≥ 120, primeira fileira ≥ 120 do topo do pilar | Adotado |

Erro: o Claude assumiu o pilar como 25x50; o usuário corrigiu duas vezes (face de 40, espessura atravessada de 250). **Regra: confirmar as dimensões reais do apoio antes de calcular.**

### 1.2 Detalhamento no Revit (05/10)
| O usuário pediu | O Claude propôs ou fez | Avaliação |
|---|---|---|
| Detalhamento executivo da prancha 12 (cotas, indicações, nomenclaturas) | Marcas curtas (VM, VS, CH, L, PAR, S, RC) com legenda de peças e notas gerais | Acerto, depois de corrigir a sobreposição |
| Cotas em mm com o estilo criado pelo usuário | Converteu 22 cotas; 6,35 continuou saindo "6.4" | Parcial: conferir as casas decimais do estilo |
| "Há muita sobreposição das indicações" | Trocou textos longos por marcas e mandou a descrição para a legenda | Erro na 1ª entrega; corrigido |
| Vista só da placa base (fabricação) | FAB - CH-01: corte com profundidade de 30 mm, resto oculto, furos e linhas de centro cotados | Acerto |
| Cotas de recorte na vista de fabricação da VS | Cotas de recorte RC-1 | **Elogiado** ("é interessante esse tipo de cota em vistas de fabricação") |
| Fabricação da ligação tipo 2 | Só pôs uma nota de diferença | **Erro grave, criticado.** Todo tipo diferente ganha a sua própria vista de fabricação |
| Solda de campo, não de oficina | Mudou só os textos | **Parcial:** os elementos de solda do modelo continuam como "Oficina" |
| Um isométrico travado por tipo de ligação | Duplicou um 3D já travado e cortou a caixa pela alma da VM | Acerto |
| Anotar isométricos | Anotou uma vista não travada e teve de apagar tudo | **Erro.** Regra: travar antes de anotar |
| Símbolo de solda | Não conseguiu inserir o "Weld Symbol" (o add-in desfazia a transação a qualquer aviso) | **Pendente.** Ficou um Weld Symbol órfão no modelo |
| Padrão ABNT, eletrodo revestido E70XX, parafuso A36 passante, furo Ø22 (folga 2 mm) | Notas gerais e legenda reescritas com essas regras | Acerto |

### 1.3 Interpretação errada da placa base (identificada em 09/10)
- As vistas "DET - PLACA BASE TIPO - 1" e "TIPO - 2" foram tratadas como duas vistas (lateral e frente) da **mesma** placa, sem confirmar com o usuário. O usuário chama de "tipo" a **variante de furação**: furos descentralizados (placa encostada em viga de concreto) e furos centralizados (placa centrada no pilar, sem viga ao lado).
- Quando o usuário deslocou uma chapa 24 mm para tirar a interferência com a viga 20x40, a furação ficou 108,5 / 80 / 61,5. O Claude cotou como estava e só sugeriu centralizar, sem perguntar o motivo.
- **Regra: quando um nome de vista ou peça for ambíguo, perguntar antes de detalhar. Quando o modelo mostrar uma assimetria, perguntar se é intencional e registrar a resposta.**

### 1.4 Problemas do modelo encontrados em 09/10
- 20 ligações com a CH-01 centrada na viga W, mas a W encostada numa viga 20x40: a chapa avança cerca de 24 mm dentro da viga de concreto (interferência).
- 2 ligações modeladas em dobro (grupo de parafusos e as duas chapas repetidos no mesmo lugar).
- Soldas modeladas com "Localização = Oficina", contrariando a nota "todas de campo".
- O afastamento entre furos e bordas no modelo (60 / 80 / 110) difere 1,5 mm do detalhe (61,5 / 80 / 108,5). Regra: cotar a partir do modelo, ou acertar o modelo antes de cotar.

### 1.5 Placa base tipo 2 e símbolos de solda (09/10/2026)
| O usuário pediu | O Claude propôs ou fez | Avaliação |
|---|---|---|
| Revisão 2 = adição da placa base tipo 2 | Criou a revisão nº 2 e aplicou nas folhas; atualizou o total de folhas e a revisão do carimbo | Acerto. Erro de acento na 1ª gravação (codificação do conector), corrigido |
| Quantificar e ajustar as placas descentralizadas e centralizadas | Classificação por código (CustomExporter): 45 descentralizadas e 13 centralizadas; 20 chapas com interferência deslocadas; 2 duplicatas apagadas; marcas CH-01 e CH-02 | Acerto. Achou erros de modelo que ninguém tinha visto |
| Detalhe da placa centralizada | Vistas tipo 2 e FAB CH-02 numa folha nova (15), porque não cabiam nas folhas 12 e 13 | Mudou o plano, com justificativa e registro |
| Símbolos de solda | 11 símbolos nas vistas 2D; 5 isométricos seguem em texto | Acerto, mas com várias tentativas (espelhamento, chamada solta, sobreposição) até chegar às regras de 4.3 |
| Furação vertical | Descobriu a divergência entre modelo (75/75) e detalhe (55/95) e perguntou | O usuário escolheu 55/95. A correção do modelo fica pela interface (limitação da API) |

---

## 2. Regras de detalhamento (padrão TMS para aço)

### 2.1 Antes de começar
1. Ler o .md do projeto e os cálculos que definiram a peça. O detalhe tem de respeitar as condições de cálculo (bordas, espaçamentos, posição das fileiras em relação ao topo do apoio).
2. Confirmar com o usuário o significado dos nomes existentes (tipo 1, tipo 2, A, B...) e o que diferencia cada tipo.
3. Fazer o inventário por código: quantas peças de cada tipo, onde estão, quais encostam em quê. Classificar com uma regra declarada e mostrar ao usuário a contagem e os casos duvidosos antes de alterar.
4. Procurar interferências e duplicatas no modelo antes de detalhar. O detalhe não pode esconder um erro do modelo.
5. Salvar antes de alterar e ao fim de cada etapa (ver `memory\revit-fluxo-etapas-autonomo.md`).

### 2.2 Nomenclatura e marcas (ABNT; representação conforme NBR 14611, cálculo pela NBR 8800)
- VM = viga metálica principal (apoio); VS = viga secundária; CH = chapa; L = cantoneira; PAR = parafuso (não usar "P", que é pilar); S = solda; RC = recorte.
- Peças com a mesma geometria externa e furação diferente recebem **marcas diferentes** (ex.: CH-01 e CH-02). A marca vai no parâmetro "Marca" do elemento, para entrar nas tabelas.
- Cada marca tem uma linha na LEGENDA DE PEÇAS: perfil ou chapa, dimensões, material, quantidade por ligação e referência da vista de fabricação.

### 2.3 Conteúdo mínimo de cada ligação
1. Vista de montagem (corte lateral e vista frontal), escala 1:5, com o concreto existente identificado.
2. **Vista de fabricação de cada peça diferente** (nunca só uma nota dizendo que "é igual, menos..."). Na chapa: dimensões, espessura, furos com diâmetro e quantidade, distâncias às bordas, linhas de centro, peso e quantidade total.
3. Isométrico travado por tipo de ligação (ilustrativo, sem escala; as cotas ficam nas pranchas de detalhe).
4. **Símbolo de solda em toda junta soldada** (simbologia ABNT, conforme AWS A2.4): tipo de solda (filete), perna em mm, lado (seta, outro lado ou ambos), todo o contorno quando for o caso, bandeira de solda de campo e processo/eletrodo na cauda (E70XX). A marca S-x continua na legenda, mas não substitui o símbolo.
5. Notas gerais: unidades, nomenclatura, materiais, parafusos, soldas, furação (broca, proibido maçarico), furos no concreto (broca diamantada, pacômetro, epóxi), proteção anticorrosiva, sequência de montagem e "conferir medidas no local".

### 2.4 Cotas
- Em mm, com o estilo de cota do usuário (1 casa decimal só quando necessária: 12,5 e 6,35).
- Cotas em cadeia (bordas e passos) mais a cota total; as bordas numa linha e os passos em outra, para os textos não encostarem.
- Em peças de Steel Connection (sem faces para referência), usar linhas de detalhe "<Linha de centro>" coincidentes como referência. As medidas vêm do modelo (CustomExporter, ver 4.3), não de leitura de imagem, sempre que possível.

### 2.5 Anotação e pranchas
- Zero sobreposição (regra universal). Conferir a vista e a prancha por imagem em alta resolução antes de entregar.
- Textos longos ficam na legenda; na vista, só marcas curtas com chamadas em ordem, sem se cruzarem.
- Isométricos: **travar a vista antes de anotar**. Tags nativas giram em 3D; usar texto com linha de chamada.
- Notas e legendas acima do carimbo (largura de ~179 mm), nunca soltas.
- Toda alteração entra numa revisão: criar a revisão com a descrição e aplicá-la em todas as folhas.

---

## 3. Roteiro passo a passo

1. **Status e arquivo**: `get_revit_status`, confirmar o arquivo oficial e salvar.
2. **Ler o histórico**: .md do projeto, memórias, cálculo da ligação.
3. **Inventário por código** (seção 4): peças, posições, vizinhança com concreto, duplicatas, interferências.
4. **Classificar** as peças com uma regra explícita; mostrar ao usuário a tabela com a contagem e os casos duvidosos.
5. **Plano** com etapas, o que muda no modelo e nas folhas; aprovar com o usuário.
6. **Corrigir o modelo** (posições, duplicatas, parâmetros como Marca e Localização da solda). Ler de volta e salvar.
7. **Vistas de montagem** (corte e frente) por tipo. Cotas, marcas, símbolos de solda. Conferir por imagem.
8. **Vistas de fabricação** por peça diferente. Conferir por imagem.
9. **Isométricos** travados, anotados com texto e chamada.
10. **Legenda, notas e tabelas** atualizadas (marcas novas, quantidades lidas do modelo).
11. **Revisão** criada e aplicada; carimbo e número de folhas conferidos.
12. **Revisão final**: exportar as pranchas em alta resolução, ampliar as regiões densas, corrigir e só então relatar. Salvar.
13. **Registrar** no .md do projeto (decisões, contagens, pendências) e atualizar este roteiro com as lições novas.

---

## 4. Técnicas que funcionaram (conector pyRevit, IronPython 2.7)

### 4.1 Peças de Steel Connection (SteelProxyElement: placas, parafusos, soldas)
- Não têm `get_BoundingBox`, `Location` útil nem `get_Geometry`. Parâmetros úteis: "Tipo" (ex.: "Pl 12.5x650x250"), Largura, Comprimento, Espessura, Material estrutural, Peso.
- Posição real: **CustomExporter** com um IExportContext que acumula os pontos de `OnPolymesh` por ElementId, exportando uma vista 3D temporária sem template, com nível de detalhe Fino. Criar a vista dentro de um `TransactionGroup` e dar `RollBack` no fim (o modelo não muda).
- Gravar resultados com `System.IO.File.WriteAllText` (o `open().write()` sem fechar trunca o arquivo).
- A API do Advance Steel (`FabricationTransaction`, `FilerObject`) foi carregada, mas não abriu os objetos: não insistir.
- `ElementTransformUtils.MoveElements` move a chapa; o grupo de parafusos **não acompanha** (útil para deslocar a chapa e manter os furos no eixo da alma). Depois de mover, conferir se os furos da chapa foram regenerados.

### 4.2 Vistas
- Fabricação de chapa: corte com profundidade de 20 a 30 mm, demais elementos ocultos na vista.
- Isométrico travado: duplicar um 3D já travado e ajustar a caixa de corte.
- Conferência: `get_revit_view` ou `doc.ExportImage` em alta resolução.

### 4.3 Lições de 09/10/2026 (placa base tipo 2 e símbolos de solda)
**Furos e parafusos de Steel Connection**
- Mover a **chapa** recalcula os furos na posição dos parafusos. Mover o **parafuso** pela API **não** recalcula os furos: o parafuso sai do furo. Para mudar a furação, mover pela interface do Revit e conferir depois por código.
  - **Confirmado no 26-083:** o usuário moveu 57 grupos pela interface; o Steel recriou os grupos com IDs novos e refez todos os furos. Fluxo: Claude pré-seleciona os grupos (`Selection.SetElementIds`), o usuário move pela interface e Claude confere por CustomExporter. Os IDs antigos deixam de existir, então não reutilizar listas de IDs de antes da movimentação.
- Antes de cotar, comparar a furação do modelo com a do detalhe em **todas** as ligações. No 26-083, o protótipo estava em 55/95 e as outras 57 ligações em 75/75.

**Vistas duplicadas e anotações**
- Vista duplicada "com detalhamento" e levada para outra ligação (movendo a marca de corte): linhas de detalhe, regiões e cotas acompanham a vista; **textos não acompanham**. Mover os textos à parte.
- Os elementos ocultos um a um na vista original continuam ocultos, e as peças da nova ligação podem ficar ocultas ou aparecer sem querer. Revisar o que está visível.
- Mover a origem da vista no próprio plano exige reposicionar as linhas de referência das cotas pelas coordenadas do modelo. `SetGeometryCurve` precisa da coordenada exata do plano (não arredondar).

**Símbolo de solda (família "Weld Symbol", anotação genérica)**
- Tipo "Bottom" = solda do lado da seta. Parâmetros: Bottom Weld Size (perna), Bottom Weld Length (deixar vazio, senão aparece "0"), Field Weld, Weld All Around, Tail Note e Tail Note Visible. A seta da chamada é um parâmetro do tipo ("Seta da chamada de detalhe").
- **Nunca espelhar:** espelhar troca a perna de lado e inverte o triângulo, fora da convenção ABNT/AWS.
- **O alvo tem de ficar à esquerda do símbolo.** Com o alvo à direita, o Revit prende a chamada num ponto de fixação à direita da família, solto no papel.
- Não mover o símbolo depois de criar a chamada (a chamada desalinha). Criar o símbolo já na posição final e só então `addLeader`, com End no alvo e Elbow na reta.
- Tamanho no papel: cerca de 20 x (37 + 1,7 x caracteres da cauda) mm. Na cauda vai só a marca (S-1); processo e eletrodo ficam na legenda.
- Posicionamento: busca automática por posição fora do recorte do modelo, sem tocar textos, cotas e outros símbolos, sem cruzar chamadas. Depois, conferir se a viewport cresceu e se não passou da borda da folha nem encostou em vizinhos.
- Vista 3D não aceita anotação genérica: nos isométricos a solda fica descrita em texto.

**Conector pyRevit**
- Acentos: o conector converte `\uXXXX` em caracteres reais e grava com codificação dupla. Montar o texto com `unichr()` e conferir com `ord()` depois de gravar.
- `doc.Regenerate()` fora de transação gera erro depois do commit (a alteração já foi gravada). Ler de volta antes de repetir.
- `get_revit_view` pode devolver imagem em cache. Para conferir, usar `doc.ExportImage` com nome novo.
- Legendas e notas crescem para baixo quando o texto aumenta. Depois de editar, conferir se não invadiram a borda ou a tabela de revisões.

**Plantas de indicação de ligações (padrão a partir do 26-083)**
- Uma planta por nível, em 1:50, duplicada da planta de forma de aço **sem detalhamento**, recortada na área metálica e com as marcas de corte ocultas. As folhas entram **antes** das folhas de detalhe, renumerando as seguintes e atualizando todos os textos que citam folhas (procurar por "FOLHA", "PRANCHA", "PR.").
- Uma indicação por ligação (PB-n = aço-concreto, LG-n = viga-viga; não usar L-n, que é cantoneira). Texto com borda de 2 mm e chamada até o ponto da ligação. A caixa mede a largura do texto mais 2 vezes o deslocamento da borda: com largura de 7 mm e deslocamento de 1 mm, a caixa tem 9 mm.
- Lista de nós feita por código: placas pelos grupos de parafusos (CustomExporter); viga-viga pelas extremidades das VS (dois VS no mesmo ponto sobre VM-2 = tipo 2).
- Posicionamento calculado fora do Revit: anel de candidatos ao redor do nó; descartar os que tocam perfis, pilares ou vigas de concreto (folga de 1,2 mm), outras caixas (1,5 mm) ou a borda do recorte (6 mm), e os que cruzam chamadas. Preferir chamada a cerca de 45 graus.
- Referência aos detalhes: tabela acima do carimbo (indicação, descrição, quantidade, detalhe/folha) e indicação no título das vistas de detalhe. Chamadas de referência do Revit só servem se a cabeça de corte mostrar número e folha. Trocar o tipo da referência troca também o tipo da vista referenciada.
- Títulos de vista curtos ("PB-1 - FRENTE"): títulos longos passaram da borda da folha.

**Símbolo de solda: posicionamento definitivo (09/10/2026)**
- Com o **cotovelo à esquerda** do círculo, o Revit prende a chamada no círculo mesmo com o alvo à direita. Assim o símbolo pode ficar na coluna de marcas, à esquerda, e a chamada passa por baixo dele até o alvo, sem cruzar as cotas do lado direito.
- **Vista 3D, mesmo travada, não aceita anotação genérica.** A etiqueta "Identificador de soldas" funciona em 3D, mas mostra só texto ("Solda de canto 0.4"). Solução: colocar o símbolo **na folha**, sobre o isométrico. Para achar a posição de um ponto 3D na folha: ativar temporariamente um recorte pequeno em volta do ponto (TransactionGroup + RollBack) e ler `vp.GetBoxCenter()`. `GetProjectionToSheetTransform` não serve para 3D com recorte inativo. Se a viewport for movida, o símbolo da folha não acompanha.

**Tabelas que citam detalhe/folha**
- Sempre geradas do modelo, por script (ex.: `estudos\26-083\scripts\tabela_ligacoes.py`, com `conferir()` e `atualizar()`). Nunca digitadas.

**Verificação obrigatória após edição**
- `C:\Users\toshi\claude_verif_lib.py` (`verificar(param_total, folhas_alvo)`), com o agente `verificador-revit` (`~/.claude/agents/`) para a conferência por imagem. A biblioteca confere referências DET/FOLHA/PR., acentuação corrompida, total de folhas, revisões, margens, sobreposições (texto, texto de cota, símbolos) e chamadas cruzadas.
- Inserir linhas numa nota com lista numerada renumera os itens: usar `SetListType(ListType.None)` nas linhas novas.
- Antes de inserir texto numa coluna de notas, medir o espaço até a tabela de revisões e até o elemento de cima. Se não couber, não forçar.

### 4.4 Cuidados
- Transação sempre aberta no script, com pré-processador de falhas (avisos de "Marca duplicada" desfazem a transação se não forem tratados).
- Nunca usar o conector KoreOS com dois Revit abertos.

### 4.5 Lições de 12/10/2026 (26-103: Steel Connections, tabelas, detalhes de fundação)
- **Steel Connection "Cantoneira" pela API** (`StructuralConnectionHandler.Create(doc, [pilar, viga], tipo)`): funciona, mas abre o aviso "modificador de encurtamento" que trava o conector se a transação não tiver pré-processador de falhas. Depois de criar, as extensões das vigas deixam de existir (viram modificador). **Conferir a rotação dos pilares e o comprimento de corte das vigas**: no 26-103 o Steel girou um pilar em 90°; desvirar e recriar a ligação daquele nó.
- As chapas, cantoneiras e parafusos das Steel Connections não aparecem como elementos tabeláveis (categorias de placas/perfis/parafusos vazias). Quantificar as ligações por tabela da categoria Conexões estruturais (contagem por tipo).
- Tabelas novas: copiar o estilo de uma tabela do template (padrão do usuário). Ver `estudos\26-103-mezanino\scripts\tabelas_f00.py` (`copiar_estilo`, `layout_f00`). Campo calculado não é criável pela API: gravar o valor num parâmetro de projeto por script (ex.: peso do vergalhão).
- Vista de detalhe criada com `ViewSection.CreateDetail` e Transform BasisX = +X saiu espelhada (direita da vista = -X). Usar BasisX = -X e conferir por imagem antes de anotar.
- Família de sapata sem referências de face pela API (`GetReferences` vazio): cotas não saem; pôr as dimensões no título da vista.
- Ao editar scripts com Python fora do Revit, abrir com `newline=''`: um CR literal dentro de string (`u"\r"`) vira quebra de linha e quebra o script.

### 4.6 Lições de 12/10/2026 (26-103: detalhe de curvatura, carimbo TMS V05, exportação)
- **Detalhe de curvatura pela API:** `RebarBendingDetail.Create(doc, vista, vergalhão, 0, tipo, posição, rotação)` (chave de subelemento 0; -1 dá erro). A barra precisa estar **visível na vista**. No tipo esquemático a posição do identificador não é controlável; no realista, `SetTagRelativePosition` não teve efeito e "Alinhamento do identificador" com valor 2 ou mais dá SEHException nativa. Solução adotada: identificador desligado e legenda em texto gerada do modelo, posicionada pela caixa do detalhe (`get_BoundingBox(vista)`); detalhes movidos com `SetPosition` depois de medidos.
- Comprimentos do detalhe seguem a unidade "comprimento de armadura" do projeto; cotas do realista seguem o "Estilo de cota linear" do tipo (criar estilo em cm sem decimal).
- Identificador "Vergalhão ABNT - Completo" mostra o **Comentários** do vergalhão: manter o Comentários vazio e a marca na Marca. O número (REBAR_NUMBER) é editável: numerar como o Eberick (passar antes por números temporários).
- Barra em L com gancho para +X e para -X dá comprimentos diferentes (2,5 mm) e posições diferentes: usar o mesmo sentido em todas.
- **Carimbo TMS V05** ("Folhas TMS-V05"): rótulos leem as Informações do Projeto (TMS_End. Obra, TMS_Nome do Cliente, TMS_CÓDIGO DE PROJETO, RMS_Número de Folhas, TMS_Revisão, TMS_Residêncial/Comercial = nome da obra em letra grande, curto) e Data de emissão do projeto. O número da folha (10 mm) não cabe "F-00": usar 7 mm na cópia do projeto. Conferir a posição do carimbo (0; 0) em todas as folhas ao trocar.
- Exportação: `PDFExportOptions.FileName` ignorado com Combine = False (sai "Folha-<nome>"): renomear depois. Pasta com acento criada pelo conector sai corrompida: criar/renomear pelo PowerShell ([char]199).

### 4.7 Lições de 13/10/2026 (25-121: correções de RAD em armadura)
- Quantidade de barras: usar `DoesBarExistAtPosition` (conjuntos com posições excluídas); descrição por grupo = instâncias do grupo de modelo x barras existentes por instância. Gerar textos do modelo.
- `ArcLengthDimension.Create` pela API dá valor errado ou "não é possível formar cota angular"; copiar/espelhar cota de arco faz o Revit excluí-la. Fazer pela interface (PostCommand + cliques) e conferir o valor por código.
- Rótulos de cotas curtas: levar para fora do vão com `TextPosition` (fica com chamada automática); onde não couber, apagar o rótulo da cota e criar TextNote com chamada, posicionado por máscara de ocupação da imagem exportada (scripts\posicionar_mascara.py).
- Chamadas cruzadas de identificadores: converter Attached→Free (mantém a ponta), depois buscar nova ponta na mesma barra e cabeça nas colunas já existentes (scripts\desembaracar.py).
- Acento em parâmetro de vista via conector: montar com `unichr()`; o conector corrompe `\u` e `LookupParameter(u"Descrição")` falha (usar BuiltInParameter).
- Revisão em carimbo de família: editar a família (copiar a linha anterior 7,35 mm acima) e `LoadFamily` com IFamilyLoadOptions; subir a coluna de notas na mesma medida.
- Nunca usar `open_application("Revit 2026")` do computer use: abriu outra instância. Viewport pinada: despinar, mover e repinar.

### 4.8 Lições de 16/10/2026 (25-112: placas, anexos e geometria)
- Ao levar a ponta de uma barra até uma face, **conferir se a barra é perpendicular à face** (componente do eixo na normal > 0,5). Barra paralela à face (longarina presa lateralmente) gera divisão por quase zero e estica a barra a quilômetros. Depois de qualquer ajuste em lote, varrer o modelo por caixas fora da obra.
- `doc.Regenerate()` depois de `t.Commit()` levanta "Modification of the document is forbidden", mas a transação já foi gravada: ler de volta antes de repetir.
- Pilar com topo anexado (ColumnAttachment): mudar o deslocamento de topo não tem efeito; recriar o anexo com `AttachOffset` (afastamento da chapa) ou remover o anexo antes. Anexo a duas vigas que terminam no mesmo ponto falha: usar chapa horizontal e deslocamento.
- Família "Placa Base" (TMS 540): `Nº = 1` faz 2 chumbadores numa linha (eixo da viga); `Nº >= 2` faz grade. Inserção na face de baixo da chapa; plano com normal invertida para placa sob viga (chumbadores para cima).
- Erro "Não é possível manter unidos os elementos" ao estender vigas: desligar a união nas pontas da viga e das vizinhas que apontam para ela (`StructuralFramingUtils.DisallowJoinAtEnd`); em diálogo, "Desunir elementos" resolve.
- Revit dentro de pasta sincronizada (SharePoint): um clique do usuário no arquivo reverteu o documento aberto a uma versão anterior; recuperado pelo backup .000N. Salvar a cada etapa é o que permite voltar.
- Tabela em TextNote: tabulação não alinha (fonte proporcional; cabeçalho maior que a parada pula uma coluna). Usar uma TextNote por coluna, lado a lado, ou uma tabela do Revit. Acentos em scripts lidos por `exec(open().read())`: escrever com `unichr()`; letras acentuadas literais no arquivo saem corrompidas (Ã§).
- Corte de detalhe criado por API sai sem recorte de fundo: definir VIEWER_BOUND_FAR_CLIPPING = 2 e o deslocamento (o modelo de vista pode controlar esses parâmetros; liberar com SetNonControlledTemplateParameterIds), senão aparecem peças da fila inteira atrás.
- Origem de vista de corte (`view.Origin`) é um canto do recorte, não o centro: para linhas de referência e cotas, usar o ponto conhecido da ligação, que está no plano do corte.
- Peso de perfil em folhas de fabricação: ler da tabela do modelo (ex.: Quantidade Pilares, Peso/m), não calcular à parte.
- Família de pilar "GERDAU CANTONEIRAS DE ABAS IGUAIS PILAR" (tipo L2' - 1/4'): geometria com aba de ~193 mm, errada. Cantoneira vertical: usar a família de barra "Cantoneira Simples Abas Iguais" com linha vertical (o Revit aceita) e o giro STRUCTURAL_BEND_DIR_ANGLE, testando os 4 quadrantes pelo centróide do sólido.
- Correntes e contraventamento de barra redonda: família "Contraventamento" (barra com rosca e porcas, Quadro estrutural, curva), tipo duplicado com o Diâmetro; marcas por comprimento (sufixo). Contraventamento em X em painéis de ~1,5 x a largura (ângulo de ~35 graus), não um X só no vão inteiro.
- Identificador de conexão estrutural não vem no template: criar do "Indicador genérico métrico.rft", trocar a categoria por API (OwnerFamily.FamilyCategory) e incluir o rótulo pela interface (Legenda, clicar, Comentários, OK). Ao carregar no projeto, ativar o projeto ANTES (a carga com a família ativa a carrega nela mesma).
- Script gravado pela ferramenta com letras acentuadas literais quebra o IronPython: converter tudo fora de comentário para escape unicode antes de rodar (`estudos\25-112\scripts\esc_unicode.py`), ou usar unichr().
- Identificadores em planta densa (25-112, 300 por folha): posicionar por código com `estudos\25-112\scripts\ident_livre.py`. Obstáculos que precisam entrar: barras inclinadas em planta (tirantes) como segmento, peças do vínculo IFC, **linhas dos DWG importados** (são elas os espigões e bordas grossas), marcas de corte, cabeças e chamadas já colocadas. A caixa do identificador é a do texto real medido na imagem, não a centrada na cabeça. A sobreposição acusada pela biblioteca entre IndependentTag inclui a chamada: confirmar por imagem.
- Cantoneira lançada como barra (família Cantoneira Simples): o giro da seção só alcança 4 orientações de uma mão; para a outra mão (a cantoneira do lado oposto do perfil) é preciso inverter o sentido da linha. Testar as 8 combinações pelo centróide do sólido: aba horizontal embaixo (centróide a ~15 mm do fundo), corpo acima do apoio e centróide para fora do perfil. Conferir sempre no corte da folha de detalhe.
- Símbolo de solda (família Weld Symbol) pela API: addLeader() não devolve a chamada no IronPython; pegar GetLeaders()[0] e definir End (junção). A família prende a ORIGEM da chamada 16,7 mm de papel abaixo do ponto de inserção ("Origin Position", só leitura): o desenho é origem -> cotovelo -> ponta, então cotovelo no ponto de inserção faz a chamada ir e voltar (reclamação do usuário em 17/10). Certo: cotovelo na linha de referência e ponta na reta cotovelo -> origem, além dela (script estudos\25-112\scripts\soldas_retas.py).
- Weld Symbol, regra final (17/10, aprovada após 8 rodadas): a origem da chamada (âncora) muda com o lado e com o comprimento do texto da cauda/comprimento; usar fórmula fechada sem ler a âncora: lado -1 cotovelo = J + d·(-0,855; 0,517) e inserção = cotovelo + 25 mm de papel; lado +1 cotovelo = J + d·(0,405; 0,914) e inserção = cotovelo - 10 mm (d em mm de papel × escala). Cauda curta ("S-1", "CS"); texto longo vai para a nota. Em instância recém-criada, regenerar e aplicar duas vezes.
- Chapa de família por plano de trabalho (Placa Base, Chapa) criada com SketchPlane próprio (25-112, 17/10): MoveElement na direção da normal é ignorado em silêncio e o "Deslocamento" não grava. Mover o SketchPlane da chapa (id da chapa - 1, ou procurar o plano paralelo que contém a origem) com MoveElement/RotateElement: a chapa e os chumbadores (subcomponentes compartilhados) vão junto e o id se mantém (anotações preservadas). RotateElements da chapa fora do plano dá erro "não é possível rotacionar"; para inverter a normal usar MirrorElements (copy=False). Script `estudos\25-112\scripts\pb6_face.py`.
- Antes de mover chapa de ligação para a "face do concreto", medir a face real no vínculo (vigas/pilares do R05) e descartar peças do IFC que são o metálico do arquiteto (no 25-112, "G1".."G42" = pergolado), senão a chapa fica alinhada com o desenho de arquitetura e não com o concreto.
- Pilar com topo cortado no ângulo e folga para chapa inclinada: ColumnAttachment na viga não aplica o afastamento ao corte (Minimum deixa topo reto, Midpoint/Maximum cortam na face da viga). Anexar o pilar a um ReferencePlane criado no plano da face de baixo da chapa (NewReferencePlane2 numa vista 3D), CutColumn, Maximum, offset 0. Chapa não é alvo válido de anexo.
- Barra de contraventamento ligada no nó da treliça: ponta no plano da treliça, 150 mm para dentro do painel e 55 mm abaixo do eixo do banzo (região do gusset), nunca no nó exato (bate em montante e diagonal) nem recuada para fora do plano. Script `estudos\25-112\scripts\i4_cv_nos.py`.
- Depois de mover peça detalhada, conferir as vistas da folha D: corte que não pega mais a peça (mover o elemento Viewers do corte), cotas presas a linhas de detalhe (mover as linhas), ponta de símbolo de solda e recorte da planta.
- Etiqueta em vista densa (25-112, 18/10): não deslocar no escuro. `estudos\25-112\scripts\livre_export.py` exporta a folha com as etiquetas a mover ocultas (transação desfeita) e grava o alvo de cada uma em mm de folha (modelo -> projeção da vista -> folha: GetModelToProjectionTransforms x GetProjectionToSheetTransform); `livre_busca.py` (CPython/PIL) acha a cabeça em papel branco: caixa do texto conferida PIXEL A PIXEL (amostrar a cada 0,25 mm deixa passar linha de 1 px), centro do texto 1,2 mm acima do ponto da cabeça (2 mm Arial, ~1,6 mm por caractere), tom < 225 é obstáculo (hachura clara 234/248 passa), chamada sem cruzar outra e sem passar por texto; `livre_export.aplicar` devolve a cabeça para a vista. Conferir sempre o mosaico de recortes antes de aplicar.
- Cota cujo texto cai sobre eixo: tipo duplicado com "Mostrar chamada de detalhe quando o texto mover" = "Além das linhas de testemunha" e TextPosition deslocado ao longo da linha de cota (sem chamada). Apagar linha de detalhe "solta" pode apagar cota que a usa como referência: conferir `Dimension.References` antes.
- Trocar chumbador aninhado na "Placa Base" (25-112, 18/10): o tipo do chumbador é o parâmetro de tipo "Tipo de Conexão" (troca para qualquer família da categoria Conexões estruturais carregada no projeto); a família nova precisa ser COMPARTILHADA, senão vira geometria sem elemento próprio (sem tabela, sem etiqueta). Os aninhados ficam em grupos de matriz com "Deslocamento do hospedeiro" = 9 mm fixo (não editável sem quebrar a matriz): compensar no tipo do chumbador (parâmetro "Deslocamento da origem"). Sem chumbador: "Nº" = 0 ("n", "Parafuso Unico/Multiplo" são fórmulas). Chave de dicionário por prefixo de nome pega "PB-1B" no lugar de "PB-1": usar o id do tipo.
- Recarregar família que edita aninhados dentro de grupo dá "alterações nos grupos só no modo de edição" (diálogo que trava o conector): Cancelar (id 3) desfaz tudo.
- Mover recorte de planta (CropBox) ou caixa de corte de 3D desloca a VIEWPORT na folha (o título fica): depois, devolver o centro da viewport (LabelOffset é medido a partir do canto mínimo da caixa). Mover corte = mover o elemento Viewers; cotas presas à geometria antiga não acompanham: apagar e recriar por linhas de referência.
- Símbolo de solda (Weld Symbol): `addLeader()` devolve None; pegar `list(sym.GetLeaders())[0]` e então definir End e Elbow (senão a ponta vai para a origem do projeto e estica a vista).

### 4.9 Lições de 18/10/2026 (26-102: placas base, fundo, pranchas)
- **Família paramétrica pela API:** `NewFamilyDocument` + `fm.NewType` DENTRO da transação; planos de referência com `NewReferencePlane2`, `Regenerate()` antes de cotar; cota rotulada com `dim.FamilyLabel`; cota de 3 referências com `AreSegmentsEqual = True` para simetria; limites de extrusão ligados a parâmetros (`EXTRUSION_START_PARAM`/`END` + `AssociateElementParameterToFamilyParameter` com fórmula); alinhamento de contorno a plano só funciona nas linhas (círculo: usar cota do centro ao plano de centro rotulada por parâmetro de fórmula e `NewDiameterDimension`). Cada passo em transação própria com `IFailuresPreprocessor` que devolve `ProceedWithRollBack` em erro: erro de restrição ("croqui sobre-restrito") abre diálogo que não pode ser ignorado e trava o conector (resolver por UIA + clique de mouse em "Cancelar"). Modelo: `scripts\fam_placa_base.py`.
- **Fundo DWG/DXF:** DXF do ezdxf era recusado por Import/Link; regerar limpo em R2010 (linhas, arcos, círculos, polilinhas; INSERT explodido; sem DIMENSION/MTEXT/TEXT; layers sem acento; Z = 0) e testar antes em projeto descartável (`NewProjectDocument`). `doc.Link(arquivo, DWGImportOptions, vista, id)` cria instância VISÍVEL EM TODAS AS VISTAS (aparece como linha horizontal nas seções): ocultar a instância com `v.HideElements` em todas as vistas menos na planta que a usa. O vínculo nasce fixado: `Pinned = False` antes de `MoveElement`. A planta só enxerga o vínculo dentro da faixa do intervalo de vista (subir o vínculo até a faixa).
- **Eixos** só aparecem em vistas cujo corte está dentro da extensão vertical do eixo: `Grid.SetVerticalExtents`. Extensão 2D por vista: usar a curva de `GetCurvesInView(ViewSpecific, v)` (Z do plano de corte, não 0) e `SetCurveInView`.
- **Seções de corte criadas pela API:** `ViewDirection` é a direção PARA o observador; o recorte (`CropBox`) tem origem no canto inferior esquerdo (Min.X = 0), não no centro: ao mudar a largura, manter Min.X >= 0 ou a vista foge. A "fatia" (Min.Z = -profundidade) isola o pórtico; o que sobra oculta-se com `HideElements` por marca. Ocultar categorias `OST_Levels`, `OST_Viewers`, `OST_SectionBox`, grades (detalhes) para a caixa da viewport não crescer.
- **Rótulo da viewport:** `GetLabelOutline` / `LabelOffset`; tipo "Título da vista - Nome Vista e Escala" (título menor). Depois de mudar o recorte, realinhar o rótulo abaixo-esquerda da caixa.
- **Tabelas:** parâmetros próprios (compartilhados, GUID novo) `TMS_Massa linear` (tipo), `TMS_Massa total` (massa, kg) e `TMS_Comprimento total` (comprimento) por instância, `TMS_Perfil` (texto, tipo); campo calculado não existe pela API. Spec "Número" gera "e+217" na tabela: usar spec Massa/Comprimento. NÃO sobrescrever estilo de célula em lote (corpo não aceita; o cabeçalho some): recriar a tabela. `sd.GrandTotalTitle = u"TOTAL"`.
- **Pelo conector, escape `\u` em strings corrompe:** usar `unichr()` (erro real: "ó" no lugar de "õ", "Õ" no lugar de "Ó").
