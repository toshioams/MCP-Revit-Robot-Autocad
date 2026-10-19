# Pesquisa de MCPs para Revit — 15/09/2026

## Conclusão

As melhores referências para o nosso próximo ciclo são **mskim274/revit-mcp-v2** (ciclo de desenvolvimento e sessões), **Horizun** (DWG rastreável e famílias) e **RevitCortex** (contratos e descoberta contextual). O ganho está em adaptar componentes específicos à nossa arquitetura, preservando as ferramentas de modelagem e as verificações que já desenvolvemos.

Esta é uma pesquisa com leitura seletiva de código, não uma validação das implementações no Revit. Nenhum add-in externo foi instalado, executado ou compilado. Nenhum modelo foi alterado.

## Abrangência e método

- Pesquisa web por Revit + MCP, servidores, documentação de API, IFC e automação; buscas direcionadas a GitHub, npm, Glama, MCP.so, Smithery, Autodesk, YouTube, GitLab e Gitee.
- Consulta direta à API pública do GitHub: `revit mcp in:name,description`, ordenada por estrelas, três páginas. **249 candidatos** coletados no [inventário JSON](revit-mcp-github-2026-09-15.json). Não são 249 servidores independentes: há componentes, tutoriais, skills e soluções relacionadas. A busca padrão também não enumera todos os forks.
- Triagem dos principais resultados por descrição e documentação; inspeção de arquivos selecionados nos três projetos prioritários.
- Diretórios e redes sociais serviram para descoberta. As conclusões técnicas abaixo se apoiam nos repositórios dos autores e na Autodesk.
- GitLab não trouxe candidato adicional convincente nas consultas realizadas; Gitee trouxe uma listagem de usuário, sem evidência suficiente para classificar uma implementação. Isso não demonstra ausência de projetos nesses serviços.
- Não é possível garantir cobertura literal de toda a internet, repositórios privados ou comunidades fechadas. O inventário registra o alcance reproduzível desta busca.

## Nossa base real

`origin` aponta para **KenLP/RevitMCPServer**: esse resultado de busca é a nossa origem, não uma alternativa externa. `src/McpServer/package.json` e CHANGELOG indicam **0.8.64**; a abertura do HANDOFF ainda menciona 0.8.52. Versão em disco não estabelece a versão carregada no Revit.

Leitura local: `docs/HANDOFF.md`, `docs/README.md`, `docs/ARCHITECTURE.md`, CHANGELOG, `src/McpServer/src/index.ts`, `revitClient.ts`, `src/RevitAddin/App.cs`, servidor HTTP, dispatcher e inventário de comandos.

Já temos:

- Ponte TypeScript/stdio → HTTP local autenticado → ExternalEvent/C#.
- Transações, batches, dryRun, captura de falhas e indicação de commit.
- Direcionamento por `documentTitle`/`documentPath`, consulta de documentos e resolução por UniqueId.
- Perfis de ferramentas e paginação em operações específicas.
- Modelagem, cobertura, famílias hospedadas, DWG, nuvem de pontos, materiais, cotas e revisão visual.
- Painéis próprios e registros de verificação em casos reais.

Portanto, chat embutido, criar paredes, seleção, exportação básica e simples divisão em camadas não justificam trocar nossa base. A ausência de uma capacidade nas buscas locais abaixo significa **lacuna aparente a confirmar na implementação**, não uma auditoria exaustiva.

## Referências prioritárias

