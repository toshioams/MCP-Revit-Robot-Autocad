# Validação no Revit 2026 — 09/09/2026

Documento: `C:\Users\Mana02\Desktop\teste mcp.rvt`.
Addin: versão nominal 0.8.34, gitState dirty, buildTimestampUtc
`2026-09-09T21:14:51Z`. Alterações locais reconhecidas pelo retorno dos novos campos.

## Confirmado

- Consulta `list_family_types` com `includeSystemTypes:true`, `OST_Ceilings`:
  retornou cinco CeilingTypes, com classe e distinção de família carregável.
- Criação de forro com `heightOffset:2.4`: leitura efetiva 2.4 metros;
  batch dryRun retornou committed:false.
- Carregadas sem sobrescrever famílias existentes: banco articulado, barras,
  alarme PCD e chuveiro Aspen com ducha manual da pasta familias.
  As quatro famílias declararam `WorkPlaneBased`.
- Inserção sem referência de face, com Z explícito: chuveiro a 2.10 e banco a 0.46
  retornaram placement_mismatch e batch_aborted com committed:false.
  A proteção contra falso sucesso está funcionando nesse caso real.

## Resultado e próximo desenvolvimento

O tratamento de Z não resolve a hospedagem. Essas famílias continuam sem inserção
correta pelo overload atual. Implementar leitura de faces referenciáveis e inserção
com referência de face e direção tangente. Validar com uma parede de teste em dryRun,
conferindo ponto de inserção, hospedeiro efetivo e orientação antes de modelar o banheiro.
Nenhuma peça foi inserida permanentemente nesta validação; as quatro famílias foram
adicionadas à biblioteca do documento. O arquivo não foi salvo pelo agente.
