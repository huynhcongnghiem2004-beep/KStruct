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
        internal static V3HookChoice Resolve(IEnumerable<V3HookChoice> source,string selected,double degrees,bool required)
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
            if(matches.Count==0)
                throw new InvalidOperationException("Project chưa có móc "+degrees+"° kiểu Stirrup/Tie được loại thép đai cho phép. Nạp/chọn Hook Type có sẵn phù hợp; lệnh không tự tạo móc.");
            throw new InvalidOperationException("Có "+matches.Count+" móc "+degrees+"° phù hợp. Chọn một loại trong tab Thép đai; không cần xóa các loại khác trong project.");
        }
    }
}
