# KStruct

## Preview thép cột và pyRevit

Giao diện cột cập nhật preview sau 300 ms dừng gõ, giữ con trỏ nhập và cấu hình hợp lệ gần nhất khi số đang nhập dở. Cỡ chữ chung là 13 px, nhãn preview tối thiểu 12 px. Preview là hình minh họa; không thay thế kiểm tra Rebar trong mô hình.

Chạy `powershell -NoProfile -File tools/Install-KStructPyRevitDev.ps1` để build DLL phát triển riêng và cài extension vào thư mục người dùng. Có thể truyền `-ExtensionsDirectory` nếu đã cấu hình đường dẫn extension khác trong pyRevit. Trong Revit 2024: **pyRevit → Reload → KStruct Dev → Preview thép cột**, chọn một cột chữ nhật. Cửa sổ này không cho tạo thép và không lưu cấu hình lần dùng cuối; DLL KStruct đang dùng chính thức không bị thay thế. Sau các lần build tiếp theo, có thể cần khởi động lại Revit do WPF giữ assembly đã nạp.

Nếu Reload báo thiếu `System.Runtime, Version=8.0.0.0` trong `pyrevit/framework.py`, chưa thể xác nhận extension hoạt động. Đây là lỗi nạp assembly của phiên pyRevit cần chẩn đoán; không chép DLL .NET 8 vào Revit 2024 để chữa lỗi.

Kiểm tra WPF thật trên máy có Revit 2024 (PowerShell):

```powershell
dotnet build tests/ColumnRebarUi/ColumnRebarUi.csproj -c Debug
$env:PATH = 'C:\Program Files\Autodesk\Revit 2024;' + $env:PATH
& tests/ColumnRebarUi/bin/Debug/net48/ColumnRebarUi.exe
python tests/check_column_v4_ui.py
dotnet run --project tests/ColumnRebarLayout/ColumnRebarLayout.csproj -c Release
```

Bộ thử WPF dùng dữ liệu mẫu, kiểm tra nhập trực tiếp, giữ con trỏ, dữ liệu sai, số thanh, bước đai và xuất ảnh vào `column-ui-previews/`. Cần desktop Windows tương tác cho kiểm tra focus. CI build bộ thử này và kiểm tra XAML; không chạy nó trên hosted runner vì thiếu thư viện native của Revit. Chưa kiểm tra giao dịch tạo thép bằng bộ thử WPF.

Add-in C# cho Revit 2024, .NET Framework 4.8, WPF, x64. Mã nguồn gồm các lệnh mô hình, cốt thép và hồ sơ bản vẽ kết cấu.

## Quy trình phát triển

1. Người dùng mô tả tính năng hoặc lỗi cần xử lý.
2. Codex đọc mã nguồn, đề xuất phương án và thực hiện thay đổi trên nhánh riêng.
3. Mở pull request để GitHub Actions chạy các kiểm tra đã cấu hình.
4. Trong phiên làm việc, Codex kiểm tra kết quả cho đúng commit, đọc log và sửa lỗi nếu cần.
5. Báo cáo thay đổi, kết quả kiểm tra và hạn chế; người dùng quyết định merge.

## GitHub Actions

Workflow `Repository checks` chạy khi push, mở/cập nhật pull request hoặc chạy thủ công. Workflow chỉ kiểm tra các tệp nền và lỗi khoảng trắng trong thay đổi. Đây chưa phải build/test ứng dụng.

Workflow `Revit 2024 build and preferences` chạy trên Windows, kiểm tra lưu lựa chọn/preset cũ và build toàn bộ add-in với compile references Revit 2024 được cố định phiên bản/checksum. Artifact gồm `KStruct.dll` và log build; workflow không chạy Revit hoặc tự cài DLL.

Kiểm tra độc lập: `dotnet run --project tests/FootingPreferences/FootingPreferences.csproj --configuration Release`.

Giao diện hồ sơ móng, chức năng nhớ lựa chọn và checklist kiểm tra Revit: [hướng dẫn](docs/FOUNDATION_DIALOG_IMPLEMENTATION.md). Build thành công chưa xác nhận hình học/đồ họa trong Revit; thử trên bản sao mô hình trước khi phát hành.

Nhớ thông số lệnh thép móng và nhận diện lần tạo trước: [hướng dẫn](docs/FOOTING_REBAR_MEMORY.md).

Phân vùng đai, nhóm thép, ghi đè và hồ sơ cột V3: [hướng dẫn và checklist Revit](DRAWING_V3.md).

Neo chân L Fixed Number, hướng chân neo và view 3D kiểm tra: [hướng dẫn](docs/COLUMN_L_ANCHOR.md).

Callout, view range cốt thép và Breakline dầm: [hướng dẫn](docs/FOUNDATION_CALLOUTS_VISIBILITY_BREAKLINES.md).

Thép chờ qua khoảng hở dầm/sàn và nhấn cổ chai: [hướng dẫn](docs/COLUMN_BOTTLENECK.md).

## Codex kiểm tra lại

GitHub Actions tự chạy theo sự kiện đã cấu hình. Workflow này không tự gọi Codex và không cần API key. Codex đọc kết quả và xử lý lỗi trong phiên làm việc được người dùng giao; chưa có dịch vụ Codex chạy nền sau khi phiên kết thúc.

Cột/dầm chỉ dùng Rebar Shape và Hook có sẵn: [quy tắc và kiểm tra](docs/EXISTING_PROJECT_REBAR.md).
