# Lỗi Rebar Hook Type 135°

`V3RebarBuilder.Build` hiện thu thập tất cả `RebarHookType` có `HookAngle` gần 3π/4 trong project và yêu cầu đúng một loại khi dùng đai móc hoặc đai phụ. Cả 0 loại và nhiều hơn 1 loại đều phát cùng thông báo. Ảnh lỗi không đủ xác định project thuộc trường hợp nào. Đai phụ luân phiên còn yêu cầu một loại 90°.

Phương án sửa đề xuất: thêm lựa chọn Hook Type 135° cho đai và Hook Type 90° cho đai phụ luân phiên trong cấu hình; chỉ tự chọn khi duy nhất một loại tương thích. Nếu không có, báo thiếu loại cụ thể để người dùng tạo/nạp cấu hình phù hợp. Nếu có nhiều loại, hiện tên và ID để chọn, không xóa loại khác hay lấy phần tử đầu danh sách. Lưu lựa chọn cùng cấu hình, kiểm tra lại khi mở project hoặc loại đã bị xóa.

Preflight cần phân biệt 0/1/nhiều loại và xác minh loại được chọn đúng góc, tương thích kiểu đai/đường kính. Trước khi ghi đè thép, báo rõ các tuyến bị ảnh hưởng. Kiểm tra riêng 0 loại, 1 loại, nhiều loại, loại đã xóa, góc sai, đai kín không cần móc và đai phụ luân phiên cần cả 135°/90°. Sau đó build và thử thực tế trong Revit.

Đây là phân tích và phương án, chưa thay đổi logic chọn Hook Type. Không thể bỏ kiểm tra rồi truyền null vì có thể làm mất móc hoặc Revit từ chối tạo đai.
