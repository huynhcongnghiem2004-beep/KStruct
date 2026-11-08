# Giao diện hồ sơ móng — Revit 2024

## Phạm vi bản sửa

- Giữ nguyên Ribbon ID và các entry point tạo thép/cột/móng băng. Chỉ thay giao diện lệnh hồ sơ móng và thêm helper dùng bởi lệnh này.
- Giao diện ba cột: danh sách móng có checkbox, tab View/Dim–Tag/Sheet/Cấu hình, preview mặt bằng và bố cục sheet.
- Preview là sơ đồ từ kích thước hình học host và đường tâm Rebar có thể đọc được (giới hạn để giữ UI phản hồi), không phải ảnh render Revit và chưa phản ánh đầy đủ hình thép/annotation. Không dùng số liệu thép giả trong preview.
- Chọn lại mô hình đóng dialog trước khi gọi Selection API. Danh sách được tích chọn được truyền đúng sang command.
- Tạo plan độc lập hoặc callout theo view nguồn được chọn; chọn tỷ lệ, template, lớp trên/dưới, offset tuyến cắt, chiều sâu nhìn và đảo chiều.
- Model crop mặc định 6 mm; biên chữ nhật thực trong hệ trục family được kiểm tra trước khi offset. Hình không chữ nhật/nghiêng chưa được hỗ trợ và báo lỗi; không thay bằng world bounding box rồi coi là crop chính xác.
- Tạo dim tổng thể hai phía theo mỗi phương; lớp chi tiết dùng reference cột/cọc hợp lệ. Nếu không đọc được tim có thể dùng mép thực và báo rõ; nếu thiếu reference thì báo lớp/cạnh thiếu, không tạo dim giả.
- Annotation crop tách khỏi model crop. Tỷ lệ sheet được giữ; đo cả viewport/label và chặn không vừa/chồng nội dung hiện có. Lề/vùng chừa khung tên được nhập theo mm giấy.

## Nhớ lựa chọn

Tab Cấu hình có `Nhớ lựa chọn cho lần tạo hồ sơ kế tiếp`, mặc định bật. Khi bấm Tạo hồ sơ với cấu hình hợp lệ, lưu các lựa chọn cho đúng project; việc lưu xảy ra trước transaction tạo hồ sơ, nên vẫn nhớ khi model tạo lỗi. Hủy không lưu. Khi bỏ chọn và bấm tạo, xóa cấu hình tự nhớ cũ cho project này. Không lưu lại danh sách host đã chọn.

Lưu tại `%APPDATA%/KStruct/FootingDrawingSessions/<SHA256 project-key>.xml`, với project-key gồm UniqueId của ProjectInformation và đường dẫn (hoặc tên khi chưa lưu); riêng với file preset có tên `FootingDrawingProfiles.xml`. Dùng temporary file và atomic replace. Lỗi đọc/ghi có thông báo và không chặn dùng cấu hình mới. View/sheet/type được đối chiếu với document hiện tại; thiếu lựa chọn thì yêu cầu chọn lại, không tạo đối tượng thay thế ngầm.

Schema mô hình giữ nguyên GUID và các trường; Version=2 đánh dấu đầu ra từ bố cục mới, Version=1 vẫn dùng nhánh crop cập nhật cũ. Không tự cập nhật sheet/view cũ khi mở cửa sổ.

## Kiểm tra

- `dotnet run --project tests/FootingPreferences/FootingPreferences.csproj --configuration Release`: round-trip lựa chọn, phân tách project, opt-out, preset XML cũ, file lỗi và atomic replace.
- Workflow `Revit 2024 build and preferences`: Windows, .NET 8 SDK build net48 với compile references Revit 2024.0.0 từ package NuGet được cố định phiên bản và kiểm tra SHA256. Không cài Revit hoặc chạy mô hình; artifact chỉ gồm KStruct.dll và log build.
- Build ở máy có Revit vẫn dùng RevitInstallDir hiện tại, không đổi project framework/API mặc định.

## Kiểm tra bắt buộc trong Revit trước khi phát hành

1. Mở lệnh, chọn/loại móng bằng checkbox, chọn lại từ mô hình, resize cửa sổ/DPI và kiểm tra các tab.
2. Chọn chỉ thép dưới rồi tạo: không xuất thêm plan thép trên. Tắt callout và giữ plan: vẫn tạo plan độc lập.
3. Móng chữ nhật xoay/mirror/origin lệch: crop 6 mm, A–A/B–B đúng offset/chiều nhìn; profile cũ giữ giá trị crop đã lưu.
4. Đủ/thiếu family reference cột/cọc: dim được liên kết thật, cảnh báo thiếu đúng; không thể coi build đạt là đã có đủ tám chuỗi dim trên mọi family.
5. Sheet nhỏ, template khóa tỷ lệ/view range, sheet có nội dung, số sheet trùng: báo lỗi/rollback sạch, giữ tỷ lệ và nội dung cũ.
6. Lưu lựa chọn, mở lại cùng project, mở project khác, xóa view/type đã lưu, tắt tự nhớ, file XML lỗi, Cancel và Undo.
7. Kiểm tra update/delete hồ sơ cũ và mới; so sánh Rebar và các đối tượng ngoài phạm vi. Xuất PDF/DWG qua lệnh Revit hiện có để kiểm tra dim/tag không bị cắt.

Chưa bổ sung chức năng tự tạo bảng gia công thép hoặc export mới trong PR này. Không có môi trường Revit chạy trực tiếp trong phiên cloud; các kiểm tra mô hình/đồ họa trên cần thực hiện trên bản sao project trước khi dùng chính thức.
