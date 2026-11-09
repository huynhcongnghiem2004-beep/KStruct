# Kế hoạch lấy view và trình bày hồ sơ móng — Revit 2024

Tài liệu này chi tiết hóa và thay thế phần bố cục/lấy view trong FOUNDATION_DRAWINGS_REVIT2024_PLAN.md. Đây là thiết kế để triển khai, chưa thay đổi hành vi các lệnh hiện có.

## 1. Đối chiếu với cách làm hiện tại

Đã rà soát thepmong/FootingDrawingBuilder.cs và FootingDrawingProfileStore.cs trên main:

- CreatePlanOutput tạo callout từ active plan hoặc tạo ViewPlan theo level; SetLayerRange đặt khoảng nhìn quanh cao độ trung bình nhóm thép.
- IsLayer phân loại theo vị trí hình học; cần xử lý riêng thanh đứng, thanh uốn xuyên nhiều lớp và thép tăng cường để tránh gán sai lớp.
- CreateSections lấy hai hướng từ LocationPoint.Rotation, origin ở location của family, depth bao bounding box móng. Vị trí đặt family có thể không trùng tâm hình học hoặc tim cột/cọc cần cắt.
- PlaceViewports chia hai cột theo số view. FitViewportToCell tự đổi Scale, kể cả tăng kích thước hình khi ô còn rộng. Chưa thể xem đây là quy tắc trình bày hồ sơ đã được chấp thuận.
- GetPiles tìm từ active view và heuristic tên/bounding box; dễ phụ thuộc visibility và lẫn móng lân cận.
- Tag đặt lệch lên theo nhịp lặp ba mức; chưa có kiểm tra va chạm đầy đủ. Viewport title được căn giữa nhưng chưa tính chung trong kiểm tra va chạm.

## 2. Bộ hồ sơ và bố cục đề xuất

Mặc định giai đoạn đầu: một móng/đài cho một bộ hồ sơ. Không gộp theo Type khi chưa đối chiếu hình học, cao độ, vị trí cột/cọc, Rebar và cấu hình thiết kế.

### Móng đơn

| Vị trí | Nội dung |
|---|---|
| Trên trái | Mặt bằng thép dưới, tim cột/trục tham chiếu, ký hiệu A–A/B–B |
| Trên phải | Mặt bằng thép trên nếu có; không tạo hình thép trên rỗng |
| Dưới trái | Mặt cắt A–A theo trục local X, qua tim cột hoặc vị trí xác nhận |
| Dưới phải | Mặt cắt B–B theo trục local Y, thể hiện phương vuông góc |
| Vùng bổ sung | Chi tiết neo/uốn cần thiết, thống kê thép và ghi chú |

Nếu thiếu thép trên, dùng preset ba view và tính lại bố cục; không tự thay bằng một view khác mà không báo. Nếu cần mặt bằng định vị độc lập, người dùng bật rõ trong danh sách đầu ra.

### Đài cọc

| Vị trí | Nội dung |
|---|---|
| Trên | Mặt bằng bố trí cọc/tim cột, kích thước tim cọc–mép đài; tách khỏi mặt bằng thép khi chật |
| Giữa | Mặt bằng thép dưới và trên, cùng hướng và tỷ lệ nếu phù hợp |
| Dưới | A–A/B–B qua hàng cọc hoặc tim cột đã chọn; thêm C–C khi hai mặt cắt chưa đủ |
| Vùng bổ sung | Chi tiết đầu cọc/neo do dự án cung cấp, thống kê thép đài, ghi chú |

Bộ nhiều view được chia sheet theo nhóm logic: sheet định vị/mặt bằng và sheet mặt cắt/chi tiết/thống kê. Không ép mọi nội dung vào một sheet bằng cách tự giảm tỷ lệ. View 3D minh họa là tùy chọn và không thay mặt cắt.

## 3. Cách xác định và lấy view

### Hệ trục và dữ liệu đầu vào

1. Lấy hình học host và FamilyInstance.GetTransform; kiểm tra local X/Y, mirror và hướng Z đứng. LocationPoint.Rotation chỉ là thông tin đối chiếu.
2. Tính biên hình học trong hệ local; không dùng tâm bounding box toàn cục làm tim cột/cọc. Với family có origin lệch, người dùng chọn tim hoặc offset từ hình học xác nhận.
3. Nhận diện cọc bằng quan hệ dữ liệu nếu có; nếu không, tìm ứng viên trên document theo vùng hình học và cao độ, rồi hiển thị để người dùng xác nhận. Không giới hạn tìm kiếm vào active view và không quyết định chỉ từ tên family.
4. Xác định hướng mặt bằng theo trục host hoặc giữ hướng project theo lựa chọn. Hiển thị mũi tên X/Y và chiều nhìn trước khi tạo.

