# Auto Rebar Tag Free End

KStruct tạo Rebar Tag theo hệ tọa độ của View, không dùng trục X/Y toàn cục để chọn trái/phải.

## Quy tắc đã tích hợp

- Tag thép chủ, Tag thép đai ở mặt cắt ngang và Tag hình khai triển mặt cắt dọc dùng Leader `Free`.
- Khoảng cách từ Tag đến cấu kiện là kích thước trên giấy: mặc định 15 mm, quy đổi theo tỷ lệ View.
- Khoảng cách đứng tối thiểu giữa các đầu Tag cùng cột căn là 5 mm trên giấy.
- Tag giữ Leader ngang khi không va chạm. Khi đầu Tag phải dịch đứng, KStruct tạo elbow và giữ đoạn cuối nằm ngang.
- Token `P`/`T` trong Type hoặc Family tiếp tục chọn bên phải/trái; token trong Type ưu tiên hơn Family để tương thích cấu hình cũ.
- Lõi bố trí hỗ trợ `Left`, `Right`, `BothSides` và `Auto`. `BothSides` chọn bên theo vị trí neo và cân bằng các neo trên tim; `Auto` ưu tiên biên gần nhất rồi cân bằng trường hợp bằng nhau.

## Giới hạn kiểm chứng

Build và kiểm thử chính sách bố trí không thay thế kiểm thử trong Revit. Cần kiểm tra Family Tag thực tế có hỗ trợ `LeaderEndCondition.Free`, Reference của từng Rebar Set, kích thước chữ thật sau regenerate, cột xoay và các View có hướng nhìn khác trục toàn cục. Nếu Family không hỗ trợ Free End, lệnh dừng và rollback thay vì tạo Tag sai dạng.
