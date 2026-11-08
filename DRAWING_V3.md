# Triển khai bản vẽ và thép cột V3

## Luồng sử dụng

1. Chọn cột đứng chữ nhật, mở **Tạo thép cột**. Mỗi tuyến chọn một cột;
   chương trình tìm các đoạn theo tầng. Cột tròn dùng lệnh riêng.
2. Chọn tầng, nhập Cx/Cy (2–20 thanh mỗi cạnh), đường kính, cover ngoài đai và thông số đai.
   **Áp dụng cho tất cả tầng** sao chép cấu hình tầng đang chọn cho toàn bộ tuyến trong cửa sổ.
3. Bật **Cài đặt triển khai bản vẽ cột** nếu muốn tạo cả hồ sơ. Nút chạy hiển thị
   **Tạo thép 3D** hoặc **Tạo thép và bản vẽ** theo trạng thái này.
4. Để triển khai lại từ thép đã tạo: dùng **Tạo bộ bản vẽ thép cột** hoặc
   **Cập nhật bản vẽ V3**. Cần cấu hình V3 đã lưu trên cột và cấu hình Triển khai của project.

Không yêu cầu nhập mã bản vẽ tham chiếu hoặc đánh dấu xác nhận từng tầng; tab Cài đặt chung đã bỏ.
Các kiểm tra hình học, loại thép và cấu hình bản vẽ vẫn chạy trước khi dựng.

## Phân bố thép thực tế

- Thép dọc thẳng: bật nhóm để tạo Fixed Number theo cạnh, bốn góc chỉ tạo một lần.
  Bỏ nhóm sẽ tạo từng thanh; cùng cover và chiều dài ở cả hai chế độ.
- Neo chân L cũng hỗ trợ Fixed Number. Chọn hướng và nhóm tại tab Thép chờ / Neo móng;
  xem [hướng dẫn neo L và view mô hình](docs/COLUMN_L_ANCHOR.md). Không tự tính neo hoặc nối.
- Đai có ba vùng: chân, giữa, đầu. Chiều dài mỗi vùng đầu tính từ tâm đai đầu/cuối.
  Hai vùng đầu không được chồng nhau. Nhập chiều dài 0 để chỉ có vùng giữa.
- Maximum Spacing chia đều vị trí trong từng vùng, bước thực tế không lớn hơn bước nhập.
  Fixed Number dùng số vị trí của mỗi vùng, bao gồm hai đầu vùng; đai chung tại ranh giới
  chỉ tạo một lần. Ví dụ 6 + 9 + 6 vị trí tạo 19 đai, không phải 21.
- Bật nhóm đai để tạo Rebar Set theo vùng; tắt nhóm tạo từng đai cùng vị trí.
  Đai phụ dùng cùng các cao độ này. Luân phiên tạo móc 135°/90° và đổi hướng mỗi cao độ;
  khi nhóm, chia hai tập xen kẽ. Cần đúng một Hook Type cho mỗi góc dùng, xét HookAngle,
  không suy từ tên. Đai lồng kín không được gắn móc 135° của đai móc.
- Đai nút là vùng bổ sung **phía trên đỉnh cột**, cao theo giá trị người dùng nhập,
  bước theo Joint Spacing (kể cả khi vùng thân dùng Fixed Number). Cần bật thép chờ,
  chiều dài chờ ít nhất bằng chiều cao nút. Không tự tìm hoặc đo dầm/sàn.
- Preview dọc và dựng đai dùng chung bộ tính vị trí. Preview vẫn là minh họa, không phải
  hình gia công chính xác của móc hoặc neo. Thép chờ nối giữa hai tầng cùng cấu hình
  dùng nhấn cổ chai: xem [quy tắc khoảng hở và cổ chai](docs/COLUMN_BOTTLENECK.md).
  So le và chuyển tiếp tiết diện tự động chưa được hỗ trợ.

## Ghi đè và lưu cấu hình

- Chỉ thay Rebar có Partition khớp chính xác `KSTR_V3_<ElementId cột>`, kể cả thanh neo
  được host bởi móng. Không xóa theo host cột. Thép có Partition khác/trống được giữ.
  Partition là dấu nhận diện V3 tương thích dữ liệu cũ: không gán Partition này cho thép ngoài V3.
- Nếu đã có thép V3 mà chưa bật ghi đè, thao tác bị chặn để tránh tạo trùng.
  Không ghi được Partition lên thép mới thì hủy tạo.
- Cấu hình thép lưu trên cột. Cấu hình bản vẽ lưu XML theo project trong AppData/KStruct/Deployment.
  Hai ô thép chờ ở hai tab là cùng một thiết lập, được đồng bộ.

## Bố cục hồ sơ

- Một sheet cho một tuyến: một mặt cắt dọc toàn tuyến và một mặt cắt ngang mỗi đoạn cột.
  Xem thêm [bố cục tuyến cột](DRAWING_STACK_LAYOUT.md).
- Nhiều tuyến thêm hậu tố số sheet; không ghi đè sheet ngoài KStruct V3.
- Dùng Title Block, Section/Detail View Family Type, View Template, tỷ lệ, Dimension Type,
  Text Type, Rebar Tag và Break Line đã chọn từ project. Các bộ chọn view chỉ liệt kê
  Section/Detail; kiểm tra tương thích thực tế vẫn chạy trước khi dựng.
- Template khóa tỷ lệ khác giá trị nhập hoặc cấu hình không phù hợp sẽ bị chặn.
- Kích thước hình học ưu tiên reference family; kích thước phân vùng, bảng và ghi chú
  là dữ liệu lúc tạo, cần cập nhật bản vẽ sau khi đổi thông số/mô hình.
- Break Line không tự rút ngắn hình học hoặc tạo split crop.
- Không tự đổi tỷ lệ để ép vừa sheet; không đủ chỗ thì rollback tuyến đó.
- Thép và bản vẽ cùng transaction mỗi tuyến, nhiều tuyến cùng TransactionGroup để Undo.

## Kiểm tra

Kiểm thử độc lập, không cần Revit:

```sh
dotnet run --project tests/ColumnRebarLayout/ColumnRebarLayout.csproj --configuration Release
```

CI Windows chạy kiểm thử này và build add-in bằng compile references Revit 2024.
Build và kiểm thử số học không xác nhận được hình học hoặc giao diện thực trong Revit.
Cần thử trên bản sao mô hình:

- Cột một tầng/nhiều tầng, xoay/mirror, Cx/Cy = 2, 3, 20; so sánh nhóm và từng thanh.
- Vùng đầu 0, hai vùng đầu vừa chạm, Fixed Number, Maximum Spacing, đai nút.
- Đai kín/móc, đai phụ có/không đảo móc; kiểm tra hướng móc và vị trí từng set.
- Cột có cả thép thủ công và V3, neo host móng: ghi đè chỉ thay Partition V3;
  không ghi đè phải chặn tạo trùng; Undo phục hồi đủ thép và bản vẽ.
- Đổi thông số rồi mở lại, nhất là Maximum Spacing không nhập số đai và tắt đai nút.
- Thiếu/trùng loại móc, thiếu Bar Type, lỗi dựng sheet: rollback tuyến, giữ mô hình trước đó.
- Template, family reference, tag, break line, sheet trùng và khổ giấy không đủ chỗ.
- Cửa sổ trên màn hình nhỏ và DPI 125%/150%; tab, nút chạy và các ô nhập không bị che.
