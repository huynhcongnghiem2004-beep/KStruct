# Bổ sung yêu cầu crop và dimension móng

Yêu cầu người dùng: crop lấy từ mép móng ra 6 mm theo kích thước thực tế; tạo Dim hai lớp cho bốn cạnh móng. Tài liệu này ưu tiên hơn các đề xuất crop/dimension trong FOUNDATION_VIEW_AND_SHEET_PLAN.md và FOUNDATION_DRAWINGS_REVIT2024_PLAN.md. Chỉ cập nhật kế hoạch, chưa thay đổi code.

## Crop hình học

- Khoảng cách crop = 6 mm trong mô hình, không phải mm trên giấy; không nhân View.Scale. Chuyển bằng UnitUtils.ConvertToInternalUnits(6, UnitTypeId.Millimeters).
- Với mặt bằng móng chữ nhật: xác định bốn mép hình học thực của host trong hệ trục local, offset ra ngoài 6 mm mỗi phía. Không dùng world-axis bounding box cho móng xoay vì không giữ được khoảng cách từ mép thực.
- Dùng hệ tọa độ view/crop phù hợp và kiểm tra hình chữ nhật crop sau khi xoay; mép crop phải song song mép móng. Nếu callout/view type không hỗ trợ hướng crop yêu cầu, báo không tương thích trước khi tạo thay vì âm thầm tăng biên.
- Với hình móng không chữ nhật: yêu cầu được hiểu là biên offset từ mép thực; kiểm tra CropRegionShapeManager và khả năng non-rectangular crop trên đúng view Revit 2024. Chưa hỗ trợ thì báo rõ, không thay bằng bounding box rồi tuyên bố đúng 6 mm.
- Với mặt cắt: áp khoảng cách 6 mm cho các mép hình học móng được chiếu lên mặt phẳng cắt. Near/far depth là thông số riêng, không đồng nhất với biên crop. Nếu crop chặt làm mất phần cột chờ/đầu cọc cần thể hiện, báo xung đột và dùng view chi tiết riêng hoặc cấu hình được người dùng chọn; không tự nới crop.
- Annotation crop phải được xử lý riêng để nhìn thấy dim ngoài mép móng. Không dùng biên model crop 6 mm làm giới hạn dim/tag/title. Kiểm tra thực tế khả năng hiển thị annotation trong Revit 2024 và cảnh báo nếu view/template chặn.
- Profile cũ có CropMarginMm phải tiếp tục đọc được. Giá trị 6 mm áp cho cấu hình mới này; không tự ghi đè profile/hồ sơ hiện hữu. Không đổi GUID/định nghĩa schema đã phát hành; metadata crop mới phải đủ để lệnh cập nhật giữ đúng 6 mm và đúng hướng.

## Dim hai lớp, đủ bốn cạnh mặt bằng

- Bốn cạnh được hiểu là trên, dưới, trái, phải trong view mặt bằng móng; tạo hai chuỗi kích thước ngoài mỗi cạnh, tổng cộng tám chuỗi nếu đủ reference.
- Lớp trong: chuỗi kích thước chi tiết, gồm mép móng → tim cột/hàng cọc hoặc các reference chi tiết thực sự thuộc phạm vi thiết kế → mép móng. Theo từng cạnh, sắp reference theo phương đo và loại trùng vị trí.
- Lớp ngoài: kích thước tổng thể giữa hai mép ngoài móng. Trên/dưới đo chiều dài cùng phương; trái/phải đo phương vuông góc.
- Cách chia lớp trong/ngoài là phương án triển khai đề xuất để cụ thể hóa yêu cầu hai lớp. Nếu chưa đủ reference chi tiết, báo cạnh nào thiếu; không tạo hai dim tổng thể trùng nhau chỉ để đủ số lớp và không tạo reference giả.
- Dimension liên kết reference mô hình qua doc.Create.NewDimension; ưu tiên family reference/geometry reference hợp lệ trong view. Không thay bằng TextNote hoặc số đo từ bounding box.
- Offset lớp dim đầu và khoảng cách giữa hai lớp là thông số riêng, tính bằng mm trên giấy theo DimensionType, tỷ lệ và kích thước chữ; người dùng chưa chốt các giá trị này. Khoảng crop 6 mm không phải khoảng cách đặt dim.
- Kiểm tra chữ, đường dim và leader với nhau và với viewport lân cận. Annotation crop phải đủ chứa cả hai lớp ở bốn cạnh.
- Dim này bổ sung cho mặt bằng; dimension mặt cắt theo nội dung riêng đã nêu trong kế hoạch, không suy rằng mọi mặt cắt đều cần cùng tám chuỗi.
- Lệnh cập nhật chỉ thay dimension có metadata sở hữu đúng host/view của tính năng mới; giữ dimension người dùng tạo/chỉnh ngoài phạm vi. Chạy lại không tạo trùng.

## Kiểm tra nghiệm thu

1. Móng chữ nhật không xoay, xoay 30/45/90 độ và mirror: đo trong model, từng mép crop cách mép móng đúng 6 mm trong sai số hình học API đã công bố.
2. Đổi tỷ lệ view 1:10, 1:20, 1:50: model crop vẫn 6 mm; dim/tag hiển thị đủ ngoài crop.
3. Móng có cột lệch tâm, đài nhiều hàng cọc: lớp trong đúng vị trí reference, lớp ngoài đúng tổng thể, đủ bốn cạnh.
4. Thiếu reference, hình học không hỗ trợ hoặc template khóa crop/annotation: báo rõ; không báo hoàn tất tám chuỗi khi chưa tạo đủ.
5. Cập nhật crop, Undo/Cancel và mở profile cũ: không thay hồ sơ hiện hữu ngoài phạm vi; không tạo dim trùng.
6. Kiểm tra PDF/DWG có đủ hai lớp dim và không bị cắt; đo crop trong model, không nhầm với khoảng cách trên bản in.
