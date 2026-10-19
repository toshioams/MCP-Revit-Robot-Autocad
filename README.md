# MCP Revit, Robot e AutoCAD (TMS Projetos)

Ferramentas de automação estrutural via MCP (Model Context Protocol) usadas com o Claude Code.

## Conteúdo

| Pasta | O que é |
|---|---|
| `RevitMCP-KoreOS-Traco/` | Add-in C# do Revit (`src/RevitMCP.Core`) + servidor MCP Node/TS (`src/McpServer`). Veja o README próprio. |
| `agentes/` | Subagentes do Claude Code: `verificador-revit` (desenho das pranchas), `auditor-revit` (conteúdo técnico) e `compatibilizador-revit` (decisões x modelo). Copiar para `~/.claude/agents/`. |
| `skills/padrao-detalhamento-tms/` | Skill do padrão TMS de detalhamento. Copiar para `~/.claude/skills/`. |
| `docs/` | `PADRAO-DETALHAMENTO-TMS.md` (o quê), `ROTEIRO-DETALHAMENTO-ACO.md` (como fazer), catálogo de famílias TMS e bibliotecas Python. |
| `ferramentas/` | `porta-fixa-pyrevit` (porta fixa por instância do Revit) e utilitários. |
| `planos/` | Planos de evolução (ex.: armaduras). |
| `sessoes/` | Modelos de sessão por instância do Revit (pyRevit nas portas 48884 e 48885) com `.mcp.json`. Ajuste os caminhos locais. |

## Conectores pyRevit

Os agentes usam o servidor [mcp-server-for-revit-python](https://github.com/mcp-servers-for-revit/mcp-server-for-revit-python) rodando em duas instâncias do Revit (`REVIT_PORT` 48884 e 48885). Veja `sessoes/` e `ferramentas/porta-fixa-pyrevit`.
