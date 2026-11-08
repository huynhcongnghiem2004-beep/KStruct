# Kế hoạch hoàn thiện hồ sơ thép móng — Revit 2024

## Mục tiêu và hiện trạng đã đối chiếu

Mở rộng KStruct hiện có để hoàn thiện hồ sơ móng đơn và đài cọc; không khởi tạo lại add-in. KStruct.csproj đang dùng net48, WPF, x64, tham chiếu RevitAPI/RevitAPIUI từ RevitInstallDir mặc định Revit 2024.

Đã đọc App.cs, KStruct.csproj, các command/builder/storage/profile dưới đây và tài liệu DRAWING_V3.md, DRAWING_STACK_LAYOUT.md, thepmong/trienkhaithepmong.md. Tài liệu kế hoạch cũ có mô tả trước khi các lệnh mới xuất hiện; mã nguồn và đăng ký Ribbon hiện tại là căn cứ đối chiếu. Việc có mã nguồn không chứng minh lệnh đã được nghiệm thu trên mô hình.

| Nút/nhóm hiện hữu | Entry point hiện đang đăng ký | Cách xử lý khi mở rộng |
|---|---|---|
| Móng dưới cột | DrawFootingsCommand trong App.cs | Giữ lệnh đặt family |
| Thép móng cọc & đơn | SDK_HD.KStruct.FootingRebar.FootingRebarCommand | Giữ luồng tạo thép độc lập với xuất hồ sơ |
| Tạo hồ sơ móng cọc, đơn | NativeDocumentation.FootingDrawingCommand | Mở rộng có kiểm soát; không thêm nút trùng |
| Cập nhật hồ sơ móng cọc, đơn | NativeDocumentation.UpdateFootingDrawingsCommand | Giữ nhận diện metadata và fallback hồ sơ cũ |
| Xóa hồ sơ móng đã tạo | NativeDocumentation.DeleteFootingDrawingsCommand | Kiểm tra phạm vi xóa trước mọi thay đổi metadata |
| Thép móng / đài cọc | NativeRebar.ReinforceIsolatedFootingsCommand | Lệnh riêng, không chuyển hướng đăng ký |
| Bản vẽ móng băng | CreateStripFootingDrawingsCommand / UpdateStripFootingDrawingsCommand | Giữ hành vi hiện tại |

NativeDocumentation còn có CreateIsolatedFootingDrawingSheetsCommand và UpdateIsolatedFootingDrawingSheetsCommand cũ; không nhầm chúng với entry point đang dùng trên Ribbon.

## Các thành phần tái sử dụng và giới hạn đã thấy

- thepmong/FootingDrawingBuilder.cs: callout mặt bằng thép trên/dưới, hai mặt cắt, dimension, tag, sheet, viewport và cập nhật crop; lấy Rebar theo GetHostId.
- FootingDrawingCommand: Transaction toàn lệnh và SubTransaction theo móng; báo lỗi từng móng. Giữ chính sách này trừ khi thay đổi có lý do cụ thể.
- FootingDrawingStorage: schema 5798295B-9EF3-41CE-9A09-1A36563FCD3B; HostUniqueId, Role, Version, CropMarginMm. Không đổi GUID hoặc định nghĩa trường của schema đã phát hành.
- FootingDrawingProfileStore: cấu hình XML người dùng trong AppData/KStruct/FootingDrawingProfiles.xml. Các trường mới phải có giá trị mặc định tương thích; đọc được file cũ và không tự ghi đè preset.
- UpdateFootingDrawingsCommand cập nhật crop/view range liên quan; chưa được coi là tái tạo đầy đủ annotation hoặc bảng thống kê.
- DeleteFootingDrawingsCommand nhận diện sheet/view bằng metadata và tên legacy. Schedule hoặc đầu ra mới cần quy tắc sở hữu riêng và kiểm tra với cơ chế này.
- CI hiện tại chỉ kiểm tra tài liệu và whitespace; phần mô tả chưa có ứng dụng đã lỗi thời. Chưa có build Revit trong workflow.

## Kế hoạch thực hiện theo PR

### PR 1 — Chốt baseline và build

1. Ghi SHA main trước thay đổi, lập danh sách Ribbon ID → command và các tệp dùng chung.
2. Kiểm tra build baseline trên Windows với .NET Framework 4.8 và Revit API 2024 trước khi đánh giá lỗi mới.
3. Thêm workflow build riêng; giữ nguyên project chính và workflow kiểm tra hiện có trong bước đầu. Dùng runner Windows có bộ API 2024 hợp lệ hoặc nguồn reference được xác minh; không mặc định runner GitHub có Revit.
4. Lưu log build và DLL artifact, ghi commit SHA; không tự cài/ghi đè DLL vào máy đang dùng.
5. Phân biệt lỗi baseline với lỗi do PR. Nếu chưa có runner/reference phù hợp, báo build bị chặn; không dùng kiểm tra tài liệu để kết luận build đạt.

