# -*- coding: utf-8 -*-
from pyrevit import revit, DB, forms
from Autodesk.Revit.DB.Structure import Rebar, MultiplanarOption
doc = revit.doc
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
hosts = [doc.GetElement(i) for i in revit.uidoc.Selection.GetElementIds()]
hosts = [h for h in hosts if h.Category and h.Category.Id.Value == int(DB.BuiltInCategory.OST_StructuralFoundation)]
if not hosts:
    forms.alert("Select structural foundations.", exitscript=True)
bars = list(DB.FilteredElementCollector(doc).OfClass(Rebar).WhereElementIsNotElementType())
for h in hosts:
    ss = list(solids(h.get_Geometry(opt)))
    if len(ss) != 1:
        print("Foundation {}: unsupported solid count".format(h.Id.Value))
        continue
    for b in bars:
        try:
            for i in range(b.NumberOfBarPositions):
                if not b.DoesBarExistAtPosition(i):
                    continue
                length = 0.0
                for c in b.GetTransformedCenterlineCurves(False,False,False,MultiplanarOption.IncludeAllMultiplanarCurves,i):
                    hit = ss[0].IntersectWithCurve(c, DB.SolidCurveIntersectionOptions())
                    for j in range(hit.SegmentCount):
                        length += hit.GetCurveSegment(j).Length
                if length > 0:
                    print("Foundation {} bar {} position {} inside length {:.2f} mm".format(h.Id.Value,b.Id.Value,i,length*304.8))
        except Exception as ex:
            print("Bar {} unknown: {}".format(b.Id.Value,ex))
print("Geometric intersection only, not effective development length or code compliance.")
