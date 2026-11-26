using System;
using System.Collections.Generic;
using System.Linq;

namespace ColumnRebarPro.V3
{
    // V4 safety invariant; kept free of Revit dependencies for regression testing.
    internal static class V4DeletionGuard
    {
        internal static void EnsureOnlyExpected<T>(
            IEnumerable<T> expectedIds, IEnumerable<T> actuallyDeletedIds, string operation)
        {
            if (expectedIds == null) throw new ArgumentNullException(nameof(expectedIds));
            if (actuallyDeletedIds == null) throw new ArgumentNullException(nameof(actuallyDeletedIds));
            var expected = new HashSet<T>(expectedIds);
            var unexpected = actuallyDeletedIds.Where(id => !expected.Contains(id)).Distinct().ToList();
            if (unexpected.Count != 0)
                throw new InvalidOperationException(
                    "KStruct V4: Hủy " + operation + " vì Revit xóa kèm " +
                    unexpected.Count + " đối tượng ngoài bộ thép dự kiến (có thể là Tag/Dimension). " +
                    "Transaction sẽ rollback để giữ mô hình và hồ sơ cũ.");
        }
    }
}
