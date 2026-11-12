using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SDK_HD.KStruct.Core.V5;

int checks=0;
void Check(bool ok,string message) {checks++;if(!ok)throw new Exception("FAIL "+message);}
void Near(double actual,double wanted,string message) =>
    Check(Math.Abs(actual-wanted)<0.00001,message+" expected "+wanted+" got "+actual);
Near(AuditV2Policy.Mm(1),304.8,"foot to mm");
Check(AuditV2Policy.IsPositiveLength(10),"positive length");
Check(!AuditV2Policy.IsPositiveLength(0),"zero length rejected");
Check(!AuditV2Policy.IsPositiveLength(double.PositiveInfinity),"infinite rejected");
Check(AuditV2Policy.IsValidBounds(new[]{0.0,0.0,-500.0},new[]{100.0,200.0,0.0}),"valid bounds");
Check(!AuditV2Policy.IsValidBounds(new[]{100.0,0.0,0.0},new[]{50.0,10.0,10.0}),"inverted bounds");
Check(!AuditV2Policy.IsValidBounds(new[]{double.NaN,0.0,0.0},new[]{1.0,1.0,1.0}),"nan bounds rejected");
Check(AuditV2Policy.Direction(100,0,0)=="X","axis X");
Check(AuditV2Policy.Direction(0,100,0)=="Y","axis Y");
Check(AuditV2Policy.Direction(0,0,-100)=="Z","axis Z");
Check(AuditV2Policy.Direction(100,95,0)=="oblique","oblique");
Check(AuditV2Policy.Direction(double.NaN,0,0)=="unknown","invalid vector");
Check(AuditV2Policy.Layer(700,0,1000)=="top-candidate","top candidate");
Check(AuditV2Policy.Layer(300,0,1000)=="bottom-candidate","bottom candidate");
Check(AuditV2Policy.Layer(500,0,1000)=="unknown","center ambiguous");
Check(AuditV2Policy.HasUniqueOwnershipIds(new[]{"id1","id2"}),"unique ownership IDs");
Check(!AuditV2Policy.HasUniqueOwnershipIds(new[]{"id1","id1"}),"duplicated ID");
Check(!AuditV2Policy.HasUniqueOwnershipIds(new[]{"id1",""}),"empty ID");
Check(AuditOwnershipPolicy.Classify(false,"h","e")=="unverified","no marker");
Check(AuditOwnershipPolicy.Classify(true,"h","h")=="invalid-self-host","self reference");
Check(AuditOwnershipPolicy.Classify(true,"h","e")=="marked-owned","persisted marker");
Check(AuditOwnershipPolicy.Classify(true,default!,"e")=="invalid-marker","malformed marker");
Check(AuditOwnershipPolicy.MatchesHost("hostA","hostA"),"ownership matching");
Check(!AuditOwnershipPolicy.MatchesHost("hostA","hostB"),"ownership mismatching");
Check(AuditOwnershipPolicy.NormalizeRebarRole("Top-X")=="top","top group");
Check(AuditOwnershipPolicy.NormalizeRebarRole("Bottom-Y")=="bottom","bottom group");
Check(AuditOwnershipPolicy.NormalizeRebarRole("SideTies")=="tie","ties");
var obj=new Dictionary<string,object>{
 {"text","Cọc \"D300\"\nline 2"},{"count",15},
 {"bad",double.NaN},{"inf",double.PositiveInfinity},
 {"items",new object[]{null!,1,true,"x"}},
 {"nested",new Dictionary<string,object>{{"id","abc"},{"dimension",300.0}}}
};
string json=AuditV2Policy.Json(obj);
using(var parsed=JsonDocument.Parse(json)) {
    Check(parsed.RootElement.GetProperty("count").GetInt32()==15,"numeric JSON");
    Check(parsed.RootElement.GetProperty("text").GetString()=="Cọc \"D300\"\nline 2","Unicode JSON escape");
    Check(parsed.RootElement.GetProperty("bad").ValueKind==JsonValueKind.Null,"nan to null");
    Check(parsed.RootElement.GetProperty("inf").ValueKind==JsonValueKind.Null,"infinity to null");
    Check(parsed.RootElement.GetProperty("items").GetArrayLength()==4,"array JSON");
}
Check(AuditV2Policy.SafeSegment(new[]{0.0,0.0,0.0},new[]{10.0,20.0,30.0}),"valid segment");
Check(!AuditV2Policy.SafeSegment(new[]{0.0,double.NaN,0.0},new[]{10.0,20.0,30.0}),"nonfinite segment");
// Revit 2024: Single does not expose ArrayLength. Distributed layout does.
Check(!AuditRebarLayoutPolicy.HasArrayLength("Single"),"Single must not access ArrayLength");
Check(!AuditRebarLayoutPolicy.HasArrayLength("single"),"Single case insensitive");
Check(!AuditRebarLayoutPolicy.HasArrayLength("unavailable"),"Missing layout must not access ArrayLength");
Check(!AuditRebarLayoutPolicy.HasArrayLength(null!),"Null layout must not access ArrayLength");
Check(AuditRebarLayoutPolicy.HasArrayLength("FixedNumber"),"FixedNumber distribution");
Check(AuditRebarLayoutPolicy.HasArrayLength("MaximumSpacing"),"MaximumSpacing distribution");
Check(AuditRebarLayoutPolicy.HasArrayLength("NumberWithSpacing"),"NumberWithSpacing distribution");
Check(AuditRebarLayoutPolicy.IsConsistentSingle("Single",1,1),"Single exactly one bar");
Check(!AuditRebarLayoutPolicy.IsConsistentSingle("Single",2,2),"Single multiple positions invalid");
Check(!AuditRebarLayoutPolicy.IsConsistentSingle("Single",1,0),"Single absent bar invalid");
Check(!AuditRebarLayoutPolicy.IsValidArrayLength(double.NaN),"Reject NaN distribution");
Check(!AuditRebarLayoutPolicy.IsValidArrayLength(double.PositiveInfinity),"Reject infinity distribution");
Check(!AuditRebarLayoutPolicy.IsValidArrayLength(-1),"Reject negative distribution");
Check(AuditRebarLayoutPolicy.IsValidArrayLength(0),"Allow zero length edge case for diagnosis");
Check(AuditRebarLayoutPolicy.IsValidArrayLength(8),"Accept positive distribution length");
// Verify successful two-file promotion, failed staging and preservation of prior reports.
var testDir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"kstruct-v5-audit-"+Guid.NewGuid().ToString("N"));
System.IO.Directory.CreateDirectory(testDir);
try
{
    var outJson=System.IO.Path.Combine(testDir,"model.json");
    var outCsv=System.IO.Path.Combine(testDir,"model.csv");
    System.IO.File.WriteAllText(outJson,"OLD-JSON");
    System.IO.File.WriteAllText(outCsv,"OLD-CSV");
    bool failed=false;
    try
    {
        AuditPairPublisher.Write(outJson,outCsv,(j,c)=>{
            j.Write("{\"schema\":\"test\"}");
            throw new System.IO.IOException("Injected failure before CSV");
        });
    }
    catch(System.IO.IOException){failed=true;}
    Check(failed,"injected output failure");
    Check(System.IO.File.ReadAllText(outJson)=="OLD-JSON","preserve old JSON on staging failure");
    Check(System.IO.File.ReadAllText(outCsv)=="OLD-CSV","preserve old CSV on staging failure");
    AuditPairPublisher.Write(outJson,outCsv,(j,c)=>{
        j.Write("{\"count\":1}");
        c.Write("id\n1\n");
    });
    Check(System.IO.File.ReadAllText(outJson)=="{\"count\":1}","publish JSON successfully");
    Check(System.IO.File.ReadAllText(outCsv).TrimStart('\uFEFF')=="id\n1\n","publish CSV successfully");
    // Second destination cannot be promoted: JSON must roll back to its old value.
    var blockedCsv=System.IO.Path.Combine(testDir,"blocked.csv");
    System.IO.Directory.CreateDirectory(blockedCsv);
    bool promotionFailed=false;
    try
    {
        AuditPairPublisher.Write(outJson,blockedCsv,(j,c)=>{
            j.Write("LATEST-JSON");
            c.Write("LATEST-CSV");
        });
    }
    catch(System.IO.IOException){promotionFailed=true;}
    catch(UnauthorizedAccessException){promotionFailed=true;}
    Check(promotionFailed,"injected second-file promotion failure");
    Check(System.IO.File.ReadAllText(outJson)=="{\"count\":1}","restore original JSON on CSV promotion failure");
    Check(System.IO.Directory.Exists(blockedCsv),"destination folder remains intact");
    Check(System.IO.Directory.GetFiles(testDir,"*.tmp").Length==0,"no leftover staged files");
    Check(System.IO.Directory.GetFiles(testDir,"*.bak").Length==0,"no leftover backup files");
}
finally {System.IO.Directory.Delete(testDir,true);}

