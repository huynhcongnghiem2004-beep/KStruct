# Triển khai bản vẽ V3

Bật 'Cài đặt triển khai bản vẽ cột' trong tab Triển khai. Chọn các loại từ project rồi nhấn 'Tạo thép / Bản vẽ'.
Các lựa chọn hiện tại được đọc trực tiếp khi chạy; nút Lưu giữ cấu hình cho các lần mở sau.

- Một cột một sheet: một mặt cắt ngang giữa chiều cao, một mặt cắt dọc qua tâm theo trục X cột.
- Nhiều cột: số sheet gốc thêm -01, -02...; trùng số bị chặn, không xóa sheet có sẵn.
- Title Block, View Family Type, View Template, Scale được dùng trực tiếp.
- Template khóa scale khác giá trị nhập hoặc ẩn Structural Rebar sẽ bị chặn.
- Dimension Type tạo kích thước liên kết reference family: ngang có rộng/sâu; dọc có rộng/cao.
  Family cần reference Left/Right, Front/Back, Bottom/Top tương ứng. Không thay bằng chữ giả kích thước.
- Show Tag tạo IndependentTag gắn subelement thép và đúng Tag Type đã chọn.
- Show Value tạo TextNote theo Text Type đã chọn, gồm tầng, bố trí thép và chiều dài chờ.
  Đây là ghi chú riêng; nội dung label trong tag do family tag quyết định.
- Break Line đặt Detail Item đã chọn giữa chiều cao mặt cắt dọc, hỗ trợ family đặt theo điểm/đường.
  Ký hiệu này không tự rút ngắn hình học hay tạo split crop.
- Kéo thép lấy đường tâm một đai thực trong mô hình, tịnh tiến ra cạnh tiết diện ở đúng kích thước.
- Viewport được bố trí hai bên sheet; nếu không vừa hoặc chồng nhau thì rollback.
- Thép và bản vẽ nằm cùng transaction, Undo một lần. Nếu tạo lại thép đang có, cần bật ghi đè V3;
  chọn số sheet mới để giữ bản vẽ cũ.

Kiểm thử thủ công trong Revit cần làm: cột một tầng và tuyến nhiều tầng, cột xoay/mirror,
family đủ/thiếu reference, template khóa scale/ẩn thép, tag và break line của project,
khổ A0/A1 nhỏ hơn phạm vi hai view, số sheet trùng, Undo và ghi đè thép cũ.
Đã kiểm tra biên dịch API Revit 2024; chưa kiểm thử tạo bản vẽ trực tiếp trên mô hình người dùng.
