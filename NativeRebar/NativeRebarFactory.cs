using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace SDK_HD.KStruct.NativeRebar
{
    internal static class NativeRebarFactory
    {
        public static Rebar CreateFromCurves(Document doc, RebarStyle style, RebarBarType type,
            RebarHookType startHook, RebarHookType endHook, Element host, XYZ normal,
            IList<Curve> curves, RebarHookOrientation startOrientation,
            RebarHookOrientation endOrientation, bool useExistingShapeIfPossible, bool createNewShape)
        {
            Exception shapeFailure = null;
            using (var shapeAttempt = new SubTransaction(doc))
            {
                shapeAttempt.Start();
                try
                {
                    Rebar shaped = Rebar.CreateFromCurves(doc, style, type, startHook, endHook, host, normal,
                        curves, startOrientation, endOrientation, useExistingShapeIfPossible, createNewShape);
                    if (shaped != null && shapeAttempt.Commit() == TransactionStatus.Committed) return shaped;
                    if (shapeAttempt.GetStatus() == TransactionStatus.Started) shapeAttempt.RollBack();
                    shapeFailure = new InvalidOperationException("Revit không tạo được Rebar theo RebarShape.");
                }
                catch (Exception ex)
                {
                    if (shapeAttempt.GetStatus() == TransactionStatus.Started) shapeAttempt.RollBack();
                    shapeFailure = ex;
                }
            }

            if (style != RebarStyle.Standard || startHook != null || endHook != null)
                throw new InvalidOperationException("Không tạo được RebarShape cho thanh có hook hoặc kiểu StirrupTie; FreeForm không thay được kiểu móc/đai.", shapeFailure);

            RebarFreeFormValidationResult validation;
            var curveSets = new List<IList<Curve>> { curves.ToList() };
            Rebar freeForm = Rebar.CreateFreeForm(doc, type, host, curveSets, out validation);
            if (freeForm == null || validation != RebarFreeFormValidationResult.Success)
                throw new InvalidOperationException("Không tạo được RebarShape hoặc Rebar FreeForm (" + validation + ").", shapeFailure);
            return freeForm;
        }
    }
}
