# Callout, hiển thị thép và Breakline dầm — Revit 2024

## Callout mặt bằng → mặt cắt trên callout → Reference Detail

Luồng tạo hồ sơ: tạo callout mặt bằng thép trước, tạo **Detail View A–A/B–B** thuộc `ViewFamily.Detail` bằng `ViewSection.CreateDetail`, rồi đặt ký hiệu mặt cắt trên chính callout tham chiếu tới các Detail View đó. Không tạo view đích thuộc `ViewFamily.Section`.

Revit API dùng lớp `ViewSection` cho cả Detail và Section; bản sửa kiểm tra `ViewType.Detail` của view đích. Hàm `CreateReferenceSection` được dùng để đặt **ký hiệu mặt cắt trên mặt bằng callout**, không tạo Section View mới. View đích vẫn là Detail. Không dùng reference callout hình chữ nhật để thay đường cắt A–A/B–B.

UI chỉ liệt kê **Detail Type**, lưu riêng `DetailTypeName`. Trường `SectionTypeName` cũ được giữ để đọc preset XML, nhưng không tự nhận Section Type cũ thành Detail Type có cùng tên. Preset cũ cần chọn lại Detail Type khi bật mặt cắt. Nếu project thiếu Detail Type, lệnh báo thiếu; không tự tạo Section View thay thế.

Các view mới lưu đúng cặp Detail đích theo từng bộ hồ sơ. Khi cập nhật, kiểm tra view đích vẫn là Detail và đúng host; không nối tùy ý view của bộ khác hoặc chuyển Section cũ thành Detail ngầm. Ký hiệu tham chiếu có parent là callout và mở đúng Detail View trên sheet. Không giả lập thuộc tính ParentView trong Project Browser; kiểu đầu/ký hiệu do Detail View Family Type/family của dự án quyết định.

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
