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
    public sealed class MergeLinearDimensionsCommand : IExternalCommand
    {
        private sealed class DimensionFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) { return element is Dimension; }
            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }

        private sealed class DimensionChain
        {
            public DimensionType Type;
            public Line Axis;
            public List<Dimension> Dimensions = new List<Dimension>();
            public Dictionary<string, Reference> References = new Dictionary<string, Reference>();
            public Dictionary<string, HashSet<string>> Links = new Dictionary<string, HashSet<string>>();
            public double Min;
            public double Max;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Dimension> dimensions = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Dimension>()
                    .Where(d => d.OwnerViewId == ui.ActiveView.Id).Distinct().ToList();
                if (dimensions.Count < 2)
                {
                    var picked = ui.Selection.PickObjects(ObjectType.Element, new DimensionFilter(),
                        "Chọn từ hai kích thước thẳng liền nhau, cùng đường và cùng kiểu");
                    dimensions = picked.Select(r => doc.GetElement(r)).OfType<Dimension>().Distinct().ToList();
                }
                if (dimensions.Count < 2)
                {
                    TaskDialog.Show("KStruct - Gộp kích thước", "Cần chọn ít nhất hai kích thước.");
                    return Result.Cancelled;
                }

                DimensionChain chain = BuildChain(doc, dimensions);
                List<string> order = OrderChain(chain);
                var refs = new ReferenceArray();
                foreach (string key in order) refs.Append(chain.References[key]);

                XYZ start = chain.Axis.Origin + chain.Axis.Direction * chain.Min;
                XYZ end = chain.Axis.Origin + chain.Axis.Direction * chain.Max;
                Line dimensionLine = Line.CreateBound(start, end);

                using (var tx = new Transaction(doc, "KStruct - Gộp kích thước thẳng"))
                {
                    tx.Start();
                    Dimension merged = doc.Create.NewDimension(ui.ActiveView, dimensionLine, refs, chain.Type);
                    if (merged == null) throw new InvalidOperationException("Revit không tạo được kích thước đã gộp.");
                    doc.Delete(chain.Dimensions.Select(d => d.Id).ToList());
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận giao dịch gộp kích thước.");
                }

                TaskDialog.Show("KStruct - Gộp kích thước", "Đã gộp " + chain.Dimensions.Count +
                    " kích thước thành một chuỗi " + (order.Count - 1) + " đoạn.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("KStruct - Gộp kích thước", ex.Message);
                return Result.Failed;
            }
        }

        private static DimensionChain BuildChain(Document doc, IList<Dimension> dimensions)
        {
            var chain = new DimensionChain();
            foreach (Dimension dimension in dimensions)
            {
                Line line = dimension.Curve as Line;
                if (line == null) throw new InvalidOperationException("Chỉ hỗ trợ kích thước thẳng.");
                DimensionType type = doc.GetElement(dimension.GetTypeId()) as DimensionType;
                if (type == null) throw new InvalidOperationException("Không đọc được kiểu kích thước.");

                if (chain.Axis == null)
                {
                    chain.Axis = line;
                    chain.Type = type;
                    chain.Min = double.PositiveInfinity;
                    chain.Max = double.NegativeInfinity;
                }
                else
                {
                    if (chain.Type.Id != type.Id)
                        throw new InvalidOperationException("Các kích thước phải dùng cùng kiểu Dimension.");
                    if (Math.Abs(chain.Axis.Direction.Normalize().DotProduct(line.Direction.Normalize())) < 0.9999)
                        throw new InvalidOperationException("Các đường kích thước phải song song.");
                    double offset = (line.Origin - chain.Axis.Origin).CrossProduct(chain.Axis.Direction.Normalize()).GetLength();
                    if (offset > 1.0 / 304.8)
                        throw new InvalidOperationException("Các kích thước phải nằm trên cùng một đường kích thước (sai lệch tối đa 1 mm).");
                }

                chain.Dimensions.Add(dimension);
                chain.Min = Math.Min(chain.Min, (line.GetEndPoint(0) - chain.Axis.Origin).DotProduct(chain.Axis.Direction));
                chain.Min = Math.Min(chain.Min, (line.GetEndPoint(1) - chain.Axis.Origin).DotProduct(chain.Axis.Direction));
                chain.Max = Math.Max(chain.Max, (line.GetEndPoint(0) - chain.Axis.Origin).DotProduct(chain.Axis.Direction));
                chain.Max = Math.Max(chain.Max, (line.GetEndPoint(1) - chain.Axis.Origin).DotProduct(chain.Axis.Direction));

                var keys = new List<string>();
                foreach (Reference reference in dimension.References)
                {
                    if (reference == null) throw new InvalidOperationException("Có mốc kích thước không còn hợp lệ.");
                    string key = reference.ConvertToStableRepresentation(doc);
                    if (!chain.References.ContainsKey(key)) chain.References.Add(key, reference);
                    keys.Add(key);
                }
                if (keys.Count < 2) throw new InvalidOperationException("Mỗi kích thước phải có ít nhất hai mốc.");
                for (int i = 0; i < keys.Count - 1; i++)
                {
                    if (keys[i] == keys[i + 1]) continue;
                    AddLink(chain, keys[i], keys[i + 1]);
                }
            }

            if (chain.Links.Count < 3) throw new InvalidOperationException("Chuỗi gộp phải có ít nhất hai đoạn.");
            if (chain.Links.Values.Any(neighbors => neighbors.Count > 2))
                throw new InvalidOperationException("Các mốc tạo thành nhánh; chỉ hỗ trợ một chuỗi thẳng liên tục.");
            return chain;
        }

        private static void AddLink(DimensionChain chain, string a, string b)
        {
            if (!chain.Links.ContainsKey(a)) chain.Links[a] = new HashSet<string>();
            if (!chain.Links.ContainsKey(b)) chain.Links[b] = new HashSet<string>();
            chain.Links[a].Add(b);
            chain.Links[b].Add(a);
        }

        private static List<string> OrderChain(DimensionChain chain)
        {
            List<string> ends = chain.Links.Where(pair => pair.Value.Count == 1).Select(pair => pair.Key).ToList();
            if (ends.Count != 2 || chain.Links.Any(pair => pair.Value.Count == 0))
                throw new InvalidOperationException("Kích thước không tạo thành một chuỗi liên tục có hai đầu.");

            var result = new List<string>();
            string previous = null;
            string current = ends[0];
            while (true)
            {
                result.Add(current);
                string next = chain.Links[current].FirstOrDefault(x => x != previous);
                if (next == null) break;
                previous = current;
                current = next;
            }
            if (result.Count != chain.Links.Count)
                throw new InvalidOperationException("Chuỗi có nhánh rời; hãy chọn riêng một chuỗi liền nhau.");
            return result;
        }
    }
}
