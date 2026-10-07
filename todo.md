# TODO

## Tách 4 nhóm kho (Kho Hải đoàn, Máy, Phương tiện, Tàu & Tổng) trên Tab NXT Tổng (2026-10-07)
- [x] Models (`APPQLXD.Core/Models/Records.cs`): Mở rộng `NxtTotalRow` với 20 trường số lượng (`OpeningMain`, `OpeningMachine`, `OpeningVehicle`, `OpeningShip`, `OpeningTotal`, `In...`, `Out...`, `Closing...`), giữ alias properties `Opening`, `In`, `Out`, `Closing` và constructor tương thích ngược.
- [x] Engine (`APPQLXD.Core/Services/NxtBook.cs`): Thêm phân loại `NxtWhCategory`, hàm `GetWhCategory`, nạp consumers, cập nhật `BuildTotalCore` phân bổ chính xác Tồn đầu, Nhập, Xuất, Tồn sau cho từng nhóm kho và tính tổng đơn vị độc lập với điều chuyển nội bộ.
- [x] ViewModel (`APPQLXD/ViewModels/NxtVm.cs`): Cập nhật `NxtTotalRowVm` và `NxtTotalVm` tính tổng nhóm và tổng loại lô trên 20 cột số lượng. Thêm `SheetRevision` để trigger render UI.
- [x] View WPF (`APPQLXD/Views/NxtTotalView.xaml` & `NxtTotalView.xaml.cs`): Thiết kế bảng động 24 cột với tiêu đề 2 tầng (Tồn đầu, Nhập, Xuất, Tồn sau tách 5 cột nhỏ gồm Kho HĐ, Máy, PT, Tàu, Tổng), tô màu dòng theo nhóm nhiên liệu, đóng băng tiêu đề.
- [x] Xuất Excel & Word (`APPQLXD.Core/Export/NxtExportMapper.cs`): Cập nhật `WriteNxtTotal` xuất file Excel 24 cột với 2 tầng header và styling chuẩn; cập nhật `MapNxtTotal` xuất Word với các placeholder `$TonDau_Kho`, `$TonDau_May`, `$TonDau_Xe`, `$TonDau_Tau`, `$TonDau`...
- [x] Unit tests: Thêm `Nxt_total_breaks_down_by_main_machine_vehicle_ship_and_total` trong `FlowTests.cs` và cập nhật `ExportTests.cs`. 135/135 tests pass 100%.


## Cập nhật Tab Hạn Mức (2026-10-07)
- [x] Thêm nút "Xóa hạn mức" để xóa toàn bộ các ô hạn mức đã điền của năm & bảng hiện tại
- [x] Dữ liệu hiển thị các cột trước ô "Lũy tích sử dụng": tính riêng trong quý đang chọn (không tính từ 01/01 đầu năm)
- [x] Lũy tích sử dụng: tính từ 01/01 đầu năm đến cuối quý đang chọn
- [x] Cột "Lũy tích sử dụng" chia làm 3 cột nhỏ: Xăng, Điêzel, Tổng
- [x] Cột "Còn" và "Quá" chia làm 3 cột nhỏ: Xăng, Điêzel, Tổng cho mỗi loại (tổng cộng bảng 25 cột)
- [x] Đồng bộ xuất Excel (`QuotaExcelWriter`) 25 cột với header 3 tầng tương ứng
- [x] Cập nhật test `MissionQuotaTests` kiểm tra quý vs lũy tích và so sánh hạn mức

## Quản lý Nguồn gốc Lô & Tinh chỉnh Phiếu xuất & Sổ tiêu thụ quý (2026-10-07)
- [x] Entities & Persistence: Entity `LotOrigin`, seed dữ liệu mẫu "Tự mua", "Trên cấp", cập nhật `Lot`, `FuelDocument`, `FuelDocumentLine`
- [x] SchemaPatch: Migration cột `Origin` và bảng `LotOrigins`, cập nhật Unique Index cho `Lots`
- [x] Core Models & Services: Thêm `LotOriginRow`, `CatalogStore.GetLotOrigins`, cập nhật `EnsureLot`, `GetLots`, `LotOption`
- [x] ViewModel: `VoucherLineVm`, `VoucherDeskVm` tích hợp chọn nguồn gốc trên phiếu nhập và hiển thị trên phiếu xuất
- [x] View: `VoucherDeskView.xaml` thêm cột Nguồn gốc cạnh cột Loại lô cho cả phiếu nhập (ComboBox) và phiếu xuất (Label)
- [x] Cập nhật `StockView.xaml` hiển thị cột Nguồn gốc
- [x] Tồn đầu kỳ: Cập nhật `OpeningLotWindow` dropdown chọn nguồn gốc lô, `OpeningView` thêm cột Nguồn gốc bên phải cột Loại lô, `OpeningVm` nạp/lưu theo bộ khóa bao gồm Nguồn gốc
- [x] Phiếu xuất (`ExportDesk`): Dòng đầu tiên và các dòng mới không nạp sẵn Loại lô và Nguồn gốc (để trống); chỉ hiển thị khi chọn lô hoặc tự nạp từ nhiên liệu mặc định
- [x] Nút "Tiếp tục thêm phiếu": Mặc định bật (`ContinueAfterSave = true`)
- [x] Nút "Các loại lô hiển thị": Nằm bên trái nút "Tiếp tục thêm phiếu", cho phép lọc các loại lô hiển thị ở dropdown chọn lô (dạng checklist), lưu trong phiên làm việc (in-memory session) và reset về full khi khởi động lại ứng dụng
- [x] Phiếu xuất điều chuyển: Danh sách chọn "Kho nhận" (`Destinations`) tự động loại trừ kho nguồn (`Warehouse`) đang chọn
- [x] Sổ tiêu thụ quý của tàu (`ShipQuarterBookWindow`):
  - Khi chuyển từ thủ công sang tự động: giữ nguyên dữ liệu thủ công đang có, hiển thị icon tính toán lại (🔄) nếu chưa khớp công thức.
  - Nhấp vào icon 🔄: tính lại theo công thức và xóa icon.
  - Khi nhập dữ liệu mới ở các ô giờ / số máy: tự động tính toán lại theo công thức và xóa icon.
