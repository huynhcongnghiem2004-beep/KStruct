# Đặc tả và kế hoạch triển khai code: Thép móng cọc, móng đơn

**Tên file bàn giao:** `trienkhaithepmong.md`  
**Ngày:** 07/11/2026  
**Dự án mục tiêu:** KStruct — Revit 2024, .NET Framework 4.8, WPF, x64  
**Trạng thái:** Đặc tả để lập trình; chưa phải hồ sơ thiết kế kết cấu hay bản vẽ thi công được duyệt.

## 1. Mục tiêu

Phát triển một lệnh trong add-in KStruct để người dùng chọn móng/đài cọc đã có trong mô hình Revit, nhập hoặc rà soát thông số cốt thép từ hồ sơ thiết kế được duyệt, xem trước bố trí, rồi tạo cốt thép Rebar native trong Revit.

Phạm vi có hai nhóm:

1. **Móng đơn dưới cột**: lưới thép đáy theo hai phương; thép trên hoặc thép tăng cường chỉ khi được nhập/được thiết kế yêu cầu; thép chờ cột là hạng mục tùy chọn có thông số neo được cung cấp.
2. **Đài cọc**: lưới thép đáy hai phương; lớp thép trên/tăng cường tùy chọn; thép chờ cột/vách tùy chọn; liên kết đầu cọc được mô hình theo cấu tạo đã duyệt. Không tự tạo cốt thép lồng cọc trong phiên bản đầu nếu chưa có yêu cầu và dữ liệu thiết kế cọc riêng.

Mục tiêu là **tự động hóa dựng hình theo dữ liệu thiết kế**, không tính thiết kế móng, không chọn phương án nền/cọc, không tự quyết định đường kính, bước, số lượng, neo/nối hoặc sức chịu tải.

## 2. Bối cảnh mã nguồn hiện tại

