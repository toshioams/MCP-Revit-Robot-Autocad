# -*- coding: utf-8 -*-
# claude_verif_lib.py - verificacao de pranchas e referencias (IronPython 2.7, conector pyRevit)
# Uso: g=dict(globals()); exec(open(r"C:\Users\toshi\claude_verif_lib.py").read(), g); r=g["verificar"]()
# Serve para qualquer porta (48884/48885): usa apenas "doc" e "DB" do conector.
import re, System
F=304.8

def _u(s):
    try: return s if isinstance(s,unicode) else unicode(s,"utf-8")
    except: return s

def folhas():
    return dict((s.SheetNumber,s) for s in DB.FilteredElementCollector(doc).OfClass(DB.ViewSheet) if not s.IsPlaceholder)

def mapa_detalhes():
    """{folha: {num_detalhe: nome_vista}} e {nome_vista: (det, folha)}"""
    por_folha={}; por_vista={}
    for vp in DB.FilteredElementCollector(doc).OfClass(DB.Viewport):
        sh=doc.GetElement(vp.SheetId); v=doc.GetElement(vp.ViewId)
        d=vp.get_Parameter(DB.BuiltInParameter.VIEWPORT_DETAIL_NUMBER).AsString()
        por_folha.setdefault(sh.SheetNumber,{})[d]=v.Name
        por_vista[v.Name]=(d,sh.SheetNumber)
    return por_folha,por_vista

def det_ref(nome_vista):
    """'DET. n / FOLHA m' de uma vista, lido do modelo."""
    _,pv=mapa_detalhes(); r=pv.get(nome_vista)
    return r

def _corrompido(s):
    # texto gravado com codificacao dupla: 'Ã' seguido de caractere 0x80-0xBF
    return bool(re.search(u"\u00c3[\u0080-\u00bf]", _u(s)))

# ------------------------------------------------------------------ referencias em texto
_P_DET=re.compile(u"DET\\.?\\s*((?:\\d+)(?:\\s*(?:E|,)\\s*\\d+)*)\\s*/\\s*FOLHA\\s*(\\d+)", re.I)
_P_FOL=re.compile(u"(?:FOLHAS?|PRANCHAS?)\\s+(\\d+)(?:\\s*(E|A)\\s*(\\d+))?", re.I)
_P_PR=re.compile(u"PR\\.\\s*(\\d+)(?:\\s*/\\s*(\\d+))?", re.I)

def verificar_referencias():
    out=[]; fs=folhas(); pf,_=mapa_detalhes()
    def existe(n): return n in fs or n.zfill(2) in fs
    for tn in DB.FilteredElementCollector(doc).OfClass(DB.TextNote):
        s=_u(tn.Text); onde=doc.GetElement(tn.OwnerViewId); onde=onde.Name if onde else "?"
        for m in _P_DET.finditer(s):
            fol=m.group(2); dets=re.findall(u"\\d+", m.group(1))
            if not existe(fol): out.append(u"[REF] {} ({}): folha {} inexistente em '{}'".format(tn.Id.Value,onde,fol,m.group(0))); continue
            for d in dets:
                if d not in pf.get(fol,{}) and d not in pf.get(fol.zfill(2),{}):
                    out.append(u"[REF] {} ({}): detalhe {} nao existe na folha {} ('{}')".format(tn.Id.Value,onde,d,fol,m.group(0)))
        for m in _P_FOL.finditer(s):
            a=m.group(1); b=m.group(3); lst=[a]
            if b: lst = [str(i) for i in range(int(a),int(b)+1)] if (m.group(2) or "").upper()=="A" else [a,b]
            for n in lst:
                if not existe(n): out.append(u"[REF] {} ({}): folha {} inexistente ('{}')".format(tn.Id.Value,onde,n,m.group(0)))
        for m in _P_PR.finditer(s):
            for n in [x for x in m.groups() if x]:
                if not existe(n): out.append(u"[REF] {} ({}): prancha {} inexistente ('{}')".format(tn.Id.Value,onde,n,m.group(0)))
        if _corrompido(s): out.append(u"[ACENTO] texto {} ({}) com acentuacao corrompida".format(tn.Id.Value,onde))
    return out

