---
name: padrao-detalhamento-tms
description: Padrão TMS Projetos de detalhamento estrutural no Revit (origem 26-083), válido para TODOS os projetos. Usar sempre que o pedido envolver detalhar, detalhamento, prancha, folha, ligação, conexão, placa base, chapa, solda, símbolo de solda, isométrico, planta de ligações, notas gerais, legenda de peças, fabricação, revisão de prancha ou verificação de pranchas, em qualquer projeto ou porta do Revit (pyRevit 48884/48885).
---

# Padrão TMS de detalhamento

Antes de planejar ou executar qualquer detalhamento:

1. Ler o padrão: `C:\Users\toshi\OneDrive\1-ARQUIVOS EMPRESARIAIS\1-Projetos\CLAUDE\MCP REVIT\docs\PADRAO-DETALHAMENTO-TMS.md`.
2. Ler o "como fazer" (técnicas, scripts, erros e acertos): `...\MCP REVIT\docs\ROTEIRO-DETALHAMENTO-ACO.md`.
3. Ler as regras de sessão e prancha: `...\MCP REVIT\sessoes\CLAUDE.md` (seções 4 e 7) e o .md do projeto em `...\MCP REVIT\docs\`.

Durante o trabalho:
- Plano em ordem cronológica, aprovado pelo usuário; salvar antes e a cada etapa; desfazer a etapa em caso de erro.
- Seguir a sequência de folhas, a nomenclatura (VM, VS, CH, L, PAR, S, RC; PB/LG nas plantas), o conteúdo obrigatório por ligação (montagem, fabricação por peça, isométrico, símbolo de solda, indicação em planta), as notas e legendas e a nota de escopo do padrão.
- Textos e tabelas que citam detalhe/folha: gerar a partir do modelo (viewports), nunca digitar.
- Depois de cada edição: agente `verificador-revit` (porta + folhas alteradas). Corrigir tudo antes de relatar.
- Lição nova: registrar no .md do projeto, no ROTEIRO e, se mudar o padrão, no PADRAO (com a data).
