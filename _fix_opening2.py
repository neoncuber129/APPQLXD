from pathlib import Path
p = Path(r"c:\Users\AD\source\repos\APPQLXD\APPQLXD\ViewModels\DocumentVms.cs")
text = p.read_text(encoding="utf-8")
bad_start = "        if (System.HasActiveImportOrIssue())"
bad_end = "        _saving = true;"
i = text.find(bad_start)
j = text.find(bad_end, i)
if i < 0 or j < 0:
    raise SystemExit(f"block not found {i} {j}")
fixed = '''        if (System.HasActiveImportOrIssue())
        {
            if (!GroupsVm.Confirm(
                    "Đã có phiếu nhập/xuất. Sửa tồn đầu kỳ có thể làm lệch sổ và tồn kho. Bạn có chắc muốn tiếp tục?"))
                return;
            if (!GroupsVm.Confirm(
                    "Xác nhận lần 2: vẫn lưu thay đổi tồn đầu kỳ khi đã có phiếu nhập/xuất?"))
                return;
        }

'''
text = text[:i] + fixed + text[j:]
p.write_text(text, encoding="utf-8")
print("fixed")
