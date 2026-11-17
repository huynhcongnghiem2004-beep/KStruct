# Hồ sơ chiều dài nối KStruct V3 — B20 / CB400-V

Nguồn: TCVN 5574:2018, Bảng 7, Bảng 13, 6.1.2.3, 10.3.5.4 và 10.3.6.2.
https://www.rds.com.vn/TCXD1/TCVN5574-2018.pdf

Phạm vi: bê tông nặng thông thường B20; CB400-V có gân cán nóng; không ứng suất trước.
Người dùng không nêu yêu cầu kháng chấn riêng. Không tự nhận dạng vật liệu từ family Revit.

Giả thiết cho tính chiều dài: chịu kéo; 100% nối trong một vùng; As,cal/As,ef=1;
không giảm vì neo bổ sung. Dùng hệ số thời hạn tải trọng 0.9 để xét trường hợp dài hạn.
Rs=350 MPa; Rbt bảng=0.9 MPa; Rbt hiệu dụng=0.81 MPa; eta1=2.5;
eta2=1 khi D<=32; eta2=0.9 cho D36/D40; alpha=2.

Rbond=eta1*eta2*Rbt hiệu dụng.
L0=Rs*D/(4*Rbond).
Lnối=max(alpha*L0,0.4*alpha*L0,20D,250 mm).
Lchờ=ceil(max(H tầng trên/4 hoặc chiều dài người dùng, Lnối)/10)*10 mm.
H/4 và làm tròn 10 mm là lựa chọn triển khai của dự án, không phải công thức TCVN.

So le +15% bị tắt; không coi đó là phân vùng nối đáp ứng giới hạn tỷ lệ thanh nối.
Thép tầng trên bắt đầu ở mạch ngừng và chuyển tiếp sau đoạn chờ.
Khi cùng tuyến thay đổi Cx/Cy, đường kính, lớp bảo vệ hoặc tiết diện, chưa hỗ trợ ghép thanh.

Kết quả kiểm thử chiều dài: D16=1390, D20=1730, D25=2170,
D32=2770, D36=3460, D40=3850 mm. Chặn D>40, số không hữu hạn,
D<=0 và đường kính ngoài nhánh đã hỗ trợ. Kiểm thử max(H/4,Lnối) và giữ yêu cầu dài hơn đã đạt.

Chưa xác nhận toàn bộ TCVN: nội lực thực tế, sức chịu lực đai trong vùng nối,
khoảng cách các cặp nối kề nhau, bán kính uốn thực tế theo RebarBarType,
nút dầm/sàn, yêu cầu kháng chấn và điều kiện thi công. Không ghi nhãn 'đạt TCVN' cho toàn cấu tạo.
Cần kiểm tra trực tiếp hình học trong Revit trước khi phát hành shopdrawing.
