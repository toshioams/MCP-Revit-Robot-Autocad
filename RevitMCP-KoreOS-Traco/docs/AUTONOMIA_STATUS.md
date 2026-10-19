# Autonomia — estado consolidado em 10/09/2026

Este arquivo complementa PLANO_MODELAGEM_DETALHAMENTO.md. O plano anterior contém
propostas, não uma garantia de cobertura nem de suporte da API a todos os itens.
Objetivo mantido: briefing de Lucas e sua esposa, execução pelo agente e conferências
de projeto pelos profissionais. Não há autonomia completa implementada.

## Pacote local 0.8.35

Atualização 0.8.36: adicionada ferramenta `create_floor_type_variant`, compilada
e com execução no Revit pendente de reinício. Ver FLOOR_TYPE_VARIANT.md para
contrato e aplicação prevista ao banheiro. 99 ferramentas MCP e 105 comandos C#.
Build C#/TypeScript e testes existentes (170 C#, 26 Node) passaram; esses testes
não exercitam a nova edição de camadas dentro do Revit.

| Capacidade | Estado | Limite/aceite restante |
|---|---|---|
| Busca local de RFA | Implementada e testada com 450 arquivos | Nomes não comprovam conteúdo nem adequação |
| Lista de tipos de sistema | Validada para forros no Revit | Ampliar cenários |
| Altura de forro | Validada em dryRun a 2.40 m | Não testa avisos de commit |
| Inspeção de família/instância | Hospedeiro e ponto conferidos no Revit | Não identifica semanticamente topo do assento |
| Hospedagem por face | Banco/chuveiro validados em parede plana | Não cobre curvas, links ou outros hospedeiros |
| Altura relativa ao piso | Compilada | Testar inclinação, rebaixo, abertura; sem vínculo paramétrico posterior |
| Telhado nativo de duas águas | Compilado | Testar ao vivo; sem empenas, anexação ou composição cerâmica |
| Deslocamento da base na criação de parede | Compilado | Testar base negativa e topo resultante |
| get_type_structure | Leitura real do piso do banheiro validada | Paredes verticais compostas exigem regiões; ampliar cenários |
| get_warnings | Paginação, filtro e IDs validados no Revit | 34 avisos existentes, incluindo piso/laje |
| Captura de falhas no commit | Rollback de sobreposição validado individualmente e em batch | Ampliar para outros tipos de falha; não cobre diálogos genéricos |
| Isolamento de etapas do batch | Compilado | Etapa com efeito parcial seguido de erro não deve sobreviver |

## Política de falhas da 0.8.35

Todas as transações de escrita recebem IFailuresPreprocessor após Start.
Avisos/erros registrados provocam rollback. Retorno: transaction_not_committed,
committed:false, transactionStatus e failures (GUID, severidade, descrição,
elementos envolvidos e elementos adicionais). Nada é aceito automaticamente.
Isso substitui inclusive o antigo SuppressWarningsOnCommit dos comandos de percurso.
Pode mudar o resultado de operações antes aceitas com avisos; essa mudança é intencional.

O status retornado por Commit é verificado. Em batch, resultados de etapas executadas
não significam persistência se o envelope do lote indicar committed:false.
DryRun continua fazendo rollback antes do commit: commitValidationPerformed:false.
Logo, passar no dryRun não garante ausência de avisos na execução real.
Não cobre TaskDialog genérico, carga de família, diálogos de terceiros ou salvar arquivos.

## Ordem de implementação restante

1. Validar pacote no Revit e corrigir falhas identificadas; nenhuma fase depende de
   afirmar sucesso apenas porque a compilação passou.
2. Leitura de faces, referências estáveis e contornos de ambientes; posicionamento
   por afastamentos da face acabada e calibração das origens das famílias.
3. Editar perfil de piso sem recriar elementos; pisos por ambiente, soleiras,
   camadas de acabamento e apoio, caimentos e rebaixos sem sobreposição.
4. Criar/editar tipos compostos e materiais; identificar acabamento e estrutura
   sem confiar no nome; preservar parâmetros Filtro/Subfiltro.
5. Completar cobertura: anexação de paredes, empenas, beirais, materiais,
   estrutura de madeira, calhas/rufos, cortes verificáveis.
6. Identidade persistente dos elementos gerados e atualização sem duplicar;
   proteção de alterações manuais, retomada de etapas e registro de dependências.
7. Biblioteca entre documentos: listar documentos, selecionar explicitamente o alvo,
   consultar e transferir famílias com política de conflito. RFA local já disponível.
8. Documentação: chamadas, elevações internas, referências de cotas, etiquetas,
   paginação e layout de pranchas, exportação e quantitativos.
9. Modelagem adicional: escadas, guarda-corpos, rampas, fachadas cortina e terreno,
   com verificação de viabilidade por versão da API antes de prometer comandos.
10. Orquestração: briefing estruturado, padrões do escritório, planejamento,
    conferência geométrica/visual, recuperação de erros e checkpoints persistentes.
    Regras normativas devem registrar fonte, versão e cobertura; não declarar
    conformidade quando uma condição não foi verificada.

## Cabana: pendências concretas

Telhado/empenas e acabamento colonial; equipar cozinha e banheiro; compatibilizar
o piso rebaixado com a laje (sobreposição registrada); conferir acessos e mobiliário;
enquadramento de vistas. A cabana ainda não está concluída.

## Validação executada

Após reinício em 10/09/2026: health confirmou 0.8.35, 104 comandos, build
2026-09-10T13:11:50Z. Cópias coincidentes do piso 19389484 retornaram
transaction_not_committed, committed:false e RolledBack, tanto individualmente
(ID temporário 19394246) quanto em batch (19394265). Consultas posteriores
confirmaram ausência de ambos. Nenhuma interação manual foi necessária para
concluir as chamadas; não houve inspeção visual de diálogos.
Cópia deslocada 20 m (19394284) confirmou committed:true; removida em seguida,
incluindo seus dependentes, com commit confirmado. Ausência verificada por ID.
Contagem final de avisos permaneceu 34, igual à inicial.

get_type_structure retornou no tipo 18691565: porcelanato 1 cm, massa de
assentamento 1 cm e regularização 3 cm. Piso a -0.01 m possui base -0.06 m;
o aviso original com laje 19389468 permanece e precisa de compatibilização.
Estes testes não validam ainda falhas parciais internas de etapas do batch,
telhado ou hospedagem relativa em pisos inclinados.

Verificação de instalação: `rtk proxy powershell -NoProfile -File scripts/verify-local-addin.ps1`.
O script confere o arquivo apontado pelo manifesto e a versão realmente carregada;
somente após confirmar 0.8.35 consulta a primeira página de avisos, sem alterações
no documento. Não valida falhas de commit. Em 10/09/2026, confirmou DLL preparada
0.8.35 e processo ainda em 0.8.34: reinício do Revit pendente. Salvar o trabalho
antes de fechar. Reiniciar também o cliente MCP para expor os novos esquemas.

Builds C# Revit 2026 e TypeScript passaram. 170 testes C# e 26 Node existentes
passaram. Não exercitam o novo processamento de falhas dentro do Revit.
Para validar: registrar warnings/counts iniciais; criar duas peças coincidentes em
uma transação de teste; esperar committed:false e failures; conferir ausência de
elementos residuais; testar operação válida com committed:true; repetir em batch.
Não usar nem salvar o modelo de produção como fixture destrutivo.
