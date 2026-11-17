# Hướng dẫn bố trí và vẽ thép móng cọc

**Cẩm nang triển khai bản vẽ kết cấu**  
Phiên bản 1.0 · 07/11/2026 · Tiếng Việt

> **Mục đích và giới hạn:** Tài liệu hướng dẫn triển khai, trình bày và kiểm tra bản vẽ cốt thép lồng cọc và đài cọc. Đây không phải hồ sơ tính toán, bản vẽ thi công đã phê duyệt hay chỉ dẫn thay thế kỹ sư thiết kế. Không tự suy ra đường kính, số lượng, bước thép, chiều dài neo, kích thước đài, chiều dài cọc hoặc cấp vật liệu khi hồ sơ thiếu dữ liệu.

## 1. Phạm vi và nguyên tắc sử dụng

Hướng dẫn tập trung vào móng cọc bê tông cốt thép đổ tại chỗ, gồm lồng thép cọc khoan nhồi và đài cọc đơn/nhóm cọc, có hoặc không có giằng móng. Cấu tạo cụ thể phải theo loại cọc, mô hình tính, thiết kế được duyệt và biện pháp thi công của dự án.

### 1.1 Thứ tự ưu tiên

1. Dùng bản vẽ kết cấu và thuyết minh/tính toán đã được phê duyệt; đối chiếu tải trọng, cao độ, lưới trục, loại cọc và mã đài.
2. Tuân theo chỉ dẫn kỹ thuật, yêu cầu dự án và biện pháp thi công đã thống nhất.
3. Áp dụng tiêu chuẩn hiện hành được hồ sơ viện dẫn; ghi đúng phiên bản.
4. Khi mặt bằng, mặt cắt, bảng thống kê hoặc ghi chú mâu thuẫn, phát hành yêu cầu làm rõ (RFI), chờ kỹ sư chủ trì xác nhận rồi mới phát hành bản vẽ ảnh hưởng.

### 1.2 Phân biệt dữ kiện dự án với hướng dẫn chung

Các ghi nhận ở Mục 9 được đọc từ hồ sơ `Mr.Khoa-Full-28.04.2026.pdf`. Chúng chỉ là dữ kiện nhận dạng để phối hợp, không phải chỉ định cốt thép cho thiết kế mới. Cần xác nhận bản kết cấu mới nhất và trạng thái phê duyệt.

## 2. Tiêu chuẩn tham khảo

| Tiêu chuẩn | Vai trò | Lưu ý |
|---|---|---|
| **TCVN 10304:2025 — Móng cọc — Tiêu chuẩn thiết kế** | Thiết kế nền móng cọc; phân loại đài, liên kết cọc–đài, yêu cầu khảo sát và cấu tạo. | Bản 2025 thay TCVN 10304:2014. Kiểm tra phiên bản được dự án chấp thuận. |
| **TCVN 5574:2018 — Kết cấu bê tông và bê tông cốt thép — Tiêu chuẩn thiết kế** | Tính toán và cấu tạo bê tông cốt thép: lớp bảo vệ, khoảng cách, neo/nối, đài và vùng nút. | Cốt thép đài theo nội lực và thiết kế; không có sơ đồ chung đúng cho mọi đài. |
| **TCVN 5572:2012 — Bản vẽ xây dựng và kết cấu — Nguyên tắc chung về trình bày bản vẽ kết cấu** | Nguyên tắc lập và trình bày bản vẽ kết cấu. | Thể hiện rõ hình chiếu, mặt cắt, kích thước và ký hiệu. |
| **TCVN 6084:2012 — Bản vẽ xây dựng — Thể hiện cốt thép bê tông** | Ký hiệu, hình dạng thanh, kích thước và bảng thống kê. | Mã thanh phải khớp giữa mặt bằng, mặt cắt và bảng. |
| **TCVN 9362:2012 — Tiêu chuẩn thiết kế nền nhà và công trình** | Tham khảo cho nền/móng nông khi cần phối hợp. | Phạm vi tiêu chuẩn loại trừ móng cọc; không dùng thay TCVN 10304. |
| **TCVN 9395:2012 — Cọc khoan nhồi — Thi công và nghiệm thu** | Thi công/nghiệm thu cọc khoan nhồi đường kính từ 600 mm trở lên. | Kiểm tra phạm vi đường kính; không mặc nhiên áp dụng nguyên vẹn cho cọc nhỏ hơn 600 mm. |

