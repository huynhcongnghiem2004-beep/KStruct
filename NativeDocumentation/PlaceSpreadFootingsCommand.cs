using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class PlaceSpreadFootingsCommand : IExternalCommand
    {
        private sealed class FootingChoice
        {
            public FamilySymbol Symbol { get; set; }
            public string Display { get; set; }
        }

        private sealed class ColumnFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                return element is FamilyInstance instance && instance.Category != null &&
                    instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;
            }
            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> columns = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .OfType<FamilyInstance>().Where(IsColumn).Distinct().ToList();
                if (columns.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new ColumnFilter(),
                        "Chọn các cột cần đặt móng đơn");
                    columns = picked.Select(x => doc.GetElement(x)).OfType<FamilyInstance>().Where(IsColumn).Distinct().ToList();
                }
                if (columns.Count == 0) return Result.Cancelled;

                List<FamilySymbol> symbols = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFoundation).OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>().OrderBy(x => x.FamilyName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (symbols.Count == 0)
                    throw new InvalidOperationException("Project chưa nạp Family Type thuộc Structural Foundations.");
                FamilySymbol target = ChooseType(data.Application.MainWindowHandle, symbols);
                if (target == null) return Result.Cancelled;

                var placed = new List<ElementId>();
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Rải móng đơn"))
                {
                    transaction.Start();
                    if (!target.IsActive) target.Activate();
                    doc.Regenerate();
                    foreach (FamilyInstance column in columns)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                LocationPoint location = column.Location as LocationPoint;
                                if (location == null) throw new InvalidOperationException("Cột không có LocationPoint.");
                                Level level = ColumnBaseLevel(doc, column, ui.ActiveView);
                                if (level == null) throw new InvalidOperationException("Không xác định được Level chân cột.");
                                double offset = ParameterDouble(column, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                                XYZ point = new XYZ(location.Point.X, location.Point.Y, level.Elevation + offset);
                                FamilyInstance footing = doc.Create.NewFamilyInstance(point, target, level, StructuralType.Footing);
                                if (footing == null) throw new InvalidOperationException("Revit không tạo được móng.");
                                if (Math.Abs(location.Rotation) > 1e-9)
                                {
                                    Line axis = Line.CreateBound(point, point + XYZ.BasisZ);
                                    ElementTransformUtils.RotateElement(doc, footing.Id, axis, location.Rotation);
                                }
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận móng vừa tạo.");
                                placed.Add(footing.Id);
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Cột " + column.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (placed.Count == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Rải móng", "Không tạo được móng nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi.");
                }

                ui.Selection.SetElementIds(placed);
                string report = "Đã đặt " + placed.Count + "/" + columns.Count + " móng loại ‘" + target.Name + "’.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " cột:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Rải móng", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Rải móng", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsColumn(FamilyInstance instance)
        {
            return instance.Category != null && instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;
        }

        private static Level ColumnBaseLevel(Document doc, FamilyInstance column, Autodesk.Revit.DB.View activeView)
        {
            Parameter levelParameter = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
            if (levelParameter != null && levelParameter.StorageType == StorageType.ElementId)
            {
                Level level = doc.GetElement(levelParameter.AsElementId()) as Level;
                if (level != null) return level;
            }
            Level byId = doc.GetElement(column.LevelId) as Level;
            if (byId != null) return byId;
            return activeView == null ? null : doc.GetElement(activeView.GenLevel == null ? ElementId.InvalidElementId : activeView.GenLevel.Id) as Level;
        }

        private static double ParameterDouble(Element element, BuiltInParameter id)
        {
            Parameter parameter = element.get_Parameter(id);
            return parameter != null && parameter.HasValue && parameter.StorageType == StorageType.Double ? parameter.AsDouble() : 0.0;
        }

        private static FamilySymbol ChooseType(IntPtr owner, IList<FamilySymbol> symbols)
        {
            using (var form = new System.Windows.Forms.Form())
            using (var combo = new System.Windows.Forms.ComboBox())
            using (var ok = new System.Windows.Forms.Button())
            using (var cancel = new System.Windows.Forms.Button())
            {
                form.Text = "Chọn loại móng đơn - KStruct";
                form.Width = 500; form.Height = 145;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MaximizeBox = false; form.MinimizeBox = false;
                var label = new System.Windows.Forms.Label { Left = 12, Top = 12, Width = 450, Text = "Family Type Structural Foundation:" };
                combo.Left = 12; combo.Top = 36; combo.Width = 455;
                combo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
                var choices = symbols.Select(s => new FootingChoice { Symbol = s, Display = s.FamilyName + " : " + s.Name }).ToList();
                combo.DisplayMember = "Display"; combo.Items.AddRange(choices.Cast<object>().ToArray()); combo.SelectedIndex = 0;
                ok.Text = "Đặt móng"; ok.Left = 300; ok.Top = 72; ok.Width = 78; ok.DialogResult = DialogResult.OK;
                cancel.Text = "Hủy"; cancel.Left = 386; cancel.Top = 72; cancel.Width = 78; cancel.DialogResult = DialogResult.Cancel;
                form.Controls.AddRange(new System.Windows.Forms.Control[] { label, combo, ok, cancel });
                form.AcceptButton = ok; form.CancelButton = cancel;
                var ownerWindow = new NativeWindow();
                if (owner != IntPtr.Zero) ownerWindow.AssignHandle(owner);
                try { return form.ShowDialog(ownerWindow) == DialogResult.OK ? ((FootingChoice)combo.SelectedItem).Symbol : null; }
                finally { if (owner != IntPtr.Zero) ownerWindow.ReleaseHandle(); }
            }
        }
    }
}
