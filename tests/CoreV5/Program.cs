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
Check(AuditOwnershipPolicy.Classify(true,null,"e")=="invalid-marker","malformed marker");
Check(AuditOwnershipPolicy.MatchesHost("hostA","hostA"),"ownership matching");
Check(!AuditOwnershipPolicy.MatchesHost("hostA","hostB"),"ownership mismatching");
Check(AuditOwnershipPolicy.NormalizeRebarRole("Top-X")=="top","top group");
Check(AuditOwnershipPolicy.NormalizeRebarRole("Bottom-Y")=="bottom","bottom group");
Check(AuditOwnershipPolicy.NormalizeRebarRole("SideTies")=="tie","ties");
var obj=new Dictionary<string,object>{
 {"text","Cọc \"D300\"\nline 2"},{"count",15},
 {"bad",double.NaN},{"inf",double.PositiveInfinity},
 {"items",new object[]{null,1,true,"x"}},
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
Console.WriteLine("PASS: "+checks+" V5 audit JSON, geometry, Rebar classification and ownership policy checks.");
