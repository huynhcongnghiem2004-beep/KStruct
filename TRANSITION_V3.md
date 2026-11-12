# Chuyển tiếp thép cột V3

Khi hai tầng có số lượng thanh dọc khác nhau, V3 ghép tối đa các cặp theo thứ tự quanh chu vi, giữ ghép góc với góc và tối thiểu hóa tổng khoảng cách ngang. Các thanh không ghép được kết thúc ở mạch ngừng: thanh dư tầng dưới dừng trước đầu cột trong phạm vi cover/đai; thanh dư tầng trên bắt đầu sau cover/đai ở chân cột.

Đây là quy tắc triển khai hình học theo lựa chọn của người dùng, không phải kiểm tra thiết kế TCVN. Bản vẽ và hộp thoại xác nhận phải nêu số cặp ghép và số thanh dư. Kỹ sư kết cấu phải kiểm tra nội lực, vị trí cắt, neo thanh dư, cốt đai vùng nối, nút dầm-sàn và khả năng thi công trước khi phát hành. Không hỗ trợ thay đổi Cx/Cy nếu các cặp ghép gần nhất vượt các giới hạn hình học hiện có; khi đó lệnh dừng trước khi sửa mô hình.

Ở vùng nối chồng, đầu thanh tầng trên được nhấn vào lõi một đoạn bằng tổng bán kính hai thanh cộng khe hở cấu hình 2 mm, nối song song, rồi chuyển xiên về vị trí danh định. Khả năng đặt vị trí này được kiểm tra theo vùng tâm thép thực tế từng thanh; không dùng kiểm tra bán kính lõi chung để chặn trước phương án. Nếu không đủ chỗ, lệnh báo cặp thanh cụ thể và dừng trước khi sửa mô hình.
