# PLANO - Armaduras (vergalhões) nativas no MCP Revit

Status: **aguardando aprovação** | Criado: 2026-10-05 | Responsável estrutural: Toshio | Repositório: `RevitMCP-KoreOS-Traco` (base `mana/revit-2026-executivo`, v0.8.66)

## 1. Feature

Permitir que o Claude modele **armadura nativa do Revit** (Rebar, Area Reinforcement, Fabric Area) em lajes, paredes, vigas, sapatas e blocos, a partir de instruções em linguagem natural, de detalhamentos em PDF ou de IFC, e gere a documentação no padrão TMS (cortes, etiquetas `N.. Ø.. c/.. C=..`, tabela de vergalhões, resumo do aço). Pilares ficam para a etapa 2. O dimensionamento continua do engenheiro; o MCP modela, valida mínimos da NBR 6118 e documenta.

## 2. Evidências levantadas

**Código (v0.8.66):** nenhuma ferramenta de armadura existe (154 arquivos de comando; "rebar" só aparece num comentário de `CreateAlignedDimensionCommand`). Já existem e serão reaproveitados:
- hospedeiros: `create_beam`, `create_floor`, `create_wall`, `create_framing_member`, `place_family_instance` (fundação → `StructuralType.Footing`);
- documentação: `create_section_view`, `create_schedule`, `configure_schedule`, `tag_element`, `tag_all_in_view`, `create_aligned_dimension`, `place_view_on_sheet`, `create_sheet`, `export_view_pdf`;
- transporte: `revit_batch` (N passos numa só Transaction, rollback no primeiro erro), `dryRun`, idempotência;
- padrão "planejador puro + comando fino" (`PurlinRoofPlanner.cs`, `cadWalls.ts`), testável sem Revit.

Padrão de comando: classe `IRevitCommand` em `src/RevitMCP.Core/Commands/`, `Register(new ...)` em `CommandRegistry.cs`, `server.tool("revit_...", zod, fwdWrite(...))` em `src/McpServer/src/index.ts`.

**Referências do usuário:**
- `guia para implementação.docx`: regras de escolha de função por elemento (seção 4).
- Piscina 26-014 R02 (**gerada no Revit**, 9 folhas): isométrico de armaduras, tabela de vergalhões `Aço | N | Ø DIAM (mm) | Quant | C.Unit (cm) | P.Tot (kg)` (47 posições, 1930,16 kg), tabelas de piso/parede (volume, forma), plantas de armadura por seção, detalhes de laje POS/NEG com etiqueta `24 N34 Ø8.0 c/20 C=1085`, cortes com chamadas `N33/N34` e dobras cotadas em vermelho. **É o padrão de saída alvo.**
- Casa 25-112 R07 (**AutoCAD**, 62 folhas): padrão de conteúdo de detalhamento convencional: vigas em elevação com barras desenhadas fora da peça, estribos por trechos (`42 N1 c/10`, `20 N13 c/5`), pele, porta-estribo, `(1c)` = camada; sapatas planta + corte com malha `12 N1 ø10.0 c/10 C=168`; lajes positiva/negativa separadas; "Relação do aço" + "Resumo do aço" por bitola com peso CA-50/CA-60, volume de concreto e área de forma.
- O texto dos PDFs é vetorial: as chamadas são extraíveis por regex (base da fase 8).