### PR 2 — Kiểm tra đầu vào và xem trước hồ sơ

1. Rà soát đầy đủ UI/settings/builder; nhận diện rõ loại móng từ dữ liệu xác nhận, hình học hỗ trợ và Rebar thuộc host.
2. Hiển thị các đầu ra dự kiến, view/template, tỷ lệ, title block, tag, dimension và sheet đích trước khi tạo.
3. Kiểm tra thiếu family/type, template khóa tỷ lệ/ẩn thép, số sheet trùng, móng xoay và khổ giấy không đủ; hành vi phải rõ và có kiểm tra thực tế.
4. Mặc định không sửa thép, family, template, style, sheet hay annotation ngoài đầu ra được người dùng chọn.

### PR 3 — Hoàn thiện hồ sơ móng đơn

1. Hoàn thiện mặt bằng trên/dưới, mặt cắt theo trục local host và annotation liên kết mô hình.
2. Bổ sung thống kê theo đúng host, lấy số lượng/chiều dài từ Rebar và parameter thực; không suy chiều dài gia công chỉ từ bounding box.
3. Dùng service mới nếu cần, không refactor đồng loạt module tạo thép/cột/móng băng.
4. Khi tạo lại: cảnh báo đầu ra đã có và yêu cầu chọn tạo mới/cập nhật; không tự xóa hồ sơ cũ.

### PR 4 — Hồ sơ đài cọc

1. Xác định tập cọc thuộc đài; xem trước và cho xác nhận khi việc gán theo hình học không rõ.
2. Bổ sung tim cọc, cao độ, mặt cắt qua vị trí cần thể hiện và thống kê thép đài theo cấu hình dự án.
3. Không thay đổi hình học/cốt thép cọc hoặc coi thép đài là thép lồng cọc.
4. Thử đài 2/3/4 cọc và đài xoay; chỉ công bố hỗ trợ những dạng đã kiểm tra.

### PR 5 — Cập nhật, xuất và kiểm tra hồi quy

1. Xác định rõ cập nhật crop, annotation và schedule; giữ tương thích metadata/profile cũ và phần người dùng chỉnh tay.
2. Export PDF/DWG qua API Revit 2024 theo sheet được chọn, kiểm tra đường dẫn và ghi đè tệp.
3. Kiểm tra tạo → cập nhật → xuất → xóa trên bản sao mô hình; kiểm tra Undo/Cancel và lỗi giữa chừng.

## Ma trận kiểm tra bảo vệ lệnh hiện có

| Nhóm | Ca kiểm tra bắt buộc |
|---|---|
| Ribbon | ID/command cũ giữ nguyên, không trùng nút, add-in nạp được |
| Tạo thép móng | Số lượng, host, vị trí trước/sau lệnh hồ sơ không bị thay đổi |
| Hồ sơ hiện tại | Tạo mới trên sheet mới/có sẵn, trên/dưới, mặt cắt, tag/dimension, khổ giấy |
| Tương thích | Profile XML cũ, metadata cũ, hồ sơ legacy, tên view đổi bằng tay |
| Cập nhật/xóa | Đúng host/đầu ra, không xóa view/sheet thủ công hoặc hồ sơ khác; kiểm tra sheet chứa nội dung hỗn hợp |
| Nhóm khác | Smoke test lệnh cột, móng băng và NativeRebar liên quan sau khi thay thành phần chung |
| Lỗi/hoàn tác | Cancel, Undo, thiếu type, template sai, nhiều host có một host lỗi, rollback sạch |
| Xuất | PDF/DWG đúng sheet, tỷ lệ, annotation và thống kê; kiểm tra trực quan trong Revit 2024 |

Trước/sau mỗi ca ghi số lượng Rebar, ElementId/UniqueId host và đầu ra liên quan; so sánh dữ liệu ngoài phạm vi. Với kiểm tra thực tế, ghi phiên bản Revit, mô hình thử, kết quả và ảnh/log. Không tuyên bố không ảnh hưởng chỉ dựa vào build.

## Trạng thái bàn giao kế hoạch

PR kế hoạch chỉ thêm tài liệu này. Chưa sửa App.cs, command, project, schema, preset hoặc workflow. Chưa chạy build ứng dụng hay kiểm thử trực tiếp trong Revit. Không tự merge hoặc triển khai. Bản vẽ mẫu và mô hình thử sẽ dùng để đối chiếu thể hiện trong các bước triển khai; không cản việc khảo sát và chuẩn bị build.
