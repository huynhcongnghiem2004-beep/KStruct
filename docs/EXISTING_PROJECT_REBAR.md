# Cột và dầm: chỉ dùng Shape/Hook có sẵn

## Phạm vi

Áp dụng cho Tạo thép cột V3; thép dầm thẳng, nhập Excel, dầm theo trục (dùng chung
bộ dựng dầm thẳng), dầm cong và lanh tô. Các lệnh này dùng chung
`NativeRebar/ExistingProjectRebar.cs`.

- Chỉ gọi Revit `CreateFromCurves` với `useExistingShapeIfPossible=true`,
  `createNewShape=false`. Không gọi tạo shape hoặc Hook Type, không chuyển sang FreeForm.
- Kiểm tra ShapeId của thanh vừa dựng thuộc danh sách shape có trước lần gọi.
- Revit chịu trách nhiệm khớp hình học, style và Hook với shape đã nạp. Có shape trong
  Browser chưa có nghĩa là shape đó tương thích với cấu tạo đang dựng.
- Không có shape phù hợp: báo loại thanh và Hook liên quan, hủy tuyến/cấu kiện lỗi
  trong transaction hiện có; không thay bằng thanh khác và không để lại thép tạo dở.
- Thanh neo L kết hợp cổ chai nhiều mặt phẳng ở cột chưa được dựng bằng shape có sẵn;
  lệnh báo rõ giới hạn này trước khi tạo, không tự đổi sang FreeForm.

## Chọn móc cột

Tab Thép đai có lựa chọn Hook 135° và Hook 90° cho đai phụ luân phiên.
Chỉ liệt kê Hook Type có sẵn với góc thực tương ứng và `Style=StirrupTie`, không dựa
vào tên. Tên và ElementId giúp phân biệt; cấu hình lưu UniqueId để dùng đúng loại
khi mở lại hoặc áp dụng cho tầng khác trong cùng project.

Tự chọn chỉ khi có đúng một loại cùng góc/style và được RebarBarType đai cho phép
(`GetHookPermission`). Có nhiều loại thì người dùng chọn rõ; không yêu cầu xóa loại
khác. Móc đã lưu bị xóa/sai góc/sai style/không được phép phải chọn lại. Đai kín không
móc không yêu cầu Hook; đai phụ luân phiên mới yêu cầu móc 90°.

Không sửa các Hook Type/Shape hiện có, không tự thêm thư viện. Người dùng nạp shape/hook
của project khi cần. Bộ kiểm tra XML nối cũ và hình học cổ chai vẫn được giữ.

## Xác nhận

CI chạy kiểm tra chọn Hook, lưu/mở XML và bộ kiểm tra hàm strict bằng API doubles,
cùng kiểm tra các điểm tạo thép cột/dầm không đi vòng qua hàm strict. Sau đó build toàn
bộ add-in với API Revit 2024 thật. Đây không phải xác nhận khớp shape trong Revit runtime.

Thử trên bản sao model: ghi lại danh sách ShapeId/HookId trước và sau; tạo đai với nhiều
Hook Type 135°, chọn móc rõ; thử thiếu shape hoặc móc bị xóa; thử cột/dầm có/không nhóm
Fixed Number, nhập Excel, dầm theo trục/cong và lanh tô; kiểm tra rollback/Undo/ghi đè.
Danh sách Shape/Hook không được tăng sau lệnh. Đối chiếu hình thép, Hook thực và số thanh.
