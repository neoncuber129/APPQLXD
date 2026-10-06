flowchart TD

    A["HỆ THỐNG QUẢN LÝ XĂNG DẦU"]

    %% =====================================================
    %% 1. QUẢN LÝ DANH MỤC
    %% =====================================================
    A --> B["QUẢN LÝ DANH MỤC"]

    %% Mặt hàng
    B --> B1["QUẢN LÝ MẶT HÀNG"]

    B1 --> B11["Nhóm mặt hàng"]
    B1 --> B12["Tên / loại nhiên liệu"]
    B1 --> B13["Đơn vị tính"]
    B1 --> B14["Thông tin chất lượng"]

    B1 --> B15["THÔNG TIN QUY ĐỔI"]

    B15 --> B151["Nhiệt độ / dữ kiện đo lường"]
    B15 --> B152["VCF"]
    B15 --> B153["Quy tắc tính số lượng"]

    B153 --> B154["Dùng khi tính phiếu nhập / xuất"]

    %% Kho
    B --> B2["QUẢN LÝ KHO"]

    B2 --> B21["Kho chính"]
    B2 --> B22["Kho phụ"]

    %% Đối tượng tiêu thụ
    B --> B3["QUẢN LÝ ĐỐI TƯỢNG TIÊU THỤ"]

    B3 --> B31{"LOẠI ĐỐI TƯỢNG"}

    B31 -->|Máy| B32["Máy"]
    B32 --> B33["Nhiên liệu mặc định"]
    B32 --> B34["Kho nguồn mặc định"]

    B31 -->|Phương tiện| B35["Phương tiện"]
    B35 --> B36["Nhiên liệu mặc định"]
    B35 --> B37["Kho nguồn mặc định"]
    B35 --> B38["Định mức nhiên liệu"]

    B31 -->|Đối tượng khác| B39["Đối tượng khác"]
    B39 --> B40["Nhiên liệu mặc định"]
    B39 --> B41["Kho nguồn mặc định"]

    %% =====================================================
    %% 2. TRƯỜNG DỮ LIỆU ĐỘNG
    %% =====================================================
    B --> C["QUẢN LÝ TRƯỜNG DỮ LIỆU"]

    C --> C1["Trường dữ liệu phiếu nhập"]
    C --> C2["Trường dữ liệu phiếu xuất"]

    C1 --> C3["Người dùng tự tạo trường"]
    C2 --> C3

    C3 --> C4["Tên trường"]
    C3 --> C5["Kiểu dữ liệu"]
    C3 --> C6["Bắt buộc / Không bắt buộc"]
    C3 --> C7["Hiển thị / Ẩn"]

    %% =====================================================
    %% 3. DỮ LIỆU MẪU
    %% =====================================================
    B --> D["QUẢN LÝ DỮ LIỆU MẪU"]

    D --> D1["Dữ liệu mẫu phiếu nhập"]
    D --> D2["Dữ liệu mẫu phiếu xuất"]

    D1 --> D3["Tạo / sửa / xóa dữ liệu mẫu"]
    D2 --> D3

    D3 --> D4["Gán dữ liệu mẫu cho trường"]

    B2 --> D5["Dữ liệu mẫu mặc định của kho"]
    D5 --> D6["Kho → Bộ dữ liệu mẫu mặc định"]

    B3 --> D7["Dữ liệu mẫu mặc định của đối tượng"]
    D7 --> D8["Đối tượng → Bộ dữ liệu mẫu mặc định"]

    %% =====================================================
    %% 4. NGUYÊN TẮC DỮ LIỆU MẪU
    %% =====================================================
    D --> E["NGUYÊN TẮC DỮ LIỆU MẪU"]

    E --> E1["Khi tạo phiếu: lấy dữ liệu từ danh mục / dữ liệu mẫu"]
    E1 --> E2{"Giá trị đã có trong dữ liệu mẫu?"}

    E2 -->|Có| E3["Chọn Dropdown"]
    E2 -->|Không| E4["Người dùng tự nhập"]

    E4 --> E5["Có thể thêm giá trị mới vào dữ liệu mẫu"]

    E3 --> E6["Hoàn thiện phiếu"]
    E5 --> E6

    %% =====================================================
    %% 5. NGUYÊN TẮC SNAPSHOT PHIẾU
    %% =====================================================
    E6 --> F["LƯU PHIẾU"]

    F --> F1["Tạo Snapshot dữ liệu tại thời điểm lưu"]

    F1 --> F2["Lưu riêng dữ liệu của phiếu"]

    F2 --> F21["Thông tin trường dữ liệu"]
    F2 --> F22["Giá trị trường dữ liệu"]
    F2 --> F23["Tên mặt hàng tại thời điểm lưu"]
    F2 --> F24["Đơn vị tính tại thời điểm lưu"]
    F2 --> F25["Thông tin kho tại thời điểm lưu"]
    F2 --> F26["Thông tin đối tượng tại thời điểm lưu"]
    F2 --> F27["VCF / dữ kiện tính toán đã sử dụng"]

    F2 --> F3["Phiếu độc lập với danh mục"]

    F3 --> F4["Danh mục thay đổi"]
    F4 --> F5["KHÔNG làm thay đổi phiếu đã lưu"]

    %% =====================================================
    %% 6. QUẢN LÝ LÔ
    %% =====================================================
    A --> G["QUẢN LÝ LÔ"]

    G --> G1["Tên mặt hàng"]
    G --> G2["Đơn giá"]

    G1 --> G3["Xác định lô"]
    G2 --> G3

    G3 --> G4["Lô = Tên mặt hàng + Đơn giá + Loại lô"]

    G4 --> G5["Mỗi lô có tồn thực tế riêng"]

    %% =====================================================
    %% 7. TỒN ĐẦU
    %% =====================================================
    A --> H["NHẬP TỒN ĐẦU"]

    H --> H1["Chọn kho"]
    H1 --> H2["Chọn mặt hàng / đơn giá"]
    H2 --> H3["Xác định lô"]

    H3 --> H4["Nhập số lượng tồn thực tế"]
    H4 --> H5["Tăng tồn lô"]

    %% =====================================================
    %% 8. PHIẾU NHẬP
    %% =====================================================
    A --> I["PHIẾU NHẬP"]

    I --> I1["Tạo phiếu nhập"]

    I1 --> I2["Lấy nhanh dữ kiện từ danh mục"]
    I2 --> I3["Mặt hàng / VCF / dữ liệu mẫu / kho"]

    I3 --> I4["Người dùng nhập SỐ LƯỢNG NHẬP"]

    I4 --> I5["SỐ LƯỢNG NHẬP"]
    I5 --> I6["Tính theo VCF"]

    I6 --> I7["THỰC NHẬP"]

    I7 --> I8["Lưu THỰC NHẬP"]
    I8 --> I9["Tăng tồn lô bằng THỰC NHẬP"]

    I4 --> I10["Lưu giá trị NHẬP"]

    I9 --> I11["Lô = Tên mặt hàng + Đơn giá + Loại lô"]

    %% =====================================================
    %% 9. ĐIỀU CHỈNH PHIẾU NHẬP
    %% =====================================================
    A --> J["ĐIỀU CHỈNH PHIẾU NHẬP"]

    J --> J1["Kiểm tra Đơn giá × Số lượng"]
    J1 --> J2{"Có bằng Thành tiền?"}

    J2 -->|Có| J3["Giữ nguyên 1 lô"]

    J2 -->|Không| J4["Tách thành 2 lô"]

    J4 --> J5["Điều chỉnh số lượng"]
    J4 --> J6["Phần chênh lệch"]

    J5 --> J7["Đảm bảo đơn giá nguyên"]
    J6 --> J7

    %% =====================================================
    %% 10. PHIẾU XUẤT
    %% =====================================================
    A --> K["PHIẾU XUẤT"]

    K --> K1["Tạo phiếu xuất"]

    K1 --> K2["Lấy nhanh dữ kiện từ danh mục"]
    K2 --> K3["Kho / mặt hàng / VCF / đối tượng / dữ liệu mẫu"]

    K3 --> K4["Tạo Snapshot khi lưu"]

    K4 --> K5{"LOẠI XUẤT"}

    %% =====================================================
    %% 11. ĐIỀU CHUYỂN KHO
    %% =====================================================
    K5 -->|Điều chuyển kho| L["ĐIỀU CHUYỂN KHO"]

    L --> L1["Chọn kho nguồn"]
    L1 --> L2["Chọn kho nhận"]
    L2 --> L3["Chọn lô"]

    L3 --> L4["Xác định THỰC XUẤT"]

    L4 --> L5["Kiểm tra tồn lô"]

    L5 --> L6{"Đủ tồn?"}

    L6 -->|Không| L7["Không cho xuất"]
    L6 -->|Có| L8["Giảm tồn kho nguồn bằng THỰC XUẤT"]

    L8 --> L9["Tăng kho nhận bằng THỰC XUẤT"]

    L4 --> L10["Tính ngược giá trị XUẤT bằng VCF"]
    L10 --> L11["Lưu giá trị XUẤT"]

    %% =====================================================
    %% 12. XUẤT TIÊU THỤ
    %% =====================================================
    K5 -->|Xuất tiêu thụ| M["XUẤT TIÊU THỤ"]

    M --> M1["Chọn đối tượng tiêu thụ"]

    M1 --> M2["Lấy kho nguồn mặc định"]
    M1 --> M3["Lấy nhiên liệu mặc định"]
    M1 --> M4["Lấy dữ liệu mẫu mặc định"]

    M2 --> M5{"Có thay đổi kho nguồn?"}

    M5 -->|Không| M6["Dùng kho mặc định"]
    M5 -->|Có| M7["Chọn kho nguồn khác"]

    M3 --> M8{"LOẠI ĐỐI TƯỢNG"}

    %% Máy
    M8 -->|Máy| M9["Người dùng nhập số lượng"]
    M9 --> M10["THỰC XUẤT"]

    %% Phương tiện
    M8 -->|Phương tiện| M11["Lấy định mức"]
    M11 --> M12["Nhập quãng đường"]
    M12 --> M13["Tính THỰC XUẤT"]
    M13 --> M14["Thực xuất = Quãng đường × Định mức"]

    %% Đối tượng khác
    M8 -->|Đối tượng khác| M15["Người dùng nhập số lượng"]
    M15 --> M16["THỰC XUẤT"]

    %% Kiểm tra tồn
    M6 --> M17["Kiểm tra tồn lô"]
    M7 --> M17
    M10 --> M17
    M14 --> M17
    M16 --> M17

    M17 --> M18{"Đủ tồn?"}

    M18 -->|Không| M19["Không cho xuất"]
    M18 -->|Có| M20["Giảm tồn lô bằng THỰC XUẤT"]

    %% Tính ngược
    M10 --> M21["Tính ngược bằng VCF"]
    M14 --> M21
    M16 --> M21

    M21 --> M22["GIÁ TRỊ XUẤT"]
    M22 --> M23["Lưu giá trị XUẤT"]

    %% =====================================================
    %% 13. TIÊU THỤ KHO PHỤ
    %% =====================================================
    A --> N["TIÊU THỤ KHO PHỤ"]

    N --> N1["Chọn kho phụ"]
    N1 --> N2["Nhập THỰC XUẤT"]
    N2 --> N3["Kiểm tra tồn lô"]

    N3 --> N4{"Đủ tồn?"}

    N4 -->|Không| N5["Không cho xuất"]
    N4 -->|Có| N6["Giảm tồn kho phụ bằng THỰC XUẤT"]

    N2 --> N7["Tính ngược giá trị XUẤT bằng VCF"]
    N7 --> N8["Lưu giá trị XUẤT"]

    %% =====================================================
    %% 14. QUẢN LÝ TỒN
    %% =====================================================
    A --> O["QUẢN LÝ TỒN KHO"]

    O --> O1["Theo kho"]
    O --> O2["Theo nhóm mặt hàng"]
    O --> O3["Theo mặt hàng"]
    O --> O4["Theo lô"]

    O4 --> O5["Tồn lô"]

    O5 --> O6["Tồn = Tổng THỰC NHẬP - Tổng THỰC XUẤT"]

    H5 --> O6
    I9 --> O6
    L8 --> O6
    L9 --> O6
    M20 --> O6
    N6 --> O6
