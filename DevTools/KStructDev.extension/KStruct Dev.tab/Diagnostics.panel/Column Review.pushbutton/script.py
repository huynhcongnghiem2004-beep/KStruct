# -*- coding: utf-8 -*-
"""Read-only column rebar inventory and local-XY centerline projection."""
import math
import os
import clr
clr.AddReference("System.Data, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")
from System.Data import DataTable
from System.Windows import Thickness
from System.Windows.Media import Brushes
from System.Windows.Shapes import Line, Ellipse
from System.Windows.Controls import Canvas
from pyrevit import revit, DB, forms
from Autodesk.Revit.DB.Structure import Rebar, MultiplanarOption
from column_review_math import fit_projection

MAX_PREVIEW_POSITIONS = 300
MAX_POINTS_PER_CURVE = 1000


def collect(doc, column):
    rows = []
    lines_by_id = {}
    errors = []
    requested = 0
    sampled = 0
    omitted = 0
    transform = column.GetTransform().Inverse
    for bar in DB.FilteredElementCollector(doc).OfClass(Rebar).WhereElementIsNotElementType():
        try:
            if bar.GetHostId() != column.Id:
                continue
        except Exception as ex:
            errors.append("Không đọc được host của Rebar {}: {}".format(bar.Id.Value, ex))
            continue
        element_id = bar.Id.Value
        row_errors = []
        lines_by_id[element_id] = []
        try:
            bar_type = doc.GetElement(bar.GetTypeId())
            type_name = bar_type.Name if bar_type else "Không rõ"
        except Exception as ex:
            type_name = "Không rõ"
            row_errors.append("Loại: {}".format(ex))
        try:
            shape_id = bar.GetShapeId()
            shape = doc.GetElement(shape_id) if shape_id != DB.ElementId.InvalidElementId else None
            shape_name = shape.Name if shape else "Không có / không rõ"
        except Exception:
            shape_name = "Không có / không rõ"
        present = 0
        try:
            total = bar.NumberOfBarPositions
            for index in range(total):
                try:
                    if not bar.DoesBarExistAtPosition(index):
                        continue
                    present += 1
                    requested += 1
                    if sampled >= MAX_PREVIEW_POSITIONS:
                        omitted += 1
                        continue
                    sampled += 1
                    curves = bar.GetTransformedCenterlineCurves(False, False, False,
                        MultiplanarOption.IncludeAllMultiplanarCurves, index)
                    for curve in curves:
                        pts = []
                        tessellated = list(curve.Tessellate())
                        if len(tessellated) > MAX_POINTS_PER_CURVE:
                            row_errors.append("Vị trí {}: đường quá nhiều điểm; bỏ qua.".format(index))
                            continue
                        for world in tessellated:
                            local = transform.OfPoint(world)
                            x, y = float(local.X), float(local.Y)
                            if math.isnan(x) or math.isinf(x) or math.isnan(y) or math.isinf(y):
                                raise ValueError("Tọa độ không hữu hạn")
                            pts.append((x, y))
                        if pts:
                            lines_by_id[element_id].append(pts)
                except Exception as ex:
                    row_errors.append("Vị trí {}: {}".format(index, ex))
        except Exception as ex:
            total = None
            row_errors.append("Không đọc được vị trí: {}".format(ex))
        rows.append({"Id": str(element_id), "Type": type_name, "Shape": shape_name,
                     "Positions": "{} / {}".format(present, total if total is not None else "?"),
                     "Errors": str(len(row_errors))})
        errors.extend(["Rebar {}: {}".format(element_id, x) for x in row_errors])
    return rows, lines_by_id, errors, requested, sampled, omitted


