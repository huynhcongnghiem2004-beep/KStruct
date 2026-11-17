using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CreateStripFootingsCommand : IExternalCommand
    {
        private sealed class WallFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                Wall wall = element as Wall;
                return wall != null && wall.WallType != null && wall.WallType.Kind == WallKind.Basic;
            }
            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }

        private sealed class TypeChoice
        {
            public WallFoundationType Type { get; set; }
            public string Display { get; set; }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Wall> walls = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Wall>()
                    .Where(IsSupportedWall).Distinct().ToList();
                if (walls.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new WallFilter(),
                        "Chọn các tường cơ bản cần tạo móng băng");
                    walls = picked.Select(x => doc.GetElement(x)).OfType<Wall>().Where(IsSupportedWall).Distinct().ToList();
                }
                if (walls.Count == 0) return Result.Cancelled;

                List<WallFoundationType> types = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallFoundationType)).Cast<WallFoundationType>()
                    .OrderBy(x => x.FamilyName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (types.Count == 0)
                    throw new InvalidOperationException("Project chưa có Wall Foundation Type. Nạp hoặc tạo type móng tường trước.");
                WallFoundationType target = ChooseType(data.Application.MainWindowHandle, types);
                if (target == null) return Result.Cancelled;

                HashSet<ElementId> wallIds = new HashSet<ElementId>(walls.Select(x => x.Id));
                HashSet<ElementId> existingHostIds = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallFoundation)).Cast<WallFoundation>()
                    .Select(x => x.WallId).Where(id => id != ElementId.InvalidElementId).ToHashSet();

                var created = new List<ElementId>();
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Tạo móng băng"))
                {
                    transaction.Start();
                    foreach (Wall wall in walls)
                    {
                        if (existingHostIds.Contains(wall.Id))
                        {
                            skipped.Add("Tường " + wall.Id.Value + ": đã có móng băng.");
                            continue;
                        }
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                WallFoundation footing = WallFoundation.Create(doc, wall.Id, target.Id);
                                if (footing == null) throw new InvalidOperationException("Revit không tạo được WallFoundation.");
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận móng vừa tạo.");
                                created.Add(footing.Id);
                                existingHostIds.Add(wall.Id);
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Tường " + wall.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (created.Count == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Móng băng", "Không tạo được móng nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi.");
                }

                ui.Selection.SetElementIds(created);
                string report = "Đã tạo " + created.Count + "/" + walls.Count + " móng băng theo tường, type ‘" + target.Name + "’.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " tường:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Móng băng", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Móng băng", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsSupportedWall(Wall wall)
        {
            return wall != null && wall.WallType != null && wall.WallType.Kind == WallKind.Basic && wall.Location is LocationCurve;
        }

        private static WallFoundationType ChooseType(IntPtr owner, IList<WallFoundationType> types)
        {
            using (var form = new System.Windows.Forms.Form())
            using (var combo = new System.Windows.Forms.ComboBox())
            using (var ok = new System.Windows.Forms.Button())
            using (var cancel = new System.Windows.Forms.Button())
            {
                form.Text = "Chọn loại móng băng - KStruct";
                form.Width = 500; form.Height = 145;
                form.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
                form.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
                form.MaximizeBox = false; form.MinimizeBox = false;
                var label = new System.Windows.Forms.Label { Left = 12, Top = 12, Width = 450, Text = "Wall Foundation Type:" };
                combo.Left = 12; combo.Top = 36; combo.Width = 455;
                combo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
                var choices = types.Select(x => new TypeChoice { Type = x, Display = x.FamilyName + " : " + x.Name }).ToList();
                combo.DisplayMember = "Display"; combo.Items.AddRange(choices.Cast<object>().ToArray()); combo.SelectedIndex = 0;
                ok.Text = "Tạo móng"; ok.Left = 300; ok.Top = 72; ok.Width = 78; ok.DialogResult = System.Windows.Forms.DialogResult.OK;
                cancel.Text = "Hủy"; cancel.Left = 386; cancel.Top = 72; cancel.Width = 78; cancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
                form.Controls.AddRange(new System.Windows.Forms.Control[] { label, combo, ok, cancel });
                form.AcceptButton = ok; form.CancelButton = cancel;
                var ownerWindow = new System.Windows.Forms.NativeWindow();
                if (owner != IntPtr.Zero) ownerWindow.AssignHandle(owner);
                try
                {
                    return form.ShowDialog(ownerWindow) == System.Windows.Forms.DialogResult.OK
                        ? ((TypeChoice)combo.SelectedItem).Type : null;
                }
                finally { if (owner != IntPtr.Zero) ownerWindow.ReleaseHandle(); }
            }
        }
    }
}
