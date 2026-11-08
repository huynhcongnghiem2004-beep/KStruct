using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.FootingRebar
{
    /// <summary>Update an existing managed footing rebar set using the current host geometry.</summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class UpdateFootingRebarCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            FootingRebarCommand.ExecuteUpdate(data, ref message);
    }
}