# ------------------------------------------------------------------ folhas
def _box(vp):
    o=vp.GetBoxOutline(); x0,y0,x1,y1=o.MinimumPoint.X,o.MinimumPoint.Y,o.MaximumPoint.X,o.MaximumPoint.Y
    try:
        l=vp.GetLabelOutline()
        if l.MinimumPoint.X<1e20: x0=min(x0,l.MinimumPoint.X); y0=min(y0,l.MinimumPoint.Y); x1=max(x1,l.MaximumPoint.X); y1=max(y1,l.MaximumPoint.Y)
    except: pass
    return (x0*F,x1*F,y0*F,y1*F)

def _rect_texto(e,P,esc):
    """retangulo do texto em mm de papel. P converte ponto do modelo/folha para mm; esc=1 na folha."""
    c=P(e.Coord); w=e.Width*F; h=e.Height*F
    ha=e.HorizontalAlignment; va=e.VerticalAlignment
    x0=c[0]-(w/2 if ha==DB.HorizontalTextAlignment.Center else (w if ha==DB.HorizontalTextAlignment.Right else 0))
    y1=c[1]+(h/2 if va==DB.VerticalTextAlignment.Middle else (h if va==DB.VerticalTextAlignment.Bottom else 0))
    return (x0,x0+w,y1-h,y1)

def _rect_solda(e,P,R,sc):
    """retangulo visivel do 'Weld Symbol' (medido): circulo 19 mm a esquerda da origem; 20 mm de altura."""
    a=P(e.Location.Point-R*(19.0*sc/F)); tail=e.LookupParameter("Tail Note"); n=len(tail.AsString() or "") if tail else 4
    return (a[0]-3,a[0]+37+1.7*n,a[1]-8,a[1]+12)

def _rint(a,b,g=0.0): return not (a[1]+g<=b[0] or b[1]+g<=a[0] or a[3]+g<=b[2] or b[3]+g<=a[2])

