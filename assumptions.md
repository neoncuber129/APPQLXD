Tài liệu này ghi lại các giả định nghiệp vụ để Cursor không tự suy diễn. Mục `NEEDS CONFIRMATION` phải được xác nhận trước khi khóa implementation.

## 1. Snapshot chứng từ
**ASSUMED: FIXED**

Mỗi phiếu nhập/xuất lưu độc lập toàn bộ dữ liệu cần thiết để tái hiện đúng chứng từ tại thời điểm lưu.

Không phụ thuộc vào:
- Danh mục hiện tại
- Dữ liệu mẫu hiện tại
- Kho mặc định hiện tại
- Đối tượng tiêu thụ hiện tại
- VCF hiện tại
- Định mức hiện tại

## 2. VCF
**ASSUMED: FIXED**

VCF của danh mục chỉ hỗ trợ lập chứng từ.

Khi lưu phải lưu VCF đã sử dụng. Phiếu cũ không được tính lại khi VCF danh mục thay đổi.

## 3. Ý nghĩa số lượng
**ASSUMED: FIXED**

### Nhập
`Thực nhập = Số lượng nhập × VCF`

Tồn kho sử dụng Thực nhập.

### Xuất
Tồn kho sử dụng Thực xuất.

Các giá trị nhập/xuất mang tính lưu trữ/hiển thị; không thay thế cho Thực nhập/Thực xuất khi tính tồn.

## 4. Lô
**ASSUMED: FIXED**

Khóa logic:
`Tên mặt hàng + Đơn giá`

Cùng item + cùng giá => cùng lô logic.
Cùng item + khác giá => lô khác.

## 5. FIFO / chọn lô
**NEEDS CONFIRMATION**

Chưa khóa:
- Chọn lô thủ công
- FIFO tự động
- Kết hợp cả hai

Không tự triển khai FIFO nếu chưa xác nhận.

## 6. Xuất nhiều lô
**NEEDS CONFIRMATION**

Chưa xác định khi lượng xuất vượt tồn một lô:
- Tự chia sang lô tiếp theo
- Người dùng lập nhiều dòng
- Tự FIFO

## 7. Xuất một phần lô
**ASSUMED: YES**

Có thể xuất một phần tồn lô.

Ví dụ: tồn 1.000, xuất 300 => còn 700.

## 8. Tồn đầu kỳ
**NEEDS CONFIRMATION**

Chưa khóa:
- Nhập trực tiếp Thực nhập
- Hay nhập Số lượng nhập + VCF

Mặc định an toàn: tồn đầu kỳ là số tồn thực tế và không tự áp VCF.

## 9. Điều chuyển
**ASSUMED: FIXED**

Điều chuyển:
- Trừ Thực xuất ở kho nguồn.
- Cộng lượng thực tế tương ứng ở kho nhận.
- Không coi điều chuyển là tiêu thụ.

## 10. Kho mặc định của đối tượng
**ASSUMED: FIXED — ĐÃ BỎ**

Đối tượng không còn kho nguồn mặc định. Khi lập phiếu xuất, người dùng chọn kho nguồn trực tiếp trên phiếu.

## 10b. Đối tượng như vị trí tồn
**ASSUMED: FIXED**

Mỗi đối tượng đồng thời là vị trí tồn kho phụ (cùng Id với bản ghi kho phụ đồng bộ mã/tên).

Giữ đủ tính chất cũ của đối tượng (loại máy/xe/tàu/khác, định mức, nhiên liệu mặc định, dữ liệu mẫu) — trừ kho nguồn mặc định đã bỏ.

Thêm tính chất kho phụ nội bộ cho đối tượng (vị trí tồn): tham gia xuất điều chuyển và tiêu thụ quý. Trong **danh mục**: mục **Kho XD** (Kho XD + Máy + Phương tiện + Tàu) và mục **Kho PTKT-VTXD** cùng cấp, độc lập. Không hiện Đối tượng khác / Kho phụ trong danh mục. Vị trí tồn đối tượng không liệt kê như kho độc lập.

