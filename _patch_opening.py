from pathlib import Path

p = Path(r"c:\Users\AD\source\repos\APPQLXD\APPQLXD\ViewModels\DocumentVms.cs")
text = p.read_text(encoding="utf-8")
marker = 'if (voids.Count > 0 && !GroupsVm.Confirm("Lưu bảng sẽ hoàn tồn các ô hoặc dòng đã xóa. Tiếp tục?"))'
idx = text.find(marker)
if idx < 0:
    raise SystemExit("voids confirm not found")
# Find the _saving = true after this marker within OpeningVm Save (first occurrence after Opening Save)
tail = text[idx:]
saving = "\n        _saving = true;"
sidx = tail.find(saving)
if sidx < 0:
    raise SystemExit("_saving not found after voids confirm")
# Ensure this is OpeningVm save (SaveOpeningSheet nearby)
window = tail[: sidx + 200]
if "SaveOpeningSheet" not in tail[:800]:
    raise SystemExit("SaveOpeningSheet not near this block")

insert = """
        if (System.HasActiveImportOrIssue())
        {
            if (!GroupsVm.Confirm(
                    "Đã có phiếu nhập/xuất. Sửa tồn đầu kỳ có thể làm lệch sổ và tồn kho.\\nBạn có chắc muốn tiếp tục?"))
                return;
            if (!GroupsVm.Confirm(
                    "Xác nhận lần 2: vẫn lưu thay đổi tồn đầu kỳ khi đã có phiếu nhập/xuất?"))
                return;
        }
"""
# Use real newline in message, not escaped
insert = insert.replace("\\n", "\n")

pos = idx + sidx
text = text[:pos] + insert + text[pos:]
p.write_text(text, encoding="utf-8")
print("Opening Save confirm OK")
