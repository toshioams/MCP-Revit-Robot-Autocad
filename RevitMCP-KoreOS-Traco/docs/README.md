# Documentação — comece aqui

- [Armaduras nativas — 0.9.0](REBAR.md): leitura, auditoria do template e parede circular armada com traspasses.
- [Referências externas baixadas](../referencias/README.md): seis repositórios com commits e licenças registrados.
- [Operações com chave — 0.8.65](OPERATION_IDEMPOTENCY.md): primeira absorção, protocolo e validação pendente no Revit.

- [Pesquisa de MCPs para Revit — 15/09/2026](research/REVIT_MCP_ECOSYSTEM_2026-09-15.md):
  inventário externo, comparação com a base 0.8.64 e prioridades de absorção.

- [Encontro paredes/telhado e oitões por perfil](WALL_ROOF_CONNECTION.md):
  regra de decisão, anexação validada ao vivo, incidente de dryRun, 0.8.41 pendente.
- [Caixa de escopo](SCOPE_BOX.md): regra do usuário, correção aplicada e limites da API.
- [Estrutura da cobertura](ROOF_STRUCTURE.md): decisões (terças nos oitões, lambril sobre
  caibros, capa-canal, junta lambril × alvenaria), geometria, roteiro e pendências de cálculo.

1. [HANDOFF.md](HANDOFF.md): passagem consolidada para outro agente.
2. [Snapshot da cabana](snapshots/cabana-2026-09-10.json): leitura real de elementos,
   parâmetros e avisos; IDs e UniqueIds para comparação, não restauração automática.
3. [AUTONOMIA_STATUS.md](AUTONOMIA_STATUS.md): capacidades e roadmap.
4. [CABANA_SESSAO_2026-09-10.md](CABANA_SESSAO_2026-09-10.md): diário das intervenções.
5. [FAMILY_VISUAL_VALIDATION.md](FAMILY_VISUAL_VALIDATION.md): erros aprendidos,
   interpretação das famílias, validação visual e calibrações.
6. [SEMANTIC_FOUNDATION.md](SEMANTIC_FOUNDATION.md): ferramentas novas da 0.8.37.

Contexto anterior: PLANO_MODELAGEM_DETALHAMENTO.md, SESSAO_01_WC_PCD.md,
CABANA_COLONIAL.md, LIVE_VALIDATION_2026-09-09.md.
Contratos específicos: FLOOR_TYPE_VARIANT.md, FLOOR_RELATIVE_PLACEMENT.md,
WALL_FACE_PLACEMENT.md, PLACEMENT_AND_TYPE_INSPECTION.md, CEILING_HEIGHT_OFFSET.md,
FAMILY_LIBRARY.md. Referência geral: COMMANDS.md, ARCHITECTURE.md e CHANGELOG na raiz.

O histórico registra estados antigos e tentativas desfeitas. Em caso de divergência,
priorize a solicitação recente do usuário e a leitura real do modelo; HANDOFF resume
o último estado documentado. Não executar cegamente os comandos do diário.
- [CAD_TRACING.md](CAD_TRACING.md) — modelagem a partir de DWG (import_cad, inspect_cad, planejador de paredes)