- [x] Viết unit tests kiểm thử tạo lô theo nguồn gốc, xuất đúng lô, tồn đầu kỳ theo nguồn gốc và tra cứu danh mục nguồn gốc
- [x] Chạy kiểm thử toàn bộ `dotnet test` (132/132 tests pass) và cập nhật tài liệu Walkthrough

## Cộng dồn ĐC → tiêu thụ quý (2026-09-30)
- [x] Cờ RollTransfersIntoQuarter trên Máy/Xe (mặc định bật) + UI danh mục
- [x] SumInboundTransfers + SuggestQuarterRollups
- [x] Mở tiêu thụ quý: điền ô trống từ tổng ĐC (không ghi đè phiếu đã lưu)
- [x] Nếu tổng ĐC > số đã lưu: gợi ý + nút Cập nhật từ điều chuyển
- [x] Tests

## Chế độ kho XD / PTKT (2026-09-30)
- [x] Cài đặt: Kho XD (mặc định) hoặc Kho PTKT-VTXD
- [x] Lọc GetWarehouses theo chế độ (phiếu/tồn); danh mục luôn hiện cả hai mục tách loại kho
- [x] Lọc GetGroups/GetItems theo chế độ (không lẫn XD ↔ PTKT)
- [x] Hiển thị chế độ trên thanh tiêu đề
- [x] Dữ liệu mẫu: XD = nhiên liệu @ Kho XD; PTKT = nhóm PTKT-VTXD @ Kho PTKT; không trộn

## Danh mục kho (2026-09-30)
- [x] Đổi mục Kho → Kho XD (Main + Máy + Phương tiện + Tàu)
- [x] Thêm mục Kho PTKT-VTXD cùng cấp, độc lập
- [x] Ẩn Đối tượng khác và Kho phụ khỏi danh mục

## Phiếu xuất — chế độ (2026-09-30)
- [x] Tab phiếu xuất chỉ hiện: Điều chuyển kho (mặc định), Xuất xăng dầu lẻ
- [x] Tạm ẩn Xuất máy / phương tiện (logic giữ để mở phiếu cũ)

## Kho tàu (2026-09-30)
- [x] Loại ConsumerType.Ship + Labels "Tàu"
- [x] Định mức nhiều dòng: nhãn + nhóm nhiên liệu + tỷ lệ; cùng nhóm được nhiều dòng; tổng = cộng các dòng
- [x] Danh mục Kho: nhóm Tàu dưới Phương tiện
- [x] Editor định mức (CatalogHost + ConsumersView)
- [x] Tiêu thụ quý: vận hành × tỷ lệ theo nhóm của lô; snapshot OperatingQuantity + Norm
- [x] Điều chuyển: không dùng định mức tàu (chỉ phương tiện)
- [x] Seed/Excel tàu + tests

## Đối tượng như kho (2026-09-30)
- [x] Mỗi đối tượng có vị trí tồn = `Warehouse` cùng Id, loại Kho phụ
- [x] Giữ nguyên thuộc tính đối tượng (loại, định mức, NL mặc định, mẫu…)
- [x] Xuất điều chuyển và tiêu thụ quý thấy đối tượng như kho phụ
- [x] Danh mục Kho không liệt kê vị trí tồn của đối tượng
- [x] Backfill DB hiện có + seed/Excel
- [x] Test lưu/xóa/điều chuyển/tiêu thụ quý
- [x] Bỏ kho nguồn mặc định của đối tượng
- [x] Điều chuyển: chọn đối tượng → định mức/km + nhiên liệu/mẫu mặc định
- [x] Đưa đối tượng vào danh mục Kho (dưới kho chính)