var origin=new HostVector3(100,200,300);
Check(HostCoordinateFrame.TryLine(origin,new HostVector3(1100,200,300),out var horizontal),"Horizontal beam frame");
Check(horizontal.Source=="location-curve-straight","Line frame source");
var pLocal=horizontal.ToLocal(new HostVector3(600,300,400));
Near(pLocal[0],500,"Beam local X");
Near(pLocal[1],100,"Beam local Y");
Near(pLocal[2],100,"Beam local Z");
var reconstructed=horizontal.ToWorld(500,100,100);
Near(reconstructed[0],600,"Beam roundtrip X");
Near(reconstructed[1],300,"Beam roundtrip Y");
Near(reconstructed[2],400,"Beam roundtrip Z");
Check(HostCoordinateFrame.TryLine(origin,new HostVector3(100,200,800),out var vertical),"Vertical line frame");
Near(HostVector3.Dot(HostVector3.Cross(vertical.X,vertical.Y),vertical.Z),1,"Right-handed vertical axis");
Check(!HostCoordinateFrame.TryLine(origin,origin,out _),"Reject zero-length beam");
Check(!HostCoordinateFrame.TryLine(new HostVector3(double.NaN,0,0),origin,out _),"Reject invalid origin");
Check(HostCoordinateFrame.TryLine(origin,new HostVector3(1100,900,700),out var sloped),"Sloped frame");
var world=sloped.ToWorld(135.5,-17.3,26.2);
var back=sloped.ToLocal(new HostVector3(world[0],world[1],world[2]));
Near(back[0],135.5,"Sloped roundtrip X");
Near(back[1],-17.3,"Sloped roundtrip Y");
Near(back[2],26.2,"Sloped roundtrip Z");
Check(HostCoordinateFrame.TryInstance(origin,new HostVector3(0,1,0),
    new HostVector3(-1,0,0),new HostVector3(0,0,1),out var rotated),"Rotated column Family frame");
