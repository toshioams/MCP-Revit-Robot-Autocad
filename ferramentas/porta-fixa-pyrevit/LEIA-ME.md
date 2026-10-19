# Porta fixa do conector pyRevit (48884 / 48885)

## Problema (diagnóstico de 15/10/2026)
O pyRevit (`C:\Program Files\pyRevit-Master\pyrevitlib\pyrevit\routes\server\serverinfo.py` e `loader\sessionmgr.py`):
1. A cada carga ou Reload do pyRevit, a instância apaga o próprio registro e escolhe a porta de novo ("primeira livre a partir de 48884").
2. "Livre" vem de arquivos `%APPDATA%\pyRevit\2026\pyRevit_2026_<PID>_serverinfo.pickle`, não do Windows. Ao abrir, um Revit novo apaga os arquivos dos PIDs que a listagem dele não enxerga (`cleanup_appdata_folder`). Se a listagem falha, apaga o registro de um Revit vivo, e os próximos Revits escolhem uma porta já ocupada: ficam sem conector ou trocam de porta.
3. Um Revit travado ou fechando continua dono da porta até o processo morrer.

## Correção
`porta_fixa.py` troca a função `register()` do pyRevit, só dentro da sessão do Revit:
- cada instância grava a porta em `%APPDATA%\pyRevit\mcp_porta_<PID>.txt`; no Reload volta para a mesma porta;
- porta nova = primeira a partir de 48884 que o Windows diz estar livre e que nenhuma outra instância viva reservou;
- registros de PIDs mortos são apagados;
- log: `%APPDATA%\pyRevit\mcp_portas.log`.

Não altera o pyRevit em Arquivos de Programas (uma atualização do pyRevit não desfaz a correção).

## Instalação (precisa da autorização do usuário: script que roda sozinho na abertura do Revit)
1. Copiar `porta_fixa.py` para `%APPDATA%\pyRevit\Extensions\mcp-server-for-revit-python.extension\`.
2. No `startup.py` da mesma pasta (backup já feito: `startup.py.bak-2026-10-15`), logo depois de `import logging`, acrescentar:
   ```python
   try:
       import porta_fixa
       porta_fixa.aplicar()
   except Exception as _ex:
       logging.getLogger(__name__).error("porta_fixa: %s", _ex)
   ```
3. Fechar e abrir os Revits (ou pyRevit > Reload) e conferir o log.

## Desfazer
Apagar as 5 linhas do `startup.py` (ou restaurar o `.bak`) e apagar `porta_fixa.py`.
