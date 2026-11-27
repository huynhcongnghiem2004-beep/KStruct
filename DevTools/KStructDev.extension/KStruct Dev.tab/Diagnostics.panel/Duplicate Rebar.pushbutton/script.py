# -*- coding: utf-8 -*-
from pyrevit import revit, DB, forms
from Autodesk.Revit.DB.Structure import Rebar, MultiplanarOption
from collections import defaultdict
doc = revit.doc
ids = set(i.Value for i in revit.uidoc.Selection.GetElementIds())
if not ids:
    forms.alert("Select hosts or bars.", exitscript=True)
groups = defaultdict(list)
errors = []
def pt(p):
    return tuple(int(round(v * 304.8)) for v in (p.X,p.Y,p.Z))
for b in DB.FilteredElementCollector(doc).OfClass(Rebar).WhereElementIsNotElementType():
    try:
        if b.Id.Value not in ids and b.GetHostId().Value not in ids:
            continue
        for i in range(b.NumberOfBarPositions):
            if not b.DoesBarExistAtPosition(i):
                continue
            curves = b.GetTransformedCenterlineCurves(False,False,False,MultiplanarOption.IncludeAllMultiplanarCurves,i)
            keys = []
            for c in curves:
                pts = tuple(pt(p) for p in c.Tessellate())
                keys.append((c.GetType().Name,min(pts,pts[::-1])))
            if keys:
                groups[(b.GetHostId().Value,b.GetTypeId().Value,tuple(sorted(keys)))].append((b.Id.Value,i))
    except Exception as ex:
        errors.append((b.Id.Value,str(ex)))
print("Duplicate candidates (1 mm coordinate quantization; same host/type only)")
for group in groups.values():
    if len(group)>1:
        print(group)
print("Errors: {}".format(errors))