def verificar_folhas(margem=5.0, param_total=None, folhas_alvo=None):
    out=[]; fs=folhas()
    revs=[r for r in DB.FilteredElementCollector(doc).OfClass(DB.Revision)]
    for num,s in sorted(fs.items()):
        if folhas_alvo and num not in folhas_alvo: continue
        if _corrompido(s.Name): out.append(u"[ACENTO] nome da folha {} corrompido".format(num))
        tb=DB.FilteredElementCollector(doc,s.Id).OfCategory(DB.BuiltInCategory.OST_TitleBlocks).FirstElement()
        if not tb: continue
        b=tb.get_BoundingBox(s); lim=(b.Min.X*F+margem,b.Max.X*F-margem,b.Min.Y*F+margem,b.Max.Y*F-margem)
        itens=[]
        for i in s.GetAllViewports():
            vp=doc.GetElement(i); v=doc.GetElement(vp.ViewId); bx=_box(vp); itens.append((u"vista '"+v.Name+u"'",bx))
            if bx[0]<lim[0] or bx[1]>lim[1] or bx[2]<lim[2] or bx[3]>lim[3]:
                out.append(u"[FOLHA {}] vista '{}' passa da margem ({:.0f},{:.0f},{:.0f},{:.0f})".format(num,v.Name,*bx))
            t=v.get_Parameter(DB.BuiltInParameter.VIEW_DESCRIPTION)
            if t and t.AsString() and _corrompido(t.AsString()): out.append(u"[ACENTO] titulo da vista '{}'".format(v.Name))
        textos=[]
        for e in DB.FilteredElementCollector(doc,s.Id).WhereElementIsNotElementType():
            if e.OwnerViewId!=s.Id or isinstance(e,DB.Viewport) or not e.Category: continue
            cid=e.Category.Id.Value
            if cid==int(DB.BuiltInCategory.OST_TitleBlocks) or cid==int(DB.BuiltInCategory.OST_RevisionClouds): continue
            if isinstance(e,(DB.AnnotationSymbol,DB.CurveElement)): continue   # simbolos e linhas sobre vistas sao propositais
            if isinstance(e,DB.TextNote):
                r=_rect_texto(e,lambda p:(p.X*F,p.Y*F),1.0)
                textos.append((u"{} {}".format(e.Category.Name,e.Id.Value),r)); continue
            bb=e.get_BoundingBox(s)
            if bb: textos.append((u"{} {}".format(e.Category.Name,e.Id.Value),(bb.Min.X*F,bb.Max.X*F,bb.Min.Y*F,bb.Max.Y*F)))
        for i in range(len(itens)):            # vista x vista (caixa + titulo)
            for j in range(i+1,len(itens)):
                if _rint(itens[i][1],itens[j][1]):
                    out.append(u"[AVISO FOLHA {}] caixas se tocam (conferir na imagem): {} x {}".format(num,itens[i][0],itens[j][0]))
        for i in range(len(textos)):           # texto/tabela x texto/tabela
            for j in range(i+1,len(textos)):
                if _rint(textos[i][1],textos[j][1],-0.5):
                    out.append(u"[FOLHA {}] sobreposicao: {} x {}".format(num,textos[i][0],textos[j][0]))
        for a in itens:                        # vista x texto/tabela
            for b in textos:
                if _rint(a[1],b[1],-0.5): out.append(u"[AVISO FOLHA {}] {} x {} (conferir na imagem)".format(num,a[0],b[0]))
        ids=set(x.Value for x in s.GetAllRevisionIds())
        if revs and revs[-1].Id.Value not in ids and num not in ("0",):
            out.append(u"[FOLHA {}] sem a revisao mais recente ({})".format(num,revs[-1].RevisionNumber))
    for r in revs:
        if _corrompido(r.Description): out.append(u"[ACENTO] revisao {} com acentuacao corrompida".format(r.RevisionNumber))
    if param_total:
        p=doc.ProjectInformation.LookupParameter(param_total)
        n=len([k for k in fs if k not in ("0",)])
        if p and p.AsString()!=str(n): out.append(u"[TOTAL] '{}'={} mas existem {} folhas numeradas".format(param_total,p.AsString(),n))
    return out

# ------------------------------------------------------------------ anotacoes dentro das vistas
def _segx(p1,p2,q1,q2):
    def o(a,b,c): return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
    return (o(q1,q2,p1)*o(q1,q2,p2)<0) and (o(p1,p2,q1)*o(p1,p2,q2)<0)

