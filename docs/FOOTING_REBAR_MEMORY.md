# Nhớ thông số và lần tạo thép móng cọc / móng đơn

## Luồng dùng

Áp dụng cho nút **Thép móng cọc & đơn** → `SDK_HD.KStruct.FootingRebar.FootingRebarCommand`; không thay lệnh NativeRebar khác hoặc lệnh hồ sơ móng.

- Chọn lại cùng móng: ưu tiên nạp thông số của lần tạo thành công lưu trên chính host trong mô hình.
- Chọn móng mới: nạp thông số gần nhất của cùng loại móng trong project khi tùy chọn **Nhớ thông số cho móng kế tiếp cùng loại** bật. Móng đơn/đài cọc lưu mặc định riêng.
- Nhớ cover, đường kính/bước hai phương đáy, bật/tắt các lớp, móc/chiều dài neo, thép trên và đai hông; không nhớ kích thước hoặc vị trí hình học của móng cũ.
- Có nút **Nạp lại thông số đã lưu** để phục hồi cấu hình đang mở mà chưa thay đổi mô hình.
- Thiếu đường kính trong project thì để trống và yêu cầu chọn/nạp lại; không lấy đường kính gần nhất. Tình trạng bê tông lót khác cấu hình cũ được báo để đối chiếu.
- Khi người dùng đổi loại móng trong UI, loại đã chọn được dùng nhất quán để lập kế hoạch, kiểm tra và ghi nhớ.

Chỉ lưu sau khi tạo thành công: Cancel, từ chối cảnh báo, lỗi lập kế hoạch, hậu kiểm/commit lỗi không ghi cấu hình mới. Metadata trên host/Rebar và việc tạo/thay thép cùng transaction nên Undo/rollback đồng bộ trong model. File mặc định người dùng bên ngoài model không tự quay về khi Undo trong Revit.

## Nút Cập nhật thép móng

Trong **KStruct → Thép móng**, nút **Cập nhật thép móng** nằm ngay sau nút tạo thép. Entry point mới là `SDK_HD.KStruct.FootingRebar.UpdateFootingRebarCommand`, Ribbon ID `KStruct_UpdateFootingRebar`; nút tạo hiện có giữ nguyên ID/entry point.

1. Chọn một móng/đài hoặc một/nhiều nhóm thép KStruct thuộc cùng móng. Nếu chưa chọn, lệnh cho pick móng hoặc nhóm thép đã nhận diện.
2. Lệnh yêu cầu có cả bộ thép sở hữu và lịch sử cấu hình đúng host. Không có thì báo rõ và dừng, không chuyển sang tạo mới.
3. Mở cửa sổ **Cập nhật thép móng cọc, móng đơn**, tự nạp thông số host và bật cố định chế độ cập nhật. Có thể đổi đường kính, bước, cover, bật/tắt lớp thép và kiểu/chiều dài neo.
4. Chiều dài thân thanh/phạm vi phân bố được tính lại từ hình học móng hiện tại và thông số đã nhập; không sửa kích thước bê tông móng, không tự tính thiết kế kết cấu.
5. Bấm **Cập nhật thép móng** để thay đúng bộ được nhận diện trong một transaction có hậu kiểm. Các nhóm chưa nhận diện trên cùng host được giữ nguyên, không cần chọn tạo bổ sung trong chế độ cập nhật.
6. Các bảo vệ rollback/không xóa annotation liên quan bên dưới vẫn áp dụng. Chưa hỗ trợ sửa thép legacy không có metadata hoặc tự chuyển reference của tag/dimension sang Rebar mới.

Ca kiểm tra Revit bổ sung: chọn từ host hoặc Rebar, chọn nhiều Rebar cùng host, chọn nhiều host/lệnh khác, host chưa có bộ sở hữu, đổi kích thước host rồi cập nhật, đổi D/bước/neo, Cancel, Undo và cập nhật bộ có annotation. Build và kiểm tra thuật toán không thay thế các ca chạy Revit này.

## Nhận diện lần tạo trước và tránh trùng

Mỗi nhóm Rebar mới có ToolId, HostUniqueId, ElementUniqueId, RunId, tên nhóm và version. Kiểm tra cả host thực từ GetHostId. Rebar sao chép/rehost hoặc của lệnh khác không được tự nhận là thép sở hữu chỉ do còn metadata cũ.

