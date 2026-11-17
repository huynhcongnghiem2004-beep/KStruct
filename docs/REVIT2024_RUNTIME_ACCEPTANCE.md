# KStruct — Ma trận nghiệm thu trong Revit 2024

> Trạng thái: CHECKLIST CHƯA CHẠY. Build/test GitHub Actions không thay thế kiểm chứng trong ứng dụng Revit 2024.

## Phạm vi, môi trường và dữ liệu
- Revit 2024 trên Windows, DLL x64 build từ commit được ghi lại, file `KStruct.addin` trỏ đúng DLL.
- Thử trên **bản sao** của các file RVT; không dùng duy nhất file khách hàng đang làm việc.
- Ghi phiên bản Revit, SHA commit, SHA256 DLL, tên RVT copy và người thực hiện.
- Trước và sau test lưu danh sách ElementId/UniqueId của Rebar, Tag, Dimension, View, Sheet, và thời điểm Undo.
- Nếu cần test tính toán kết cấu, yêu cầu kỹ sư chịu trách nhiệm thiết kế nghiệm thu kết quả; add-in không thay thế thiết kế.

## A. Ribbon (bắt buộc)
| ID | Thao tác | Điều kiện đạt |
|---|---|---|
| R01 | Khởi động Revit, mở KStruct | Không có lỗi startup; 7 panel và 92 nút đăng ký |
| R02 | Kiểm tra 5 panel đã loại: Khối lượng, Mô hình, Quản lý cấu kiện, Tải trọng, Phân tích | Không xuất hiện |
| R03 | Mở Thép cột, Thép móng, KStruct Dầm, Cốt thép, Hồ sơ, Thiết lập | Các nút còn giữ gọi được đúng command |
| R04 | Mở Quản lý panel, ẩn/hiện một panel rồi mở lại Revit | Không có lỗi, panel Thiết lập vẫn truy cập được |

## B. Thép móng
| ID | Thao tác | Điều kiện đạt |
|---|---|---|
| F01 | Tạo thép mới móng đơn và đài cọc | Đúng host, số lượng, phân bố và lớp bảo vệ theo cấu hình |
| F02 | Cập nhật móng có thép do KStruct sở hữu | Không xóa thép thủ công/thép công cụ khác |
| F03 | Móng có Tag/Dimension liên kết với thép, thử cập nhật | Nếu có đối tượng phụ thuộc bị ảnh hưởng ngoài phạm vi thì rollback, giữ nguyên hồ sơ |
| F04 | Hủy hộp thoại trước khi tạo thép | Không có Rebar/metadata mới |
| F05 | Thiếu Rebar Shape/Hook cần thiết | Không tạo hình học thay thế sai; có cảnh báo |
| F06 | Chỉnh kích thước Family rồi chạy cập nhật | Lấy hình học host mới; lưu cấu hình đúng sau commit |
| F07 | Mở preset XML legacy, XML lỗi và XML có DTD | Legacy đọc được; XML không an toàn bị từ chối; file cũ được giữ |

## C. Thép cột V3
| ID | Thao tác | Điều kiện đạt |
|---|---|---|
| C01 | Cột một tầng / nhiều tầng, tiết diện không đổi | Bộ thép và nhóm đai đúng cấu hình |
| C02 | Cột lệch trục, xoay/mirror, giảm tiết diện | Không đảo hướng hình học, không xuyên bê tông |
| C03 | Không có Shape/Hook phù hợp | Báo rõ, rollback tuyến lỗi, không tự tạo loại Shape/Hook |
| C04 | Chạy nhiều tuyến, cố tình gây lỗi một tuyến | Các tuyến khác xử lý theo chính sách hiện hành; báo tuyến lỗi |
| C05 | Cập nhật thép và bản vẽ cột có Tag/Dimension | Không làm mất đối tượng ngoài phạm vi; Undo đúng |
| C06 | Chạy lại cùng cấu hình nhiều lần | Không nhân đôi bộ thép, metadata vẫn nhất quán |

## D. Thép dầm / PR #22
| ID | Thao tác | Điều kiện đạt |
|---|---|---|
| B01 | Dầm không có Tag/Dimension, tạo lại bộ thép | Thay đúng bộ KStruct cũ, không còn nhóm mồ côi |
| B02 | Dầm có Rebar Tag, Dimension hoặc annotation tham chiếu Rebar cũ | Nếu Revit dự định cascade-delete đối tượng ngoài Rebar cũ thì rollback dầm, không mất Tag/Dim |
| B03 | Chọn 3 dầm, một dầm có lỗi ở bước xóa | Dầm lỗi rollback; dầm khác xử lý theo chính sách hiện hành; báo số lượng chính xác |
| B04 | Nhập cấu hình thép dầm qua luồng bảng (nếu còn được gọi nội bộ) | Cũng dùng bảo vệ xóa phụ thuộc |
| B05 | Dầm thẳng nhiều nhịp, dầm nghiêng và dầm cong | Kiểm tra số thanh, chiều dài, đai, Shape/Hook |
| B06 | Undo sau tạo/cập nhật thành công | Khôi phục đúng Rebar, Tag, Dimension liên quan |
| B07 | Thiếu Shape/Hook cũ trong project | Báo lỗi/rollback, không chuyển FreeForm âm thầm |

## E. Hồ sơ bản vẽ
| ID | Thao tác | Điều kiện đạt |
|---|---|---|
| D01 | Tạo hồ sơ móng mới, hai mặt cắt A/B, sheet, viewport | Đúng view, scale, crop, text, tag và dim |
| D02 | Cập nhật sau đổi kích thước hoặc quay móng | Không mất annotation thủ công, view/sheet update đúng |
| D03 | Tạo/cập nhật hồ sơ cột V3 | Đúng chiều nhìn, khoảng cách thể hiện, break line |
| D04 | Cố tình gây lỗi đặt view trên sheet | Rollback theo tuyến / không để view mồ côi |
| D05 | Mở preset legacy, xuất ra file cùng tên | Không đổi semantics của preset cũ; overwrite an toàn |

## Ghi nhận kết quả
Mỗi test điền: `PASS / FAIL / BLOCKED`, commit SHA, tên RVT (bản sao), số lượng Rebar và annotation trước/sau, screenshot hoặc log, bug/issue liên quan, người kiểm tra và ngày.

**Điều kiện chấp nhận cho PR ảnh hưởng Transaction hoặc hình học:** không có FAIL ở các test bắt buộc liên quan; với BLOCKED cần nêu nguyên nhân và chấp nhận rủi ro rõ ràng. CI thành công một mình không đủ chứng minh runtime đạt.

## Quy trình xử lý lỗi
1. Không tiếp tục thử trên file dự án gốc.
2. Giữ lại RVT copy, screenshot, log và commit SHA của DLL.
3. Tạo issue GitHub với các bước tái hiện, kết quả mong đợi/thực tế.
4. Sửa trên branch riêng; chạy lại test lỗi và regression trước khi xét merge.