### Mặt bằng

- Hai chế độ rõ ràng: callout từ mặt bằng nguồn được chọn; hoặc Structural Plan độc lập theo level thích hợp. Không âm thầm đổi chế độ khi active view không hợp lệ.
- Callout chỉ tạo khi parent/view type tương thích API; kiểm tra plan trả về, hướng và level. Mặt bằng độc lập không mang ký hiệu liên kết callout như khi tạo thật từ parent.
- Thép trên/dưới đều là nhìn từ trên xuống. Tên 'thép dưới' không đồng nghĩa đổi thành nhìn từ dưới lên.
- Phân nhóm thép ưu tiên dữ liệu lớp do lệnh tạo thép cung cấp nếu có; tiếp theo xét centerline, hướng và vùng cao độ. Thanh chờ/uốn xuyên lớp được phân loại riêng hoặc yêu cầu xác nhận; không chỉ dùng tâm bounding box.
- View range theo hình học và nội dung cần thể hiện, bảo đảm quan hệ top/cut/bottom/depth hợp lệ theo level. Cho xem trước và điều chỉnh; không cố định khoảng 150 mm cho mọi cấu kiện.
- Kiểm soát visibility trong view mới bằng filter/hide theo nhóm được xác nhận; giữ thép thủ công thuộc host nếu đúng phạm vi. Kiểm tra thuộc tính hiển thị Rebar phù hợp API 2024; không giả định đặt Fine là đủ nhìn thấy thép.
- Crop bao cấu kiện, phần cột/cọc cần thể hiện và nội dung được chọn; annotation crop tính riêng theo kích thước trên giấy.

### Mặt cắt

- A–A là mặt phẳng chứa local X và Z; chiều nhìn theo ±local Y. B–B chứa local Y và Z; chiều nhìn theo ±local X. UI phải phân biệt hướng tuyến cắt với chiều nhìn.
- Vị trí mặc định đề xuất qua tim cột đã nhận diện; đài cọc cho chọn hàng cọc hoặc pick hai điểm xác định tuyến cắt, offset và đảo chiều nhìn.
- Với đài 3 cọc hoặc bố trí lệch, thêm mặt cắt qua hàng cọc thực tế; không buộc hai đường qua tâm đài phải thể hiện đủ cọc.
- Dựng BoundingBoxXYZ trong hệ section: BasisY hướng lên, các basis vuông góc/đơn vị; kiểm tra quy ước Revit 2024 và chiều nhìn bằng mô hình thử. Min/Max trong tọa độ local của box, không trộn với world coordinates.
- Near/far depth là thông số riêng theo nội dung cần nhìn, không mặc định xuyên toàn móng. Bao chiều cao cột chờ/đầu cọc cần thể hiện; không kéo dài toàn cọc xuống đất.
- Tạo bằng ViewSection.CreateSection. Nếu cần ký hiệu mặt cắt xuất hiện trên mặt bằng hồ sơ khác, kiểm tra visibility/range của marker; dùng reference section/callout qua API tương thích khi phù hợp. Không vẽ đường và chữ giả như ký hiệu có liên kết.

## 4. Quy tắc lên sheet

1. Chọn title block và vùng trình bày; xác định lề, vùng khung tên, revisions, ghi chú. Sheet.Outline chỉ là khổ tổng thể, không đủ để suy vùng trống. Cho cấu hình vùng sử dụng theo từng title block.
2. Preset khổ giấy chọn được; đề xuất A1 ngang nhưng không coi mọi dự án có sẵn family A1. Cho cấu hình tỷ lệ; ban đầu đề xuất 1:20 cho plan/section và 1:10 cho chi tiết, phải xem trước theo kích thước thực.
3. Dùng đơn vị mm trên giấy cho khoảng hở, lề và vùng title; chuyển sang feet tại biên Revit API. Không nhân nhầm khoảng cách trên giấy với Scale.
4. Tạo view/annotation và Regenerate trước khi đo. Với viewport đo GetBoxOutline và GetLabelOutline; schedule đo bounding box của ScheduleSheetInstance sau khi tạo.
5. Xếp theo vai trò, căn thẳng tâm/trục hình khi phù hợp, dành khoảng cho dimension/leader/title; kiểm tra với khung tên, schedule và các viewport có sẵn.
6. Mặc định giữ tỷ lệ được chọn. Nếu không vừa: báo view/vùng vượt, đề xuất khổ lớn hơn, tách sheet hoặc tỷ lệ khác; chỉ áp dụng tỷ lệ khác khi người dùng chọn rõ.
7. Sheet có sẵn: chọn vùng trống hoặc xem preview vị trí, không di chuyển nội dung cũ. Kiểm tra Viewport.CanAddViewToSheet trước khi đặt; không lấy view đang đặt nơi khác rồi xóa placement cũ.
8. Đánh số chi tiết đồng bộ với ký hiệu A–A/B–B và liên kết sheet; tên dễ đọc theo Mark/Level/Role, còn nhận diện đối tượng bằng metadata.

