
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
def items_of(s):
    its=[]
    for i in s.GetAllViewports():
        vp=doc.GetElement(i); o=vp.GetBoxOutline(); l=vp.GetLabelOutline()
        x0,y0,x1,y1=o.MinimumPoint.X,o.MinimumPoint.Y,o.MaximumPoint.X,o.MaximumPoint.Y
        if l.MinimumPoint.X<1e20: x0=min(x0,l.MinimumPoint.X); y0=min(y0,l.MinimumPoint.Y); x1=max(x1,l.MaximumPoint.X); y1=max(y1,l.MaximumPoint.Y)
        its.append(dict(kind='vp',el=vp,name=doc.GetElement(vp.ViewId).Name,x0=x0/MM,y0=y0/MM,x1=x1/MM,y1=y1/MM))
    for x in DB.FilteredElementCollector(doc,s.Id).OfClass(DB.ScheduleSheetInstance):
        if x.IsTitleblockRevisionSchedule: continue
        bb=x.get_BoundingBox(s)
        its.append(dict(kind='sch',el=x,name=doc.GetElement(x.ScheduleId).Name,x0=bb.Min.X/MM,y0=bb.Min.Y/MM,x1=bb.Max.X/MM,y1=bb.Max.Y/MM))
    for it in its: it['w']=it['x1']-it['x0']; it['h']=it['y1']-it['y0']
    return its
def pack(its, X0=117.0, XC=757.0, XR=948.0, YT=501.0, YB=-83.0, YC=271.0, gap=8.0):
    order=sorted(its,key=lambda it:(-it['h'],-it['w']))
    shelves=[]; pl={}
    for it in order:
        done=False
        for sh in shelves:
            lim = XR if sh['bot']>=YC else XC
            if it['h']<=sh['h']+0.01 and sh['x']+it['w']<=lim:
                pl[id(it)]=(sh['x'],sh['top']); sh['x']+=it['w']+gap; done=True; break
        if done: continue
        top = YT if not shelves else shelves[-1]['bot']-gap
        bot=top-it['h']
        lim = XR if bot>=YC else XC
        if bot<YB or X0+it['w']>lim: return None
        sh=dict(top=top,bot=bot,h=it['h'],x=X0+it['w']+gap); shelves.append(sh); pl[id(it)]=(X0,top)
    return pl
def apply(its,pl):
    for it in its:
        x,top=pl[id(it)]
        dx=(x-it['x0'])*MM; dy=(top-it['y1'])*MM
        if it['kind']=='vp':
            it['el'].SetBoxCenter(it['el'].GetBoxCenter()+DB.XYZ(dx,dy,0))
        else:
            it['el'].Point=it['el'].Point+DB.XYZ(dx,dy,0)