## 10c. Kho tàu
**ASSUMED: FIXED**

Nhóm **Tàu** (dưới Phương tiện trong danh mục kho) có thuộc tính như đối tượng/phương tiện, riêng:
1. Loại = Tàu.
2. Định mức = nhiều dòng; mỗi dòng có **nhãn**, chọn **nhóm nhiên liệu** + **tỷ lệ quy đổi** (vận hành = 1 → bao nhiêu lít). Cùng một nhóm có thể nhiều dòng (khác nhãn).
3. **Tổng** định mức hiển thị = cộng tất cả tỷ lệ các dòng (lít khi vận hành = 1).
4. **Tiêu thụ quý (hiện tại):** nhập **thực xuất trực tiếp** như máy — tỷ lệ định mức **chưa** dùng để tính và **không** validate bắt buộc. Không áp dụng định mức tàu trên phiếu điều chuyển.
5. (Dự kiến sau) có thể dùng `Thực xuất = Vận hành × Σ tỷ lệ nhóm` khi bật tính theo định mức.

## 10d. Chế độ kho XD / PTKT
**ASSUMED: FIXED — TẠM THỜI**

Trong **Cài đặt**, chọn một chế độ làm việc (mặc định **Kho XD**):
- **Kho XD**: chỉ làm việc với kho loại XD và vị trí đối tượng; danh mục hiện mục Kho XD.
- **Kho PTKT-VTXD**: chỉ làm việc với kho PTKT-VTXD; danh mục hiện mục Kho PTKT-VTXD.

Lọc áp dụng cho danh sách kho trên phiếu, tồn, tiêu thụ quý và danh mục.


**Mặt hàng / nhóm:** mỗi nhóm thuộc một họ (XD hoặc PTKT). Chế độ Kho XD chỉ hiện nhóm/mặt hàng XD; chế độ Kho PTKT-VTXD chỉ hiện nhóm/mặt hàng PTKT. Nhóm mới tạo theo chế độ đang chọn. Không lẫn danh mục hai họ.

**Dữ liệu mẫu:** tạo riêng cho từng họ kho (XD rồi PTKT). Một phiếu không trộn mặt hàng/kho của hai họ độc lập; không điều chuyển XD ↔ PTKT.

Ma trận dữ liệu mẫu (`LoadDemoActivity`):
- Chạy **tách hoàn toàn theo chế độ kho**: ghi xong toàn bộ bước Kho XD (scope XD) rồi mới ghi bước Kho PTKT (scope PTKT). Không dùng mặt hàng/kho PTKT trên phiếu XD và ngược lại.
- **Kho XD:** tồn đầu (Main + aux/Máy/Xe/Tàu) **chia đủ 3 loại lô TX / SSCĐ / IUU**; nhập có phiếu từng loại; **xuất lẻ ~10 phiếu/quý** (mix loại); **nhiều điều chuyển** Main→Máy/Xe/Tàu trong quý (**mỗi phiếu ĐC một loại**, tổng mix đủ 3 loại); tiêu thụ quý **qua sổ** — `SaveConsumerQuarterBook` cho Máy/Xe có ĐC (= tổng ĐC theo loại), `SaveShipQuarterBook` cho **từng tàu** (có ĐC: trừ ~70% theo từng loại nhận; không ĐC: tiêu thụ từ tồn đầu đủ 3 loại). **Không** ghi qua bảng ngoài (`SaveConsumptionSheet`). Không tạo phiếu xuất máy/phương tiện và không tạo phiếu Auxiliary.
- **Kho PTKT-VTXD:** tồn đầu (mix loại) + nhập + xuất lẻ trên đúng 1 kho PTKT; không điều chuyển và không tiêu thụ quý.

## 10e. Cộng dồn điều chuyển → tiêu thụ quý (máy/xe)
**ASSUMED: FIXED**

