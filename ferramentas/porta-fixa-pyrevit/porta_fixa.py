# -*- coding: UTF-8 -*-
"""Porta fixa por instancia do Revit para o servidor de rotas do pyRevit (TMS, 15/10/2026).

Problema do pyRevit original (pyrevit/routes/server/serverinfo.py):
- a cada carga/Reload do pyRevit o registro da instancia e apagado e a porta e escolhida de novo;
- "porta livre" e decidida por arquivos .pickle de registro, nao pelo Windows; um Revit novo
  apaga registros de instancias que ele nao enxergou e passa a escolher porta ja ocupada.

Correcao (monkeypatch aplicado no startup.py da extensao MCP, antes de o pyRevit ativar o servidor):
- cada instancia (PID) guarda sua porta em %APPDATA%/pyRevit/mcp_porta_<pid>.txt (nao e apagado no Reload);
- se o arquivo existe e a porta nao esta ocupada por outro processo, a instancia volta para ela;
- senao, escolhe a primeira porta a partir de user_config.routes_port (48884) que o Windows diz estar livre;
- arquivos de PIDs que nao existem mais sao removidos;
- cada escolha vai para %APPDATA%/pyRevit/mcp_portas.log.
"""
import os
import datetime

APPDIR = os.path.join(os.environ.get("APPDATA", ""), "pyRevit")
LOG = os.path.join(APPDIR, "mcp_portas.log")


def _log(msg):
    try:
        with open(LOG, "a") as f:
            f.write("{} | {}\n".format(datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"), msg))
    except Exception:
        pass


def _portas_ocupadas():
    from System.Net.NetworkInformation import IPGlobalProperties
    return set(ep.Port for ep in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())


def _pid_vivo(pid):
    from System.Diagnostics import Process
    try:
        p = Process.GetProcessById(int(pid))
        return not p.HasExited
    except Exception:
        return False


def _arq(pid):
    return os.path.join(APPDIR, "mcp_porta_{}.txt".format(pid))


def _limpa_mortos():
    try:
        for n in os.listdir(APPDIR):
            if n.startswith("mcp_porta_") and n.endswith(".txt"):
                pid = n[len("mcp_porta_"):-4]
                if not _pid_vivo(pid):
                    os.remove(os.path.join(APPDIR, n))
                    _log("removido registro de PID morto {}".format(pid))
    except Exception as ex:
        _log("erro limpando registros: {}".format(ex))


def _escolhe_porta(base, pid):
    ocupadas = _portas_ocupadas()
    reservadas = set()   # portas guardadas por outras instancias vivas
    try:
        for n in os.listdir(APPDIR):
            if n.startswith("mcp_porta_") and n.endswith(".txt"):
                outro = n[len("mcp_porta_"):-4]
                if outro != str(pid) and _pid_vivo(outro):
                    reservadas.add(int(open(os.path.join(APPDIR, n)).read().strip()))
    except Exception:
        pass
    minha = None
    if os.path.exists(_arq(pid)):
        try:
            minha = int(open(_arq(pid)).read().strip())
        except Exception:
            minha = None
    if minha and minha not in ocupadas and minha not in reservadas:
        return minha, "mantida (mesma instancia)"
    porta = base
    while porta in ocupadas or porta in reservadas:
        porta += 1
    motivo = "nova (primeira livre a partir de {})".format(base)
    if minha:
        motivo += "; a anterior {} estava ocupada".format(minha)
    return porta, motivo


def aplicar():
    from pyrevit.routes.server import serverinfo
    from pyrevit import HOST_APP
    from pyrevit.userconfig import user_config

    def register():
        pid = HOST_APP.proc_id
        _limpa_mortos()
        porta, motivo = _escolhe_porta(int(user_config.routes_port), pid)
        with open(_arq(pid), "w") as f:
            f.write(str(porta))
        rsinfo = serverinfo.RoutesServerInfo(
            host=HOST_APP.pretty_name, version=HOST_APP.version, process_id=pid,
            server_host=user_config.routes_host, server_port=porta)
        try:
            serverinfo._write_serverinfo(serverinfo._get_host_serverinfo_file(), rsinfo)
        except Exception:
            pass
        _log("PID {} -> porta {} | {}".format(pid, porta, motivo))
        return rsinfo

    serverinfo.register = register
    _log("porta_fixa aplicada (PID {})".format(HOST_APP.proc_id))
