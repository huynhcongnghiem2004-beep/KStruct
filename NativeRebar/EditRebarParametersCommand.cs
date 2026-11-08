using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class EditRebarParametersCommand : IExternalCommand
    {
        private sealed class ParameterChoice
        {
            public string Name { get; set; }
            public string Display { get; set; }
            public StorageType Storage { get; set; }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Rebar> rebars = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().Distinct().ToList();
                if (rebars.Count == 0)
                {
                    IList<Autodesk.Revit.DB.Reference> picked = ui.Selection.PickObjects(
                        Autodesk.Revit.UI.Selection.ObjectType.Element, new RebarFilter(),
                        "Chọn một hoặc nhiều Rebar cần sửa tham số");
                    rebars = picked.Select(x => doc.GetElement(x)).OfType<Rebar>().Distinct().ToList();
                }
                if (rebars.Count == 0) return Result.Cancelled;

                List<ParameterChoice> choices = GetCommonWritableParameters(rebars);
                if (choices.Count == 0)
                    throw new InvalidOperationException("Các Rebar đã chọn không có tham số instance chung có thể sửa.");

                ParameterChoice choice;
                string value;
                if (!ChooseParameter(data.Application.MainWindowHandle, choices, out choice, out value)) return Result.Cancelled;

                int changed = 0;
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Sửa tham số Rebar"))
                {
                    transaction.Start();
                    foreach (Rebar rebar in rebars)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                Parameter parameter = rebar.LookupParameter(choice.Name);
                                if (parameter == null || parameter.IsReadOnly || parameter.StorageType != choice.Storage)
                                    throw new InvalidOperationException("Tham số không còn khả dụng hoặc chỉ đọc.");
                                bool ok = SetParameter(parameter, value);
                                if (!ok) throw new InvalidOperationException("Revit không chấp nhận giá trị nhập.");
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận thay đổi.");
                                changed++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add(rebar.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (changed == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Sửa Rebar", "Không có Rebar nào nhận được giá trị mới.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi.");
                }

                string report = "Đã cập nhật tham số ‘" + choice.Name + "’ cho " + changed + "/" + rebars.Count + " Rebar.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " phần tử không tương thích.";
                TaskDialog.Show("KStruct - Sửa Rebar", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Sửa Rebar", ex.Message);
                return Result.Failed;
            }
        }

        private static List<ParameterChoice> GetCommonWritableParameters(IList<Rebar> rebars)
        {
            var first = new Dictionary<string, ParameterChoice>(StringComparer.OrdinalIgnoreCase);
            foreach (Parameter parameter in rebars[0].Parameters)
            {
                if (parameter == null || parameter.IsReadOnly || parameter.Definition == null) continue;
                if (parameter.StorageType != StorageType.Double && parameter.StorageType != StorageType.Integer &&
                    parameter.StorageType != StorageType.String) continue;
                string name = parameter.Definition.Name;
                if (!first.ContainsKey(name))
                    first.Add(name, new ParameterChoice { Name = name, Storage = parameter.StorageType, Display = name });
            }

            return first.Values.Where(choice => rebars.All(rebar =>
            {
                Parameter p = rebar.LookupParameter(choice.Name);
                return p != null && !p.IsReadOnly && p.StorageType == choice.Storage;
            })).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static bool SetParameter(Parameter parameter, string value)
        {
            switch (parameter.StorageType)
            {
                case StorageType.Double: return parameter.SetValueString(value);
                case StorageType.Integer:
                    int integer;
                    if (int.TryParse(value, out integer)) return parameter.Set(integer);
                    return parameter.SetValueString(value);
                case StorageType.String: return parameter.Set(value);
                default: return false;
            }
        }

        private static bool ChooseParameter(IntPtr owner, IList<ParameterChoice> choices,
            out ParameterChoice choice, out string value)
        {
            using (var form = new System.Windows.Forms.Form())
            using (var combo = new System.Windows.Forms.ComboBox())
            using (var input = new System.Windows.Forms.TextBox())
            using (var ok = new System.Windows.Forms.Button())
            using (var cancel = new System.Windows.Forms.Button())
            {
                form.Text = "Sửa tham số Rebar - KStruct";
                form.Width = 510;
                form.Height = 185;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                var label = new Label { Left = 12, Top = 12, Width = 470, Text = "Tham số chung trên các Rebar được chọn:" };
                combo.Left = 12; combo.Top = 36; combo.Width = 470; combo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
                combo.DisplayMember = "Display";
                combo.Items.AddRange(choices.Cast<object>().ToArray());
                combo.SelectedIndex = 0;
                var valueLabel = new Label { Left = 12, Top = 70, Width = 470, Text = "Giá trị mới (double dùng định dạng đơn vị của project):" };
                input.Left = 12; input.Top = 94; input.Width = 470;
                ok.Text = "Áp dụng"; ok.Left = 320; ok.Top = 126; ok.Width = 78; ok.DialogResult = DialogResult.OK;
                cancel.Text = "Hủy"; cancel.Left = 404; cancel.Top = 126; cancel.Width = 78; cancel.DialogResult = DialogResult.Cancel;
                form.Controls.AddRange(new System.Windows.Forms.Control[] { label, combo, valueLabel, input, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                var handle = new NativeWindow();
                if (owner != IntPtr.Zero) handle.AssignHandle(owner);
                DialogResult result;
                try { result = form.ShowDialog(handle); }
                finally { if (owner != IntPtr.Zero) handle.ReleaseHandle(); }
                choice = result == DialogResult.OK ? combo.SelectedItem as ParameterChoice : null;
                value = input.Text;
                return result == DialogResult.OK && choice != null && !string.IsNullOrWhiteSpace(value);
            }
        }

        private sealed class RebarFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
        {
            public bool AllowElement(Element element) { return element is Rebar; }
            public bool AllowReference(Autodesk.Revit.DB.Reference reference, Autodesk.Revit.DB.XYZ position) { return false; }
        }
    }
}
