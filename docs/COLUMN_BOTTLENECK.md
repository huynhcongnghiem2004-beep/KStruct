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
- Khi hai tầng có cùng số thanh và phân bố cạnh tương thích, lệnh ghép từng thanh theo
  tọa độ mô hình, giữ ghép góc với góc. Có thể khác tiết diện, cover, đường kính hoặc
  hướng trục; không còn chặn chỉ vì các thông số này khác nhau.
- Thanh dưới nhấn đến vị trí nằm cạnh thanh trên; thanh trên giữ thẳng. Khoảng cách tâm
  tại đoạn nối = (d dưới + d trên)/2 + khe hở, mặc định khe hở dựng hình 2 mm.
  Độ dịch và chiều cao nhấn tính riêng từng thanh; chiều cao = độ dịch × n, mặc định 1:6.
  Khe hở và tỷ lệ này là thông số dựng hình, không gán cho TCVN 5574.
- Chỉ ghép Fixed Number khi đường thanh phẳng, cùng hình dạng và phân bố đều theo
  phương vuông góc mặt phẳng. Thanh khác hình tách riêng; neo L kết hợp nhấn khác mặt
  phẳng dùng FreeForm có cung uốn. Không ép các thanh khác hình vào một set.
- Tab Thép chờ/Neo móng có bảng thông số hai tầng, kết quả ghép và lỗi cụ thể.
  Preview chiếu các vị trí ghép theo trục X cột dưới, có thể chồng hình; hiển thị khoảng
  hở và vị trí cột trên. Hình uốn cuối phải kiểm tra trên Rebar trong mô hình.

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

Giữ chặn cột chồng cao độ quá 2 mm, khác số thanh, phân bố cạnh không ghép được góc,
phương án ghép mơ hồ, thiếu cover/chiều dài hoặc giao đường tâm nhấn. Không tự bổ sung
hay kết thúc thanh khi đổi số lượng. Nối so le và cấu hình nối cũ chưa hỗ trợ.
Không tự nhận diện bê tông trong khoảng hở và không tự thay chiều dài đã nhập.
Kiểm tra tránh giao hiện xét các đoạn đường tâm trước bo; chưa thay thế kiểm tra va chạm
solid, đai và toàn bộ thanh sau khi Revit dựng.

Bộ `tests/ColumnRebarLayout` liên kết trực tiếp bộ ghép, adapter, resolver và toán đường
thanh dùng trong ứng dụng; không thay bộ so sánh bằng boolean giả. Kiểm tra đổi tiết diện,
cover, đai, đường kính, tâm, xoay/mirror, đổi số thanh, phân bố cạnh, neo L, nhóm set,
đường nhiều mặt phẳng, cung uốn, khoảng hở 0/400/1500 mm và chiều dài nối.
Build CI dùng API Revit 2024; kiểm tra toán học và build không xác nhận chạy Revit thực.

Cần thử trong Revit 2024 trên bản sao mô hình: cột Trệt/Lầu 1 hở 400 mm; mở tab Thép chờ
để xem phần nối còn lại; chọn loại thép có gân và chiều dài phù hợp; kiểm tra cả GroupLongBars
bật/tắt, kết hợp neo L, cột xoay/mirror, hình cổ chai và bán kính sau dựng, tag/hình khai triển,
Undo và ghi đè. Đai và vị trí/tỷ lệ nối vẫn phải đối chiếu thiết kế của công trình.
