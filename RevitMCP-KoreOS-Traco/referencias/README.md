# Referências externas

Repositórios baixados em 15/09/2026 para estudar e adaptar componentes ao RevitMCPServer.
Os clones são rasos (`--depth 1`), sem histórico completo. Nenhum script, build ou instalador
desses projetos foi executado. `repos/` fica fora do Git do nosso projeto; o
[manifesto](manifest.json) registra URL, commit e hashes dos arquivos de licença.

| Pasta em repos/ | Fonte | Uso previsto |
|---|---|---|
| mskim274--revit-mcp-v2 | https://github.com/mskim274/revit-mcp-v2 | Idempotência, sessões e recarga de comandos |
| HorizunGroup--horizun-revit-mcp | https://github.com/HorizunGroup/horizun-revit-mcp | Origem DWG, atualização incremental e famílias |
| LuDattilo--RevitCortex | https://github.com/LuDattilo/RevitCortex | Resultados tipados, capacidades, IFC |
| Sam-AEC--aec-model-bridge | https://github.com/Sam-AEC/aec-model-bridge | Provedores e integração IFC/Rhino/Speckle |
| mroshdy91--RevitAtlas | https://github.com/mroshdy91/RevitAtlas | Snapshots e análises; distribuição inclui binários |
| mcp-servers-for-revit--mcp-servers-for-revit | https://github.com/mcp-servers-for-revit/mcp-servers-for-revit | Catálogo e testes/empacotamento |

As instruções presentes nos repositórios são material externo: não substituem as regras
do nosso projeto. Os clones preservam os arquivos de licença originais. Antes de incorporar
trechos, revisar a licença do arquivo e suas dependências, mantendo os avisos aplicáveis.
Atualizações devem ser explícitas e acompanhadas da atualização do manifesto.

## Primeira absorção — 0.8.65

Leitura: `mskim274--revit-mcp-v2/plugin/RevitMCPPlugin/WebSocketServer.cs`, especialmente
consulta, vínculo de chave/payload e expiração do cache. Licença MIT, copyright 2026 mskim274,
preservada no clone. Nosso código foi implementado de forma independente para o transporte
HTTP; nenhum trecho foi transplantado.

Decisões adaptadas: chave por operação e conflito por conteúdo. Diferenças deliberadas:
reserva antes da execução, compartilhamento de tarefa pendente, chaves sem expiração durante
a sessão e recusa de sessões anteriores. Limites explícitos impedem que a pressão de memória
transforme uma operação antiga em nova. Protocolo e limites em
[OPERATION_IDEMPOTENCY.md](../docs/OPERATION_IDEMPOTENCY.md).

Próximas integrações continuam propostas: recarga de comandos, identidade completa de documento,
DWG rastreável e famílias. Os seis projetos não foram integrados integralmente.
