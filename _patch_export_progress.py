from pathlib import Path

def replace(path: Path, old: str, new: str, label: str) -> None:
    text = path.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"NOT FOUND in {label}: {path}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"OK {label}")

voucher = Path(r"c:\Users\AD\source\repos\APPQLXD\APPQLXD\ViewModels\VoucherDeskVm.cs")
replace(
    voucher,
    """    [RelayCommand]
    private void ExportWord()
    {
        try
        {
            var ids = new List<Guid>();
            if (IsFormOpen && _editingId is Guid editing)
                ids.Add(editing);
            else
                ids.AddRange(ChosenRows().Select(x => x.Id));

            if (ids.Count == 0)
            {
                Fail(ExportDesk ? "Chọn phiếu xuất để xuất Word." : "Chọn phiếu nhập để xuất Word.");
                return;
            }

            var docs = new List<Core.Models.DocumentDetail>();
            foreach (var id in ids)
            {
                var doc = System.GetDocument(id);
                if (doc is null)
                {
                    Fail("Không đọc được phiếu.");
                    return;
                }
                docs.Add(doc);
            }

            var mode = docs.Count == 1
                ? Core.Export.MultiExportMode.IndividualFiles
                : Services.ExportUi.AskMultiMode(docs.Count);
            if (mode is null)
                return;

            var svc = Services.ExportUi.CreateService();
            var artifacts = svc.ExportSlips(docs, mode.Value);
            Services.ExportUi.SaveArtifacts(artifacts, artifacts[0].FileName);
            Ok($"Đã xuất {docs.Count} phiếu Word.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }""",
    """    [RelayCommand]
    private async Task ExportWord()
    {
        try
        {
            var ids = new List<Guid>();
            if (IsFormOpen && _editingId is Guid editing)
                ids.Add(editing);
            else
                ids.AddRange(ChosenRows().Select(x => x.Id));

            if (ids.Count == 0)
            {
                Fail(ExportDesk ? "Chọn phiếu xuất để xuất Word." : "Chọn phiếu nhập để xuất Word.");
                return;
            }

            var docs = new List<Core.Models.DocumentDetail>();
            foreach (var id in ids)
            {
                var doc = System.GetDocument(id);
                if (doc is null)
                {
                    Fail("Không đọc được phiếu.");
                    return;
                }
                docs.Add(doc);
            }

            var mode = docs.Count == 1
                ? Core.Export.MultiExportMode.IndividualFiles
                : Services.ExportUi.AskMultiMode(docs.Count);
            if (mode is null)
                return;

            var svc = Services.ExportUi.CreateService();
            var artifacts = await Services.ExportUi.RunBusyAsync(
                $"Đang xuất {docs.Count} phiếu Word...",
                progress => svc.ExportSlips(docs, mode.Value, progress));
            if (artifacts is null || artifacts.Count == 0)
                return;
            Services.ExportUi.SaveArtifacts(artifacts, artifacts[0].FileName);
            Ok($"Đã xuất {docs.Count} phiếu Word.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }""",
    "VoucherDesk",
)

