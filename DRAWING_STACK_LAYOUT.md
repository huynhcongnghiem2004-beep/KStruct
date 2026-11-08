# Bố cục triển khai theo tuyến cột

Bản mới: một sheet toàn tuyến. Số sheet lấy đúng giá trị nhập, không thêm hậu tố theo tầng.
- Bên trái: một mặt cắt dọc bao toàn tuyến và các đoạn thép chờ.
- Bên phải: một mặt cắt ngang giữa mỗi cột, xếp từ tầng cao xuống thấp; số chi tiết 2,3,...
- Mỗi mặt cắt ngang có bảng thép chủ, đai đầu/chân và đai giữa khi bật Hiện giá trị.
- Mặt cắt dọc có phân vùng đai theo chiều dài vùng đầu, bước hoặc số đai đã nhập.
- Các kích thước hình học cột ưu tiên reference family, thiếu thì tìm mặt phẳng geometry thực.
- Kích thước phân vùng đai tham chiếu đường phụ trợ trong view, không tự cập nhật theo mô hình;
  bảng thép và ghi chú là dữ liệu tại thời điểm tạo, phải tạo lại sau khi đổi cấu hình/mô hình.
- Giữ View Template, tỷ lệ, loại Tag/Text/Dimension, Title Block và Break Line từ cấu hình Triển khai.
- Không tự đổi tỷ lệ để ép vừa sheet. Nếu thiếu chỗ, thao tác rollback và yêu cầu chỉnh khổ/tỷ lệ.
- Hình tham khảo dùng định hướng bố cục; không sao chép số liệu hoặc nhãn thiết kế từ ảnh.

Chưa hoàn thành toàn bộ chi tiết của ảnh: tự động kéo mọi hình dạng thanh dọc thành bảng gia công,
đánh số thanh/bảng thống kê chế tạo, và split-crop mặt cắt dọc theo break line.
Cần kiểm tra trên bản sao Project1.rvt về family reference, tag/Dimension, bảng thép, vùng đai và bố trí sheet.
Build thành công không xác nhận kết quả dựng bản vẽ thực tế trong Revit.
