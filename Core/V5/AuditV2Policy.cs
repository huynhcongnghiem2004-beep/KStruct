using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SDK_HD.KStruct.Core.V5
{
    /// <summary>Host-independent audit serialization and geometric sanity checks.</summary>
    internal static class AuditV2Policy
    {
        internal const string Schema = "kstruct.model-audit.v2";
        internal static double Mm(double feet) => feet * 304.8;
        internal static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal static bool IsPositiveLength(double millimeters) => IsFinite(millimeters) && millimeters > 0;
        internal static bool IsValidBounds(double[] min, double[] max) =>
            min != null && max != null && min.Length == 3 && max.Length == 3 &&
            Enumerable.Range(0,3).All(i => IsFinite(min[i]) && IsFinite(max[i]) && min[i] <= max[i]);
        internal static bool HasUniqueOwnershipIds(IEnumerable<string> ids)
        {
            if (ids == null) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids)
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) return false;
            return true;
        }
        internal static string Layer(double barZ, double hostLowZ, double hostHighZ, double toleranceMm = 1.0)
        {
            if (!IsFinite(barZ) || !IsFinite(hostLowZ) || !IsFinite(hostHighZ) ||
                hostLowZ >= hostHighZ || toleranceMm < 0 || !IsFinite(toleranceMm)) return "unknown";
            double center = (hostLowZ + hostHighZ) / 2;
            if (Math.Abs(barZ - center) <= toleranceMm) return "unknown";
            return barZ > center ? "top-candidate" : "bottom-candidate";
        }
        internal static string Direction(double dx, double dy, double dz)
        {
            if (!IsFinite(dx) || !IsFinite(dy) || !IsFinite(dz)) return "unknown";
            double x = Math.Abs(dx), y = Math.Abs(dy), z = Math.Abs(dz);
            double max = Math.Max(x,Math.Max(y,z));
            if (max < 0.000001) return "unknown";
            if ((new[]{x,y,z}).Count(v=>Math.Abs(v-max) <= max*0.10) > 1) return "oblique";
            return x==max?"X":y==max?"Y":"Z";
        }
        internal static bool SafeSegment(double[] start, double[] end) =>
            start != null && end != null && start.Length == 3 && end.Length == 3 &&
            Enumerable.Range(0,3).All(i => IsFinite(start[i]) && IsFinite(end[i]));
        internal static string Json(object value)
        {
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                WriteJson(writer,value);
                return writer.ToString();
            }
        }
        private static void WriteJson(TextWriter writer,object value)
        {
            if(value==null){writer.Write("null");return;}
            if(value is string text){writer.Write('"');foreach(char c in text)
            {
                switch(c){case '"':writer.Write("\\\"");break;case '\\':writer.Write("\\\\");break;
                    case '\n':writer.Write("\\n");break;case '\r':writer.Write("\\r");break;
                    case '\t':writer.Write("\\t");break;
                    default:if(c<32)writer.Write("\\u"+((int)c).ToString("x4",CultureInfo.InvariantCulture));
                        else writer.Write(c);break;}
            }writer.Write('"');return;}
            if(value is bool boolean){writer.Write(boolean?"true":"false");return;}
            if(value is double d){writer.Write(IsFinite(d)?d.ToString("R",CultureInfo.InvariantCulture):"null");return;}
            if(value is float f){writer.Write(IsFinite(f)?f.ToString("R",CultureInfo.InvariantCulture):"null");return;}
            if(value is decimal dec){writer.Write(dec.ToString(CultureInfo.InvariantCulture));return;}
            if(value is int || value is long || value is short || value is byte)
                {writer.Write(Convert.ToString(value,CultureInfo.InvariantCulture));return;}
            if(value is IDictionary<string,object> dict)
            {
                writer.Write("{");bool first=true;
                foreach(var item in dict.OrderBy(x=>x.Key,StringComparer.Ordinal))
                {if(!first)writer.Write(",");first=false;WriteJson(writer,item.Key);
                    writer.Write(":");WriteJson(writer,item.Value);}
                writer.Write("}");return;
            }
            if(value is IEnumerable enumerable)
            {
                writer.Write("[");bool first=true;
                foreach(var item in enumerable){if(!first)writer.Write(",");first=false;WriteJson(writer,item);}
                writer.Write("]");return;
            }
            throw new InvalidDataException("Unsupported audit field type: "+value.GetType().FullName);
        }
    }
}
