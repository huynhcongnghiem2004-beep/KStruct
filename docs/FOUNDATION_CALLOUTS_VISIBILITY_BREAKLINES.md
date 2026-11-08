# Callout, hiển thị thép và Breakline dầm — Revit 2024

## Callout mặt bằng → ký hiệu gốc của Detail A–A/B–B

Tạo callout mặt bằng trước, rồi tạo Detail View thuộc `ViewFamily.Detail` bằng `ViewSection.CreateDetail`. Hiển thị ký hiệu gốc của các Detail trên callout, không gọi `CreateReferenceSection` hoặc tạo Reference Other View. Revit API dùng lớp `ViewSection` cho cả Detail và Section; view đích vẫn được kiểm tra là `ViewType.Detail`.

Lưu đầy đủ metadata và liên kết trước khi điều chỉnh ký hiệu. Hai callout thép trên/dưới cùng bộ hồ sơ có thể hiển thị cùng ký hiệu gốc. Ký hiệu gốc của Detail do lệnh sở hữu được ẩn trong các ViewPlan khác để không xuất hiện dư trên mặt bằng tổng hoặc callout khác. Không ẩn/xóa reference thủ công, không sửa các ký hiệu của view ngoài bộ hồ sơ.

Khi cập nhật hồ sơ cũ, gỡ các reference phụ được đánh dấu DetailReference của KStruct trên callout và bỏ ẩn ký hiệu gốc; giữ Detail, viewport, dim/tag và reference thủ công. Nếu xóa reference gây cascade sang đối tượng không thuộc lệnh, rollback như cơ chế bảo vệ sẵn có.

Lệnh bật category ký hiệu trên callout và điều chỉnh ngưỡng ẩn theo tỷ lệ của Detail khi tham số cho phép. Không giả lập Parent View bằng cách ghi tên/giá trị enum suy đoán. `CreateDetail` không nhận parent view ID; vì vậy cần kiểm tra Show in/Parent View, crop và template trực tiếp trong Revit 2024. Nếu collector chưa trả về ký hiệu gốc, lệnh báo cảnh báo và không tạo reference phụ để che lỗi.

UI chỉ liệt kê Detail Type, lưu riêng DetailTypeName. SectionTypeName cũ chỉ dùng đọc XML; không tự đổi Section Type thành Detail. Lựa chọn A–A/B–B độc lập vẫn được giữ.

## View range và hiển thị cốt thép

- Đặt khoảng nhìn bao trọn cao độ móng và nhóm thép được chọn, gồm phần neo. Chuyển world Z sang offset bằng `Level.ProjectElevation` để không nhầm với cao độ hiển thị theo survey/shared datum.
- Kiểm tra thứ tự Top/Cut/Bottom/Depth qua `CheckPlanViewRangeValidity`, đặt range rồi đọc lại. Lỗi không bị nuốt khi cập nhật.
- Chuyển Structural/Fine, bật category Rebar, bỏ ẩn riêng những thanh thuộc lớp cần xem, bật `SetUnobscuredInView` để không bị bê tông che, bật các filter đang ẩn nhóm thép trong chính view đó.
- Khi có metadata của lệnh thép móng, phân lớp theo tên nhóm Bottom/Top/SideTies; nếu không, xét các đoạn ngang của centerline thay cho tâm bounding box có thể bị lệch bởi móc neo. Thanh không phân loại rõ được giữ hiển thị thay vì đoán rồi ẩn.
- Thép lớp còn lại được ẩn riêng trong view. Chạy cập nhật sẽ bỏ ẩn lại đúng thanh mới thuộc lớp chọn, không chỉ thêm danh sách ẩn.
- Tùy chọn **Áp template một lần, chỉnh view riêng** mặc định bật: `ApplyViewTemplateParameters` sao chép định dạng rồi không giữ liên kết template, để range/visibility của hồ sơ được điều chỉnh độc lập. Template gốc và view khác không bị sửa. Nếu tắt, giữ liên kết và báo lỗi khi template ngăn thay đổi cần thiết.

Phạm vi là Rebar có host đúng móng; không tạo thêm thép hoặc xác nhận thiết kế chỉ để làm view nhìn thấy thép. Filter đồ họa đặc biệt, phase/design option, family hoặc workset có thể cần đối chiếu thêm trên mô hình thực.

## Breakline tại dầm giao biên crop

Tab Dim / Tag có checkbox bật/tắt, chọn Detail Item family/type, khoảng dư hai đầu theo mm giấy và đảo phía che.

- Tìm dầm Structural Framing/Beam gần móng trong view, lấy hình học solid của dầm theo view và chiếu các tam giác về mặt phẳng view.
- Giao đường biên crop với từng tam giác, hợp nhất các khoảng liên tiếp theo từng dầm/cạnh. Không dùng convex hull chung để che qua những vùng không có dầm.
- Đặt Detail Item ngang phần dầm bị cắt bởi biên, dịch vào trong 1 mm mô hình. Không nới model crop 6 mm đã chọn.
- Family theo đường được đặt theo đúng span. Family theo điểm được xoay theo tuyến; nếu bề rộng không đủ thì bỏ qua và báo cần type lớn hơn/family theo đường. Không sửa tham số type toàn project để ép vừa dầm.
- Mặc định giả định family che phía phải của hướng đặt; có lựa chọn đảo vì quy ước mask của family không đồng nhất. Cần kiểm tra mặt che trên family của dự án.
- Thiếu family thì cảnh báo/bỏ qua; không tự tải family hoặc vẽ ký hiệu giả.

