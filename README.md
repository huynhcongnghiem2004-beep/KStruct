# KStruct

Repository đang được khởi tạo; chưa có mã nguồn ứng dụng hoặc lệnh build/test ứng dụng.

## Quy trình phát triển

1. Người dùng mô tả tính năng hoặc lỗi cần xử lý.
2. Codex đọc mã nguồn, đề xuất phương án và thực hiện thay đổi trên nhánh riêng.
3. Mở pull request để GitHub Actions chạy các kiểm tra đã cấu hình.
4. Trong phiên làm việc, Codex kiểm tra kết quả cho đúng commit, đọc log và sửa lỗi nếu cần.
5. Báo cáo thay đổi, kết quả kiểm tra và hạn chế; người dùng quyết định merge.

## GitHub Actions

Workflow `Repository checks` chạy khi push, mở/cập nhật pull request hoặc chạy thủ công. Workflow chỉ kiểm tra các tệp nền và lỗi khoảng trắng trong thay đổi. Đây chưa phải build/test ứng dụng.

Khi có mã nguồn, bổ sung lệnh cài dependency, lint, build và test phù hợp với công nghệ thực tế, cùng artifact nếu cần.

## Codex kiểm tra lại

GitHub Actions tự chạy theo sự kiện đã cấu hình. Workflow này không tự gọi Codex và không cần API key. Codex đọc kết quả và xử lý lỗi trong phiên làm việc được người dùng giao; chưa có dịch vụ Codex chạy nền sau khi phiên kết thúc.