Mỗi **Máy** / **Phương tiện** có cờ danh mục **Cộng dồn điều chuyển vào tiêu thụ quý** (mặc định bật). Khi mở bảng tiêu thụ quý:
- Nếu cờ bật và ô chưa có phiếu tiêu thụ quý đã lưu → điền = tổng thực xuất các phiếu điều chuyển đến vị trí đó trong quý (cùng lô).
- Ô đã lưu → giữ số đã lưu. Nếu tổng ĐC sau đó **lớn hơn** số đã lưu → hiện gợi ý và nút **Cập nhật từ điều chuyển** (không tự ghi đè); người dùng bấm rồi Lưu bảng.
- Trên bảng tiêu thụ quý: bấm tên **Máy** / **Phương tiện** → popup sổ ĐC trong quý (editable). Có hàng **Cộng**. Nút **Lưu sổ** ghi phiếu Consumption ngay (giống tàu). Không sửa Xuất → lưu = tổng ĐC theo lô như cũ. Sửa Xuất NL/DM → trừ tồn FIFO theo nhóm. Không áp dụng cho Tàu.
- Không gồm Tàu / kho phụ thường. Không tự ghi tiêu thụ lúc lưu phiếu điều chuyển.

## 10f. Tiêu thụ quý — một nơi duy nhất
**ASSUMED: FIXED**

Trong **Kho XD**, trừ kho chính: máy / phương tiện / tàu / kho phụ chỉ nhập tiêu thụ tại tab **Tiêu thụ quý**, **1 lần/quý**.
- Điều chuyển tới máy/xe chỉ chuyển tồn, không phải tiêu thụ.
- Không còn nhập song song “tiêu thụ kho phụ” cho cùng kỳ (tránh trùng lô+kho).
- Phiếu Auxiliary cũ không hiện trên bảng quý; mở từ lịch sử sẽ hướng về tab tiêu thụ quý. Lưu từ tab này ghi `Consumption`.

## 11. Nhiên liệu mặc định
**ASSUMED: FIXED**

Đối tượng có nhiên liệu mặc định. Giá trị cuối cùng phải được lưu trên chứng từ.

## 12. Định mức
**ASSUMED: FIXED**

Phương tiện (phiếu xuất / điều chuyển):
`Thực xuất = Quãng đường × Định mức`

Tàu (chỉ tiêu thụ quý):
Mỗi dòng định mức: nhãn + nhóm nhiên liệu + tỷ lệ (lít khi vận hành = 1). Cùng nhóm được nhiều dòng.
`Tổng định mức = cộng tất cả tỷ lệ`
`Thực xuất (theo lô) = Lượng vận hành × (cộng tỷ lệ các dòng cùng nhóm của lô)`

Định mức (và lượng vận hành với tàu) được snapshot trên phiếu.

## 13. Dynamic fields
**ASSUMED: FIXED**

Dynamic field là cấu hình danh mục, nhưng giá trị thực tế và thông tin cần thiết của field phải được snapshot trên chứng từ.

## 14. Sample data
**ASSUMED: FIXED**

Sample data chỉ là dữ liệu gợi ý/mặc định khi tạo phiếu.

Nếu người dùng nhập thủ công và lưu vào sample data:
- Sample data được cập nhật.
- Phiếu hiện tại vẫn giữ giá trị riêng.

## 15. Thay đổi danh mục
**ASSUMED: FIXED**

Thay đổi tên mặt hàng, đơn vị, VCF, chất lượng, định mức, kho mặc định, nhiên liệu mặc định hoặc sample data không được thay đổi chứng từ đã lưu.

## 16. Sửa phiếu
**ASSUMED: FIXED**

Khi sửa phiếu đã ảnh hưởng tồn:
1. Hoàn nguyên ảnh hưởng cũ.
2. Validate dữ liệu mới.
3. Áp dụng ảnh hưởng mới.
4. Commit trong cùng transaction.

## 17. Xóa phiếu
**ASSUMED: FIXED**

Xóa phiếu đã ảnh hưởng tồn phải hoàn nguyên tồn trước khi xóa/đánh dấu hủy.

## 18. Hard delete / Soft delete
**NEEDS CONFIRMATION**

Chưa xác định xóa vật lý hay soft delete.