def verificar_vista(v):
    """Sobreposicao entre textos/cotas/simbolos e cruzamento de chamadas (papel, mm). Vistas 2D."""
    out=[]
    if isinstance(v,DB.View3D) or v.ViewType in (DB.ViewType.DrawingSheet,DB.ViewType.Schedule): return out
    R=v.RightDirection; Up=v.UpDirection; sc=float(v.Scale); O=v.CropBox.Transform.Origin
    P=lambda p: ((p-O).DotProduct(R)*F/sc,(p-O).DotProduct(Up)*F/sc)
    rects=[]; segs=[]
    for e in DB.FilteredElementCollector(doc,v.Id).WhereElementIsNotElementType():
        if e.OwnerViewId!=v.Id: continue
        nome=u"{} {}".format(e.GetType().Name,e.Id.Value)
        if isinstance(e,DB.TextNote):
            c=P(e.Coord); w=e.Width*F; h=e.Height*F
            ha=e.HorizontalAlignment; va=e.VerticalAlignment
            x0=c[0]-(w/2 if ha==DB.HorizontalTextAlignment.Center else (w if ha==DB.HorizontalTextAlignment.Right else 0))
            y1=c[1]+(h/2 if va==DB.VerticalTextAlignment.Middle else (h if va==DB.VerticalTextAlignment.Bottom else 0))
            rects.append((nome,(x0,x0+w,y1-h,y1)))
            for l in e.GetLeaders(): segs.append((nome,P(l.Anchor),P(l.Elbow))); segs.append((nome,P(l.Elbow),P(l.End)))
        elif isinstance(e,DB.Dimension):
            # so o texto de cada segmento (a caixa da cota inteira inclui as linhas de chamada)
            segl=[x for x in e.Segments] if e.NumberOfSegments>0 else [e]
            for k,sg in enumerate(segl):
                try:
                    tp=sg.TextPosition; vs=sg.ValueString or ""
                except: continue
                if tp is None: continue
                c=P(tp); w=max(2.0,len(vs)*1.6); h=2.6
                try:   # cota vertical na vista: texto girado, troca largura e altura
                    if abs(e.Curve.Direction.DotProduct(Up))>0.7: w,h=h,w
                except: pass
                rects.append((u"{} (txt {})".format(nome,vs),(c[0]-w/2,c[0]+w/2,c[1]-h/2,c[1]+h/2)))
        elif isinstance(e,(DB.SpatialElementTag,DB.IndependentTag)):
            # identificadores de ambiente e etiquetas (antes ficavam fora da checagem)
            bb=e.get_BoundingBox(v)
            if bb:
                a=P(bb.Min); b=P(bb.Max); rects.append((nome,(min(a[0],b[0]),max(a[0],b[0]),min(a[1],b[1]),max(a[1],b[1]))))
        elif isinstance(e,DB.AnnotationSymbol):
            if e.Symbol.FamilyName==u"Weld Symbol":
                rects.append((nome,_rect_solda(e,P,R,sc)))
            else:
                bb=e.get_BoundingBox(v)
                if bb:
                    a=P(bb.Min); b=P(bb.Max); rects.append((nome,(min(a[0],b[0]),max(a[0],b[0]),min(a[1],b[1]),max(a[1],b[1]))))
            for l in e.GetLeaders():
                segs.append((nome,P(l.Anchor),P(l.Elbow))); segs.append((nome,P(l.Elbow),P(l.End)))
    for i in range(len(rects)):
        for j in range(i+1,len(rects)):
            if rects[i][0].split(" (")[0]==rects[j][0].split(" (")[0]: continue
            if _rint(rects[i][1],rects[j][1],-0.3): out.append(u"[VISTA '{}'] sobreposicao: {} x {}".format(v.Name,rects[i][0],rects[j][0]))
    for i in range(len(segs)):
        for j in range(i+1,len(segs)):
            if segs[i][0]==segs[j][0]: continue
            if _segx(segs[i][1],segs[i][2],segs[j][1],segs[j][2]): out.append(u"[VISTA '{}'] chamadas cruzadas: {} x {}".format(v.Name,segs[i][0],segs[j][0]))
    return out

def verificar(param_total=None, vistas=None, folhas_alvo=None):
    """Roda tudo. folhas_alvo: lista de numeros de folha (None = todas). vistas=None: vistas 2D das folhas alvo."""
    out=verificar_referencias()+verificar_folhas(param_total=param_total, folhas_alvo=folhas_alvo)
    if vistas is None:
        vistas=[doc.GetElement(vp.ViewId) for vp in DB.FilteredElementCollector(doc).OfClass(DB.Viewport)
                if not folhas_alvo or doc.GetElement(vp.SheetId).SheetNumber in folhas_alvo]
    for v in vistas: out+=verificar_vista(v)
    return out


# ---------------------------------------------------------------------------------------------------------------
# Acrescimos de 17/10/2026 (pedido do usuario, 25-112): solda em "V", fabricacao completa, planta de montagem.

def _ang(a, b, c):
    """angulo (graus) no ponto b entre ba e bc"""
    import math
    v1 = (a[0] - b[0], a[1] - b[1]); v2 = (c[0] - b[0], c[1] - b[1])
    n1 = math.hypot(*v1); n2 = math.hypot(*v2)
    if n1 < 1e-6 or n2 < 1e-6: return 180.0
    cs = max(-1.0, min(1.0, (v1[0] * v2[0] + v1[1] * v2[1]) / (n1 * n2)))
    return math.degrees(math.acos(cs))


