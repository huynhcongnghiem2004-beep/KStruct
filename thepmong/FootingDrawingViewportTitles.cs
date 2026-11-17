using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal static class FootingDrawingViewportTitles
    {
        internal static void Center(Viewport viewport,IList<string> warnings)
        {
            try
            {
                double tolerance=UnitUtils.ConvertToInternalUnits(.1,UnitTypeId.Millimeters);
                for(int attempt=0;attempt<3;attempt++)
                {
                    viewport.Document.Regenerate();
                    Outline box=viewport.GetBoxOutline(),label=viewport.GetLabelOutline();
                    if(box==null||label==null||label.MaximumPoint.DistanceTo(label.MinimumPoint)<tolerance)return;
                    double dx=(box.MinimumPoint.X+box.MaximumPoint.X-label.MinimumPoint.X-label.MaximumPoint.X)/2;
                    if(Math.Abs(dx)<=tolerance)return;
                    int turn=viewport.Rotation==ViewportRotation.Clockwise?-1:
                        viewport.Rotation==ViewportRotation.Counterclockwise?1:0;
                    DrawingPoint2 shift=FootingDrawingMath.TitleOffsetShift(dx,turn);
                    XYZ offset=viewport.LabelOffset;
                    viewport.LabelOffset=new XYZ(offset.X+shift.X,offset.Y+shift.Y,offset.Z);
                }
                viewport.Document.Regenerate();
                Outline finalBox=viewport.GetBoxOutline(),finalLabel=viewport.GetLabelOutline();
                double error=Math.Abs((finalBox.MinimumPoint.X+finalBox.MaximumPoint.X-finalLabel.MinimumPoint.X-finalLabel.MaximumPoint.X)/2);
                if(error>tolerance)warnings.Add("Title Ref của view "+viewport.ViewId.Value+" chưa nhận căn giữa; kiểm tra family title/viewport trong Revit.");
            }
            catch(Exception ex){warnings.Add("Không căn được Title Ref của view "+viewport.ViewId.Value+": "+ex.Message);}
        }
    }
}
