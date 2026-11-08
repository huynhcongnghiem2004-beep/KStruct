# Hướng dẫn làm việc với KStruct

- Đọc yêu cầu và mã nguồn trước khi đề xuất phương án. Không tự chọn công nghệ ứng dụng khi chưa có yêu cầu.
- Thực hiện thay đổi trên nhánh riêng và mở pull request khi đã có nhánh nền.
- Chạy các kiểm tra phù hợp với thay đổi; khi bổ sung ứng dụng, thêm lệnh build/test thật vào CI và tài liệu.
- Sau khi push, kiểm tra GitHub Actions cho đúng SHA của commit mới nhất. Đọc log job lỗi, sửa nguyên nhân rồi kiểm tra lại nếu có quyền truy cập.
- Không báo thành công khi workflow còn chờ/chạy hoặc không đọc được kết quả. Phân biệt kiểm tra cấu trúc repo với build/test ứng dụng.
- Khi có ứng dụng chạy được, kiểm tra luồng sử dụng liên quan; ghi rõ luồng đã thử và các phần chưa kiểm tra.
- Không tự merge hoặc triển khai nếu chưa được người dùng yêu cầu.
- Không ghi token, mật khẩu hoặc API key vào repo.
- Báo cáo ngắn gọn: thay đổi, kiểm tra đã chạy, link CI/PR và hạn chế còn lại.