- Dự án chính là `KStruct-diagnostics`, Revit 2024, `.NET Framework 4.8`, WPF, x64.
- Ribbon hiện có nút “Móng dưới cột” đăng ký tới `DrawFootingsCommand` trong `App.cs`. Lệnh hiện tại chỉ đặt một Family Structural Foundation dưới cột, chọn Family Type đầu tiên; chưa tạo thép móng.
- Tệp `vethepmong.md` là nguồn yêu cầu kỹ thuật/biên tập đầu vào hiện có. Cần giữ nguyên nguyên tắc không suy diễn thông số thiết kế khi thiếu dữ liệu.
- Build dự án theo hướng dẫn `KStruct-diagnostics/README_INTEGRATION.md`; mặc định Revit API ở `C:\Program Files\Autodesk\Revit 2024\`.
- Trước khi sửa code, kiểm tra lại checkout làm việc thực tế, nhánh, file dự án, lệnh móng đang đăng ký trong `App.cs`, và xác nhận bản code nào là nguồn chuẩn. Không sửa thư mục diagnostics/port hoặc file tạm nếu không phải checkout triển khai.

## 3. Đối tượng sử dụng và luồng thao tác

1. Chọn lệnh **Thép móng** trên ribbon.
2. Chọn một hoặc nhiều Structural Foundation trong mô hình hoặc dùng Selection hiện có.
3. Chọn loại: móng đơn hoặc đài cọc (loại có thể đề xuất từ thông tin người dùng; không tự suy luận nếu phân loại không rõ).
4. Cửa sổ WPF hiển thị hình học host và các trường cấu hình. Người dùng chọn `RebarBarType` từ project, nhập đường kính/bước hoặc số lượng, lớp bảo vệ từng mặt, lớp thép, hướng, phạm vi, thép tăng cường và tùy chọn thép chờ.
5. Bấm **Xem trước** để thấy số thanh, vùng bố trí, khoảng cách thực tế, chiều dài dự kiến và cảnh báo hình học.
6. Bấm **Tạo thép**; lệnh kiểm tra đầu vào lần cuối, tạo Rebar trong một Transaction/TransactionGroup có kiểm soát và gán Partition/ghi chú nhận diện.
7. Báo kết quả theo từng host: thành công, bỏ qua, hoặc lỗi có lý do. Nếu có lỗi giữa chừng, không để lại cấu kiện tạo dở.

Cancel hoặc đóng cửa sổ không làm thay đổi mô hình. Mặc định không ghi đè Rebar đã tạo trước đó. Nếu yêu cầu ghi đè được bật, chỉ được xóa các thanh do chính lệnh này tạo cho đúng host và đúng mã nhận diện; không được xóa thép thủ công, thép từ add-in khác hoặc thép thuộc host khác.

## 4. Yêu cầu chức năng

### 4.1 Chọn và nhận diện host

- Chấp nhận host thuộc `OST_StructuralFoundation`; cho phép chọn một hoặc nhiều móng.
- Mỗi host phải có `BoundingBox`, mặt trên/dưới hợp lệ, hình học hỗ trợ và mức/độ xoay có thể xác định.
- Phiên bản đầu hỗ trợ móng hộp/chữ nhật hoặc hình học phẳng, lồi được kiểm tra rõ. Với family có hình dạng bất thường, mặt cong/vát, nhiều khối rời, lỗ lớn hoặc mặt nghiêng không hỗ trợ, từ chối tạo và ghi lý do cụ thể.
- Không thay đổi Family, kích thước, vị trí, cao độ, host hoặc loại móng.

### 4.2 Nhập cấu hình thép

Mỗi cấu hình Rebar cần có tối thiểu:

- Loại thép Revit `RebarBarType` đã tồn tại trong tài liệu.
- Hướng (X/Y hoặc song song/vuông góc trục host), lớp (dưới/trên/tăng cường), mặt đặt và cao độ tương đối.
- Cách phân bố: khoảng cách tối đa hoặc số thanh; thống nhất rõ quy tắc suy ra số lượng từ khoảng cách để không vượt bước khai báo.
- Lớp bảo vệ ở cạnh, đáy và mặt trên liên quan.
- Phạm vi bố trí/điểm bắt đầu và kết thúc; kiểu đầu thanh (thẳng/móc) chỉ khi Revit bar shape/hook type và dữ liệu thiết kế cho phép.
- Với thép chờ: vị trí, số lượng, phương, chiều cao nhô, chiều dài neo/đoạn uốn và loại móc lấy từ thiết kế; không tự lấy công thức ngầm.

Có thể lưu preset theo người dùng ở phiên bản sau. Không lưu giá trị mặc định có thể bị hiểu là thiết kế đã xác nhận. Nếu có giá trị gợi ý giao diện, phải ghi rõ là gợi ý và bắt buộc người dùng xác nhận trước khi tạo.

### 4.3 Sinh Rebar

- Tạo Rebar native bằng Revit API, host đúng móng đã chọn, đặt trong đúng document/phase/workset theo ngữ cảnh hiện hành.
- Đơn vị giao diện mm; chuyển sang internal units ở biên của lớp adapter. Không trộn mm với feet trong thuật toán hình học.
- Tạo riêng từng nhóm thép để dễ kiểm tra, schedule và sửa; gán Partition theo mã lệnh/host nếu API cho phép.
- Tạo shape-driven rebar khi hình dạng và hướng host phù hợp; nếu dùng free-form phải có tiêu chí hỗ trợ riêng và không tự chuyển phương thức âm thầm.
- Dùng RebarBarType có sẵn. Không tự tạo/đổi thuộc tính type, không sửa hook/bar shape trong project.
- Mọi vị trí/curve phải hữu hạn, nằm trong host theo cover đã nhập, không tự cắt xuyên ra ngoài hình học móng, và không giao cắt bất hợp lý với nhóm thép khác theo kiểm tra hình học có thể thực hiện.

### 4.4 Kết quả và nhận diện lần tạo

- Thông báo số host được xử lý, số nhóm thép/thanh tạo và các host bị bỏ qua.
- Ghi nhật ký ngắn gọn: ElementId host, loại móng, cấu hình đã dùng, thời gian, kết quả và thông báo lỗi.
- Nhận diện thanh do lệnh tạo bằng `Comments`, `Partition` hoặc Extensible Storage có schema ổn định; không chỉ dựa vào tên hiển thị có thể thay đổi.
- Hỗ trợ Undo Revit chuẩn. Không triển khai rollback bằng cách xóa hàng loạt đối tượng ngoài Transaction hiện hành.

## 5. Yêu cầu kỹ thuật phi chức năng

- Tuân thủ Revit API 2024, .NET Framework 4.8, x64; tránh dependency mới nếu chưa có trong dự án.
- Lệnh Revit đặt `TransactionMode.Manual`; mọi sửa đổi tài liệu chỉ xảy ra trong API context hợp lệ.
- Tách UI khỏi logic hình học và tạo Rebar; không truy cập Document từ thread nền/WPF worker.
- Dùng `Transaction`/`SubTransaction` phù hợp; kiểm tra `Commit()` result. Khi lỗi, rollback và báo rõ; tránh trạng thái tạo một nửa.
- Bắt lỗi theo host để báo cáo, nhưng đảm bảo chiến lược nguyên tử được xác định: mặc định một host lỗi không làm mất kết quả host khác chỉ khi từng host có transaction độc lập; nếu dùng transaction group toàn bộ thì rollback toàn nhóm. Chọn và ghi rõ chiến lược trước khi code.
- Giao diện tiếng Việt, đơn vị mm, validation inline; không đóng băng Revit khi duyệt danh sách host thông thường.
- Không ghi đè các cài đặt toàn cục, không thay đổi template, family, view hay style nếu người dùng không yêu cầu.

## 6. Kiến trúc/mô-đun đề xuất

Tên file dưới đây là gợi ý; trước khi thêm, kiểm tra quy ước namespace và cấu trúc hiện tại. Không tạo nhiều bản sao command.

| Mô-đun | Trách nhiệm |
|---|---|
| `FoundationRebarCommand` | Entry point, lựa chọn host, mở UI, điều phối API/transaction, báo kết quả. |
| `FoundationRebarModels` | DTO cấu hình, kết quả, lỗi validation; không phụ thuộc WPF. |
| `FoundationHostGeometry` | Đọc kích thước, mặt/biên host, trục local, hệ tọa độ và phạm vi hỗ trợ. |
| `FoundationRebarValidator` | Kiểm tra dữ liệu, cover, khoảng cách, số thanh, hướng, giới hạn hình học và dữ liệu thiếu. |
| `FoundationRebarPlanner` | Từ cấu hình đã xác nhận tạo danh sách thanh/đường dẫn hình học dự kiến; thuần hình học, không mutate Document. |
| `FoundationRebarCreator` | Chuyển plan thành Rebar bằng API trong Transaction; nhận diện và xử lý ghi đè an toàn. |
| `FoundationRebarWindow` | UI cấu hình, preview 2D/summary, validation, confirm tạo. |
| `FoundationRebarCommand`/`App.cs` | Đăng ký duy nhất một nút ribbon; không đổi lệnh đặt móng hiện tại nếu không có yêu cầu sản phẩm rõ. |

Ưu tiên tích hợp vào `KStruct-diagnostics` sau khi xác nhận đây là checkout đích. Phần tạo hình phải tái sử dụng helper đã được kiểm chứng khi phù hợp; không chép nguyên mã từ file `.tmp.cs` hoặc module port chưa xác nhận.

## 7. Quy tắc hình học và xử lý đầu vào

- Chuẩn hóa trục cục bộ của móng từ transform/geometry của host; không giả định móng luôn song song trục toàn cục.
- Lấy mặt đặt thép từ solid/face host đáng tin cậy; nếu dùng bounding box như phương án dự phòng, phải giới hạn ở loại host hộp đã xác minh và cảnh báo rõ.
- Tính khoảng hữu dụng sau khi trừ cover, đường kính thanh và quy ước cover đến bề mặt ngoài cốt thép. Không tự đổi ý nghĩa cover giữa mặt trong và tim thanh.
- Khi đầu vào là bước tối đa `s_max`, sinh số thanh sao cho bước thực tế không vượt `s_max`; ghi số thanh và bước thực tế ở preview. Xử lý trường hợp chỉ vừa một thanh hoặc vùng quá hẹp bằng cảnh báo/validation xác định.
- Từ chối số âm, 0, NaN/Infinity, type không thuộc document, hook không tương thích, vùng bố trí rỗng, cover lớn hơn kích thước hữu dụng, hình học suy biến và số lượng vượt ngưỡng an toàn.
- Không tự tính chiều dài neo/nối theo TCVN ở phiên bản này. Chiều dài/hook do người dùng nhập theo bản vẽ được duyệt; validator chỉ kiểm tra hình học và khả năng tạo của Revit.
- Mọi trường hợp không hỗ trợ phải dừng trước transaction và cho biết host/giá trị cần sửa.

## 8. Kế hoạch triển khai theo giai đoạn

### Giai đoạn 0 — Khảo sát và chốt hợp đồng kỹ thuật

- Mở đúng solution/branch và xác minh checkout nguồn chuẩn.
- Rà `App.cs`, command móng hiện tại, helper transaction/Rebar, WPF pattern, build script và manifest.
- Xác định family host mục tiêu (Structural Foundation), loại family hình học, cách lưu mã nhận diện và chiến lược transaction.
- Chốt các trường cấu hình MVP và loại móng hỗ trợ; liệt kê unsupported geometry.
- Đầu ra: sơ đồ tích hợp ngắn, danh sách file dự kiến, quyết định kỹ thuật và các câu hỏi blocking được ghi lại.

### Giai đoạn 1 — Mô hình dữ liệu và validation thuần

- Tạo models cho host summary, nhóm thép, lớp/phương, cover, distribution, hooks, preview và result.
- Tạo validator không gọi Revit mutation; test các biên dữ liệu bằng harness phù hợp repository nếu có.
- Đầu ra: validation có lỗi tiếng Việt rõ, không bắt đầu transaction khi dữ liệu sai.

### Giai đoạn 2 — Hình học/preview

- Viết adapter đọc host, trục local và mặt phẳng placement; giới hạn rõ loại geometry.
- Sinh plan hình học độc lập cho lưới đáy hai phương móng đơn/đài chữ nhật; thêm top/additional bars sau khi MVP ổn.
- Preview hiển thị nhóm thép, số lượng, bước thực, cover và cảnh báo. Chưa ghi document ở giai đoạn này.
- Đầu ra: plan lặp lại ổn định với host xoay trong mặt bằng và kiểm tra cover.

### Giai đoạn 3 — Tạo Rebar an toàn

- Tạo Rebar native từ plan cho một host chữ nhật trước.
- Thử với các RebarBarType và BarShape/HookType hiện có; từ chối tổ hợp không tương thích thay vì fallback mơ hồ.
- Gán metadata nhận diện lệnh/host; kiểm tra ghi đè có giới hạn và Undo.
- Đầu ra: tạo được hai phương thép đáy, không để lại đối tượng dở khi tạo lỗi.

### Giai đoạn 4 — UI, nhiều host và tích hợp ribbon

- Hoàn thiện WPF: danh sách host, input, type selectors, cover, preview, validation, nút tạo/Cancel.
- Thêm xử lý nhiều host theo chiến lược transaction đã chốt và summary từng host.
- Đăng ký một nút “Thép móng” trong ribbon. Giữ command “Móng dưới cột” hiện có làm lệnh đặt Family; không gộp chức năng nếu chưa quyết định.
- Đầu ra: luồng thao tác hoàn chỉnh trong Revit, Cancel không làm đổi mô hình.

### Giai đoạn 5 — Mở rộng cấu tạo đã duyệt

- Thêm top bars, vùng gia cường, thép chờ cột/vách, cấu hình đài nhiều cọc và các hình học host bổ sung theo ưu tiên sản phẩm.
- Mỗi loại mới cần bản vẽ mẫu được duyệt, family mẫu và tiêu chí hình học/acceptance cụ thể.
- Lồng thép cọc khoan nhồi là hạng mục/command riêng: yêu cầu dữ liệu cọc, phân đoạn đai, vòng tăng cứng, con kê và ống; không coi là mặc định của chức năng đài cọc.
- Đầu ra: phạm vi mở rộng được giao nhận độc lập, không làm suy giảm MVP.

## 9. Kế hoạch kiểm tra và tiêu chí nghiệm thu

> Thực hiện trên bản sao mô hình. Build thành công không thay thế kiểm tra trong Revit và không xác nhận thiết kế kết cấu.

### 9.1 Ca kiểm tra hình học và chức năng

1. Móng hộp chữ nhật, trục toàn cục: preview đúng số thanh, bước, lớp và cover; tạo đủ hai phương.
2. Móng xoay trong mặt bằng: thép vẫn song song/vuông góc trục local đã chọn.
3. Đài chữ nhật có 2/4/nhóm cọc: thép host không đi ra ngoài cover; đầu cọc không bị thay đổi.
4. Cover lớn, vùng rỗng, số lượng 0, số âm, NaN/Infinity, type/hook thiếu: báo lỗi trước transaction, không đổi mô hình.
5. Host family cong/vát/không hỗ trợ: từ chối có lý do, không tạo geometry gần đúng âm thầm.
6. Tạo lần hai khi không bật ghi đè: không tạo trùng và thông báo rõ.
7. Ghi đè: chỉ thay Rebar do command tạo cho đúng host; thép thủ công và host khác còn nguyên.
8. Cố tình tạo lỗi giữa chừng: transaction rollback theo chính sách đã chọn; không có Rebar dở.
9. Cancel/đóng UI và Undo: mô hình giữ nguyên hoặc hoàn tác đúng thao tác.
10. Nhiều host: summary và isolation lỗi đúng theo chiến lược transaction.

### 9.2 Tiêu chí nghiệm thu

- Một lệnh ribbon duy nhất, tên và mô tả rõ; không làm mất command đặt móng hiện tại.
- Rebar là đối tượng native trong host chính xác, nhìn/đọc được ở plan, section, 3D và schedule theo khả năng của Revit.
- Preview và kết quả thực tế khớp số lượng, hướng, lớp, bước và kích thước dự kiến.
- Tất cả trường sai/thiếu bị chặn trước khi sửa document; lỗi không để lại đối tượng một phần.
- Ghi đè có phạm vi an toàn; Undo Revit hoạt động.
- Build Debug thành công với Revit 2024 API; thử mô hình trên bản sao theo checklist trên.
- Bản vẽ đầu ra được kỹ sư kết cấu rà soát với thiết kế được duyệt trước khi dùng thi công.

## 10. Lệnh build và quy trình thực thi dự kiến

Chạy từ thư mục checkout chính sau khi đóng Revit nếu DLL đầu ra đang được Revit nạp:

```powershell
Set-Location 'D:\SDK_HD\KStruct'
.\Build-Debug.ps1
```

Theo README hiện có, script build Debug ghi DLL tại `D:\SDK_HD\KStruct\bin\Debug`; kiểm tra lại script/đường dẫn manifest trước khi chạy vì checkout nguồn và thư mục cài đặt có thể khác. Không chép đè thư mục cài đặt hoặc manifest ngoài kế hoạch. Sau build, mở Revit 2024, nạp mô hình thử trên bản sao, chạy các ca kiểm tra và ghi kết quả. Không tự phát hành DLL lên production hoặc mô hình thật từ lệnh code.

## 11. Ngoài phạm vi và giới hạn an toàn

- Không phân tích địa chất, xác định sức chịu tải cọc/nền, tính nội lực hoặc chọn kích thước móng.
- Không tự động thiết kế thép theo TCVN, không tuyên bố đạt chuẩn chỉ dựa vào việc tạo hình Rebar.
- Không suy ra thiết kế từ `vethepmong.md`, ảnh, mô hình hình học hoặc tên family nếu thiếu bản vẽ/tính toán được duyệt.
- Không tạo lồng cọc khoan nhồi, chi tiết neo/nối tự động hoặc bảng thống kê thi công trong MVP.
- Không sửa family, view/template, cốt thép hiện hữu hoặc dữ liệu người dùng ngoài phạm vi lệnh.
- Không phát hành bản vẽ/DLL như hồ sơ thi công đã được phê duyệt.

## 12. Tài liệu tham chiếu

- `vethepmong.md` trong workspace: hướng dẫn kỹ thuật/thể hiện thép móng cọc; dùng làm yêu cầu cấu tạo và điểm kiểm tra, không phải bộ dữ liệu thiết kế để tự sinh thông số.
- `KStruct-diagnostics/README_INTEGRATION.md`: Revit 2024, build và kiểm tra tích hợp hiện tại.
- `KStruct-diagnostics/App.cs`: đăng ký ribbon và `DrawFootingsCommand` hiện trạng.
- TCVN 5574:2018 — kết cấu bê tông và bê tông cốt thép.
- TCVN 10304:2025 — thiết kế móng cọc (thay TCVN 10304:2014).
- TCVN 9362:2012 — thiết kế nền nhà và công trình; không dùng thay tiêu chuẩn móng cọc.
- Tiêu chuẩn thể hiện bản vẽ/cốt thép được dự án viện dẫn; phải xác minh hiệu lực và phiên bản trước phát hành.

---

**Điều kiện bắt đầu code:** xác nhận đúng checkout nguồn, loại family móng mẫu, yêu cầu MVP (móng đơn và đài cọc chữ nhật), nhóm thép bắt buộc, hành vi ghi đè và cung cấp tối thiểu một bản vẽ thiết kế được duyệt để đối chiếu hình học. Mọi thông số kỹ thuật cụ thể trong bản vẽ phải do người thiết kế cung cấp/xác nhận.
