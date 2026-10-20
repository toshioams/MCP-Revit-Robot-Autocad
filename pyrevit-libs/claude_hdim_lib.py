
import System, math
def E(n): return DB.ElementId(System.Int64(int(n)))
MM=1/304.8
class Silencioso(DB.IFailuresPreprocessor):
    def PreprocessFailures(self, fa):
        for f in fa.GetFailureMessages():
            if f.GetSeverity() == DB.FailureSeverity.Warning: fa.DeleteWarning(f)
            else: return DB.FailureProcessingResult.ProceedWithRollBack
        return DB.FailureProcessingResult.Continue
def T(name):
    tr = DB.Transaction(doc, name); fo = tr.GetFailureHandlingOptions(); fo.SetFailuresPreprocessor(Silencioso()); tr.SetFailureHandlingOptions(fo); return tr
def p2(v,p):
    d=p-v.Origin; return (d.DotProduct(v.RightDirection), d.DotProduct(v.UpDirection))
def crop2(v):
    cb=v.CropBox; tf=cb.Transform; xs=[];ys=[]
    for x in (cb.Min.X,cb.Max.X):
        for y in (cb.Min.Y,cb.Max.Y):
            a,b=p2(v,tf.OfPoint(DB.XYZ(x,y,cb.Min.Z))); xs.append(a); ys.append(b)
    return (min(xs),min(ys),max(xs),max(ys))
def verts(v):
    cr=crop2(v); res=[]
    els=list(DB.FilteredElementCollector(doc,v.Id).OfCategory(DB.BuiltInCategory.OST_StructuralFraming).WhereElementIsNotElementType())+list(DB.FilteredElementCollector(doc,v.Id).OfCategory(DB.BuiltInCategory.OST_StructuralColumns).WhereElementIsNotElementType())
    for e in els:
        lc=e.Location
        if isinstance(lc,DB.LocationCurve):
            c=lc.Curve; a=c.GetEndPoint(0); b=c.GetEndPoint(1)
        elif isinstance(lc,DB.LocationPoint):
            bb=e.get_BoundingBox(None); a=DB.XYZ(lc.Point.X,lc.Point.Y,bb.Min.Z); b=DB.XYZ(lc.Point.X,lc.Point.Y,bb.Max.Z)
        else: continue
        d=(b-a)
        if d.GetLength()<1e-6: continue
        d=d.Normalize()
        if abs(d.DotProduct(v.UpDirection))<0.999: continue
        x,_=p2(v,a)
        # depth check: within view depth (in front of far clip)
        ya=p2(v,a)[1]; yb=p2(v,b)[1]
        lo,hi=min(ya,yb),max(ya,yb)
        lo=max(lo,cr[1]); hi=min(hi,cr[3])
        if not (cr[0]<x<cr[2]) or hi<=lo: continue
        top = a if p2(v,a)[1]>p2(v,b)[1] else b
        res.append((x,lo,hi,e,top))
    return res,cr