docs = Path(r"c:\Users\AD\source\repos\APPQLXD\APPQLXD\ViewModels\DocumentVms.cs")
replace(
    docs,
    """    [RelayCommand]
    private async Task ExportSelectedBooks()
    {
        try
        {
            var picks = BookListItems.Where(x => x.IsSelected).ToList();
            if (picks.Count == 0)
            {
                Fail("Tick chọn ít nhất một sổ để xuất Word.");
                return;
            }

            var q = Array.IndexOf(BookListQuarters, BookListQuarter) + 1;
            if (q < 1) q = 1;
            var quarterDate = new DateTime(BookListYear, q * 3, DateTime.DaysInMonth(BookListYear, q * 3));
            var year = BookListYear;
            Ok($"Đang chuẩn bị xuất {picks.Count} sổ...");
            var items = await Task.Run(() =>
            {
                var list = new List<Core.Export.ExportService.BookExportItem>();
                foreach (var pick in picks)
                {
                    if (pick.IsShip)
                    {
                        var book = System.GetShipQuarterBook(quarterDate, pick.WarehouseId);
                        list.Add(new Core.Export.ExportService.BookExportItem(pick.Name, true, null, book, year, q));
                    }
                    else
                    {
                        var book = System.GetConsumerQuarterBook(quarterDate, pick.WarehouseId);
                        list.Add(new Core.Export.ExportService.BookExportItem(pick.Name, false, book, null, year, q));
                    }
                }

                return list;
            });

            var mode = items.Count == 1
                ? Core.Export.MultiExportMode.IndividualFiles
                : Services.ExportUi.AskMultiMode(items.Count);
            if (mode is null) return;

            var artifacts = Services.ExportUi.CreateService().ExportBooks(items, mode.Value);
            Services.ExportUi.SaveArtifacts(artifacts, artifacts[0].FileName);
            Ok($"Đã xuất {items.Count} sổ Word.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }""",
    """    [RelayCommand]
    private async Task ExportSelectedBooks()
    {
        try
        {
            var picks = BookListItems.Where(x => x.IsSelected).ToList();
            if (picks.Count == 0)
            {
                Fail("Tick chọn ít nhất một sổ để xuất Word.");
                return;
            }

            var q = Array.IndexOf(BookListQuarters, BookListQuarter) + 1;
            if (q < 1) q = 1;
            var quarterDate = new DateTime(BookListYear, q * 3, DateTime.DaysInMonth(BookListYear, q * 3));
            var year = BookListYear;

            var mode = picks.Count == 1
                ? Core.Export.MultiExportMode.IndividualFiles
                : Services.ExportUi.AskMultiMode(picks.Count);
            if (mode is null) return;

            var svc = Services.ExportUi.CreateService();
            var artifacts = await Services.ExportUi.RunBusyAsync(
                $"Đang xuất {picks.Count} sổ Word...",
                progress =>
                {
                    var total = picks.Count;
                    var items = new List<Core.Export.ExportService.BookExportItem>(total);
                    for (var i = 0; i < picks.Count; i++)
                    {
                        var pick = picks[i];
                        progress.Report(new Core.Models.DemoProgress(
                            $"Đang đọc sổ {i + 1}/{total}: {pick.Name}", i, total + 1, i, total + 1));
                        if (pick.IsShip)
                        {
                            var book = System.GetShipQuarterBook(quarterDate, pick.WarehouseId);
                            items.Add(new Core.Export.ExportService.BookExportItem(pick.Name, true, null, book, year, q));
                        }
                        else
                        {
                            var book = System.GetConsumerQuarterBook(quarterDate, pick.WarehouseId);
                            items.Add(new Core.Export.ExportService.BookExportItem(pick.Name, false, book, null, year, q));
                        }
                    }

                    progress.Report(new Core.Models.DemoProgress(
                        "Đang tạo file Word...", total, total + 1, total, total + 1));
                    return svc.ExportBooks(items, mode.Value, progress);
                });
            if (artifacts is null || artifacts.Count == 0)
                return;
            Services.ExportUi.SaveArtifacts(artifacts, artifacts[0].FileName);
            Ok($"Đã xuất {picks.Count} sổ Word.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }""",
    "ExportSelectedBooks",
)

nxt = Path(r"c:\Users\AD\source\repos\APPQLXD\APPQLXD\ViewModels\NxtVm.cs")
replace(
    nxt,
    """    [RelayCommand]
    private void ExportWord()
    {
        try
        {
            if (!int.TryParse(Year?.Trim(), out var year))
            {
                Fail("Năm không hợp lệ.");
                return;
            }
            var quarter = Array.IndexOf(Quarters, Quarter) + 1;
            if (quarter < 1) quarter = 1;
            var bytes = Services.ExportUi.CreateService().ExportNxtWord(Sheet, year, quarter, WarehouseLabel);
            Services.ExportUi.SaveBytes(bytes, $"so_nxt_Q{quarter}_{year}.docx", "Word (*.docx)|*.docx", "docx");
            Ok("Đã xuất sổ NXT Word.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    [RelayCommand]
    private void ExportExcel()
    {
        try
        {
            if (!int.TryParse(Year?.Trim(), out var year))
            {
                Fail("Năm không hợp lệ.");
                return;
            }
            var quarter = Array.IndexOf(Quarters, Quarter) + 1;
            if (quarter < 1) quarter = 1;
            var bytes = Services.ExportUi.CreateService().ExportNxtExcel(Sheet, year, quarter, WarehouseLabel);
            Services.ExportUi.SaveBytes(bytes, $"so_nxt_Q{quarter}_{year}.xlsx", "Excel (*.xlsx)|*.xlsx", "xlsx");
            Ok("Đã xuất sổ NXT Excel.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }""",
    """    [RelayCommand]
    private async Task ExportWord()
    {
        try
        {
            if (!int.TryParse(Year?.Trim(), out var year))
            {
                Fail("Năm không hợp lệ.");
                return;
            }
            var quarter = Array.IndexOf(Quarters, Quarter) + 1;
            if (quarter < 1) quarter = 1;
            var sheet = Sheet;
            var label = WarehouseLabel;
            var bytes = await Services.ExportUi.RunBusyAsync(
                "Đang xuất sổ NXT Word...",
                progress =>
                {
                    progress.Report(new Core.Models.DemoProgress("Đang tạo Word", 0, 1, 0, 1));
                    var data = Services.ExportUi.CreateService().ExportNxtWord(sheet, year, quarter, label);
                    progress.Report(new Core.Models.DemoProgress("Đang tạo Word", 1, 1, 1, 1));
                    return data;
                });
            if (bytes is null) return;
            Services.ExportUi.SaveBytes(bytes, $"so_nxt_Q{quarter}_{year}.docx", "Word (*.docx)|*.docx", "docx");
            Ok("Đã xuất sổ NXT Word.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    [RelayCommand]
    private async Task ExportExcel()
    {
        try
        {
            if (!int.TryParse(Year?.Trim(), out var year))
            {
                Fail("Năm không hợp lệ.");
                return;
            }
            var quarter = Array.IndexOf(Quarters, Quarter) + 1;
            if (quarter < 1) quarter = 1;
            var sheet = Sheet;
            var label = WarehouseLabel;
            var bytes = await Services.ExportUi.RunBusyAsync(
                "Đang xuất sổ NXT Excel...",
                progress =>
                {
                    progress.Report(new Core.Models.DemoProgress("Đang tạo Excel", 0, 1, 0, 1));
                    var data = Services.ExportUi.CreateService().ExportNxtExcel(sheet, year, quarter, label);
                    progress.Report(new Core.Models.DemoProgress("Đang tạo Excel", 1, 1, 1, 1));
                    return data;
                });
            if (bytes is null) return;
            Services.ExportUi.SaveBytes(bytes, $"so_nxt_Q{quarter}_{year}.xlsx", "Excel (*.xlsx)|*.xlsx", "xlsx");
            Ok("Đã xuất sổ NXT Excel.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }""",
    "NxtVm export",
)

