# -*- coding: utf-8 -*-
from pyrevit import revit, DB, forms
ids = list(revit.uidoc.Selection.GetElementIds())
if not ids:
    forms.alert("Select structural family instances.", exitscript=True)
for eid in ids:
    e = revit.doc.GetElement(eid)
    if not isinstance(e, DB.FamilyInstance):
        print("{}: unsupported element".format(eid.Value))
        continue
    t = e.GetTransform()
    print("ID {} | origin (ft) {} | X {} | Y {} | Z {} | determinant {} | mirrored {}".format(eid.Value,t.Origin,t.BasisX,t.BasisY,t.BasisZ,t.Determinant,e.Mirrored))