Khuyến nghị kỹ thuật: giữ lịch sử hoặc trạng thái hủy đối với chứng từ đã phát sinh tồn.

## 19. Điều chỉnh phiếu nhập
**ASSUMED: FIXED**

Nếu:
`Đơn giá × Số lượng = Thành tiền`
=> một lô.

Nếu không bằng:
- Tách 2 lô.
- Tổng giá trị phải đúng.
- Đơn giá các lô là số nguyên.

**NEEDS CONFIRMATION:** thuật toán chia cụ thể trong trường hợp đặc biệt.

## 20. Làm tròn
**NEEDS CONFIRMATION**

Chưa xác định:
- Số chữ số thập phân của VCF
- Thực nhập
- Thực xuất
- Quy tắc rounding
- Làm tròn ở bước trung gian hay khi lưu/hiển thị

## 21. Công thức xuất ngược
**NEEDS CONFIRMATION**

Đã xác định Thực xuất là giá trị nghiệp vụ chính, nhưng cần khóa chính xác công thức tính giá trị xuất ngược theo VCF và đơn vị đo.

## 22. Khóa chứng từ
**NEEDS CONFIRMATION**

Chưa xác định khi nào chứng từ:
- Có thể sửa
- Bị khóa
- Bị hủy
- Chỉ được điều chỉnh bằng chứng từ mới

## 23. Audit log
**NEEDS CONFIRMATION**

Chưa xác định mức độ audit:
- Người tạo
- Người sửa
- Thời gian
- Nội dung trước/sau
- Người duyệt
- Lý do sửa/xóa

## 24. Nguồn sự thật của tồn kho
**ASSUMED: FIXED**

`Tồn = Tồn đầu kỳ + Tổng Thực nhập - Tổng Thực xuất`

Các bảng stock/stock movement có thể tối ưu truy vấn nhưng phải bảo toàn công thức này.

Khi có sai lệch phải có khả năng rebuild/reconcile.

## 25. Nguyên tắc ưu tiên
**ASSUMED: FIXED**

Nếu xung đột giữa dữ liệu chứng từ snapshot và danh mục hiện tại:
- Ưu tiên snapshot của chứng từ.

Nếu xung đột giữa Thực nhập/Thực xuất và giá trị nhập/xuất hiển thị:
- Ưu tiên Thực nhập/Thực xuất cho tính tồn.

## 26. Quy tắc cho Cursor
Khi gặp nghiệp vụ chưa được định nghĩa:
- Không tự tạo quy tắc quan trọng.
- Ghi vào `NEEDS CONFIRMATION`.
- Tạo TODO.
- Có thể triển khai phần không phụ thuộc vào quy tắc đó.
- Không làm thay đổi dữ liệu lịch sử.

## 27. Quyết định kỹ thuật khi triển khai

Các mục `NEEDS CONFIRMATION` ở trên vẫn chưa được khóa nghiệp vụ. Phần dưới là cách làm tạm để ứng dụng chạy được, không xem là quy tắc đã chốt.