## Kiểm tra tách lô (2026-09-30)
- [x] Rà `FloorPairs` / `ApplyFloorPair`: tổng thực nhập và tổng thành tiền phải bằng số người dùng nhập
- [x] Bỏ điều chỉnh `amountDrift` làm lệch thành tiền ≠ đơn giá × số lượng
- [x] Khi lưu dòng đã chọn cặp: giữ thành tiền đã nhập, không tính ngược từ thực nhập
- [x] Hiển thị thành tiền từng phần trên dialog chọn cặp
- [x] Thêm test `Matches` / không rebuild amount từ đơn giá trung bình

## 25. Checklist triển khai

### Màn hình
- [x] Vỏ ứng dụng và điều hướng
- [x] Tổng quan
- [x] Nhóm mặt hàng
- [x] Đơn vị tính
- [x] Mặt hàng (nhóm, tên, đơn vị, chất lượng, nhiệt độ, đo lường, VCF, quy tắc quy đổi)
- [x] Kho chính / kho phụ và dữ liệu mẫu mặc định
- [x] Đối tượng tiêu thụ: máy, phương tiện, đối tượng khác
- [x] Trường dữ liệu động phiếu nhập / phiếu xuất
- [x] Dữ liệu mẫu
- [x] Tồn đầu kỳ
- [x] Phiếu nhập và điều chỉnh tách lô
- [x] Phiếu xuất điều chuyển kho
- [x] Phiếu xuất tiêu thụ
- [x] Tiêu thụ kho phụ
- [x] Tồn kho theo kho / nhóm / mặt hàng / lô, truy vết, đối soát
- [x] Lịch sử chứng từ (snapshot, sửa, hủy)

### Component dùng chung
- [x] Form trường động: chọn mẫu, dropdown, nhập tay, thêm vào dữ liệu mẫu
- [x] Bảng danh sách và thông báo lỗi
- [x] Khối xem snapshot chứng từ

### Logic lõi
- [x] Schema SQLite và transaction
- [x] Tính thực nhập, thực xuất, giá trị xuất, định mức
- [x] Tách lô khi đơn giá × số lượng khác thành tiền
- [x] Khóa lô = tên mặt hàng + đơn giá
- [x] Snapshot chứng từ độc lập với danh mục
- [x] Tồn đầu, nhập, điều chuyển, tiêu thụ, kho phụ
- [x] Sửa / hủy hoàn tồn trong một transaction
- [x] Đối soát tồn từ chứng từ còn hiệu lực
- [x] Kiểm thử tự động toàn bộ luồng trên

## 1. Mục tiêu
Xây dựng ứng dụng quản lý nhập - xuất - tồn nhiên liệu, gồm danh mục, trường động, dữ liệu mẫu, tồn đầu, phiếu nhập, điều chuyển, xuất tiêu thụ, tiêu thụ kho phụ và sổ tồn. Chứng từ đã lưu độc lập với danh mục hiện tại.

## 2. Nguyên tắc đã triển khai
- Danh mục chỉ cung cấp mặc định khi tạo phiếu.
- Sau khi lưu, phiếu giữ snapshot: tên hàng, đơn vị, chất lượng, kho, đối tượng, định mức, VCF, trường động và số đã tính.
- `Thực nhập = Số lượng nhập × VCF`. Tồn cộng thực nhập.
- Tồn trừ thực xuất. Giá trị xuất lưu riêng: `Thực xuất / VCF`.
- Lô = tên mặt hàng + đơn giá nguyên. Chọn lô thủ công. Không FIFO, không tự tràn sang lô khác.
- Tồn đầu là số thực tế, không nhân VCF.
- Phương tiện: `Thực xuất = Quãng đường × Định mức`, định mức được snapshot.
- Điều chuyển trừ kho nguồn và cộng kho nhận, không tính là tiêu thụ.
- Sửa và hủy hoàn tồn trong một transaction. Hủy là giữ chứng từ trạng thái Đã hủy.
- `Tồn = tồn đầu + tổng thực nhập − tổng thực xuất`. Có đối soát lại từ chứng từ hiệu lực.

## 23. Thứ tự đã làm
1. [x] Database schema
2. [x] Nhóm/mặt hàng
3. [x] Kho
4. [x] Đối tượng tiêu thụ
5. [x] Dynamic fields
6. [x] Sample/default data
7. [x] Lot + stock
8. [x] Opening stock
9. [x] Import
10. [x] Import adjustment
11. [x] Export transfer
12. [x] Consumption export
13. [x] Auxiliary consumption
14. [x] Edit/delete + stock rebuild
15. [x] Reports
16. [x] UI polish
17. [x] Automated tests

## 24. Quy tắc giữ nguyên
- Không lấy danh mục hiện tại để vẽ lại lịch sử.
- Không dùng VCF hiện tại để tính lại phiếu cũ.
- Không cập nhật tồn ngoài transaction.
- Không đặt công thức tồn trong giao diện. Giao diện gọi `APPQLXD.Core`.
- Nghiệp vụ chưa chốt vẫn nằm ở `NEEDS CONFIRMATION` trong `assumptions.md`.
