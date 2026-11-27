# -*- coding: utf-8 -*-
from pyrevit import revit, DB, forms
from Autodesk.Revit.DB.Structure import Rebar, MultiplanarOption
doc = revit.doc
ids = set(i.Value for i in revit.uidoc.Selection.GetElementIds())
if not ids:
    forms.alert("Select hosts or bars.", exitscript=True)
def solids(g):
    if g is not None:
        for x in g:
            if isinstance(x, DB.Solid) and x.Volume > 0:
                yield x
            elif isinstance(x, DB.GeometryInstance):
                for s in solids(x.GetInstanceGeometry()):
                    yield s
opt = DB.Options()
opt.DetailLevel = DB.ViewDetailLevel.Fine
for b in DB.FilteredElementCollector(doc).OfClass(Rebar).WhereElementIsNotElementType():
    try:
        if b.Id.Value not in ids and b.GetHostId().Value not in ids:
            continue
        host = doc.GetElement(b.GetHostId())
        ss = list(solids(host.get_Geometry(opt)))
        if len(ss) != 1:
            print("Bar {}: unsupported host solid count".format(b.Id.Value))
            continue
        radius = doc.GetElement(b.GetTypeId()).BarModelDiameter / 2.0
        for i in range(b.NumberOfBarPositions):
            if not b.DoesBarExistAtPosition(i):
                continue
            ds = []
            for c in b.GetTransformedCenterlineCurves(False,False,False,MultiplanarOption.IncludeAllMultiplanarCurves,i):
                for p in c.Tessellate():
                    for face in ss[0].Faces:
                        hit = face.Project(p)
                        if hit is not None:
                            ds.append(hit.Distance)
            if ds:
                print("Bar {} position {}: unsigned sampled clearance {:.2f} mm".format(b.Id.Value,i,(min(ds)-radius)*304.8))
            else:
                print("No valid face projections.")
    except Exception as ex:
        print("Unknown: {}".format(ex))
print("Unsigned sampled estimate only; outside-host points and exact minimum are not verified.")