| Projeto / fonte primária | Evidência nesta pesquisa | O que aproveitar | Limitação importante |
|---|---|---|---|
| [mskim274/revit-mcp-v2](https://github.com/mskim274/revit-mcp-v2) | README, documento de hot reload, loader completo quase integral e trechos do runtime | Comandos recarregáveis, seleção persistente de sessão/documento, idempotência e limites de resposta | README limita suporte atual a 2025 e builds 2023/24; não assumir compatibilidade 2026 por usar .NET 8 |
| [HorizunGroup/horizun-revit-mcp](https://github.com/HorizunGroup/horizun-revit-mcp) | README, árvore, registro de origem CAD e receita de família | Rastreabilidade DWG, atualização incremental, famílias por especificação, verificação pós-commit | Suíte extensa; a leitura de dois componentes não valida o conjunto |
| [LuDattilo/RevitCortex](https://github.com/LuDattilo/RevitCortex) | README, árvore e `CortexResult<T>` | Erros com contexto/sugestão, descoberta conforme documento, IFC e relatórios | IFC/Power BI vistos na documentação, sem auditoria dos pipelines |
| [mroshdy91/RevitAtlas](https://github.com/mroshdy91/RevitAtlas) | README | Snapshots Parquet/DuckDB, relações espaciais e rastreamento MEP | Desempenho anunciado não medido; disponibilidade do código interno não confirmada |
| [Sam-AEC/aec-model-bridge](https://github.com/Sam-AEC/aec-model-bridge) | README | Separação por provedores para Revit, Rhino, IFC e Speckle | O autor distingue integrações disponíveis de Navisworks/Power BI ainda em integração |
| [kaitpw/Rvt_Docs_MCP](https://github.com/kaitpw/Rvt_Docs_MCP) | README e árvore | Consulta de documentação de API e busca técnica durante desenvolvimento | Licença não identificada pela API; o README também registra dúvidas sobre uso dos endpoints de documentação |
| [Autodesk Public MCP](https://www.autodesk.com/blogs/aec/2026/06/17/revit-public-mcp-server/) | Publicação oficial de 17/06/2026 | Contrato de consulta com filtros, resumo e projeção de dados | Tech Preview para Revit 2027, com foco em leitura; não supre nossa modelagem 2025/2026 |

### 1. Atualizar comandos sem reiniciar — maior ganho para nosso ciclo

O loader de mskim usa um `AssemblyLoadContext` descarregável, compartilha contratos e assemblies da API com o host e carrega os arquivos por streams. O documento separa o host estável do pacote recarregável. Alterações no host/contratos ainda exigem reinício. [Código do loader](https://github.com/mskim274/revit-mcp-v2/blob/9d2b3d95086c6e18653e2227947c757f8b3f3514/plugin/RevitMCPPlugin/Services/CommandSetLoadContext.cs), [contrato de recarga](https://github.com/mskim274/revit-mcp-v2/blob/9d2b3d95086c6e18653e2227947c757f8b3f3514/docs/COMMANDSET_HOT_RELOAD.md).

**Adaptação proposta:** extrair interfaces estáveis do nosso Core, manter dispatcher/transações no host, carregar implementações por geração e oferecer status/ativação do pacote. Nosso uso atual de `AssemblyLoadContext` em App.cs resolve dependências de painel; não é um mecanismo de substituição de comandos.

**Aceitação:** comando já enfileirado termina com a geração original; pacote inválido mantém a anterior; versão/hash ativos ficam visíveis; testar vazamento de referências e incompatibilidade de versão. A instalação inicial dessa infraestrutura precisará aguardar o próximo reinício decidido pelo usuário. Continuar usando staging e `DeployToRevit=false`.

### 2. DWG com origem persistida e atualização incremental

O registro CAD do Horizun distingue identidade geométrica, semântica, revisão, regra, arquivo e instância de vínculo. Guarda a geometria inicial para diferenciar mudança do desenho de alteração manual no elemento. Há código separado para armazenamento e regras. [Registro de origem](https://github.com/HorizunGroup/horizun-revit-mcp/blob/cc4ea04e9ecfe547ad349f22e0864019ce1ead1f/src/Horizun.Revit/Core/CadProvenanceRecord.cs).

**Adaptação proposta:** ampliar `cadWalls.ts` e a etapa de aplicação com `sourceUniqueId`, hash do desenho, transformação, camada, regra/versionamento e geometria usada. Persistir no RVT com esquema próprio, mantendo índices externos como apoio. Comparar origem anterior, desenho novo e modelo atual antes de atualizar.

**Aceitação:** repetir a mesma execução não cria duplicatas; deslocar o vínculo não equivale a editar manualmente a parede; paredes ajustadas pelo usuário aparecem como conflito; dois vínculos do mesmo DWG permanecem distintos. Não prometer handle original de entidade quando nossa extração via Revit não o fornece.

### 3. Famílias paramétricas por receitas

O Horizun documenta autoria de RFA a partir de RFT. No componente examinado, as receitas de prisma/tubo retangular parametrizam a altura, exigem pelo menos dois tipos com alturas diferentes e geram extrusão; o perfil XY permanece fixo. Isso é mais restrito que uma família inteiramente parametrizada. [Receita examinada](https://github.com/HorizunGroup/horizun-revit-mcp/blob/cc4ea04e9ecfe547ad349f22e0864019ce1ead1f/src/Horizun.Revit/Core/FamilyRecipe.cs).

**Adaptação proposta:** evoluir nosso `CreateProfileFamilyCommand` para receitas pequenas do escritório, com materiais, dimensões e tipos explicitamente definidos. Priorizar elementos leves úteis, como painéis e componentes repetitivos; avaliar conectores e famílias aninhadas depois.

**Aceitação:** trocar dois ou mais tipos, medir a geometria resultante, salvar/reabrir RFA e verificar a instância carregada. Preservar a decisão de evitar malhas de fios pesadas.

### 4. Idempotência e identidade de sessão

mskim documenta cache de idempotência no host e seleção de documento vinculada à sessão. Ainda requer leitura dedicada desses componentes antes de portar. [Arquitetura e sessões](https://github.com/mskim274/revit-mcp-v2#multiple-revit-sessions).

No nosso `revitClient.ts`, `x-request-id` nasce com um UUID em cada POST; isso é rastreamento, não prova de deduplicação. O timeout devolve erro de transporte; não informa por si só se uma edição chegou a ser concluída.

**Adaptação proposta:** chave por operação lógica + hash do payload + identidade de documento/sessão, mantidos no host. Consultar o estado de uma operação após timeout. Repetição de chave com payload diferente deve falhar. Uma mesma versão do Revit em dois processos deve ter identidade distinta.

**Aceitação:** resposta perdida após commit seguida de repetição cria uma única parede; requisições simultâneas iguais não executam duas vezes; troca de documento é detectada. Não habilitar retry automático de escrita antes disso.

### 5. Contratos melhores e ferramentas conforme contexto

RevitCortex contém um resultado genérico com sucesso/dados ou erro tipado; erros podem carregar sugestão e contexto. A árvore também contém analisadores de capacidade do documento e testes. [Resultado](https://github.com/LuDattilo/RevitCortex/blob/8b2556daefb2bf88f0a7a17bf28f2352fe0b0e33/src/RevitCortex.Core/Results/CortexResult.cs).

**Adaptação proposta:** ampliar nosso envelope mantendo compatibilidade: resultado de commit, evidência de verificação, próxima ação sugerida, truncamento e capacidades ativas. Os perfis estáticos que já temos podem ser complementados por descoberta contextual. Não precisamos migrar o servidor TypeScript para C# para obter esses benefícios.

**Aceitação:** documento de família e projeto expõem capacidades corretas; comando indisponível explica o requisito; cliente antigo continua recebendo resposta válida.

## Outros projetos encontrados e destino da triagem

| Projeto | Potencial / decisão |
|---|---|
| [mcp-servers-for-revit/mcp-servers-for-revit](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit) | Catálogo, empacotamento por versão e testes de integração. MIT indicada no repositório. |
| [revit-mcp original](https://github.com/mcp-servers-for-revit/revit-mcp), [plugin](https://github.com/mcp-servers-for-revit/revit-mcp-plugin), [commandset](https://github.com/mcp-servers-for-revit/revit-mcp-commandset) | Componentes históricos da mesma linhagem; os três constam arquivados na API consultada. Evitar contá-los como três concorrentes completos. |
| [mcp-server-for-revit-python](https://github.com/mcp-servers-for-revit/mcp-server-for-revit-python) | Alternativa pyRevit; referência para integração com scripts. |
| [LuDattilo/revit-mcp-server](https://github.com/LuDattilo/revit-mcp-server) | Idiomas, catálogo ampliado e instalação. README traz números divergentes de ferramentas; não usar quantidade como medida de qualidade. |
| [oakplank/RevitMCP](https://github.com/oakplank/RevitMCP) | pyRevit com superfícies web e MCP. Considerar padrões de interface, sem necessidade de substituir nosso host. |
| [schauh11/revit-mcp-server](https://github.com/schauh11/revit-mcp-server) | Chat WPF e provedores de IA; benefício menor para quem já usa o cliente Codex. |
| [shuotao/REVIT_MCP_study](https://github.com/shuotao/REVIT_MCP_study) | Material de estudo e perfis por disciplina; distribuição npm identificada. |
| [PATAGONIABIM/revit-mcp-revit-plugin](https://github.com/PATAGONIABIM/revit-mcp-revit-plugin) | Ferramentas de metadados, localização e modelagem. Comparar comandos pontuais. |
| [26730023-PhamPhuNguyenHung/revit-mcp](https://github.com/26730023-PhamPhuNguyenHung/revit-mcp) | Anuncia DWG → BIM e Dynamo; falta verificar implementações e limites desses fluxos. |
| [okuno-dsi/revit-mcp-toolkit](https://github.com/okuno-dsi/revit-mcp-toolkit) | Toolkit japonês JSON-RPC, referência de organização. |
| [Thestreetarckitect/revit-2027-mcp](https://github.com/Thestreetarckitect/revit-2027-mcp) | Catálogo de MEP/estrutura anunciado para 2027; portabilidade não verificada. |
| [DTDucas/RevitMCPSDK](https://github.com/DTDucas/RevitMCPSDK) | SDK JSON-RPC distribuído por versão; avaliar contratos e extensibilidade, sem acrescentar uma segunda ponte. |
| [GraphBIM/RevitMCPGraphQL](https://github.com/GraphBIM/RevitMCPGraphQL) | Consulta GraphQL com seleção de campos; o próprio projeto se apresenta como hackathon. Inspiração para projeções, não base de produção comprovada. |
| [bimwright/rvt-mcp](https://github.com/bimwright/rvt-mcp) | Propõe transformar padrões repetidos em ferramentas pessoais (ToolBaker). Aproxima-se das nossas receitas; autoria completa de famílias está fora do escopo declarado. |
| [Demolinator/revit-mcp-server](https://github.com/Demolinator/revit-mcp-server) | FastMCP/pyRevit, catálogo de arquitetura/MEP. Leitura de README, sem testes. |
| [APS MCP Automation](https://github.com/autodesk-platform-services/aps-sample-mcp-server-revit-automation) e [AppBundle](https://github.com/autodesk-platform-services/aps-sample-revit-mcp-tools-bundle) | Exemplos Autodesk de execução na nuvem com autenticação serviço a serviço. O autor classifica o servidor como prova de conceito. Considerar para processamento em lote futuro. |

O JSON contém candidatos adicionais, por exemplo BIM/GIS, Dynamo, servidores documentais e skills comerciais. Não foram promovidos ao mesmo nível de evidência dos componentes examinados.

## Ordem de absorção sugerida

Prioridade é julgamento desta pesquisa, baseado nas necessidades documentadas do nosso projeto. Esforço é relativo, sem promessa de prazo.

| Ordem | Entrega | Esforço | Motivo |
|---|---|---|---|
| 1 | Idempotência, consulta de operação e identidade de processo/documento | Médio | Evita duplicação e execução no alvo errado |
| 2 | Host estável + comandos recarregáveis | Alto | Reduz os reinícios recorrentes durante desenvolvimento |
| 3 | Origem DWG persistida + aplicação repetível | Médio/alto | Permite continuar/revisar modelagem sem refazer tudo |
| 4 | Receita pequena de família + teste de variação de tipos | Médio | Produz componentes do escritório com geometria verificável |
| 5 | Envelope consistente + capacidades dinâmicas | Médio | Diminui tentativas inválidas e melhora recuperação |
| 6 | Consultas com campos escolhidos, resumo e limites globais | Médio | Reduz volume de resposta em modelos grandes |
| 7 | Snapshot analítico/IFC fora do Revit | Alto | Útil quando houver demanda real de análise pesada |
| 8 | MEP especializado, Power BI e APS | Alto | Depende de escopo de negócio ainda não solicitado |

Para análises externas, snapshots devem carregar documento, revisão, instante e unidades. Antes de escrever, reler os elementos no Revit. Números de desempenho anunciados por terceiros não foram reproduzidos.

## Código reutilizável e limites

- API do GitHub identificou MIT em mskim e RevitCortex e Apache-2.0 no Horizun; são os candidatos iniciais a uma revisão de incorporação. Verificar LICENSE/NOTICE e dependências no commit efetivamente escolhido e preservar os avisos aplicáveis.
- No Rvt_Docs_MCP não foi identificada licença formal nesta inspeção. Recomenda-se avaliar integração/implementação própria com fontes autorizadas antes de copiar código.
- Não copiar mecanismos de execução arbitrária de código apenas para aumentar cobertura. Bloqueio textual de namespaces não estabelece isolamento real.
- Não transportar critérios estrangeiros de conformidade como se fossem regras do nosso escritório/projeto. Receber critérios e tolerâncias explicitamente.
- Não instalar vários add-ins concorrentes no ambiente corrente para comparar; a seleção técnica já pode avançar por revisão de código e testes isolados.

## Revisões fixadas para a leitura seletiva

| Repositório | Commit retornado pela árvore do GitHub |
|---|---|
| mskim274/revit-mcp-v2 | `9d2b3d95086c6e18653e2227947c757f8b3f3514` |
| HorizunGroup/horizun-revit-mcp | `cc4ea04e9ecfe547ad349f22e0864019ce1ead1f` |
| LuDattilo/RevitCortex | `8b2556daefb2bf88f0a7a17bf28f2352fe0b0e33` |
| kaitpw/Rvt_Docs_MCP | `e8e22792108335db907f4f38d0a0769bb0eb3ee5` |

## Diário da pesquisa

15/09/2026: pesquisa solicitada pelo usuário; inventário de 249 candidatos salvo; comparação com código local 0.8.64; leitura seletiva de fontes primárias e componentes. RTK falhou por ausência no PATH; caminhos usuais também não foram encontrados, então foram usados comandos diretos. Sem alteração de código executável, DLL, instalação, configuração MCP ou modelo Revit. Validação feita: consultas às fontes, conferência de arquivos locais e integridade do inventário; nenhuma capacidade externa declarada validada ao vivo.