Tra cứu hiệu lực tại [Cổng thông tin tiêu chuẩn quốc gia VSQI](https://tieuchuan.vsqi.gov.vn/). Kiểm tra danh mục tiêu chuẩn được dự án chấp thuận và tình trạng sửa đổi/thay thế tại thời điểm phát hành.

## 3. Hồ sơ đầu vào cần khóa trước khi vẽ

| Nhóm | Dữ liệu cần có | Đối chiếu |
|---|---|---|
| Hình học/cao độ | Lưới trục; cột/vách; cao độ đáy/đỉnh đài, mặt đất; kích thước đài/giằng; bê tông lót. | Kiến trúc, kết cấu, hố thang/bể và cao độ móng. |
| Cọc | Mã và tọa độ/tâm cọc; đường kính/tiết diện; chiều dài hoặc cao độ mũi; sức chịu tải thiết kế; phương pháp thi công. | Mặt bằng định vị, bảng cọc, địa chất, thuyết minh. |
| Tải trọng/liên kết | Nội lực chân cột/vách; lực dọc, mô men, lực cắt; tổ hợp; giả thiết liên kết cọc–đài. | Mô hình tính và chi tiết nút cột–đài–cọc. |
| Vật liệu | Cấp bê tông; nhóm thép; tiêu chuẩn vật liệu; yêu cầu nối/hàn; chống ăn mòn nếu có. | Ghi chú chung và bảng vật liệu; không trộn trị số cường độ chưa xác nhận. |
| Thi công | Dung sai cọc; cắt đầu cọc; nối lồng; ống siêu âm/bơm vữa; trình tự đổ; điều kiện nước ngầm. | Biện pháp thi công và yêu cầu nghiệm thu đã duyệt. |

> **Điểm dừng phát hành:** TCVN 10304:2025 yêu cầu thiết kế dựa trên kết quả khảo sát địa kỹ thuật đầy đủ. Nếu thiếu dữ liệu khảo sát đủ tin cậy hoặc chiều dài cọc còn chờ thử/điều chỉnh, ghi trạng thái chưa phát hành thi công cho phần bị ảnh hưởng và yêu cầu kỹ sư chủ trì xác nhận. Thử cọc không tự thay thế khảo sát địa chất đầy đủ.

## 4. Trình tự bố trí và vẽ thép đài cọc

### Bước 1 — Kiểm tra lưới cọc và mã đài

- Gắn mã đài và nhóm cọc nhất quán; xác định trục định vị và hướng nhìn.
- Kiểm tra tim cọc, khoảng cách tim–tim, độ lệch tâm so với cột/vách, biên đài, giằng móng và khoảng hở thi công.
- Thể hiện tọa độ/kích thước định vị đủ để trắc đạc; không chỉ ghi kích thước phủ bì.
- Nếu có cọc xiên, cọc lệch, cọc thử hoặc cọc phải xử lý, thể hiện riêng và dẫn chiếu chi tiết được duyệt.

### Bước 2 — Chốt hình học đài và bê tông

- Vẽ đường bao đài, cột/vách, tâm cọc, giằng móng, cổ cột, lỗ/ống xuyên.
- Ghi chiều dày đài, cao độ đỉnh/đáy; phân biệt bê tông lót với bê tông kết cấu.
- Xác định lớp bảo vệ theo môi trường, bề mặt đổ và yêu cầu thiết kế; không dùng một trị số chung cho mọi mặt.
- Rà khoảng trống cho thanh, đầu neo, đai cọc, thép chờ và đường kính cốt thép; phối hợp khả năng đổ/đầm bê tông.

### Bước 3 — Bố trí thép đáy hai phương

- Thể hiện hai phương vuông góc bằng ký hiệu riêng; nêu rõ phương song song trục nào hoặc phương X/Y.
- Ghi đường kính, bước, số thanh/phạm vi bố trí đúng thiết kế và thống nhất cách ghi trong hồ sơ.
- Chỉ rõ lớp trên/lớp dưới tại giao điểm; xác định thanh liên tục, thanh ngắt và đầu móc nếu có.
- Lượng và vị trí thép lấy từ mô hình nội lực và kiểm tra uốn, cắt/chọc thủng, cấu tạo; không tự áp một lưới thép mẫu.
- Kiểm tra không xung đột với đầu cọc, thép chờ cột, giằng, ống kỹ thuật và vùng cắt đầu cọc.

### Bước 4 — Thép lớp trên, thép tăng cường và vùng cột

- Bổ sung thép trên theo kết quả tính toán, mô men cục bộ, lực nhổ, liên kết cứng hoặc yêu cầu cấu tạo.
- Vẽ thép tăng cường quanh chân cột/vách, vùng neo thép chờ và vùng tập trung lực theo thiết kế.
- Không mặc định mọi đài đều có cùng lưới thép trên; cũng không bỏ thép trên nếu sơ đồ chịu lực yêu cầu.
- Với giằng móng nối đài, thể hiện thép dọc, đai, neo vào đài và thứ tự lớp tại giao điểm.

### Bước 5 — Liên kết đầu cọc với đài

- Thể hiện cao độ cắt đầu cọc, phần bê tông cần loại bỏ, chiều dài thép chờ/đoạn cọc neo vào đài và hình dạng đầu thanh.
- Phân biệt gối tựa khớp/tựa tự do với liên kết cứng theo mô hình thiết kế. TCVN 10304:2025 yêu cầu cấu tạo neo/đoạn chôn phù hợp trạng thái liên kết và được tính/kiểm tra tương ứng.
- Kiểm tra chọc thủng và truyền lực quanh đầu cọc, nhất là khi giả thiết liên kết cứng hoặc đầu cọc chôn sâu vào đài.
- Không áp dụng chiều dài chôn 5–10 cm cho mọi trường hợp: mức này thuộc điều kiện liên kết tự do/khớp; trường hợp khác cần tính toán và chi tiết được duyệt.

## 5. Vẽ lồng thép cọc khoan nhồi

### 5.1 Mặt đứng/mặt cắt dọc

- Ghi đường kính cọc, chiều dài hoặc cao độ mũi, chiều dài lồng, cao độ đầu lồng và phạm vi đoạn gia cường.
- Vẽ thép chủ dọc phân bố quanh chu vi; ghi số lượng, đường kính, nhóm thép và chiều dài từng nhóm thanh.
- Vẽ đai xoắn/đai vòng; ghi đường kính và bước theo từng đoạn (đầu cọc, thân, đoạn nối, mũi) đúng tính toán.
- Thể hiện vòng tăng cứng, giằng lồng, con kê định tâm, điểm treo/nâng và cấu tạo nối lồng theo biện pháp thi công.
- Thể hiện ống siêu âm, ống bơm vữa hoặc phụ kiện kiểm tra theo dự án; ghi số lượng, đường kính, vị trí và cách cố định.

### 5.2 Mặt cắt ngang lồng

- Vẽ đủ thép chủ, vòng đai, vòng tăng cứng, ống siêu âm và con kê; ghi đường kính theo cùng quy ước.
- Ghi lớp bảo vệ từ mặt ngoài bê tông đến bề mặt ngoài cốt thép ngoài cùng; làm rõ kích thước con kê và vị trí lắp.
- Bố trí ống và vòng tăng cứng không cản dòng bê tông, ống tremie hoặc thiết bị thi công; kiểm tra khe hở theo cốt liệu và quy trình đổ.
- Đặt đủ con kê quanh chu vi và theo cao độ để lồng không chạm thành hố khoan; chi tiết phù hợp phương pháp thi công.

### 5.3 Nối, nâng và hạ lồng

Ghi rõ kiểu nối đã được thiết kế/biện pháp chấp thuận (nối chồng, ống nối cơ khí hoặc hàn khi được phép), chiều dài/chi tiết nối và bố trí so le. Không tập trung mối nối tại cùng một mặt cắt nếu hồ sơ yêu cầu so le. Kiểm tra độ cứng lồng khi nâng, điểm cẩu, thanh gia cường và khả năng hạ lồng không va thành hố.

## 6. Bộ hình và bảng nên có

| Hình/bảng | Nội dung tối thiểu | Lỗi cần tránh |
|---|---|---|
| Mặt bằng định vị cọc/đài | Trục, tọa độ/tim cọc, mã cọc/đài, kích thước, cột/vách, giằng, cao độ tham chiếu. | Thiếu gốc định vị; mã không khớp bảng; không đọc được kích thước tim cọc. |
| Mặt bằng thép đáy | Hai phương, mã thanh, đường kính/bước/số lượng, phạm vi, lớp trên/dưới, mặt cắt tham chiếu. | Không rõ phương; không phân biệt lớp; ký hiệu trùng. |
| Mặt bằng thép trên/tăng cường | Vị trí, phạm vi, số lượng, neo và giao cắt; liên hệ cột/vách/giằng. | Bỏ sót thép tăng cường; nét thép che lấp định vị cọc. |
| Mặt cắt qua đài và cọc | Chiều dày, lớp bảo vệ, cao độ, neo cọc, thép chờ cột, hai lớp thép, bê tông lót. | Mặt cắt không qua đầu cọc; không rõ trình tự lớp. |
| Chi tiết lồng cọc | Mặt đứng, mặt cắt ngang, bước đai theo đoạn, vòng tăng cứng, nối lồng, ống, con kê. | Thiếu phân đoạn đai; không chỉ cách treo/nối. |
| Bảng thống kê thép | Mã thanh, hình dạng, đường kính, số lượng, chiều dài cắt, chiều dài/khối lượng theo quy ước. | Bảng không khớp bản vẽ; sai đơn vị; thiếu chiều dài uốn/móc. |

## 7. Ký hiệu, kích thước và bảng thống kê

Mỗi nhóm thanh cần mã duy nhất trong phạm vi bản vẽ/dự án. Dùng ký hiệu thống nhất cho thép trên/dưới và phương X/Y; nếu dùng mặt cắt để chỉ vị trí, ghi hướng nhìn. Bảng thống kê tóm tắt thiết kế, không sửa ngầm thiếu sót ở mặt bằng hoặc mặt cắt.

| Trường | Dạng ghi | Lưu ý |
|---|---|---|
| Mã thanh | T01, T02, C01… | Không tái dùng mã gây nhầm. |
| Đường kính/bước | Ø… a… hoặc Ø… @… | Điền đúng thiết kế; thống nhất mm. |
| Số lượng/phạm vi | n thanh; phân bố đều; từ trục … đến … | Chỉ ghi số thanh khi xác định; dùng kích thước định vị. |
| Chiều dài cắt | L = …; hình dạng thanh | Đối chiếu uốn, neo/móc và quy tắc chế tạo. |
| Vật liệu | CB… theo ghi chú dự án | Không suy ra nhóm thép từ chi tiết khác khi ghi chú mâu thuẫn. |

Trước khi phát hành, đối chiếu từng mã thanh từ mặt bằng → mặt cắt → bảng; kiểm tra số lượng theo kích thước/bước. Tách bảng thép cọc và thép đài nếu cần; ghi rõ cấu kiện áp dụng và số cấu kiện lặp. Tính khối lượng theo quy trình thống kê của dự án, lưu ý thanh khác chiều dài, đoạn nối và quy ước hao hụt.

## 8. Kiểm tra thiết kế và thi công

### 8.1 Kiểm tra kết cấu — kỹ sư thiết kế xác nhận

- Đài đủ khả năng chịu uốn, cắt một phương, chọc thủng quanh cột/vách và quanh đầu cọc theo mô hình tính.
- Lực phân phối lên từng cọc phù hợp tổ hợp tải, độ lệch tâm và độ cứng đài; xét tải ngang/lực nhổ nếu có.
- Neo thép đáy, thép trên, thép cột/vách và liên kết cọc–đài đủ theo trạng thái liên kết được thiết kế.
- Cấu tạo nút và đường truyền lực được kiểm tra; không coi đài như bản phẳng nếu sơ đồ làm việc là giàn chống–thanh kéo/cấu kiện sâu.
- Sức chịu tải và biến dạng cọc dựa trên khảo sát, tính toán và thí nghiệm/kiểm tra theo chỉ dẫn dự án.

### 8.2 Kiểm tra lắp dựng/đổ bê tông

- Có phương án lắp, nối và hạ lồng; lồng đủ cứng khi nâng, không biến dạng làm giảm lớp bảo vệ.
- Khoảng hở thanh/bó thép cho bê tông đi qua và phù hợp phương pháp đổ/đầm; không xung đột ống tremie/ống kiểm tra.
- Thép đài không cản đầu cọc, neo cột, giằng, bu lông chờ hoặc đường ống; xem xét thứ tự lắp dựng.
- Con kê và ghế kê thép lớp trên ổn định khi đi lại/đổ bê tông và giữ đúng lớp bảo vệ.
- Ghi chỉ dẫn cắt đầu cọc, vệ sinh, xử lý mối nối và kiểm tra cao độ theo biện pháp.

### 8.3 Checklist phát hành

- [ ] Đúng phiên bản bản vẽ kiến trúc/kết cấu và lưới trục.
- [ ] Mã cọc, mã đài, tọa độ, kích thước, cao độ và hướng mặt cắt khớp nhau.
- [ ] Vật liệu và lớp bảo vệ thống nhất giữa ghi chú, chi tiết và bảng.
- [ ] Các lớp thép, phương thép, bước, số lượng, phạm vi và neo rõ ràng.
- [ ] Liên kết cọc–đài và cột/vách–đài đúng giả thiết thiết kế.
- [ ] Lồng cọc đủ thép dọc, đai theo đoạn, vòng tăng cứng, con kê, ống và chi tiết nối.
- [ ] Không có xung đột hình học; khả năng thi công/đổ bê tông đã được xem xét.
- [ ] Yêu cầu làm rõ đã đóng; bản vẽ đã kiểm tra độc lập và duyệt phát hành.

## 9. Ghi nhận khi đối chiếu hồ sơ Mr.Khoa

| Ghi nhận từ hồ sơ | Ý nghĩa khi triển khai | Việc cần xác nhận |
|---|---|---|
| Cọc khoan nhồi D300, L=18 m; sức chịu tải ghi 30 t/cọc. | Dữ liệu nhận dạng để kiểm tra phối hợp, không đủ để xác nhận sức chịu tải thực tế. | Đối chiếu địa chất, kết quả thí nghiệm/kiểm tra, tải trọng và chiều dài cọc được duyệt. |
| Mặt bằng gắn nhãn P1–P26; ghi chú nói phần nhà chính P1–P24. | P25–P26 có thể thuộc cấu kiện/khu vực khác hoặc ghi chú riêng. | Làm rõ phạm vi, cấu kiện chịu tải, mã cọc và bảng thống kê. |
| Ghi chú hồ sơ nêu chưa có khảo sát địa chất và cần khoan thử ít nhất 2 vị trí xác định chiều dài thực tế. | Chiều dài/sức chịu tải còn cần kiểm chứng; không tự lấy ghi chú này làm căn cứ phát hành cuối cùng. | Có báo cáo khảo sát đầy đủ và thiết kế cập nhật được duyệt; thử cọc theo đề cương chấp thuận. |
| Trị số Rs cho CB240-T/CB300-V khác nhau giữa trang 46 và chi tiết cọc trang 47. | Có mâu thuẫn vật liệu/cách ghi giữa ghi chú tổng quát và chi tiết. | RFI/kỹ sư chủ trì xác nhận nhóm thép và chỉ tiêu chính thức; sửa đồng bộ hồ sơ. |

> **Không nội suy thiết kế:** Các dữ kiện trên không xác định được số lượng thép dọc cọc, đường kính/bước đai, đường kính lồng, thép đài, kích thước đài, chiều dài neo hoặc khả năng chịu lực. Cần bản vẽ/tính toán kết cấu đã phê duyệt.

## 10. Nguồn tham khảo

- [TCVN 10304:2025 — VSQI](https://tieuchuan.vsqi.gov.vn/tieuchuan/view?sohieu=TCVN+10304%3A2025)
- [TCVN 5574:2018 — VSQI](https://tieuchuan.vsqi.gov.vn/tieuchuan/view?sohieu=TCVN+5574%3A2018)
- [TCVN 5572:2012 — VSQI](https://tieuchuan.vsqi.gov.vn/tieuchuan/view?sohieu=TCVN+5572%3A2012)
- [TCVN 6084:2012 — VSQI](https://tieuchuan.vsqi.gov.vn/tieuchuan/view?sohieu=TCVN+6084%3A2012)
- [TCVN 9362:2012 — VSQI](https://tieuchuan.vsqi.gov.vn/tieuchuan/view?sohieu=TCVN+9362%3A2012)
- [TCVN 9395:2012 — VSQI](https://tieuchuan.vsqi.gov.vn/tieuchuan/view?sohieu=TCVN+9395%3A2012)
- [Toàn văn tham khảo TCVN 5574:2018](https://oct.vn/wp-content/uploads/2020/08/TCVN5574-2018.pdf)
- [Bản toàn văn tham khảo TCVN 10304:2025](https://static3.luatvietnam.vn/uploaded/vietlawfile/2025/7/tcvn_10304_2025_250725083747.pdf)

Trang VSQI dùng để tra cứu tên, tình trạng tiêu chuẩn. Các bản PDF toàn văn ngoài cổng VSQI chỉ là nguồn tham khảo điều khoản; hồ sơ dự án cần dùng bản chính thức hoặc bản được tổ chức có thẩm quyền cung cấp.

## Kết luận sử dụng

Bản vẽ cốt thép móng cọc dùng được tại công trường phải đúng thiết kế, đủ thông tin định vị/cao độ/cấu tạo, có bảng thống kê khớp và có thể lắp dựng/đổ bê tông. Khi thiếu dữ liệu địa chất, mâu thuẫn vật liệu hoặc chưa rõ liên kết cọc–đài, yêu cầu kỹ sư chủ trì xác nhận trước khi phát hành phần bị ảnh hưởng.