- Cơ sở dữ liệu cục bộ SQLite (`%LocalAppData%\APPQLXD\appqlxd.db`) bằng EF Core. Mọi thay đổi tồn nằm trong một transaction.
- Chọn lô thủ công. Không tự FIFO. Nếu lô đã chọn không đủ tồn thì từ chối, không tự tràn sang lô khác.
- Tồn đầu kỳ nhập thẳng số tồn thực tế, không nhân VCF.
- Hủy chứng từ là soft delete: hoàn tồn, giữ chứng từ trạng thái Đã hủy. Không xóa vật lý chứng từ đã phát sinh tồn.
- Làm tròn tạm: số lượng 4 chữ số thập phân, VCF 6 chữ số, định mức 6 chữ số, tiền VND 0 chữ số, `MidpointRounding.AwayFromZero`.
- Công thức ngược tạm: `Giá trị xuất = Thực xuất / VCF`.
- Tách phiếu nhập khi `Đơn giá × Số lượng ≠ Thành tiền`: giữ đơn giá nguyên, phần chênh lệch đưa sang lô thứ hai với bước giá nguyên nhỏ nhất sao cho tổng số lượng và tổng thành tiền khớp. Đơn giá lô không âm. Nếu một phần số lượng bằng 0 thì giữ một lô.
- Chọn cặp tách trên phiếu nhập: mỗi cặp phải `thực nhập1 + thực nhập2 = thực nhập đã nhập` và `thành tiền1 + thành tiền2 = thành tiền đã nhập`, với `thành tiền lô = đơn giá × thực nhập` (số nguyên). Không được tính ngược tổng thành tiền từ đơn giá trung bình rồi làm tròn — tổng sau tách phải đúng bằng số người dùng điền.
- Phiếu đang hiệu lực được sửa hoặc hủy. Chưa có duyệt hoặc khóa kỳ.
- Chỉ lưu thời điểm tạo và thời điểm sửa. Chưa có nhật ký trước/sau.
- Kho và đối tượng có bộ dữ liệu mẫu mặc định riêng cho phiếu nhập và phiếu xuất vì hai loại phiếu dùng hai tập trường.
- Đối tượng có vị trí tồn kho phụ cùng Id; mã/tên đồng bộ. Không trùng mã/tên với kho độc lập.
- Lô giữ tên tại lần nhập đầu. Đổi tên mặt hàng trong danh mục không gộp vào lô cũ và không sửa phiếu đã lưu.
- Báo cáo tồn là số hiện tại theo sổ. Nhóm trên báo cáo lấy từ danh mục hiện tại nếu mặt hàng còn liên kết, không ghi đè snapshot chứng từ.
- Trường ẩn không đưa vào form lập phiếu và không bắt buộc lúc lưu.
- Đơn giá và thành tiền do người dùng nhập phải là số nguyên VND.
- Không cho trùng tên nhóm, đơn vị, mặt hàng, kho, mã kho và mã đối tượng, vì lô nhận diện bằng tên mặt hàng.

## 28. Phiếu giấy nhập xuất

Mẫu `2026 PHIẾU NHẬP XUẤT XĂNG DẦU.xlsx` và `Sổ NXT.xlsx` bổ sung phần đầu phiếu và nhiều dòng trên một phiếu. Tồn vẫn dùng thực nhập / thực xuất. Thành tiền trên phiếu giấy = đơn giá × số thực tế (lít 15°C hoặc kg), làm tròn đồng. Nhiệt độ và tỉ trọng được lưu trên từng dòng, không tự tính lại VCF. **NEEDS CONFIRMATION:** bảng quy đổi nhiệt độ–tỉ trọng ra VCF, nếu có, chưa được áp dụng.

Phiếu xuất trên tab quản lý: chế độ hiện có **Điều chuyển kho** (mặc định khi tạo phiếu mới) và **Xuất xăng dầu lẻ**. Chế độ xuất máy/phương tiện tạm ẩn trên UI (vẫn mở được phiếu cũ). **Dữ liệu mẫu** theo ma trận §10d: XD = tồn/nhập/lẻ/ĐC/tiêu thụ quý; PTKT = tồn/nhập/lẻ; chỉ Điều chuyển + Xuất lẻ — không tạo phiếu xuất máy/phương tiện. Trừ tồn theo thực xuất. Định mức xe trên điều chuyển sang phương tiện vẫn áp dụng; định mức tiêu thụ tàu/xe nằm ở tiêu thụ quý / phiếu tiêu thụ. Số km và nhiệm vụ lưu trên phiếu để đối chiếu sổ NXT.

Tab **Kho**: **Sổ NXT** (theo kho chọn, cột mặt hàng), **Tồn đầu kỳ**, **NXT tổng** (kho lớn = mọi kho trong phạm vi; dòng lô = mặt hàng + đơn giá theo nhóm; tiêu đề nhóm hiện tổng tồn đầu / nhập / xuất / tồn sau; mỗi nhóm một màu; điều chuyển nội bộ không tính nhập–xuất).