class ColumnReviewWindow(forms.WPFWindow):
    def __init__(self, column, rows, lines_by_id, errors, requested, sampled, omitted):
        self.lines_by_id = lines_by_id
        self.errors = errors
        self.sampled = sampled
        self.omitted = omitted
        forms.WPFWindow.__init__(self, os.path.join(os.path.dirname(__file__), "ColumnReview.xaml"))
        self.HostText.Text = "Cột {} · {} · {} bộ Rebar".format(column.Id.Value, column.Name, len(rows))
        table = DataTable()
        for name in ("Id", "Type", "Shape", "Positions", "Errors"):
            table.Columns.Add(name)
        for data in rows:
            row = table.NewRow()
            for name in ("Id", "Type", "Shape", "Positions", "Errors"):
                row[name] = data[name]
            table.Rows.Add(row)
        self._table = table
        self.BarGrid.ItemsSource = table.DefaultView
        self.PreviewText.Text = ("Phép chiếu tâm thanh theo XY cục bộ của toàn chiều cao; không phải mặt cắt, "
            "không thể hiện cover, đường kính hoặc va chạm. Đã thử đọc {} / {} vị trí; bỏ qua {} do giới hạn.".format(sampled, requested, omitted))
        self.SummaryText.Text = ("Chỉ đọc · {} bộ · {} lỗi đọc · Không kiểm tra thiết kế hoặc nghiệm thu.".format(len(rows), len(errors)))
        if errors:
            self.SummaryText.ToolTip = "\n".join(errors[:20]) + ("\n..." if len(errors) > 20 else "")
        self.ErrorsButton.IsEnabled = bool(errors)
        self.BarGrid.SelectionChanged += self.OnSelectionChanged
        self.Redraw()

    def OnSelectionChanged(self, sender, args):
        self.Redraw()

    def OnPreviewSizeChanged(self, sender, args):
        if hasattr(self, "BarGrid"):
            self.Redraw()

    def Redraw(self):
        if not hasattr(self, "PlanCanvas") or self.PlanCanvas is None:
            return
        canvas = self.PlanCanvas
        canvas.Children.Clear()
        width, height = canvas.ActualWidth, canvas.ActualHeight
        flattened, owners = [], []
        for owner, lines in self.lines_by_id.items():
            for line in lines:
                flattened.append(line)
                owners.append(owner)
        projected = fit_projection(flattened, width, height)
        selected = self.BarGrid.SelectedItem if hasattr(self, "BarGrid") else None
        chosen = str(selected["Id"]) if selected is not None else None
        for owner, points in zip(owners, projected):
            color = Brushes.DarkOrange if chosen == str(owner) else Brushes.SteelBlue
            if len(points) == 1 or all(abs(p[0] - points[0][0]) < 0.1 and abs(p[1] - points[0][1]) < 0.1 for p in points):
                dot = Ellipse(Width=5, Height=5, Fill=color)
                Canvas.SetLeft(dot, points[0][0] - 2.5)
                Canvas.SetTop(dot, points[0][1] - 2.5)
                canvas.Children.Add(dot)
            else:
                for first, second in zip(points, points[1:]):
                    canvas.Children.Add(Line(X1=first[0], Y1=first[1], X2=second[0], Y2=second[1], Stroke=color, StrokeThickness=1.5))

    def OnClose(self, sender, args):
        self.Close()

    def OnErrors(self, sender, args):
        if self.errors:
            forms.alert("\n".join(self.errors[:50]) + ("\n..." if len(self.errors) > 50 else ""), title="KStruct - Lỗi đọc dữ liệu")


ids = list(revit.uidoc.Selection.GetElementIds())
columns = []
for element_id in ids:
    element = revit.doc.GetElement(element_id)
    if isinstance(element, DB.FamilyInstance) and element.Category and int(element.Category.Id.Value) == int(DB.BuiltInCategory.OST_StructuralColumns):
        columns.append(element)
if len(ids) != 1 or len(columns) != 1:
    forms.alert("Hãy chọn đúng một cột kết cấu trước khi review.", exitscript=True)
try:
    column = columns[0]
    result = collect(revit.doc, column)
    ColumnReviewWindow(column, *result).ShowDialog()
except Exception as error:
    forms.alert("Không thể review cột: {}".format(error))
