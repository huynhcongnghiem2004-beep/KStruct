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
- Khi hai tầng có số thanh khác nhau, lệnh giữ thứ tự quanh mặt cắt và chỉ ghép góc với
  góc. Thanh dư dưới kết thúc tại `đỉnh cột dưới + chiều cao dầm - cover trên dầm`;
  chiều cao và cover được tự đọc từ dầm phù hợp, hoặc dùng giá trị nhập tay khi Revit
  không có dữ liệu đủ rõ. Nếu thép trên nhiều hơn,
  các vị trí thừa được kéo thẳng xuống dầm theo chiều neo nhập ở tầng trên. Add-in kiểm tra
  chiều dài neo nằm trong chiều cao dầm và kiểm tra khoảng cách với thép dưới.
- Khi số thanh bằng nhau, lệnh ghép từng thanh theo tọa độ quanh mặt cắt, giữ ghép góc
  với góc; tiết diện, cover, đường kính hoặc hướng trục có thể khác nhau nếu tìm được
  phương án hình học không giao nhau.
- Với nút cần xử lý thanh dư hoặc so le, lệnh tự tìm dầm Structural Framing nằm ngang
  giao qua mặt bằng hai cột. Chiều cao lấy từ BoundingBox hình học dầm; lớp bảo vệ lấy từ
  `Rebar Cover - Top Face` và `RebarCoverType` của dầm. Mặt đáy dầm phải khớp đỉnh cột dưới,
  mặt trên phải khớp đáy cột trên trong 10 mm. Nếu không tìm được dầm duy nhất phù hợp,
  cover chưa gán, hoặc nhiều dầm khác kích thước/cover, lệnh báo lý do và dừng nếu chưa
  có đủ giá trị nhập tay. Các dầm nghiêng chưa được tự nhận diện.
- Thanh dưới nhấn đến vị trí nằm cạnh thanh trên; thanh trên giữ thẳng. Khoảng cách tâm
  tại đoạn nối = (d dưới + d trên)/2 + khe hở, mặc định khe hở dựng hình 2 mm.
  Độ dịch và chiều cao nhấn tính riêng từng thanh; chiều cao = độ dịch × n. Người dùng phải nhập n theo chi tiết được duyệt; add-in không đặt mặc định 1:6.
  Khe hở và tỷ lệ này là thông số dựng hình, không gán cho TCVN 5574.
- Chỉ ghép Fixed Number khi đường thanh phẳng, cùng hình dạng và phân bố đều theo
  phương vuông góc mặt phẳng. Thanh khác hình tách riêng. Theo yêu cầu chỉ dùng shape
  có sẵn, neo L kết hợp nhấn khác mặt phẳng hiện bị chặn; không chuyển sang FreeForm.
  Không ép các thanh khác hình vào một set.
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

Giữ chặn cột chồng cao độ quá 2 mm, phân bố cạnh không ghép được góc, phương án ghép
mơ hồ, không nhận diện được hình học dầm/cover, thiếu chiều neo, neo vượt vùng dầm hoặc giao đường tâm nhấn.
Bảng so sánh báo khi số thanh nối vượt 50% số thanh tại tiết diện nhiều thép hơn. Lệnh
chia các thanh ghép thành hai nhóm và đặt hai dải nối so le; độ dịch S được tính từ phần
nối còn lại và đường kính do người dùng nhập (S = (phần nối + đường kính lớn hơn)/3),
không dùng 800 mm hay 40d làm mặc định.
Nếu S làm phần nối còn lại thấp hơn giới hạn tối thiểu, thiếu chiều cao dầm hoặc thiếu
chiều cao cột cho nhóm trên thì lệnh chặn tạo. Đầu thanh bổ sung hiện chỉ hỗ trợ neo thẳng
theo chiều dài nhập; móc/coupler và tự xác định chi tiết neo chưa hỗ trợ.
Không tự nhận diện bê tông trong khoảng hở và không tự thay chiều dài đã nhập. Việc đọc
cover dựa trên cấu hình Rebar Cover của host trong Revit; add-in không suy cover từ kích
thước thép hoặc từ lớp bảo vệ cột.
Kiểm tra tránh giao hiện xét các đoạn đường tâm trước bo; chưa thay thế kiểm tra va chạm
solid, đai và toàn bộ thanh sau khi Revit dựng. Lệnh chưa tạo đai tăng cường riêng trong
solid dầm; bước đai của các cột vẫn theo cấu hình từng cột.