Near(rotated.ToLocal(new HostVector3(100,400,300))[0],200,"Rotated column local X");
Check(HostCoordinateFrame.TryInstance(origin,new HostVector3(1,0,0),
    new HostVector3(0,-1,0),new HostVector3(0,0,1),out var mirrored),
    "Valid mirrored Family frame can be diagnosed");
Check(mirrored.IsMirrored,"Mirrored handedness retained");
Near(mirrored.ToLocal(new HostVector3(100,300,300))[1],-100,
    "Mirrored Y signed coordinates");
Check(!HostCoordinateFrame.TryInstance(origin,new HostVector3(2,0,0),
    new HostVector3(0,1,0),new HostVector3(0,0,1),out _),"Scaled transform rejected");
Check(!HostCoordinateFrame.TryInstance(origin,new HostVector3(1,0,0),
    new HostVector3(0.5,1,0),new HostVector3(0,0,1),out _),"Skew transform rejected");
using(var frameDoc=JsonDocument.Parse(AuditV2Policy.Json(horizontal.AsRecord()))) {
    Check(frameDoc.RootElement.GetProperty("validatedOrthonormal").GetBoolean(),"Frame JSON status");
    Check(frameDoc.RootElement.GetProperty("axisX").GetArrayLength()==3,"Frame JSON axes");
}

