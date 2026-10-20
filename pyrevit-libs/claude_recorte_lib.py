
import System, math
def E(n): return DB.ElementId(System.Int64(int(n)))
MM=1/304.8
class Silencioso(DB.IFailuresPreprocessor):
    def PreprocessFailures(self, fa):
        for f in fa.GetFailureMessages():
            if f.GetSeverity() == DB.FailureSeverity.Warning: fa.DeleteWarning(f)
            else: return DB.FailureProcessingResult.ProceedWithRollBack
        return DB.FailureProcessingResult.Continue
GS={}
for s in DB.FilteredElementCollector(doc).OfClass(DB.GraphicsStyle):
    if s.GraphicsStyleCategory.Parent is not None and s.GraphicsStyleCategory.Parent.Id.Value==int(DB.BuiltInCategory.OST_Lines): GS[s.Name]=s
MED=GS[u'<Linhas m'+unichr(233)+u'dias>']; FIN=GS['<Linhas finas>']; CEN=GS['<Linha de centro>']
TT=E(936); DT=doc.GetElement(E(473485))
def mk(v):
    def P(x,y): return DB.XYZ(x*MM,y*MM,0)
    def L(a,b,st=MED):
        d=doc.Create.NewDetailCurve(v,DB.Line.CreateBound(P(*a),P(*b))); d.LineStyle=st; return d
    def PL(pts,st=MED):
        return [L(pts[i],pts[i+1],st) for i in range(len(pts)-1)]
    def D(r1,r2,a,b):
        ra=DB.ReferenceArray()
        for r in (r1,r2) if not isinstance(r1,list) else r1: ra.Append(r)
        return doc.Create.NewDimension(v,DB.Line.CreateBound(P(*a),P(*b)),ra,DT)
    def TX(x,y,s,leader=None):
        o=DB.TextNoteOptions(TT); o.HorizontalAlignment=DB.HorizontalTextAlignment.Left
        t=DB.TextNote.Create(doc,v.Id,P(x,y),s,o)
        if leader:
            l=t.AddLeader(DB.TextNoteLeaderTypes.TNLT_STRAIGHT_L); l.End=P(*leader)
        return t
    return P,L,PL,D,TX
def isec(L,PL,d,bf,tf,tw):
    h=bf/2.0; w=tw/2.0
    return PL([(-h,0),(h,0),(h,-tf),(w,-tf),(w,-d+tf),(h,-d+tf),(h,-d),(-h,-d),(-h,-d+tf),(-w,-d+tf),(-w,-tf),(-h,-tf),(-h,0)])
def cope_top(PL,x0,ytop,cl,cd):
    # returns points from flange end to web at depth cd with 3-seg radius 12
    r=12.0
    return [(x0+cl,ytop),(x0+cl,ytop-cd+r),(x0+cl-1.7,ytop-cd+6),(x0+cl-6,ytop-cd+1.6),(x0+cl-r,ytop-cd),(x0,ytop-cd)]
