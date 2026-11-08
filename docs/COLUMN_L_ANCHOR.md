# Neo chân L theo Fixed Number và hướng trục cột

## Sử dụng trong Revit 2024

1. Chọn tuyến cột và mở **Tạo thép cột**. Tại tầng chân tuyến, nhập Cx/Cy,
   đường kính, cover cột và cấu hình đai. Bật **Nhóm thép dọc (Fixed Number)**.
2. Tại **Thép chờ / Neo móng**, bật **Neo chân L xuống móng tại chân tuyến**.
3. Nhập **tổng kích thước đoạn đứng + chân ngang**, cover móng tại đáy và bên.
   Chân ngang = tổng kích thước nhập - đoạn đứng từ đáy cột đến cao độ uốn.
   Đây là tổng kích thước hình học, không phải chiều dài gia công sau khi Revit tạo bán kính uốn.
   Chiều dài thanh trong bảng hình thép lấy từ tham số Length của Rebar thực.
4. Chọn hướng **Vào tâm**, **Ra ngoài**, **+X**, **-X**, **+Y** hoặc **-Y** theo hệ trục cột.
   Sơ đồ bên phải biểu diễn hướng, không biểu diễn chiều dài. Với cột xoay/mirror,
   hướng đi theo BasisX/BasisY của family, không phải trục thế giới.
5. Nếu tạo lại, bật ghi đè V3. Không cần nhập mã bản vẽ hoặc đánh dấu xác nhận từng tầng.
6. Giữ **Mở view 3D kiểm tra sau khi tạo** để xem thép thật. Một view mới tên
   `KSTR_V3_NEO_L_<ID cột>` được tạo cho mỗi tuyến có neo, khoanh quanh móng và 1.2 m
   chân cột, cô lập tạm cột/móng/thép V3 và hiện thép không bị bê tông che.
   Khi tạo nhiều tuyến, mở view đầu tiên thành công; các view còn lại ở Project Browser.
   Có thể xoay view, chọn từng Rebar Set và xem Layout Rule/Quantity trong Properties.
   Không thay đổi các view người dùng đã có. Undo nhóm tạo sẽ xóa cả thép/bản vẽ/view mới.

## Quy tắc nhóm

- Vào tâm/ra ngoài: hai cạnh Cx giữ toàn bộ thanh góc; hai cạnh Cy chỉ chứa thanh giữa.
  Hướng chân vuông góc từng cạnh để mặt phẳng thanh L vuông góc hướng phân bố set.
  Thanh góc không uốn chéo theo đường chéo góc như cách dựng cũ.
- Cùng hướng ±X: phân nhóm dọc Y tại từng vị trí X; cùng hướng ±Y: phân nhóm dọc X
  tại từng vị trí Y. Không ép nhóm thanh có mặt phẳng không phù hợp.
- Số thanh vẫn là `2 × (Cx + Cy) - 4`. Ví dụ Cx=4, Cy=5 cho 14 thanh;
  vào tâm/ra ngoài có bốn set với số vị trí 4, 4, 3, 3.
- Nếu một nhóm chỉ có một thanh (ví dụ phần giữa cạnh khi Cy=3), Revit dùng Single.
  Tắt Nhóm thép dọc tạo từng thanh có cùng vị trí và hướng với chế độ nhóm.
- Rebar host là móng; Partition vẫn theo ID cột. Ghi đè, tag, hình thép và phạm vi
  mặt cắt dọc nhận diện theo Partition để không bỏ sót các set host móng.
- Trước commit, code kiểm tra số vị trí và phép tịnh tiến các vị trí Rebar do Revit trả về.
  Khác cấu hình thì rollback tuyến; không báo thành công một set sai chiều phân bố.

## Kiểm tra hình học và lưu cấu hình

- Tự tìm một FamilyInstance thuộc Structural Foundation tiếp giáp đáy cột, dung sai cao độ 1 mm.
  Nhiều móng cùng vị trí bị chặn, không tự chọn ngẫu nhiên.
- Cao độ uốn lấy từ đáy bounding box móng thực + cover + nửa đường kính thanh.
  Không phụ thuộc tên tham số `Foundation Thickness`.
- Kiểm tra các đường bao mẫu của chân ngang và đoạn đứng với solid móng, kể cả
  nested instance có transform. Ngoài móng/không đủ cover theo đường bao kiểm tra thì chặn
  trước khi cửa sổ đóng. Kiểm tra này không thay thế kiểm tra cấu tạo, bán kính uốn,
  va chạm thanh-thanh hoặc tính toán neo theo thiết kế.
- Lưu trạng thái bật, kích thước tổng, cover và hướng vào cấu hình cột chân tuyến.
  Mở lại sẽ nạp các thông số này. XML cũ thiếu các trường mới vẫn đọc được;
  mặc định neo tắt và hướng vào tâm, không tự bật neo từ dữ liệu thiếu.
- Không tạo được view kiểm tra thì rollback riêng view, giữ kết quả thép/bản vẽ hợp lệ
  và báo cảnh báo. Không mở được view thì mở thủ công trong Project Browser.

## Xác minh

Kiểm thử tự động trong `tests/ColumnRebarLayout` xét toàn bộ Cx/Cy 2–20 cho sáu hướng:
không lặp góc, đúng số thanh, mặt phẳng nhóm, hướng vào/ra/cùng hướng;
kiểm tra toán học với trục xoay/mirror và tương thích XML cũ/mới.
CI Windows build toàn add-in bằng API Revit 2024.

Chưa có kiểm thử Revit thực trên mô hình người dùng trong môi trường Linux.
Checklist chạy trên bản sao mô hình:

- Cx/Cy 2, 3, 4/5, 20; bật/tắt nhóm; Quantity và vị trí nhóm/từng thanh khớp nhau.
- Sáu hướng trên cột thẳng, xoay 30°/90°, mirror; kiểm tra mũi tên so với hướng thực.
- Móng đủ/chật cho chân ngang, có nhiều/không có móng tiếp giáp, móng nested hoặc có lỗ.
- Hình dạng L, bán kính uốn thực, cover và va chạm giữa các chân cùng cao độ.
- Sửa hướng rồi ghi đè: giữ thép ngoài V3; Undo phục hồi đầy đủ.
- Mở lại cửa sổ: nhớ hướng, cover, kích thước; đổi tầng không làm thay neo chân tuyến.
- View mới và cô lập tạm; xoay/chọn set; bỏ tùy chọn không tạo view mới; nhiều tuyến.
- Tag/hình khai triển và crop mặt cắt dọc chứa cả đoạn neo host móng; cập nhật lại bản vẽ.
- DPI 125%/150%, màn hình nhỏ: tab Neo móng cuộn được, sơ đồ và ô nhập không bị che.