replace(
    nxt,
    """    [RelayCommand]
    private void ExportWord()
    {
        try
        {
            if (!TryPeriod(out var year, out var quarter))
                return;
            var sheet = System.GetNxtTotal(year, quarter, _warehouseIds, LotViewMode);
            var bytes = Services.ExportUi.CreateService().ExportNxtTotalWord(sheet, year, quarter, WarehouseLabel);
            Services.ExportUi.SaveBytes(bytes, $"so_nxt_tong_Q{quarter}_{year}.docx", "Word (*.docx)|*.docx", "docx");
            Ok("Đã xuất NXT tổng Word.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    [RelayCommand]
    private void ExportExcel()
    {
        try
        {
            if (!TryPeriod(out var year, out var quarter))
                return;
            var sheet = System.GetNxtTotal(year, quarter, _warehouseIds, LotViewMode);
            var bytes = Services.ExportUi.CreateService().ExportNxtTotalExcel(sheet, year, quarter, WarehouseLabel);
            Services.ExportUi.SaveBytes(bytes, $"so_nxt_tong_Q{quarter}_{year}.xlsx", "Excel (*.xlsx)|*.xlsx", "xlsx");
            Ok("Đã xuất NXT tổng Excel.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }""",
    """    [RelayCommand]
    private async Task ExportWord()
    {
        try
        {
            if (!TryPeriod(out var year, out var quarter))
                return;
            var ids = _warehouseIds.ToList();
            var mode = LotViewMode;
            var label = WarehouseLabel;
            var bytes = await Services.ExportUi.RunBusyAsync(
                "Đang xuất NXT tổng Word...",
                progress =>
                {
                    progress.Report(new Core.Models.DemoProgress("Đang đọc NXT tổng", 0, 2, 0, 2));
                    var sheet = System.GetNxtTotal(year, quarter, ids, mode);
                    progress.Report(new Core.Models.DemoProgress("Đang tạo Word", 1, 2, 1, 2));
                    var data = Services.ExportUi.CreateService().ExportNxtTotalWord(sheet, year, quarter, label);
                    progress.Report(new Core.Models.DemoProgress("Đang tạo Word", 2, 2, 2, 2));
                    return data;
                });
            if (bytes is null) return;
            Services.ExportUi.SaveBytes(bytes, $"so_nxt_tong_Q{quarter}_{year}.docx", "Word (*.docx)|*.docx", "docx");
            Ok("Đã xuất NXT tổng Word.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    [RelayCommand]
    private async Task ExportExcel()
    {
        try
        {
            if (!TryPeriod(out var year, out var quarter))
                return;
            var ids = _warehouseIds.ToList();
            var mode = LotViewMode;
            var label = WarehouseLabel;
            var bytes = await Services.ExportUi.RunBusyAsync(
                "Đang xuất NXT tổng Excel...",
                progress =>
                {
                    progress.Report(new Core.Models.DemoProgress("Đang đọc NXT tổng", 0, 2, 0, 2));
                    var sheet = System.GetNxtTotal(year, quarter, ids, mode);
                    progress.Report(new Core.Models.DemoProgress("Đang tạo Excel", 1, 2, 1, 2));
                    var data = Services.ExportUi.CreateService().ExportNxtTotalExcel(sheet, year, quarter, label);
                    progress.Report(new Core.Models.DemoProgress("Đang tạo Excel", 2, 2, 2, 2));
                    return data;
                });
            if (bytes is null) return;
            Services.ExportUi.SaveBytes(bytes, $"so_nxt_tong_Q{quarter}_{year}.xlsx", "Excel (*.xlsx)|*.xlsx", "xlsx");
            Ok("Đã xuất NXT tổng Excel.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }""",
    "NxtTotalVm export",
)

print("All patches applied.")
