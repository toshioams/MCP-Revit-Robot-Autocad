"""Mede o cobrimento real de cada barra de um IFC exportado pelo Revit (silo circular).
Uso: python analisar_cobrimento.py arquivo.ifc   (saída: resumo no console + cobrimento_barras.csv)"""
import re, math, sys, collections, csv
import numpy as np
t = open(sys.argv[1], encoding='latin1').read()
E = dict(re.findall(r"^#(\d+)=(.*);$", t, re.M))
def args(i): return E[i][E[i].index("(")+1:-1]
def refs(s): return re.findall(r"#(\d+)", s)
def pt(i): return np.array([float(x) for x in re.search(r"\(\((.*)\)\)", E[i]).group(1).split(",")] + [0.0])[:3]
def dirv(i): v = pt(i); return v/np.linalg.norm(v)
def placement(i):
    r = refs(args(i)); loc = pt(r[0])
    z = dirv(r[1]) if len(r) > 1 else np.array([0,0,1.]); x = dirv(r[2]) if len(r) > 2 else np.array([1,0,0.])
    return loc, x, np.cross(z, x)
def sample_curve(i, n=24):
    b = E[i]
    if b.startswith("IFCPOLYLINE"): return [pt(r) for r in refs(args(i))]
    if b.startswith("IFCTRIMMEDCURVE"):
        circ = refs(args(i))[0]; cr = refs(args(circ)); rad = float(args(circ).split(",")[-1])
        loc, x, y = placement(cr[0])
        p = re.findall(r"IFCPARAMETERVALUE\(([-\d.E+]+)\)", b); a1, a2 = float(p[0]), float(p[1])
        sense = ".T." in b.split(")),")[-1]
        if sense and a2 < a1: a2 += 360
        if not sense and a2 > a1: a2 -= 360
        return [loc + rad*(math.cos(math.radians(a))*x + math.sin(math.radians(a))*y) for a in np.linspace(a1, a2, n)]
    if b.startswith("IFCCOMPOSITECURVE("): 
        out = []
        for s in refs(args(i).split("),")[0]): out += sample_curve(refs(args(s))[-1], n)
        return out
    raise ValueError(b[:40])
bars = []
for k, v in E.items():
    if not v.startswith("IFCREINFORCINGBAR("): continue
    rep = refs(args(k))[3] if False else None
    m = re.search(r"vergalh.{4,6}o:([^:]+?) : ([^:]+?):(\d+)", v)
    f = [x.strip() for x in re.split(r",(?![^()]*\))", args(k))]
    d = float(f[9])*10  # cm -> mm
    shape_rep = refs(f[6])[0]; sr = refs(args(shape_rep).split(",(")[-1])[0]
    solids = refs(args(sr).split(",(")[-1])
    pts = []
    for s in solids:
        if E[s].startswith("IFCSWEPTDISKSOLID"): pts += sample_curve(refs(args(s))[0])
    if pts: bars.append(dict(id=k, tipo=m.group(1) if m else "?", forma=m.group(2) if m else "?", elem=m.group(3) if m else "?", d=d, P=np.array(pts)))
# centro: do círculo da própria parede (coordenadas locais -> globais pela cadeia de IfcLocalPlacement)
def axes(i):
    r = refs(args(i)); o = pt(r[0]); z = dirv(r[1]) if len(r) > 1 else np.array([0,0,1.]); x = dirv(r[2]) if len(r) > 2 else np.array([1,0,0.])
    return np.c_[x, np.cross(z, x), z], o
def world(lp):
    chain = []
    while lp:
        a = refs(args(lp)); chain.append(a[-1]); lp = None if args(lp).startswith("$") else a[0]
    R, o = np.eye(3), np.zeros(3)
    for p_ in reversed(chain): Rp, op = axes(p_); o = o + R @ op; R = R @ Rp
    return R, o
wall = next(k for k, v in E.items() if v.startswith("IFCWALLSTANDARDCASE("))
R, o = world(refs(args(wall))[1])
stack, seen, circ = refs(args(wall))[2:], set(), None
while stack and circ is None:
    r = stack.pop()
    if r in seen or r not in E: continue
    seen.add(r)
    if E[r].startswith("IFCCIRCLE("): circ = r
    elif not E[r].startswith("IFCOWNERHISTORY"): stack += refs(args(r))
cl = pt(refs(args(refs(args(circ))[0]))[0])
c = o + R @ cl; cx, cy = c[0], c[1]
# faces da parede: raios dos círculos das paredes e vigas (não-armadura)
host_r = collections.defaultdict(set)
for k, v in E.items():
    if v.startswith(("IFCWALLSTANDARDCASE(", "IFCBEAM(")):
        stack, seen = [refs(args(k))[5] if len(refs(args(k))) > 5 else None], set()
        stack = [r for r in refs(args(k))[1:]]
        while stack:
            r = stack.pop()
            if r in seen or r not in E: continue
            seen.add(r)
            if E[r].startswith("IFCCIRCLE("): host_r[v.split("(")[0]+":"+re.search(r"'([^']*)'", v.split(",",2)[2]).group(1)[:40]].add(round(float(args(r).split(",")[-1]),2))
            elif not E[r].startswith(("IFCOWNERHISTORY","IFCLOCALPLACEMENT","IFCREINFORCINGBAR")): stack += refs(args(r))
print(f"centro do silo (cm): ({cx:.2f}, {cy:.2f})")
for h, rs in host_r.items(): print("hospedeiro", h, "raios", sorted(rs))
rows = []
for b in bars:
    P = b["P"]; r = np.hypot(P[:,0]-cx, P[:,1]-cy); dz = P[:,2].max()-P[:,2].min()
    span_r = r.max()-r.min()
    kind = "vertical" if dz > 50 and span_r < 2 else ("horizontal" if dz < 2 else "outra (estribo/gancho)")
    rows.append(dict(id=b["id"], elemento=b["elem"], tipo=b["tipo"], forma=b["forma"], d_mm=b["d"], classe=kind,
        r_min_cm=round(r.min(),2), r_max_cm=round(r.max(),2), variacao_radial_cm=round(span_r,2), z_min=round(P[:,2].min(),1), z_max=round(P[:,2].max(),1)))
with open("cobrimento_barras.csv","w",newline="",encoding="utf-8-sig") as fh:
    w = csv.DictWriter(fh, rows[0].keys(), delimiter=";"); w.writeheader(); w.writerows(rows)
g = collections.defaultdict(list)
for r in rows: g[(r["classe"], r["tipo"], r["forma"])].append(r)
print(f"\n{'classe':24}{'tipo':12}{'forma':34}{'n':>5}{'r_min':>9}{'r_max':>9}{'var.radial máx':>16}")
for k, rs in sorted(g.items()):
    print(f"{k[0]:24}{k[1]:12}{k[2][:33]:34}{len(rs):5}{min(x['r_min_cm'] for x in rs):9.1f}{max(x['r_max_cm'] for x in rs):9.1f}{max(x['variacao_radial_cm'] for x in rs):16.2f}")