Bộ `tests/ColumnRebarLayout` liên kết trực tiếp bộ ghép, adapter, resolver và toán đường
thanh dùng trong ứng dụng; không thay bộ so sánh bằng boolean giả. Kiểm tra đổi tiết diện,
cover, đai, đường kính, tâm, xoay/mirror, đổi số thanh, phân bố cạnh, neo L, nhóm set,
đường nhiều mặt phẳng, cung uốn, khoảng hở 0/400/1500 mm và chiều dài nối.
Build CI dùng API Revit 2024; kiểm tra toán học và build không xác nhận chạy Revit thực.

Cần thử trong Revit 2024 trên bản sao mô hình: cột Trệt/Lầu 1 hở 400 mm; mở tab Thép chờ
để xem phần nối còn lại; chọn loại thép có gân và chiều dài phù hợp; kiểm tra cả GroupLongBars
bật/tắt, kết hợp neo L, cột xoay/mirror, hình cổ chai và bán kính sau dựng, tag/hình khai triển,
Undo và ghi đè. Đai và vị trí/tỷ lệ nối vẫn phải đối chiếu thiết kế của công trình.

## Cấu hình nối cũ

XML trong cột và file cấu hình dùng chung bộ chuyển đổi `V3LegacySplice`.
`AtStoreyJoint` có chiều dài chờ hữu hạn, dương được chuyển sang bộ nhấn cổ chai,
giữ nguyên cả StarterLength và LapLength. Giao diện hiển thị thông báo chuyển đổi.
Nếu thiếu StarterLength, giữ trạng thái chờ nhập và yêu cầu chiều dài đo từ đỉnh
cột dưới; không dùng LapLength thay thế. Người dùng có thể nhập chiều dài hoặc
chủ động tắt thép chờ trong giao diện. Chuyển đổi không tự hỗ trợ so le và không
bỏ qua kiểm tra chiều dài nối, cover, ghép thanh hoặc bán kính uốn.

## Tham chiếu bản vẽ RVT của dự án

Đã xem các sheet `STR.03.03 - BỐ TRÍ THÉP CỘT` và `STR.03.04 - BỐ TRÍ THÉP CỘT` trong file `Mr.Khoa - pa2.rvt`:

- C1, mặt cắt MC 2-2 / MC 3-3: thép chủ 8Ø18; đai Top & Bot Ø8a100; đai Mid Ø8a200.
- C2, mặt cắt MC 6-6: thép chủ 8Ø16; đai Top & Bot Ø8a100; đai Mid Ø8a200.
- C3, mặt cắt MC 3-3: thép chủ 8Ø16; đai Top & Bot Ø8a100; đai Mid Ø8a200.

Trong view 3D `3D KẾT CẤU`, một thanh được chọn có D16, `Rebar Shape 64`, Partition C2,
Schedule Mark 66, chiều dài xấp xỉ 6290 mm; tham số shape đọc được A=350, B=4260,
C=400, D=1300 mm. Một nhóm đai gần vùng nối là D8, 8 vị trí, spacing 100 mm. Đây là
giá trị của các đối tượng được chọn trong file; không chứng minh tỷ lệ dốc 1:6 và không
được dùng làm mặc định chung. Các sheet/view đã xem chưa xác định chiều dài neo bổ sung,
chi tiết móc/coupler hoặc độ so le cần dùng khi số thanh hai tầng khác nhau; nhập chúng
theo chi tiết được duyệt trước khi tạo.