Check(HostCoordinateFrame.TryCanonicalLine(
    new HostVector3(5000,0,0),new HostVector3(0,0,0),
    new HostVector3(0,0,1),out var canonicalReverse,out bool reversed),
    "Canonical reversed location line");
Check(reversed,"Reversed line detected");
Check(HostCoordinateFrame.TryCanonicalLine(
    new HostVector3(0,0,0),new HostVector3(5000,0,0),
    new HostVector3(0,0,1),out var canonicalForward,out bool notReversed),
    "Canonical forward location line");
Check(!notReversed,"Forward line not reversed");
Near(canonicalForward.Origin.X,canonicalReverse.Origin.X,"Canonical stable origin");
Near(canonicalForward.X.X,canonicalReverse.X.X,"Canonical stable tangent");
Near(canonicalForward.Y.Z,canonicalReverse.Y.Z,"Canonical stable section axis");
Check(HostCoordinateFrame.TryLineWithReference(
    new HostVector3(0,0,0),new HostVector3(1000,0,0),
    new HostVector3(0,1,1),"section-roll",out var rolled),
    "Rolled beam section reference");
Near(rolled.Y.Y,Math.Sqrt(0.5),"Rolled Y direction");
Near(rolled.Y.Z,Math.Sqrt(0.5),"Rolled Z direction");
Check(!HostCoordinateFrame.TryLineWithReference(
    new HostVector3(0,0,0),new HostVector3(1000,0,0),
    new HostVector3(1,0,0),"parallel",out _),
    "Parallel section reference must fail closed");
Check(!HostCoordinateFrame.TryLineWithReference(
    new HostVector3(0,0,0),new HostVector3(1000,0,0),
    new HostVector3(double.NaN,0,0),"invalid",out _),
    "Nonfinite section reference fails");
Check(HostCoordinateFrame.TryLine(new HostVector3(0,0,0),
    new HostVector3(1,0,1000),out var almostVertical),"Near-vertical line");
Near(HostVector3.Dot(HostVector3.Cross(almostVertical.X,almostVertical.Y),almostVertical.Z),
    1,"Near-vertical frame handedness");
using(var status=JsonDocument.Parse(AuditV2Policy.Json(rolled.AsRecord())))
    Check(!status.RootElement.GetProperty("placementApproved").GetBoolean(),
        "Host diagnostics must not authorize Rebar creation");

Check(HostFaceCandidatePolicy.Orientation(horizontal,new HostVector3(0,0,1))=="+Z-face-candidate",
    "Beam local top face candidate");
Check(HostFaceCandidatePolicy.Orientation(horizontal,new HostVector3(0,0,-1))=="-Z-face-candidate",
    "Beam local bottom face candidate");
Check(HostFaceCandidatePolicy.Orientation(horizontal,new HostVector3(1,0,0))=="+X-face-candidate",
    "Beam local end face candidate");
Check(HostFaceCandidatePolicy.Orientation(horizontal,new HostVector3(0,-1,0))=="-Y-face-candidate",
    "Beam local left face candidate");
Check(HostFaceCandidatePolicy.Orientation(horizontal,new HostVector3(0,1,1))=="oblique-candidate",
    "Diagonal plane is not orthogonal concrete face");
Check(HostFaceCandidatePolicy.Orientation(horizontal,new HostVector3(0,0,0))=="unverified",
    "Zero normal rejected");
Check(HostFaceCandidatePolicy.Orientation(horizontal,new HostVector3(double.NaN,0,0))=="unverified",
    "Nonfinite normal rejected");
Check(HostFaceCandidatePolicy.Orientation(null!,new HostVector3(0,0,1))=="unverified",
    "Unresolved host frame not classified");
