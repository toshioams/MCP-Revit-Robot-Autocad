
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
