using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.Core.V5
{
    /// <summary>
    /// Read-only inventory of positive-volume solids of a selected structural host.
    /// A Family may contain steel, voids, nested piles and decorative geometry:
    /// counting solids never certifies a concrete cross-section or usable cover face.
    /// </summary>
    internal sealed class V5HostSolidInventory
    {
        internal int SolidCount;
        internal int ConcreteMaterialCandidateSolids;
        internal int DirectConcreteCandidateSolids;
        internal int NestedConcreteCandidateSolids;
        internal int NestedSolidCount;
        internal int UnknownOrMixedMaterialSolids;
        internal int PlanarFaceCount;
        internal int NonPlanarFaceCount;
        internal int UnavailableMaterialFaceCount;
        internal double GrossSolidVolumeCubicMm;
        internal bool Truncated;
        internal readonly List<string> Warnings = new List<string>();
        internal bool HasGeometricCandidates => SolidCount > 0 && !Truncated;
        internal bool ConcreteVerified => false;
        internal bool CoverVerified => false;
    }

    internal static class V5HostSolidInspector
    {
        private const int MaxSolids = 128;
        private const int MaxDepth = 6;
        internal static V5HostSolidInventory Inspect(Element host)
        {
            var result = new V5HostSolidInventory();
            if (host == null)
            {
                result.Warnings.Add("Chưa có Host để kiểm tra Solid.");
                return result;
            }
            try
            {
                var options = new Options
                {
                    DetailLevel = ViewDetailLevel.Fine,
                    IncludeNonVisibleObjects = false,
                    ComputeReferences = false
                };
                GeometryElement geometry = host.get_Geometry(options);
                if (geometry == null)
                {
                    result.Warnings.Add("Revit không trả về GeometryElement cho Host.");
                    return result;
                }
                Scan(geometry, host.Document, result, 0);
            }
            catch (Exception ex)
            {
                result.Warnings.Add("Không đọc được Solid: " + ex.GetType().Name);
            }
            if (result.SolidCount == 0)
                result.Warnings.Add("Chưa tìm thấy Solid có thể tích dương.");
            if (result.Truncated)
                result.Warnings.Add("Danh sách Solid vượt giới hạn kiểm tra; phải xác minh thủ công.");
            if (result.NestedSolidCount > 0)
                result.Warnings.Add("Có " + result.NestedSolidCount + " Solid qua GeometryInstance; không xác định được chủ sở hữu nested Family từ hình học đã flatten.");
            if (result.DirectConcreteCandidateSolids > 1)
                result.Warnings.Add("Nhiều Solid bê tông trực tiếp; chưa loại trừ chồng lắp, lỗ rỗng và các phần tử phụ.");
            result.Warnings.Add("Vật liệu bê tông trên các mặt KHÔNG chứng minh Solid thuộc đúng Host; chưa được cấp quyền tạo thép.");
            return result;
        }

        private static void Scan(GeometryElement geometry, Document doc,
            V5HostSolidInventory result, int depth)
        {
            if (geometry == null || result.Truncated) return;
            if (depth > MaxDepth)
            {
                result.Truncated = true;
                return;
            }
            foreach (GeometryObject item in geometry)
            {
                if (result.Truncated) break;
                if (item is Solid solid)
                {
                    InspectSolid(solid, doc, result, depth);
                }
                else if (item is GeometryInstance instance)
                {
                    try
                    {
                        // Instance geometry is already transformed into host coordinates.
                        // Do not additionally apply instance.Transform.
                        Scan(instance.GetInstanceGeometry(), doc, result, depth + 1);
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add("Không đọc được nested Family: " + ex.GetType().Name);
                    }
                }
            }
        }

        private static void InspectSolid(Solid solid, Document doc,
            V5HostSolidInventory result, int depth)
        {
            if (solid == null) return;
            double volume;
            try { volume = solid.Volume; }
            catch (Exception ex)
            {
                result.Warnings.Add("Không đọc được thể tích Solid: " + ex.GetType().Name);
                return;
            }
            if (!AuditV2Policy.IsFinite(volume) || volume <= 1e-10) return;
            if (result.SolidCount >= MaxSolids)
            {
                result.Truncated = true;
                return;
            }
            result.SolidCount++;
            if (depth > 0) result.NestedSolidCount++;
            double volumeMm = volume * 304.8 * 304.8 * 304.8;
            if (AuditV2Policy.IsFinite(volumeMm) &&
                AuditV2Policy.IsFinite(result.GrossSolidVolumeCubicMm+volumeMm))
                result.GrossSolidVolumeCubicMm += volumeMm;
            else
                result.Warnings.Add("Thể tích cộng dồn không hợp lệ; bỏ qua giá trị.");
            bool allConcrete=true;
            int recognizedFaces=0;
            try
            {
                foreach (Face face in solid.Faces)
                {
                    if (face == null) continue;
                    // Degenerate/two-sided faces are not reliable outer cover faces.
                    if (!AuditV2Policy.IsFinite(face.Area) || face.Area<=1e-12 ||
                        face.IsTwoSided)
                    {
                        allConcrete=false;
                        result.Warnings.Add("Solid có mặt suy biến hoặc hai phía, không duyệt đặt thép.");
                        continue;
                    }
                    if (face is PlanarFace) result.PlanarFaceCount++;
                    else result.NonPlanarFaceCount++;
                    try
                    {
                        ElementId materialId=face.MaterialElementId;
                        if (materialId==null || materialId==ElementId.InvalidElementId)
                        {
                            result.UnavailableMaterialFaceCount++;
                            allConcrete=false;
                            continue;
                        }
                        Material material=doc.GetElement(materialId) as Material;
                        if (material?.StructuralAssetId==null ||
                            material.StructuralAssetId==ElementId.InvalidElementId)
                        {
                            allConcrete=false;
                            result.UnavailableMaterialFaceCount++;
                            continue;
                        }
                        PropertySetElement set=doc.GetElement(material.StructuralAssetId)
                            as PropertySetElement;
                        StructuralAsset asset=set?.GetStructuralAsset();
                        if (asset==null || asset.StructuralAssetClass!=StructuralAssetClass.Concrete)
                            allConcrete=false;
                        else recognizedFaces++;
                    }
                    catch
                    {
                        allConcrete=false;
                        result.UnavailableMaterialFaceCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                allConcrete=false;
                result.Warnings.Add("Không đọc được các mặt Solid: " + ex.GetType().Name);
            }
            if (allConcrete && recognizedFaces>0)
            {
                result.ConcreteMaterialCandidateSolids++;
                if (depth == 0) result.DirectConcreteCandidateSolids++;
                else result.NestedConcreteCandidateSolids++;
            }
            else
                result.UnknownOrMixedMaterialSolids++;
        }
    }
}