Check(HostFaceCandidatePolicy.ValidFaceArea(10),"Positive face area");
Check(!HostFaceCandidatePolicy.ValidFaceArea(0),"Zero face area rejected");
Check(!HostFaceCandidatePolicy.ValidFaceArea(double.PositiveInfinity),"Nonfinite face area rejected");
Check(HostFaceCandidatePolicy.Orientation(mirrored,new HostVector3(0,-1,0))=="+Y-face-candidate",
    "Mirror face orientation remains signed");

double entry,exit;
var jointMin=new HostVector3(100,-100,-100);
var jointMax=new HostVector3(300,100,100);
Check(StructuralJointCandidatePolicy.TrySegmentBox(
    new HostVector3(0,0,0),new HostVector3(400,0,0),jointMin,jointMax,
    0,out entry,out exit),"Beam centerline crosses candidate box");
Near(entry,0.25,"Joint entry position");
Near(exit,0.75,"Joint exit position");
Check(!StructuralJointCandidatePolicy.TrySegmentBox(
    new HostVector3(0,200,0),new HostVector3(400,200,0),jointMin,jointMax,
    0,out _,out _),"Beam misses joint box");
Check(StructuralJointCandidatePolicy.TrySegmentBox(
    new HostVector3(0,105,0),new HostVector3(400,105,0),jointMin,jointMax,
    10,out _,out _),"Explicit bounding tolerance candidate");
Check(!StructuralJointCandidatePolicy.TrySegmentBox(
    new HostVector3(0,0,0),new HostVector3(0,0,0),jointMin,jointMax,
    0,out _,out _),"Degenerate beam excluded");
Check(!StructuralJointCandidatePolicy.TrySegmentBox(
    new HostVector3(0,0,0),new HostVector3(400,0,0),jointMin,jointMax,
    -1,out _,out _),"Negative padding excluded");
Check(StructuralJointCandidatePolicy.TrySegmentBox(
    new HostVector3(400,0,0),new HostVector3(0,0,0),jointMin,jointMax,
    0,out entry,out exit),"Reversed beam has candidate");
Near(entry,0.25,"Reversed beam entry");
Near(exit,0.75,"Reversed beam exit");
Check(!StructuralJointCandidatePolicy.TrySegmentBox(
    new HostVector3(double.NaN,0,0),new HostVector3(400,0,0),jointMin,jointMax,
    0,out _,out _),"Invalid numeric beam excluded");
Check(StructuralJointCandidatePolicy.TrySegmentBox(
    new HostVector3(0,100,0),new HostVector3(400,100,0),jointMin,jointMax,
    0,out _,out _),"Tangential contact reported as candidate");
Check(StructuralJointCandidatePolicy.TryColumnStackCandidate(
    new HostVector3(0,0,0),new HostVector3(200,200,3000),
    new HostVector3(0,0,3000),new HostVector3(200,200,6000),
    0,1,out var xyArea),"Column stack interface candidate");
Near(xyArea,40000,"Column stack XY intersection area");
Check(!StructuralJointCandidatePolicy.TryColumnStackCandidate(
    new HostVector3(0,0,0),new HostVector3(200,200,3000),
    new HostVector3(1000,0,3000),new HostVector3(1200,200,6000),
    5,1,out _),"Disjoint columns not joined");
Check(!StructuralJointCandidatePolicy.TryColumnStackCandidate(
    new HostVector3(0,0,0),new HostVector3(200,200,3000),
    new HostVector3(0,0,4000),new HostVector3(200,200,6000),
    1,1,out _),"Large vertical column gap excluded");
Check(!StructuralJointCandidatePolicy.TryColumnStackCandidate(
    new HostVector3(0,0,0),new HostVector3(200,200,3000),
    new HostVector3(0,0,3000),new HostVector3(200,200,6000),
    double.NaN,1,out _),"Nonfinite stack tolerance excluded");
Console.WriteLine("PASS: "+checks+" V5 audit JSON, geometry, Rebar classification and ownership policy checks.");
