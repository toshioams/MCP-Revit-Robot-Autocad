
import System
MM=1/304.8
class Silencioso(DB.IFailuresPreprocessor):
    def PreprocessFailures(self, fa):
        for f in fa.GetFailureMessages():
            if f.GetSeverity() == DB.FailureSeverity.Warning: fa.DeleteWarning(f)
            else: return DB.FailureProcessingResult.ProceedWithRollBack
        return DB.FailureProcessingResult.Continue
def T(name):
    tr=DB.Transaction(doc,name); fo=tr.GetFailureHandlingOptions(); fo.SetFailuresPreprocessor(Silencioso()); tr.SetFailureHandlingOptions(fo); return tr
def sheet(num): return [x for x in DB.FilteredElementCollector(doc).OfClass(DB.ViewSheet) if x.SheetNumber==num][0]
def frame(s):
    tb=DB.FilteredElementCollector(doc,s.Id).OfCategory(DB.BuiltInCategory.OST_TitleBlocks).FirstElement(); b=tb.get_BoundingBox(s)
    rv=[x for x in DB.FilteredElementCollector(doc,s.Id).OfClass(DB.ScheduleSheetInstance) if x.IsTitleblockRevisionSchedule][0].get_BoundingBox(s)
    return dict(X0=b.Min.X/MM,Y0=b.Min.Y/MM,X1=b.Max.X/MM,Y1=b.Max.Y/MM,cx0=rv.Min.X/MM,cx1=rv.Max.X/MM,ctop=rv.Max.Y/MM)
def box(vp):
    o=vp.GetBoxOutline(); l=vp.GetLabelOutline()
    x0,y0,x1,y1=o.MinimumPoint.X,o.MinimumPoint.Y,o.MaximumPoint.X,o.MaximumPoint.Y
    if l.MinimumPoint.X<1e20: x0=min(x0,l.MinimumPoint.X); y0=min(y0,l.MinimumPoint.Y); x1=max(x1,l.MaximumPoint.X); y1=max(y1,l.MaximumPoint.Y)
    return (x0/MM,y0/MM,x1/MM,y1/MM)
def moveto(vp,x,top):
    b=box(vp); vp.SetBoxCenter(vp.GetBoxCenter()+DB.XYZ((x-b[0])*MM,(top-b[3])*MM,0))
def notes_columns(s,gap=2.0):
    F=frame(s); W=F['cx1']-F['cx0']
    leg=[]
    for i in s.GetAllViewports():
        vp=doc.GetElement(i)
        if doc.GetElement(vp.ViewId).ViewType==DB.ViewType.Legend: leg.append(vp)
    leg.sort(key=lambda vp:-box(vp)[3])
    cols=[]; x=F['cx0']; top=F['Y1']-5; bottom=F['ctop']+3; col=0
    for vp in leg:
        b=box(vp); h=b[3]-b[1]
        if top-h<bottom:
            col+=1; x=F['cx0']-col*(W+4); top=F['Y1']-5; bottom=F['Y0']+5
        moveto(vp,x,top); cols.append((col,x,top-h,x+W,top)); top-=h+gap
    return F,cols
