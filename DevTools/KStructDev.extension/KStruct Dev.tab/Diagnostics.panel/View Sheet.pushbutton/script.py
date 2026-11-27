# -*- coding: utf-8 -*-
from pyrevit import revit, DB
doc = revit.doc
v = doc.ActiveView
print("View: {} | ID: {}".format(v.Name,v.Id.Value))
if isinstance(v, DB.ViewSheet):
    boxes = []
    for eid in v.GetAllViewports():
        vp = doc.GetElement(eid)
        box = vp.GetBoxOutline()
        boxes.append((eid.Value,box.MinimumPoint,box.MaximumPoint))
        print("Viewport {}: {}".format(eid.Value,doc.GetElement(vp.ViewId).Name))
    for i,(a,lo,hi) in enumerate(boxes):
        for b,l,h in boxes[i+1:]:
            if min(hi.X,h.X)>max(lo.X,l.X) and min(hi.Y,h.Y)>max(lo.Y,l.Y):
                print("Viewport overlap candidate: {} / {}".format(a,b))
    print("Labels and schedules excluded.")
else:
    print("Scale: {}".format(v.Scale))
    print("Crop active: {}".format(v.CropBoxActive))