Khi có nhóm thép của chính lệnh này trên host:

1. Cửa sổ thông báo số nhóm đã nhận diện.
2. Phải bật **Cập nhật ... nhóm thép KStruct của móng này** để tiếp tục; lệnh không tự thêm bộ trùng.
3. Xóa đúng nhóm có mã nhận diện, tạo lại và hậu kiểm trong cùng transaction. Nếu tạo/commit lỗi thì rollback giữ nguyên bộ cũ.
4. Nếu Revit xóa kéo theo bất kỳ đối tượng ngoài tập thép sở hữu (ví dụ tag/dimension), lệnh hủy và rollback để giữ hồ sơ. Phiên bản này chưa chuyển reference annotation sang các Rebar ID mới.

Thép thủ công, thép NativeRebar/lệnh khác và thép tạo từ phiên bản cũ chưa có metadata được giữ nguyên. Vì bản cũ chưa lưu cấu hình nên không thể khôi phục chính xác toàn bộ lựa chọn cũ hoặc nhận quyền sở hữu chỉ từ hình dạng/đường kính. Nếu có thép chưa nhận diện, người dùng phải chủ động chọn cho phép tạo cạnh bộ đó; mặc định không thêm thép để tránh trùng ngoài ý muốn. Việc nhận diện tự động đầy đủ áp dụng từ lần tạo bằng bản mới.

## Lưu trữ

- Host schema: `6C7CFAB8-0BA5-40A9-8DA9-2DD621AD684B`, chứa thông số XML, HostUniqueId, ToolId, RunId, Version và thời điểm UTC. Không sửa schema hồ sơ móng hiện có.
- Rebar schema: `EF7389B2-91F4-4A7A-BF29-56238C352550`, chứa mã sở hữu theo đúng element/host.
- Mặc định theo project/loại móng: `%APPDATA%/KStruct/FootingRebarSessions/<hash>.xml`. Hash gồm ProjectInformation.UniqueId, đường dẫn (hoặc tên project chưa lưu) và loại móng. Đổi đường dẫn Save As có thể bắt đầu một bộ mặc định người dùng mới; lịch sử trên host vẫn đi cùng mô hình.
- File ghi tạm rồi thay nguyên tử; file lỗi/phiên bản không hỗ trợ được báo, không ghi đè dữ liệu cũ khi chỉ mở cửa sổ. Tắt nhớ và tạo thành công sẽ xóa phần mặc định của loại móng đó; lịch sử trên host vẫn giữ để nhận diện/cập nhật.

## Kiểm tra

`dotnet run --project tests/FootingRebarMemory/FootingRebarMemory.csproj --configuration Release`

Kiểm tra round-trip cover/đường kính/bước/neo/lớp, đọc lại với kích thước host mới, phân tách project/loại móng, opt-out, file lỗi/phiên bản mới/NaN, XML không cho DTD, ownership của bản sao/rehost/legacy/lệnh khác. Workflow Revit 2024 chạy bộ này, bộ kiểm tra cấu hình hồ sơ cũ và build toàn add-in.

Cần kiểm tra trên bản sao mô hình Revit 2024:

1. Tạo lần đầu, mở lại đúng host, đổi kích thước host rồi mở lại; cấu hình được nạp nhưng hình học lấy lại từ model.
2. Chọn host mới cùng loại/khác loại và project khác; thử tắt nhớ, Cancel và tạo lỗi.
3. Cập nhật bộ đã nhận diện, đối chiếu số lượng nhóm không nhân đôi; tạo lỗi giữa chừng phải giữ bộ cũ và lịch sử.
4. Thêm Rebar thủ công/lệnh khác, copy/rehost nhóm sở hữu: kiểm tra bộ chưa nhận diện không bị xóa.
5. Gắn tag/dim vào nhóm sở hữu rồi cập nhật: lệnh phải hủy nếu delete kéo theo annotation, giữ nguyên mô hình.
6. Undo/Redo, lưu/đóng/mở RVT, worksharing/host không có quyền sửa, đường kính bị xóa hoặc metadata/file lỗi.

Các kiểm tra thuần và build không thay thế việc chạy Revit; chức năng không tự xác nhận hồ sơ thiết kế kết cấu.
