# -*- coding: utf-8 -*-
"""Read-only selection diagnostics for Revit 2024."""
from pyrevit import revit, DB, forms
from Autodesk.Revit.DB.Structure import Rebar, MultiplanarOption
import json
import io

doc = revit.doc

def mm(v):
    return DB.UnitUtils.ConvertFromInternalUnits(v, DB.UnitTypeId.Millimeters)

def point(p):
    return [mm(p.X), mm(p.Y), mm(p.Z)]

def curve(c):
    return {"type": c.GetType().Name, "length_mm": mm(c.Length),
            "points_mm": [point(p) for p in c.Tessellate()]}

def solids(geometry):
    if geometry is None:
        return
    for obj in geometry:
        if isinstance(obj, DB.Solid) and obj.Volume > 0:
            yield {"volume_ft3": obj.Volume, "faces": obj.Faces.Size,
                   "edges": obj.Edges.Size}
        elif isinstance(obj, DB.GeometryInstance):
            for item in solids(obj.GetInstanceGeometry()):
                yield item

def inspect(e):
    result = {"id": e.Id.Value, "category": e.Category.Name if e.Category else None,
              "name": e.Name, "parameters": [], "errors": []}
    for p in e.Parameters:
        try:
            result["parameters"].append({"name": p.Definition.Name,
                "display": p.AsValueString() or (p.AsString() if p.StorageType == DB.StorageType.String else None)})
        except Exception as ex:
            result["errors"].append(str(ex))
    try:
        b = e.get_BoundingBox(None)
        if b:
            result["bounding_box_mm"] = [point(b.Min), point(b.Max)]
        if isinstance(e, DB.FamilyInstance):
            t = e.GetTransform()
            result["transform"] = {"origin_mm": point(t.Origin),
                "basis": [[v.X,v.Y,v.Z] for v in [t.BasisX,t.BasisY,t.BasisZ]],
                "determinant": t.Determinant, "mirrored": e.Mirrored}
        opt = DB.Options()
        opt.DetailLevel = DB.ViewDetailLevel.Fine
        result["solids"] = list(solids(e.get_Geometry(opt)))
    except Exception as ex:
        result["errors"].append(str(ex))
    return result

def bar_data(b):
    data = {"id": b.Id.Value, "host_id": b.GetHostId().Value,
            "type_id": b.GetTypeId().Value, "quantity": b.Quantity,
            "positions": [], "errors": []}
    for i in range(b.NumberOfBarPositions):
        try:
            if b.DoesBarExistAtPosition(i):
                cs = b.GetTransformedCenterlineCurves(False, False, False,
                    MultiplanarOption.IncludeAllMultiplanarCurves, i)
                data["positions"].append({"index": i, "curves": [curve(c) for c in cs]})
        except Exception as ex:
            data["errors"].append({"index": i, "error": str(ex)})
    return data

ids = list(revit.uidoc.Selection.GetElementIds())
if not ids:
    forms.alert("Select hosts or rebar first.", exitscript=True)
selected = set(i.Value for i in ids)
report = {"schema_version": 1, "revit_version": doc.Application.VersionNumber,
          "elements": [], "rebars": [], "errors": []}
for i in ids:
    try:
        report["elements"].append(inspect(doc.GetElement(i)))
    except Exception as ex:
        report["errors"].append({"id": i.Value, "error": str(ex)})
for b in DB.FilteredElementCollector(doc).OfClass(Rebar).WhereElementIsNotElementType():
    try:
        if b.Id.Value in selected or b.GetHostId().Value in selected:
            report["rebars"].append(bar_data(b))
    except Exception as ex:
        report["errors"].append({"id": b.Id.Value, "error": str(ex)})
path = forms.save_file(file_ext="json")
if path:
    with io.open(path, "w", encoding="utf-8") as f:
        f.write(unicode(json.dumps(report, ensure_ascii=False, indent=2)))
    forms.alert("Diagnostic JSON saved.")
