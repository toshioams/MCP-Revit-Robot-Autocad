
import json, math, System
def E(n): return DB.ElementId(System.Int64(int(n)))
class Silencioso(DB.IFailuresPreprocessor):
    def PreprocessFailures(self, fa):
        for f in fa.GetFailureMessages():
            if f.GetSeverity() == DB.FailureSeverity.Warning: fa.DeleteWarning(f)
            else: return DB.FailureProcessingResult.ProceedWithRollBack
        return DB.FailureProcessingResult.Continue
def T(name):
    tr = DB.Transaction(doc, name); fo = tr.GetFailureHandlingOptions(); fo.SetFailuresPreprocessor(Silencioso()); tr.SetFailureHandlingOptions(fo); return tr
MM=1/304.8
def p2(v,p):
    o=v.Origin; d=p-o
    return (d.DotProduct(v.RightDirection), d.DotProduct(v.UpDirection))
def rect(v,el):
    bb=el.get_BoundingBox(v)
    if bb is None: return None
    tf=bb.Transform; xs=[];ys=[]
    for x in (bb.Min.X,bb.Max.X):
        for y in (bb.Min.Y,bb.Max.Y):
            for z in (bb.Min.Z,bb.Max.Z):
                a,b=p2(v,tf.OfPoint(DB.XYZ(x,y,z))); xs.append(a); ys.append(b)
    return (min(xs),min(ys),max(xs),max(ys))
def inter(a,b,g=0):
    return not (a[2]+g<b[0] or b[2]+g<a[0] or a[3]+g<b[1] or b[3]+g<a[1])
def obstacles(v):
    obs=[]
    tags=list(DB.FilteredElementCollector(doc,v.Id).OfClass(DB.IndependentTag))
    tns=[t for t in DB.FilteredElementCollector(doc,v.Id).OfClass(DB.TextNote)]
    tr=T('tmp'); tr.Start()
    try:
        for t in tags:
            try: t.HasLeader=False
            except: pass
        for t in tns:
            try: t.RemoveLeaders()
            except: pass
        doc.Regenerate()
        for e in tags+tns:
            r=rect(v,e)
            if r: obs.append(r)
    finally: tr.RollBack()
    sc=v.Scale
    for dm in DB.FilteredElementCollector(doc,v.Id).OfClass(DB.Dimension):
        segs=list(dm.Segments) if dm.NumberOfSegments>0 else [dm]
        for s in segs:
            try:
                tp=s.TextPosition
                if tp is None: continue
                a,b=p2(v,tp); h=1.5*MM*sc; w=max(4,len(str(int(round(s.Value*304.8)))))*1.0*MM*sc
                try:
                    if abs(dm.Curve.Direction.DotProduct(v.UpDirection))>0.7: w,h=h,w
                except: pass
                obs.append((a-w,b-h,a+w,b+h))
            except: pass
    pts=[]
    for fr in DB.FilteredElementCollector(doc,v.Id).OfCategory(DB.BuiltInCategory.OST_StructuralFraming).WhereElementIsNotElementType():
        lc=fr.Location
        if not isinstance(lc,DB.LocationCurve): continue
        c=lc.Curve; n=max(2,int(c.Length/(50*MM)))
        for i in range(n+1):
            pts.append((fr.Id, p2(v,c.Evaluate(float(i)/n,True))))
    return obs,pts
def run(vid, members, tt):
    v=doc.GetElement(E(vid)); sc=v.Scale
    obs,pts=obstacles(v)
    cb=v.CropBox; crop=None
    if v.CropBoxActive:
        tf=cb.Transform; xs=[];ys=[]
        for x in (cb.Min.X,cb.Max.X):
            for y in (cb.Min.Y,cb.Max.Y):
                a,b=p2(v,tf.OfPoint(DB.XYZ(x,y,cb.Min.Z))); xs.append(a); ys.append(b)
        crop=(min(xs),min(ys),max(xs),max(ys))
    out=[]
    tr=T('Inclinacoes '+v.Name); tr.Start()
    try:
        for mid,pct,h in members:
            m=doc.GetElement(E(mid))
            if m is None: out.append((mid,'none')); continue
            c=m.Location.Curve; p0=c.GetEndPoint(0); p1=c.GetEndPoint(1)
            d=(p1-p0).Normalize(); dx=d.DotProduct(v.RightDirection); dy=d.DotProduct(v.UpDirection)
            ang=math.atan2(dy,dx)
            if ang>math.pi/2: ang-=math.pi
            if ang<=-math.pi/2: ang+=math.pi
            nrm=v.RightDirection.Multiply(-math.sin(ang))+v.UpDirection.Multiply(math.cos(ang))
            hd=40.0
            try:
                pa=m.Symbol.LookupParameter('Altura'); hd=pa.AsDouble()/MM
            except: pass
            import re
            mm_=re.match(r'.*?(\d+)x', m.Name.replace(' ',''))
            try:
                nn=float(re.findall(r'(\d+)x',m.Name.replace(' ',''))[0])
                if nn<=400 and not m.Name.startswith('W'): hd=max(hd,nn)
            except: pass
            off=(hd/2.0+2.5*sc)*MM
            # visible interval
            ts=[i/40.0 for i in range(41)]
            vis=[t for t in ts if (crop is None) or (lambda q: crop[0]<q[0]<crop[2] and crop[1]<q[1]<crop[3])(p2(v,c.Evaluate(t,True)))]
            if not vis: out.append((mid,'fora')); continue
            ta,tb=min(vis),max(vis)
            txt=('i = %.1f%%' % pct).replace('.',',')
            placed=False
            for f in [0.5,0.4,0.6,0.3,0.7,0.2,0.8]:
                t=ta+(tb-ta)*f
                for sg in (1,-1):
                    pt=c.Evaluate(t,True)+nrm.Multiply(sg*off)
                    op=DB.TextNoteOptions(tt); op.HorizontalAlignment=DB.HorizontalTextAlignment.Center
                    try: op.VerticalAlignment=DB.VerticalTextAlignment.Middle
                    except: pass
                    op.Rotation=ang
                    tn=DB.TextNote.Create(doc,v.Id,pt,txt,op)
                    doc.Regenerate()
                    r=rect(v,tn)
                    ok = r is not None
                    if ok and crop and not (r[0]>crop[0] and r[2]<crop[2] and r[1]>crop[1] and r[3]<crop[3]): ok=False
                    if ok and any(inter(r,o) for o in obs): ok=False
                    if ok:
                        g=30*MM
                        for eid,(a,b) in pts:
                            if r[0]-g<a<r[2]+g and r[1]-g<b<r[3]+g:
                                # rect is loose for rotated text: test in rotated frame
                                cx,cy=p2(v,pt); ux,uy=math.cos(ang),math.sin(ang)
                                lx=(a-cx)*ux+(b-cy)*uy; ly=-(a-cx)*uy+(b-cy)*ux
                                if abs(lx)<(len(txt)*1.3*MM*sc/2+g) and abs(ly)<(1.2*MM*sc+g): ok=False; break
                    if ok:
                        obs.append(r); placed=True; break
                    doc.Delete(tn.Id)
                if placed: break
            out.append((mid,'ok' if placed else 'FALHOU'))
        # angular dims
        nang=0
        for dm in list(DB.FilteredElementCollector(doc,v.Id).OfClass(DB.Dimension)):
            if dm.DimensionShape==DB.DimensionShape.Angular: doc.Delete(dm.Id); nang+=1
        tr.Commit()
    except Exception as ex:
        tr.RollBack(); out.append(('ERRO',str(ex)))
    return v.Name,out,nang if 'nang' in dir() else 0