def verificar_soldas(v, ang_min=100.0):
    """Simbolo de solda: a chamada sai da ancora, passa pelo cotovelo e vai ao alvo. Erro se dobra para tras no cotovelo
    (angulo < ang_min: 'V', a linha vai e volta), se o cotovelo fica mais longe do alvo que a ancora (chamada volta), ou se
    a chamada cruza outra chamada de solda. Medidas no papel (mm)."""
    out = []
    if isinstance(v, DB.View3D) or v.ViewType in (DB.ViewType.DrawingSheet, DB.ViewType.Schedule): return out
    R = v.RightDirection; Up = v.UpDirection; sc = float(v.Scale); O = v.CropBox.Transform.Origin
    P = lambda p: ((p - O).DotProduct(R) * F / sc, (p - O).DotProduct(Up) * F / sc)
    segs = []
    for e in DB.FilteredElementCollector(doc, v.Id).OfClass(DB.FamilyInstance):
        if e.OwnerViewId != v.Id or not isinstance(e, DB.AnnotationSymbol): continue
        if e.Symbol.FamilyName != u"Weld Symbol": continue
        for l in e.GetLeaders():
            a = P(l.Anchor); b = P(l.Elbow); c = P(l.End)
            ang = _ang(a, b, c)
            if ang < ang_min:
                out.append(u"[SOLDA '{}'] chamada em V (angulo {:.0f} graus no cotovelo): {}".format(v.Name, ang, e.Id.Value))
            dist_ac = ((a[0] - c[0]) ** 2 + (a[1] - c[1]) ** 2) ** 0.5
            dist_bc = ((b[0] - c[0]) ** 2 + (b[1] - c[1]) ** 2) ** 0.5
            if dist_bc > dist_ac + 0.5:
                out.append(u"[SOLDA '{}'] cotovelo mais longe do alvo que a ancora (chamada volta): {}".format(v.Name, e.Id.Value))
            segs.append((e.Id.Value, a, b)); segs.append((e.Id.Value, b, c))
    for i in range(len(segs)):
        for j in range(i + 1, len(segs)):
            if segs[i][0] == segs[j][0]: continue
            if _segx(segs[i][1], segs[i][2], segs[j][1], segs[j][2]):
                out.append(u"[SOLDA '{}'] chamadas de solda cruzadas: {} x {}".format(v.Name, segs[i][0], segs[j][0]))
    return out


def _marcas_aco(excluir=(u'L-', u'CR', u'CV', u'PAR', u'CH')):
    from collections import defaultdict
    g = defaultdict(list)
    for bic in (DB.BuiltInCategory.OST_StructuralColumns, DB.BuiltInCategory.OST_StructuralFraming):
        for e in DB.FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType():
            p = e.get_Parameter(DB.BuiltInParameter.ALL_MODEL_MARK); m = (p.AsString() if p else u'') or u''
            if not m or m.startswith(excluir): continue
            g[m].append(e)
    return g


def verificar_fabricacao(prefixo_folha=u'FB-', excluir=(u'L-', u'CR', u'CV', u'PAR', u'CH')):
    """Toda marca de perfil metalico (pilar, viga, terca, pergolado, toco, trelica) precisa aparecer numa folha de
    fabricacao: no nome de uma vista colocada nela ou escrita num texto da folha/vista (tabela). Devolve as que faltam."""
    import re
    out = []
    marcas = _marcas_aco(excluir)
    textos = u''; nomes = []
    for s in DB.FilteredElementCollector(doc).OfClass(DB.ViewSheet):
        if not s.SheetNumber.startswith(prefixo_folha): continue
        for t in DB.FilteredElementCollector(doc, s.Id).OfClass(DB.TextNote): textos += u' ' + t.Text
        for vid in s.GetAllPlacedViews():
            nomes.append(doc.GetElement(vid).Name)
            for t in DB.FilteredElementCollector(doc, vid).OfClass(DB.TextNote): textos += u' ' + t.Text
    textos = u' ' + textos.replace(u'\r', u' ').replace(u'\t', u' ') + u' '
    falta = []
    for m in sorted(marcas):
        pad = u'[\\s(,;]' + re.escape(m) + u'[\\s),.;]'
        if any(re.search(pad, u' ' + n + u' ') for n in nomes): continue
        if re.search(pad, textos): continue
        falta.append(m)
    if falta:
        out.append(u"[FABRICACAO] {} marcas sem detalhe de fabricacao: {}".format(len(falta), u', '.join(falta[:80]) + (u' ...' if len(falta) > 80 else u'')))
    return out


