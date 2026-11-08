# Thép chờ qua khoảng hở và nhấn cổ chai

Lệnh Tạo thép cột trước đây bắt hai solid cột tiếp giáp trong 1 mm, nên chặn cả
khoảng hở dầm/sàn 400 mm. Luồng mới xét chiều dài thép thực thay vì điều kiện chạm solid.

## Hình học và thông số

- Chiều dài chờ L vẫn đo từ đỉnh cột dưới, không bị tự tăng khi phát hiện khoảng hở.
- Phần nằm trong cột trên = L - khoảng hở. Phần nối với thanh trên còn trừ cover và nửa
  đường kính đầu thanh trên, đồng thời xét vùng bo uốn thực khi dựng.
- Ví dụ khoảng hở 400 mm, d20, cover tầng trên 30 mm: L=1000 mm cho đoạn nối thẳng
  560 mm. L=600 mm chỉ còn 160 mm, không đủ giới hạn dưới 400 mm; giao diện báo cần
  ít nhất 840 mm. Đây chưa phải chiều dài nối tính toán cuối cùng.
- Khi có cặp cột cùng tiết diện/cấu hình, thanh dưới nhấn lệch vào lõi trước đỉnh cột,
  thanh trên giữ thẳng. Khi chưa có cột trên, thép chờ tự do giữ thẳng.
- Độ dịch tâm = đường kính thanh + khe hở; mặc định khe hở dựng hình 2 mm.
  Chiều cao đoạn nhấn = độ dịch tâm × n, với tỷ lệ xiên 1:n mặc định 1:6.
  Khe hở 2 mm và tỷ lệ 1:6 là giá trị dựng hình có thể chỉnh, không gán cho TCVN 5574.
- Hướng nhấn nằm trong cùng mặt phẳng thanh, kể cả thanh có neo L ở móng. Các set
  Fixed Number giữ mặt phẳng hợp lệ, không đổi số thanh và không tạo trùng góc.
- Preview thể hiện khoảng hở theo cao độ thực, đoạn nhấn và thanh đứng tầng trên.
  Preview là sơ đồ; hình uốn cuối lấy từ Rebar trong mô hình.

## Phạm vi kiểm tra tiêu chuẩn

Nguồn chính: [TCVN 5574:2018, mục 10.3.6.2 và 10.3.7](https://rds.com.vn/TCXD1/TCVN5574-2018.pdf).

- Gối uốn tối thiểu: thép có gân 5d khi d<20 mm, 8d khi d≥20 mm; thép trơn
  tương ứng 2,5d và 4d. Bộ dựng kiểm tra bán kính đường tâm Rebar thực theo gối uốn.
- Nối cổ chai đầu thẳng trong lệnh này áp dụng cho thép có gân. Thép trơn cần chi tiết
  neo móc/U riêng; không cho đổi checkbox sang thép trơn rồi tạo nối đầu thẳng.
- Đoạn nối có kiểm tra các giới hạn dưới 20d và 250 mm. Điều kiện chiều dài theo công
  thức (259), giới hạn 0,4αL0,an, tỷ lệ thanh nối, cốt ngang và kháng chấn chưa được tự tính.
  Giá trị thiết kế có thể lớn hơn giới hạn dưới mà lệnh kiểm tra.

Không ghi đè loại thép đang dùng trong project. Nếu gối uốn của loại gốc quá nhỏ,
lệnh tạo loại riêng có tiền tố `KSTR_TCVN_BEND_` trong cùng transaction. Các lần chạy sau
không coi các loại tự sinh này là loại gốc trùng đường kính. Không đủ không gian nhấn,
thanh giao nhau hoặc Revit tạo bán kính dưới mức kiểm tra thì rollback tuyến đó.

## Giới hạn và kiểm thử

Giữ chặn cột chồng cao độ quá 2 mm, đổi tiết diện/cấu hình, nối so le và cấu hình nối cũ
chưa hỗ trợ. Không tự nhận diện cấu kiện bê tông trong khoảng hở; không kiểm tra toàn bộ
cấu tạo nối theo TCVN và không tự thay giá trị chiều dài đã nhập.

Bộ `tests/ColumnRebarLayout` chạy resolver thực với đối tượng hình học thay thế Revit:
khoảng hở 0/400/1500 mm, dung sai chồng, thiếu chiều dài, cột trên ngắn, nhiều tầng,
reset trạng thái, giới hạn gối uốn và xung đột tâm thanh. Bộ so sánh tiết diện Revit được
thay thế trong kiểm thử này, không dùng kiểm thử để xác nhận hình học Revit thực.

Cần thử trong Revit 2024 trên bản sao mô hình: cột Trệt/Lầu 1 hở 400 mm; mở tab Thép chờ
để xem phần nối còn lại; chọn loại thép có gân và chiều dài phù hợp; kiểm tra cả GroupLongBars
bật/tắt, kết hợp neo L, cột xoay/mirror, hình cổ chai và bán kính sau dựng, tag/hình khai triển,
Undo và ghi đè. Đai và vị trí/tỷ lệ nối vẫn phải đối chiếu thiết kế của công trình.