Ký hiệu mới có ElementUniqueId/HostUniqueId/OwnerViewUniqueId và loại nguồn, nên cập nhật chỉ thay ký hiệu đúng lệnh/view/host, không tự nhận bản sao có metadata cũ. Nếu xóa kéo theo đối tượng ngoài tập sở hữu, transaction hủy. Family thiếu khi cập nhật sẽ giữ Breakline cũ và báo, không xóa rồi bỏ trống.

## Hồ sơ đã tạo trước bản sửa

Lệnh cập nhật sửa được view range của plan Top/Bot cũ có metadata. Bộ đã lưu liên kết tới Section View cần tạo lại bằng Detail; không chuyển loại view cũ ngầm. Hồ sơ cũ chưa có dữ liệu liên kết Detail hoặc lựa chọn Breakline theo view sẽ không tự ghép marker/family từ bộ khác: cần tạo bộ hồ sơ mới để có đầy đủ phần liên kết và Breakline mới. Không tự xóa hồ sơ cũ.

## Kiểm tra

CI chạy bộ cấu hình cũ, bộ thép móng và bộ `tests/FootingDrawingMath`, sau đó build toàn add-in với Revit API 2024. Các kiểm tra mới gồm range dưới level/đổi gốc cao độ, thứ tự mặt phẳng, giao crop–tam giác dầm, đảo hướng/xoay view, tiếp xúc tại góc, loại bỏ mặt suy biến và hợp nhất không trùng ký hiệu; có kiểm tra round-trip lựa chọn Breakline/template.

Cần chạy trực tiếp trên bản sao mô hình Revit 2024 trước phát hành:

1. Callout thép trên/dưới, mở marker mặt cắt phải tới đúng Detail View và sheet; kiểm tra không trùng marker, scale và chiều nhìn.
2. Móng thấp hơn level, shared elevation khác internal, thép có móc cao, SideTies/thép thủ công, template/filter đang ẩn thép; kiểm tra bằng GetViewRange và hình hiển thị thật.
3. Dầm một/hai phía, dầm xoay, dầm cong, dầm ngang/dọc mặt cắt, dầm không giao crop: đúng số/vị trí Breakline và mặt che.
4. Family theo đường/điểm, type thiếu/đổi tên, crop xoay/split, template khóa, khổ sheet: báo rõ những phần không tạo được.
5. Cập nhật nhiều lần không nhân đôi ký hiệu; marker/Breakline đặt tay và bộ hồ sơ khác không đổi. Thử Cancel, Undo/Redo, rollback và hồ sơ legacy.

Build/test thuật toán không xác nhận hiển thị/mặt che/link marker trong Revit. Phiên cloud không có Revit runtime để chạy các ca mô hình này.

## Rà soát trước khi merge

- Cấu hình mới mặc định crop 6 mm thực tế. Preset cũ có giá trị đã lưu được giữ nguyên; kiểm tra ô crop khi nạp preset cũ.
- Dim mặt bằng luôn tạo hai lớp trên bốn cạnh khi bật tùy chọn. Thiếu reference trung gian thì lớp trong đo hai mép móng và có cảnh báo, không bịa tim cột/cọc. Thiếu reference mép hoặc Revit từ chối dim thì hủy tạo bộ hồ sơ đó.
- Breakline trong Detail cắt solid theo cả hai mặt phẳng giới hạn chiều sâu, giữ mặt nắp của khối dầm xuyên qua vùng nhìn, rồi kiểm tra các tam giác trước khi chiếu; không dùng toàn bộ tam giác chỉ vì bounding depth giao nhau.
- Mỗi view mới lưu vị trí và góc xoay móng. Cập nhật dịch/xoay Detail hiện hữu, kiểm tra lại hướng nhìn và mặt phẳng cắt rồi mới cập nhật crop và ký hiệu. Nếu API không chấp nhận thao tác thì rollback bộ móng. Detail cũ thiếu dữ liệu vị trí cần tạo lại.
- Cần chạy Revit 2024 để xác nhận di chuyển/xoay Detail, reference thật của family móng/cột/cọc và hướng mask của family Breakline. Build và các kiểm tra độc lập không thay thế bước này.

## View vượt ô bố trí trên sheet

View hoặc tiêu đề vượt ô bố trí, hoặc có khả năng chồng nội dung trên sheet, chỉ tạo cảnh báo sau khi hoàn tất. Lệnh giữ các view và viewport, tiếp tục đặt các view còn lại, giữ tỷ lệ đã chọn. Có thể kéo viewport/tiêu đề sau khi tạo. Vùng đặt không hợp lệ hoặc Revit không cho đặt view lên sheet vẫn là lỗi thực thi cần xử lý.

## Chọn riêng Detail A–A và B–B

Hai checkbox độc lập cho phép tạo chỉ A–A, chỉ B–B, cả hai hoặc chỉ mặt bằng khi tắt cả hai. Mỗi Detail được chọn vẫn dùng ký hiệu gốc trên callout; chọn một Detail sẽ bật callout và ít nhất một mặt bằng. Offset và đảo hướng của mặt cắt bị tắt sẽ khóa nhập. Preview mặt bằng/sheet, tạo view, mã vai trò X/Y và danh sách liên kết chỉ dùng những Detail được chọn; chỉ B–B vẫn mang vai trò Detail-Y.

Lựa chọn A/B được lưu trong preset và lựa chọn lần trước. Preset cũ chưa có hai trường mới dùng công tắc CreateSections cũ: bật tạo cả hai, tắt không tạo Detail nào. Type, template, tỷ lệ và chiều sâu nhìn hiện vẫn dùng chung cho các Detail được bật.