**Modelos de teste (cópias em `estudos/modelos-teste/`, originais intocados):**
- `24-062-EST-PISCINA_COPIA.rvt` (Revit 2026) + `24-062-EST-PISCINA_COPIA.ifc` (IFC2X3 exportado do mesmo modelo). Gabarito extraído do IFC: **1546 barras, 3494 m, 1368,2 kg** (Ø6,3: 74 barras / 17,3 kg; Ø8,0: 1472 barras / 1350,9 kg); 18 paredes, 3 lajes; formas 01, 10, 16, 22, 33 e personalizadas "Formato do vergalhão 23 a 26". O IFC traz comprimentos e diâmetros em cm. O par RVT + IFC da mesma estrutura é o gabarito da fase 8b.
- `Template Concreto_COPIA.rte`: template estrutural **em evolução** (o usuário avisou que pode ter lacunas). A fase 1 gera o relatório de lacunas e o template é atualizado de forma iterativa.
- A piscina 26-014 (PDF, 1930,16 kg) continua como referência visual de prancha; não há RVT dela.
- `Template Concreto_COPIA.rte` é **Revit 2025**. A API não abre `.rte` como documento (nem renomeado); exige `NewProjectDocument(template)`.
- `Vergalhao ABNT - Completo_COPIA.rfa`: família de etiqueta de vergalhão do escritório.
- `25-061-ginasio/`: ginásio de Tapurah. `25-061-IFC4_COPIA.ifc` exportado do **Eberick 2026** (IFC4, cm): 1054 vigas, 496 pilares, 98 lajes, 118 blocos/sapatas, 262 estacas, 6 pavimentos, **sem geometria de barras**. Por peça traz volume de concreto, área de forma, **peso de aço por bitola** (`AltoQi_Eberick-Item_0n`: "Armadura - Aço CA50 - Ø 10.0 mm" = 6,75 kg), CAA, cobrimento (viga 3,0 cm, bloco 4,5 cm), C-30 e seção. PDFs P1 (69 folhas) e P2 (27 folhas), AutoCAD/Eberick: locação, estacas, blocos, radier, pilares, vigas, lajes positiva/negativa, escadas, resumo do aço. Na pasta original também há o RVT gerado da importação do IFC e uma tabela de fundações em CSV.

**Critérios fixados pelo usuário:** fck = 30 MPa (C30) como padrão; cobrimento conforme CAA do projeto, perguntado no ato (o IFC do Eberick já informa CAA e cobrimento por peça, usado como sugestão a confirmar).

## 3. Restrições inegociáveis

1. Toda escrita em `Transaction` nomeada, desfazível, com `dryRun`. Nada de execução arbitrária.
2. **Usar os tipos do template do usuário** (RebarBarType, RebarShape, ganchos, cobrimentos, tags). A ferramenta resolve por nome e lista as opções quando não encontra; nunca cria Rebar Shapes silenciosamente.
3. Cobrimento: **perguntar no ato** a classe de agressividade (NBR 6118 tab. 7.2), se algum elemento tem cobrimento reduzido e se há execução rigorosa (Δc = 5 mm). Padrão mais comum: 30 mm.
4. Ganchos, dobramento, ancoragem e traspasse: **mínimo NBR 6118**, emitidos como aviso quando a entrada viola; o MCP não redimensiona.
5. Aços CA-50 e CA-60, todas as bitolas comerciais da NBR 7480.
6. Revit 2025 e 2026 (as duas instaladas). Compilar e validar nas duas.
7. Distribuição padrão: espaçamento máximo, número com espaçamento ou espaço livre mínimo. Número fixo e barra única são exceção.
8. Validação ao vivo só em cópia descartável. Entrega via PR com CHANGELOG e `docs/`.

## 4. Decisões de arquitetura