## 5. Annotation và thống kê

- Dimension dùng reference thực của host/cọc/cột; nhóm chuỗi: tổng thể, tim và khoảng cách mép. Thiếu reference thì cảnh báo rõ, không thay bằng chữ giả kích thước.
- Cao độ lấy từ tham chiếu mô hình và hệ cao độ được chọn; không suy từ nội dung tên level.
- Tag chọn thanh/nhóm đại diện, tách hai phía view và kiểm tra leader/text chồng nhau. Không lặp tag từng thanh giống nhau khiến bản vẽ rối.
- Thống kê dùng dữ liệu Rebar thực và lọc theo host/nhóm xác nhận. Cần chứng minh filter không lẫn host trùng Mark; không sửa Mark/Partition người dùng chỉ để lọc schedule.
- Bảng gia công dạng thanh chỉ bổ sung khi kiểm tra được shape, hook, số lượng và chiều dài thực. Ghi chú tự do và ký hiệu không thay dữ liệu liên kết mô hình.

## 6. UI và cập nhật an toàn

Luồng: chọn móng → xác nhận cọc/cột liên quan → chọn bộ view → chỉnh tuyến cắt/chiều nhìn → chọn template/tỷ lệ → preview bố cục → tạo → báo cáo.

Preview hiển thị MB và tuyến cắt, vai trò view, tỷ lệ, sheet, vùng chiếm dụng và cảnh báo. Đây là xem trước bố cục, chưa thay thế kiểm tra kết quả đồ họa Revit. Nếu cần tạo thử để đo, dùng transaction có rollback và hủy sạch khi Cancel; không gọi Revit API từ thread nền.

Bổ sung service mới cho ViewPlanSpec, SectionSpec, SheetLayout và validation; tên cuối cùng theo quy ước repo. Giữ entry point Ribbon, schema đã có và profile cũ. Cấu hình mới phải có mặc định tương thích; bộ bố cục mới được chọn rõ, không tự áp lên hồ sơ cũ.

Cập nhật mặc định chỉ phần được chọn. Giữ phần người dùng chỉnh tay; thêm metadata ownership cho annotation/viewport mới trước khi hỗ trợ tái tạo. Không xóa dựa vào tên view hoặc Mark; không đánh dấu toàn sheet dùng chung là sở hữu riêng của một host.

## 7. Thứ tự thực hiện và nghiệm thu

1. Chốt đặc tả view/hướng cắt, test logic biến đổi và bố cục độc lập Revit.
2. Tạo plan/section trên bản sao mô hình Revit 2024; đối chiếu tuyến cắt–chiều nhìn–nội dung nhìn thấy trước khi làm sheet.
3. Triển khai layout giữ tỷ lệ, đo extents và phát hiện chồng lấn; thử khổ nhỏ, title block khác và sheet có nội dung.
4. Hoàn thiện annotation/schedule, cập nhật và export PDF/DWG.
5. Chạy build Windows với API 2024; kiểm thử Revit thực tế, so sánh số lượng/vị trí Rebar và các đối tượng ngoài phạm vi trước/sau.

Ca kiểm tra: móng chữ nhật/xoay/mirror/origin lệch; đài 2/3/4 cọc và cọc ẩn trong active view; nhiều lớp thép và thanh chờ; template khóa scale/range/ẩn thép; view không tương thích callout; sheet chật/trùng số/đã có view; thiếu thép trên; Cancel, Undo, lỗi từng host và mở lại profile cũ.

Đạt khi: view đúng mặt phẳng và chiều nhìn, đủ nội dung thiết kế, số liệu/tag đúng đối tượng, không chồng hình/chữ/khung tên, tỷ lệ khớp lựa chọn, không sửa thép hoặc hồ sơ cũ ngoài phạm vi. Build đạt chưa xác nhận các tiêu chí đồ họa. PR này chỉ thêm kế hoạch; chưa triển khai hoặc chạy Revit.
