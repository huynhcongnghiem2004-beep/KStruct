using System;
using System.Collections.Generic;
using System.Linq;

namespace ColumnRebarPro.V3
{
    internal sealed class V3HookChoice
    {
        public string Id { get; set; }
        public string Label { get; set; }
        internal double Angle;
        internal bool TieStyle, Allowed;
        public override string ToString() => Label;
    }

    internal static class V3HookSelection
    {
        // A sample may resolve ambiguity ONLY if all qualifying reference bars on the
        // selected host (or all matching project samples) use one identical HookType.
        // Never pick by arbitrary name, index, frequency or Hook angle alone.
        internal static string SuggestFromSamples(IEnumerable<V3HookChoice> source,
            double degrees, IEnumerable<string> hostSampleIds, IEnumerable<string> projectSampleIds)
        {
            var eligible = source.Where(h => h.TieStyle && h.Allowed &&
                Math.Abs(h.Angle-degrees*Math.PI/180)<1e-6).Select(h=>h.Id)
                .Where(id=>!string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal).ToList();
            foreach (var samples in new[] {hostSampleIds, projectSampleIds})
            {
                if (samples == null) continue;
                var seen = samples.Where(id=>eligible.Contains(id,StringComparer.Ordinal))
                    .Distinct(StringComparer.Ordinal).ToList();
                if (seen.Count == 1) return seen[0];
                if (seen.Count > 1) return null; // conflicting reference types
            }
            return null;
        }

        internal static V3HookChoice Resolve(IEnumerable<V3HookChoice> source,string selected,double degrees,bool required,
            IEnumerable<string> hostSampleIds=null, IEnumerable<string> projectSampleIds=null)
        {
            if (!required) return null;
            var all=source.ToList();
            Func<V3HookChoice,bool> compatible=h=>h.TieStyle && h.Allowed &&
                Math.Abs(h.Angle-degrees*Math.PI/180)<1e-6;
            if (!string.IsNullOrEmpty(selected))
            {
                var found=all.SingleOrDefault(h=>h.Id==selected);
                if(found==null || !compatible(found))
                    throw new InvalidOperationException("Móc "+degrees+"° đã chọn bị xóa, sai kiểu/góc hoặc không được loại thép đai cho phép. Chọn lại trong tab Thép đai.");
                return found;
            }
            var matches=all.Where(compatible).ToList();
            if(matches.Count==1)return matches[0];
            var suggested = SuggestFromSamples(matches, degrees, hostSampleIds, projectSampleIds);
            if (suggested != null) return matches.Single(h=>h.Id==suggested);
            if(matches.Count==0)
                throw new InvalidOperationException("Project chưa có móc "+degrees+"° kiểu Stirrup/Tie được loại thép đai cho phép. Nạp/chọn Hook Type có sẵn phù hợp; lệnh không tự tạo móc.");
            throw new InvalidOperationException("Có "+matches.Count+" móc "+degrees+"° phù hợp nhưng không tìm được mẫu Rebar dùng một Hook thống nhất. Chọn một loại trong tab Thép đai để xác nhận; không xóa các loại khác trong project.");
        }
    }
}