**D1 - Duas camadas de ferramentas.**
- *Primitivas* (C#, genéricas): conjunto de barras, área, tela, cobrimento, leitura. Cobrem qualquer caso que o Claude saiba descrever.
- *Semânticas por elemento* (C#, `reinforce_beam`, `reinforce_footing`...): recebem a armadura na linguagem do engenheiro (`3Ø12,5 inf`, `estribos Ø5 c/15`, trechos) e calculam a geometria a partir do hospedeiro. A lógica geométrica fica em **planejadores puros** (`RebarPlanners/*.cs`) com testes unitários, e o comando só traduz o plano para a API.
- Descartado: geometria calculada só no Node (exigiria várias idas e voltas para ler a geometria do hospedeiro, sem atomicidade) e "eval" de C# (proibido pela arquitetura do projeto).

**D2 - Especificação intermediária única `ReinforcementSpec` (JSON).** Linguagem natural, PDF e IFC convergem para o mesmo formato antes de ir ao Revit. Inclui o parser da notação brasileira (`24 N34 Ø8.0 c/20 C=1085`, `2x2 N7 ø6.3 C=135 (PELE)`, `(1c)`) em TypeScript, testado. Assim cada fonte nova é só um conversor.

**D3 - Escolha da função por elemento** (do guia do usuário), aplicada por padrão e sobrescrevível:

| Elemento | Função Revit |
| --- | --- |
| Piso/laje reta de grande área | Area Reinforcement |
| Laje pequena cercada por vigas | Rebar (conjuntos) |
| Laje/parede com armadura tipo tela | Fabric Area (tela soldada) |
| Parede reta | Area Reinforcement (mesmo cercada por pilares/vigas) |
| Parede curva, viga curva | Rebar seguindo o cobrimento (free form), longitudinais + transversais |
| Viga, bloco, sapata | Rebar (conjuntos) |

**D4 - Documentação nativa**, não desenho 2D: etiquetas de vergalhão (família de tag do template), Multi-Rebar Annotation (cota de distribuição + etiqueta, como na piscina), Rebar Bending Detail (dobras cotadas), numeração por partição, tabelas por categoria Structural Rebar. O formato da casa (barras rebatidas fora da peça) é reproduzido com Bending Detail e vistas de detalhe.

**D5 - Versionamento.** Cada fase concluída = MINOR (0.9.0, 0.10.0...), conferindo antes a versão do branch base para não colidir com o Lucas (frente de arquitetura).

## 5. Protocolo de conversa (antes de modelar qualquer peça)

1. Confirmar CAA, cobrimentos por tipo de elemento, cobrimento reduzido e execução rigorosa.
2. Confirmar fck (entra no comprimento de ancoragem lb da NBR).
3. Listar os tipos de barra, formas e tags do modelo (`list_rebar_types`) e mapear com o usuário quando o nome não bater.
4. Rodar em `dryRun`, mostrar o resumo (posições, quantidades, peso), e só então aplicar.

## 6. Fases

### Fase 1 - Leitura e inventário de armadura (v0.9.0)

**Entrega:** o Claude lê qualquer modelo com armadura e informa tipos disponíveis e quantitativos. Validado contra a piscina (deve reproduzir os 1930,16 kg da tabela).

**Pode assumir:** add-in instalado em 2025/2026; template e modelo da piscina disponíveis em cópia.

**Garante para as próximas:** `list_rebar_types` e `get_rebar_host_info` estáveis; parser `ReinforcementSpec` em TS; helper `Nbr6118` (cobrimento por CAA, diâmetro mínimo de dobra, lb, traspasse, ganchos de estribo), só para avisos.

**NÃO faz:** nenhuma escrita no modelo.

**Ferramentas:**
- `list_rebar_types`: RebarBarType (Ø, aço), RebarShape (nome, nº de segmentos, ganchos), RebarHookType, RebarCoverType, AreaReinforcementType, FabricSheetType, famílias de tag de vergalhão.
- `get_rebar_host_info`: se o elemento é hospedeiro válido (`RebarHostData`), cobrimentos por face, armaduras existentes.
- `get_rebars`: barras por hospedeiro, vista ou modelo: marca/N, Ø, quantidade, comprimento unitário, comprimento total, peso, partição.
- `new_project_from_template`: cria um projeto a partir de um `.rte` (cópia), necessário para auditar e testar o template; hoje `open_document` recusa `.rte`.
- `audit_rebar_template`: compara o documento aberto com um checklist e lista as lacunas: bitolas da NBR 7480 (CA-50: 6,3 a 40; CA-60: 2,4 a 10,0), ganchos 90°/135°/180° com diâmetro de dobra mínimo da NBR 6118, cobrimentos por CAA I a IV (com e sem execução rigorosa), formas básicas (reta, L, U, estribo fechado, grampo), etiquetas de vergalhão, tabela de vergalhões, telas soldadas. Saída: relatório em `estudos/` para o usuário corrigir o template.

**Arquivos previstos:** `Commands/ListRebarTypesCommand.cs`, `GetRebarHostInfoCommand.cs`, `GetRebarsCommand.cs`, `Rebar/Nbr6118.cs`, `McpServer/src/rebarSpec.ts` + testes, `CommandRegistry.cs`, `index.ts`, `docs/REBAR.md`, `docs/COMMANDS.md`, CHANGELOG.

**Como verificar:**
```powershell
node scripts/check-version.mjs
cd src/McpServer; npm test; cd ../..
dotnet test src/RevitAddin.Tests/RevitMCPAddin.Tests.csproj -p:DeployToRevit=false
```

**Critério de aceite:**
1. Na cópia do 24-062, `get_rebars` retorna 1546 barras e 1368,2 kg ±0,5%, com a mesma divisão por bitola do IFC.
2. `audit_rebar_template` roda na cópia do template e gera o relatório de lacunas.
3. O parser lê todas as chamadas das folhas 6 e 8 da piscina 26-014 (PDF).

**Rollback:** revert do PR. Nada criado no modelo.

### Fase 2 - Primitivas de escrita (v0.10.0)

**Entrega:** o Claude cria qualquer conjunto de barras descrito explicitamente, define cobrimento, área e tela.

**Pode assumir:** fase 1 (tipos resolvidos por nome).

**Garante:** contratos estáveis de `create_rebar_set`, `set_rebar_cover`, `create_area_reinforcement`, `create_fabric_area`, `delete_rebars`.

**NÃO faz:** derivar a armadura da geometria do elemento (fases 3 a 5); peças curvas (fase 7).

**Ferramentas:**
- `set_rebar_cover`: cobrimento por face (superior, inferior, demais) usando ou criando o RebarCoverType `"30 mm"`.
- `create_rebar_set`: hospedeiro, Ø, forma (nome do template) ou polilinha no plano da barra, ganchos início/fim e orientação, distribuição (`max_spacing`, `number_with_spacing`, `min_clear_spacing`, `fixed_number`, `single`), comprimento da distribuição, partição, comentário. `Rebar.CreateFromCurves` com `useExistingShapeIfPossible` + `GetShapeDrivenAccessor().SetLayoutAs*`.
- `create_area_reinforcement`: contorno (ou face inteira do hospedeiro), direção principal, camadas superior/inferior × principal/secundária com Ø, espaçamento e ganchos.
- `create_fabric_area`: tela soldada do template (ex.: Q-138) por contorno, com traspasse.
- `delete_rebars`: remoção por hospedeiro ou por partição (para refazer).

**Critério de aceite:** recriar à mão a laje de fundo e a PAR 1 da piscina numa cópia; `get_rebars` bate com a tabela original nessas posições.

**Rollback:** revert do PR; no modelo de teste, `delete_rebars` ou desfazer.

### Fase 3 - Vigas retas (v0.11.0)

**Entrega:** `reinforce_beam` arma uma viga (ou trecho contínuo) a partir da notação do engenheiro.

**Entrada:** longitudinais inferiores e superiores por camada (`(1c)`, `(2c)`), extensão (corrida ou com comprimento/posição), ancoragem nos apoios (reta, gancho, dobra), estribos por trechos (`20 N13 c/5` + `42 N1 c/10`), ramos extras, pele (`2x3 N16 ø6.3`), porta-estribo, barras de suspensão.

**Faz:** calcula a geometria pela LocationCurve + seção (b × h) do tipo; aplica cobrimento; emite avisos NBR (lb, dobra mínima, espaçamento de estribo); tudo numa Transaction.

**NÃO faz:** viga curva (fase 7), protendida, dimensionamento.

**Critério de aceite:** reproduzir V201 e V205 da casa (folha 10) com as mesmas posições, quantidades e comprimentos (±1 cm por arredondamento).

### Fase 4 - Sapatas e blocos (v0.12.0)

**Entrega:** `reinforce_footing` (malha inferior bidirecional com ganchos, opção de malha superior) e `reinforce_pile_cap` (malha inferior, estribos horizontais/verticais, armadura superior e de pele).

**NÃO faz:** arranques e esperas de pilar (etapa 2, junto com pilares).

**Critério de aceite:** reproduzir S1, S13 e S8-10 da casa (folhas 3 e 8): `12 N1 ø10.0 c/10 C=168` etc.

### Fase 5 - Lajes e paredes retas (v0.13.0)

**Entrega:** `reinforce_slab` e `reinforce_wall` aplicando a regra D3: área para lajes grandes e paredes retas, tela quando indicada, conjuntos de barras para lajes pequenas cercadas por vigas (positiva por vão, negativa sobre apoios com comprimento informado), armadura de borda livre.

**Critério de aceite:** reproduzir as lajes POS/NEG e as paredes da piscina (folhas 4 a 9) com quantitativo igual à tabela original ±1%.

### Fase 6 - Documentação (v0.14.0)

**Entrega:** prancha de armação gerada pelo MCP no padrão da piscina.
- `tag_rebars`: etiquetas `N.. Ø.. c/.. C=..` com a família do template; Multi-Rebar Annotation (cota de distribuição + etiqueta).
- `set_rebar_presentation`: primeira/última/meio por vista; visibilidade sólida no 3D.
- `create_rebar_bending_detail`: dobras cotadas (como os detalhes em vermelho).
- `number_rebars`: numeração N por partição (por pavimento ou por peça).
- Tabelas: reaproveitar `create_schedule`/`configure_schedule` para "Tabela de vergalhões" e "Resumo do aço" por bitola (CA-50/CA-60, kg), volume de concreto e área de forma.
- Cortes por viga/sapata com `create_section_view` + crop; montagem com `place_view_on_sheet`.

**Critério de aceite:** gerar a folha "ISOMETRICO ARMADURAS + Tabela de vergalhões" e uma folha de detalhe de laje da piscina numa cópia, conferidas lado a lado com o PDF R02.

### Fase 7 - Paredes e vigas curvas (v0.15.0)

**Entrega:** Rebar free form acompanhando o cobrimento (longitudinais e transversais) em paredes curvas e vigas curvas.

**Risco principal:** API free form é a mais frágil; fase isolada para não travar as anteriores.

### Fase 8 - Entradas automáticas (v0.16.0)

- **8a PDF de detalhamento:** extrair as chamadas vetoriais (`N`, Ø, c/, C=, camada, PELE), associar a peças pela posição e título da vista, montar `ReinforcementSpec` e pedir confirmação antes de aplicar.
- **8b IFC com barras** (ex.: Revit, IFC2X3 do 24-062): ler `IfcReinforcingBar` (eixo, Ø, comprimento, forma) e recriar como Rebar nativo. Aceite: 1546 barras / 1368,2 kg iguais ao original.
- **8c IFC do Eberick** (IFC4, sem barras; ex.: ginásio 25-061): converter as peças em elementos nativos (vigas por seção bw × h, pilares, lajes, blocos, estacas, níveis), ler cobrimento/CAA/fck por peça, modelar as barras a partir do PDF (8a) e **conferir o peso por bitola de cada peça contra o `AltoQi_Eberick-Item_0n`** (tolerância a definir com o usuário, sugestão ±3%).

## 7. Riscos e mitigação

| Risco | Mitigação |
| --- | --- |
| Forma do template não bate com as curvas | `useExistingShapeIfPossible`; listar formas candidatas; parar e perguntar em vez de criar forma nova |
| Hospedeiro inválido (material não concreto, elemento não estrutural) | `get_rebar_host_info` antes; erro claro com o motivo |
| Ganchos com orientação invertida | planejador define orientação pela normal da face; teste com casos simétricos |
| Diferenças de API 2025 × 2026 | CI compila as duas; validação ao vivo nas duas antes do merge |
| Desempenho em lajes grandes | Area Reinforcement em vez de milhares de barras; batch numa Transaction |
| Contagem de ferramentas nos docs diverge | `check-version.mjs` falha; atualizar README/ARCHITECTURE/TROUBLESHOOTING/API_COVERAGE/COMMANDS na mesma fase |
| Conflito de versão com o Lucas | conferir a versão do branch base antes de cada bump; PRs pequenos por fase |

## 8. Fora de escopo (desta etapa)

Pilares e arranques (etapa 2), dimensionamento pela NBR a partir de esforços (só por decisão do usuário, com revisão do responsável técnico), armadura ativa/protensão, punção e fretagem, modelagem de formas a partir de planta em PDF (pode virar plano próprio), path reinforcement de borda (avaliar após a fase 5).

## 9. Pendências do usuário antes da fase 1

1. ~~Template `.rte` e modelo `.rvt` com armadura~~ **recebidos em 2026-10-05** (24-062 + IFC + Template Concreto).
2. **Reiniciar o Revit** (salvando antes) para carregar o add-in; sem isso não há como ler os modelos.
3. ~~Modelo de teste com vigas, sapata, bloco e laje~~ **ginásio 25-061 recebido** (IFC Eberick + PDFs).
4. ~~Etiqueta de vergalhão~~ **recebida** (`Vergalhão ABNT - Completo.rfa`). Tabelas: usar os nomes das pranchas ("Tabela de vergalhões", "Resumo do aço", "Volume de concreto", "Área de forma"); reaproveitar se já existirem no template.
5. ~~fck~~ **C30**.
6. Telas soldadas no template: conferidas pelo próprio MCP na fase 1 (`new_project_from_template` + `audit_rebar_template`); não exige ação do usuário.
