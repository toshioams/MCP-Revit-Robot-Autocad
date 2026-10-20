
PT=('U 125x50x3','150x75x25x2.65','100x40x17x2,00')
def purlins(v,cr):
    res=[]
    for e in DB.FilteredElementCollector(doc,v.Id).OfCategory(DB.BuiltInCategory.OST_StructuralFraming).WhereElementIsNotElementType():
        if e.Name not in PT: continue
        lc=e.Location
        if not isinstance(lc,DB.LocationCurve): continue
        c=lc.Curve; dd=(c.GetEndPoint(1)-c.GetEndPoint(0)).Normalize()
        if abs(dd.DotProduct(v.ViewDirection))<0.99: continue
        x,y=p2(v,c.GetEndPoint(0))
        if cr[0]<x<cr[2] and cr[1]<y<cr[3]: res.append((x,y,e))
    res.sort(key=lambda q:q[0])
    ded=[]
    for q in res:
        if ded and abs(q[0]-ded[-1][0])<5*MM: continue
        ded.append(q)
    return ded
def terca_dims(vid, dt, ls):
    v=doc.GetElement(E(vid)); sc=v.Scale; cr=crop2(v)
    P=purlins(v,cr)
    if len(P)<2: return (v.Name,'<2 tercas')
    obs,pts=OBS(v)
    levels=[p2(v,DB.XYZ(0,0,l.Elevation))[1] for l in DB.FilteredElementCollector(doc).OfClass(DB.Level)]
    ymax=max(q[1] for q in P)
    tr=T('Cotas tercas '+v.Name); tr.Start()
    try:
        refs=[]
        for x,y,e in P:
            base=v.Origin+v.RightDirection.Multiply(x)+v.UpDirection.Multiply(y)
            dl=doc.Create.NewDetailCurve(v,DB.Line.CreateBound(base+v.UpDirection.Multiply(20*MM),base-v.UpDirection.Multiply(20*MM))); dl.LineStyle=ls
            refs.append(dl.GeometryCurve.Reference)
        ra=DB.ReferenceArray()
        for r in refs: ra.Append(r)
        best=None
        for k in [8,11,14,17,20,24]:
            yd=ymax+k*MM*sc
            if yd>cr[3]-2*MM*sc: break
            if any(abs(yd-L)<2.5*MM*sc for L in levels): continue
            a=v.Origin+v.RightDirection.Multiply(P[0][0]-50*MM)+v.UpDirection.Multiply(yd)
            b=v.Origin+v.RightDirection.Multiply(P[-1][0]+50*MM)+v.UpDirection.Multiply(yd)
            dm=doc.Create.NewDimension(v,DB.Line.CreateBound(a,b),ra,dt); doc.Regenerate()
            segs=list(dm.Segments) if dm.NumberOfSegments else [dm]
            bad=0
            for s in segs:
                tx,ty=p2(v,s.TextPosition); w=len(str(int(round(s.Value*304.8))))*1.1*MM*sc; h=1.4*MM*sc
                rr=(tx-w,ty-h,tx+w,ty+h)
                if any(INTER(rr,o) for o in obs): bad+=1
                if s.Value*304.8 < 2.4*len(str(int(round(s.Value*304.8))))*sc: bad+=1
            if bad==0: best=(k,dm); break
            if best is None or bad<best[2]:
                if best and best[1] is not None: doc.Delete(best[1].Id)
                best=(k,dm,bad)
            else: doc.Delete(dm.Id)
        if best is None: tr.RollBack(); return (v.Name,'sem posicao')
        dm=best[1]
        vals=[int(round(s.Value*304.8)) for s in dm.Segments] if dm.NumberOfSegments else [int(round(dm.Value*304.8))]
        tr.Commit(); return (v.Name,len(P),'k=%s'%best[0],'conflitos=%s'%(best[2] if len(best)>2 else 0),vals)
    except Exception as ex:
        tr.RollBack(); return (v.Name,'ERRO',str(ex))

def hdim_xs(v, excl=None):
    xs=[]
    for dm in DB.FilteredElementCollector(doc,v.Id).OfClass(DB.Dimension):
        if excl and dm.Id in excl: continue
        try:
            if dm.DimensionShape!=DB.DimensionShape.Linear: continue
            if abs(dm.Curve.Direction.DotProduct(v.RightDirection))<0.99: continue
        except: continue
        segs=list(dm.Segments) if dm.NumberOfSegments else [dm]
        for s in segs:
            cx=p2(v,s.Origin)[0]; xs+= [cx-s.Value/2, cx+s.Value/2]
    return xs
def covered(v,P,excl=None):
    xs=hdim_xs(v,excl)
    return all(any(abs(q[0]-x)<5*MM for x in xs) for q in P)
