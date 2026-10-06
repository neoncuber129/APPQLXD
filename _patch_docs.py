# -*- coding: utf-8 -*-
from pathlib import Path

p = Path(r"c:\Users\AD\source\repos\APPQLXD\assumptions.md")
t = p.read_text(encoding="utf-8")
needle = "Lọc áp dụng cho danh sách kho trên phiếu, tồn, tiêu thụ quý và danh mục."
idx = t.find(needle)
if idx < 0:
    raise SystemExit("needle not found")
# Insert paragraph after the filter line block before **Dữ liệu mẫu:**
marker = "**Dữ liệu mẫu:**"
m = t.find(marker, idx)
if m < 0:
    raise SystemExit("marker not found")
insert = (
    "\n**Mặt hàng / nhóm:** mỗi nhóm thuộc một họ (XD hoặc PTKT). "
    "Chế độ Kho XD chỉ hiện nhóm/mặt hàng XD; chế độ Kho PTKT-VTXD chỉ hiện nhóm/mặt hàng PTKT. "
    "Nhóm mới tạo theo chế độ đang chọn. Không lẫn danh mục hai họ.\n\n"
)
if "Mặt hàng / nhóm:" not in t[idx:m]:
    t = t[:m] + insert + t[m:]
    p.write_text(t, encoding="utf-8")
    print("assumptions OK")
else:
    print("assumptions already updated")

p2 = Path(r"c:\Users\AD\source\repos\APPQLXD\todo.md")
t2 = p2.read_text(encoding="utf-8")
line = "- [x] Lọc GetWarehouses theo chế độ (phiếu/tồn); danh mục luôn hiện cả hai mục tách loại kho\n"
add = line + "- [x] Lọc GetGroups/GetItems theo chế độ (không lẫn XD ↔ PTKT)\n"
if "Lọc GetGroups/GetItems" not in t2:
    if line not in t2:
        raise SystemExit("todo line not found")
    t2 = t2.replace(line, add, 1)
    p2.write_text(t2, encoding="utf-8")
    print("todo OK")
else:
    print("todo already updated")