def verificar_montagem(palavra=u'MONTAGEM', excluir=(u'L-', u'CR', u'CV', u'PAR', u'CH')):
    """Precisa existir planta de montagem (folha com 'MONTAGEM' no nome). Em cada vista dessas folhas, toda peca metalica
    com marca visivel na vista precisa de identificador (IndependentTag). Lista tambem as marcas que nao aparecem em
    nenhuma planta de montagem (trelicas TR podem estar so como conjunto)."""
    out = []
    folhas_m = [s for s in DB.FilteredElementCollector(doc).OfClass(DB.ViewSheet) if palavra in s.Name.upper()]
    if not folhas_m:
        return [u"[MONTAGEM] nao existe folha de planta de montagem (nome com '{}')".format(palavra)]
    vistas = []
    for s in folhas_m:
        for vid in s.GetAllPlacedViews():
            v = doc.GetElement(vid)
            if isinstance(v, DB.ViewPlan) or v.ViewType in (DB.ViewType.Section, DB.ViewType.Elevation): vistas.append((s, v))
    marcadas = set()
    for s, v in vistas:
        for tg in DB.FilteredElementCollector(doc, v.Id).OfClass(DB.IndependentTag):
            try:
                for r in tg.GetTaggedReferences(): marcadas.add(r.ElementId.Value)
            except Exception: pass
    sem = {}; vistos = set()
    for s, v in vistas:
        # trelicas (marca TR-) sao identificadas como conjunto: basta uma barra da marca etiquetada na vista
        tr_ok = set()
        for bic in (DB.BuiltInCategory.OST_StructuralColumns, DB.BuiltInCategory.OST_StructuralFraming):
            for e in DB.FilteredElementCollector(doc, v.Id).OfCategory(bic).WhereElementIsNotElementType():
                p = e.get_Parameter(DB.BuiltInParameter.ALL_MODEL_MARK); m = (p.AsString() if p else u'') or u''
                if m.startswith(u'TR') and e.Id.Value in marcadas: tr_ok.add(m)
        for bic in (DB.BuiltInCategory.OST_StructuralColumns, DB.BuiltInCategory.OST_StructuralFraming):
            for e in DB.FilteredElementCollector(doc, v.Id).OfCategory(bic).WhereElementIsNotElementType():
                p = e.get_Parameter(DB.BuiltInParameter.ALL_MODEL_MARK); m = (p.AsString() if p else u'') or u''
                if not m or m.startswith(excluir): continue
                vistos.add(e.Id.Value)
                if m in tr_ok: continue
                if e.Id.Value not in marcadas: sem.setdefault(s.SheetNumber, []).append(m)
    for fn, ms in sorted(sem.items()):
        out.append(u"[MONTAGEM {}] {} pecas sem identificador de marca (ex.: {})".format(fn, len(ms), u', '.join(sorted(set(ms))[:25])))
    todas = _marcas_aco(excluir)
    nunca = [m for m, es in todas.items() if not any(e.Id.Value in vistos for e in es)]
    if nunca:
        out.append(u"[MONTAGEM] {} marcas que nao aparecem em nenhuma planta de montagem: {}".format(len(nunca), u', '.join(sorted(nunca)[:40])))
    return out


def verificar_completo(param_total=None, folhas_alvo=None, fabricacao=True, montagem=True):
    """verificar() + soldas em V nas vistas das folhas alvo + fabricacao completa + planta de montagem"""
    out = verificar(param_total=param_total, folhas_alvo=folhas_alvo)
    vistas = [doc.GetElement(vp.ViewId) for vp in DB.FilteredElementCollector(doc).OfClass(DB.Viewport)
              if not folhas_alvo or doc.GetElement(vp.SheetId).SheetNumber in folhas_alvo]
    for v in vistas: out += verificar_soldas(v)
    if fabricacao: out += verificar_fabricacao()
    if montagem: out += verificar_montagem()
    return out
