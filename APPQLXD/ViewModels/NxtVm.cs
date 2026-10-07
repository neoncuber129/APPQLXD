using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using APPQLXD.Controls;
using APPQLXD.Core;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using APPQLXD.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace APPQLXD.ViewModels;

/// <summary>Sổ NXT — NxtView bind theo interface này.</summary>
public interface INxtBoard : INotifyPropertyChanged
{
    NxtSheet Sheet { get; }
    string PageTitle { get; }
    string PageHint { get; }
    bool ShowWarehousePicker { get; }
    string WarehouseLabel { get; }
    bool TakePreserveScroll();
    void Edit(NxtRow row);
}

public partial class NxtWarehouseVm : ObservableObject
{
    public NxtWarehouseVm(Guid id, string name, WarehouseType type, bool isChecked)
    {
        Id = id;
        Name = name;
        Type = type;
        _isChecked = isChecked;
    }

    public Guid Id { get; }
    public string Name { get; }
    public WarehouseType Type { get; }
    [ObservableProperty] private bool _isChecked;
}

public partial class NxtVm : PageVm, INxtBoard
{
    private bool _loading;
    private bool _preserveScroll;
    private string _loadedKey = "";

    public NxtVm(FuelSystem system) : base(system)
    {
        Year = DateTime.Today.Year.ToString();
        Quarter = Quarters[(DateTime.Today.Month - 1) / 3];
    }

    public string PageTitle => "Sổ NXT";
    public string PageHint =>
        "Sổ theo quý, chỉ số lượng. Chế độ xem: TX + SSCĐ hoặc IUU. Mặc định xem kho chính. Bấm Chọn kho để đổi hoặc thêm kho. Dòng Mang sang là tồn đầu kỳ. Dòng Cộng mang sang gộp nhập–xuất phát sinh và tồn cuối kỳ. Cột chứng từ: N = phiếu nhập; XX = phiếu xuất có xăng; XD = các phiếu còn lại. Diễn giải là tính chất nhập hoặc tính chất xuất. Bấm một dòng phiếu để sửa.";
    public bool ShowWarehousePicker => true;

    public string[] Quarters { get; } = ["Quý 1", "Quý 2", "Quý 3", "Quý 4"];
    public ObservableCollection<NxtWarehouseVm> Warehouses { get; } = [];
    [ObservableProperty] private string _year = "";
    [ObservableProperty] private string _quarter = "Quý 1";
    [ObservableProperty] private NxtSheet _sheet = new([], []);
    [ObservableProperty] private NxtLotViewMode _lotViewMode = NxtLotViewMode.TxSscd;

    public bool IsTxSscdView => LotViewMode == NxtLotViewMode.TxSscd;
    public bool IsIuuView => LotViewMode == NxtLotViewMode.Iuu;

    partial void OnLotViewModeChanged(NxtLotViewMode value)
    {
        OnPropertyChanged(nameof(IsTxSscdView));
        OnPropertyChanged(nameof(IsIuuView));
        if (!_loading)
            Reload();
    }

    [RelayCommand]
    private void ShowTxSscdView() => LotViewMode = NxtLotViewMode.TxSscd;

    [RelayCommand]
    private void ShowIuuView() => LotViewMode = NxtLotViewMode.Iuu;

    [RelayCommand]
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
            var svc = Services.ExportUi.CreateService();
            var bytes = await Services.ExportUi.RunBusyAsync(
                "Đang xuất sổ NXT Word...",
                progress =>
                {
                    progress.Report(new Core.Models.DemoProgress("Đang tạo Word", 0, 1, 0, 1));
                    var data = svc.ExportNxtWord(sheet, year, quarter, label);
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
            var svc = Services.ExportUi.CreateService();
            var bytes = await Services.ExportUi.RunBusyAsync(
                "Đang xuất sổ NXT Excel...",
                progress =>
                {
                    progress.Report(new Core.Models.DemoProgress("Đang tạo Excel", 0, 1, 0, 1));
                    var data = svc.ExportNxtExcel(sheet, year, quarter, label);
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
    }

    partial void OnYearChanged(string value)
    {
        if (!_loading)
            Reload();
    }

    partial void OnQuarterChanged(string value)
    {
        if (!_loading)
            Reload();
    }

    public override void Refresh() => Reload();

    public override async Task RefreshAsync(IProgress<DemoProgress>? progress = null)
    {
        progress?.Report(new DemoProgress("Đang đọc sổ NXT", 0, 1, 0, 1));
        _loading = true;
        if (string.IsNullOrWhiteSpace(Year))
            Year = DateTime.Today.Year.ToString();
        if (string.IsNullOrWhiteSpace(Quarter))
            Quarter = Quarters[(DateTime.Today.Month - 1) / 3];
        var previous = Warehouses.ToDictionary(x => x.Id, x => x.IsChecked);
        var first = Warehouses.Count == 0;
        var rows = await Task.Run(() => System.GetWarehouses().OrderBy(x => x.Type).ThenBy(x => x.Name).ToList());
        Warehouses.Clear();
        foreach (var row in rows)
        {
            var on = first
                ? row.Type == WarehouseType.Main
                : previous.TryGetValue(row.Id, out var was) ? was : row.Type == WarehouseType.Main;
            Warehouses.Add(new NxtWarehouseVm(row.Id, row.Name, row.Type, on));
        }

        _loading = false;
        OnPropertyChanged(nameof(WarehouseLabel));
        if (Sheet.Rows.Count > 0 && CurrentSheetKey() == _loadedKey)
        {
            progress?.Report(new DemoProgress("Sổ NXT sẵn sàng", 1, 1, 1, 1));
            return;
        }

        await ReloadAsync(progress);
    }

    public string WarehouseLabel
    {
        get
        {
            var names = Warehouses.Where(x => x.IsChecked).Select(x => x.Name).ToList();
            return names.Count == 0 ? "Chưa chọn kho" : string.Join(", ", names);
        }
    }

    [RelayCommand]
    private void ChooseWarehouses()
    {
        var snapshot = Warehouses.ToDictionary(x => x.Id, x => x.IsChecked);
        if (!NxtWarehouseWindow.Show(Warehouses))
        {
            foreach (var box in Warehouses)
                box.IsChecked = snapshot[box.Id];
            return;
        }

        OnPropertyChanged(nameof(WarehouseLabel));
        Reload(false);
    }

    public bool TakePreserveScroll()
    {
        var keep = _preserveScroll;
        _preserveScroll = false;
        return keep;
    }

    private void Reload() => Reload(false);

    private void Reload(bool preserveScroll)
    {
        if (_loading)
            return;
        if (!int.TryParse(Year?.Trim(), out var year))
        {
            Sheet = new NxtSheet([], []);
            Fail("Năm không hợp lệ.");
            return;
        }

        var quarter = Array.IndexOf(Quarters, Quarter) + 1;
        if (quarter < 1)
            quarter = 1;
        var ids = Warehouses.Where(x => x.IsChecked).Select(x => x.Id).ToList();
        _ = ReloadAsync(null, year, quarter, ids, preserveScroll);
    }

    private async Task ReloadAsync(IProgress<DemoProgress>? progress, int? year = null, int? quarter = null, List<Guid>? ids = null, bool preserveScroll = false)
    {
        if (year is null || quarter is null || ids is null)
        {
            if (!int.TryParse(Year?.Trim(), out var parsed))
            {
                Sheet = new NxtSheet([], []);
                Fail("Năm không hợp lệ.");
                return;
            }

            year = parsed;
            quarter = Array.IndexOf(Quarters, Quarter) + 1;
            if (quarter < 1)
                quarter = 1;
            ids = Warehouses.Where(x => x.IsChecked).Select(x => x.Id).ToList();
        }

        progress?.Report(new DemoProgress("Đang đọc sổ NXT", 0, 1, 0, 1));
        var capturedYear = year.Value;
        var capturedQuarter = quarter.Value;
        var capturedIds = ids;
        var capturedMode = LotViewMode;
        var sheet = await Task.Run(() => System.GetNxt(capturedYear, capturedQuarter, capturedIds, capturedMode));
        progress?.Report(new DemoProgress("Đang vẽ sổ NXT", 1, 1, 1, 1));
        _preserveScroll = preserveScroll;
        Sheet = sheet;
        _loadedKey = SheetKey(capturedYear, capturedQuarter, capturedMode, capturedIds);
        Ok("");
    }

    private string CurrentSheetKey()
    {
        if (!int.TryParse(Year?.Trim(), out var year))
            return "";
        var quarter = Array.IndexOf(Quarters, Quarter) + 1;
        if (quarter < 1) quarter = 1;
        var ids = Warehouses.Where(x => x.IsChecked).Select(x => x.Id).ToList();
        return SheetKey(year, quarter, LotViewMode, ids);
    }

    private static string SheetKey(int year, int quarter, NxtLotViewMode mode, IReadOnlyList<Guid> ids) =>
        $"{year}|{quarter}|{(int)mode}|{string.Join(',', ids.OrderBy(x => x))}";

    public void Edit(NxtRow row)
    {
        if (row.Kind != NxtRowKind.Slip || row.DocumentId is not Guid id || row.DocumentKind is not DocumentKind kind)
            return;
        SlipEditWindow.Open(System, kind, id, () =>
        {
            _loadedKey = "";
            Reload(true);
        });
    }
}

/// <summary>
/// NXT tổng (mọi kho, bỏ ĐC) hoặc NXT từng kho (chọn 1 kho, tính ĐC) — cùng giao diện bảng.
/// </summary>
public partial class NxtTotalVm : PageVm
{
    private bool _loading;
    private List<Guid> _warehouseIds = [];
    private string _loadedKey = "";

    public NxtTotalVm(FuelSystem system, bool perWarehouse = false) : base(system)
    {
        PerWarehouse = perWarehouse;
        Year = DateTime.Today.Year.ToString();
        Quarter = Quarters[(DateTime.Today.Month - 1) / 3];
    }

    /// <summary>True = tab NXT từng kho (chọn 1 kho, tính điều chuyển).</summary>
    public bool PerWarehouse { get; }

    public string PageTitle => PerWarehouse ? "NXT từng kho" : "NXT tổng";

    public string PageHint => PerWarehouse
        ? "Chọn một kho để xem từng lô trong quý (mỗi dòng = một lô: mặt hàng + đơn giá + loại lô). Cột: tồn đầu → nhập (PN, ĐC vào) → xuất (ĐC ra, tiêu thụ, xuất lẻ) → tồn sau. Đổi loại lô TX↔SSCĐ không ghi nhập/xuất; tồn sau điều chỉnh và cột Ghi chú (Tồn sau tăng/giảm do đổi →…) giống NXT tổng."
        : "Theo nhóm nhiên liệu, trong mỗi nhóm chia theo loại lô. Chế độ xem: Toàn bộ, TX + SSCĐ hoặc IUU. Đổi loại lô TX↔SSCĐ hiện Xuất/Nhập theo loại kèm ghi chú. Tiêu đề nhóm và loại lô hiện tổng tồn đầu / nhập / xuất / tồn sau. Điều chuyển nội bộ không tính nhập–xuất.";

    public string WarehouseLabel => PerWarehouse
        ? SelectedWarehouse is null
            ? "Chưa chọn kho"
            : $"{SelectedWarehouse.Name} ({SelectedWarehouse.ConsumerTypeName ?? SelectedWarehouse.TypeName})"
        : "Kho lớn — toàn bộ kho trong phạm vi làm việc";

    public string[] Quarters { get; } = ["Quý 1", "Quý 2", "Quý 3", "Quý 4"];
    public ObservableCollection<NxtTotalRowVm> Rows { get; } = [];
    public ObservableCollection<WarehouseRow> Warehouses { get; } = [];

    [ObservableProperty] private string _year = "";
    [ObservableProperty] private string _quarter = "Quý 1";
    [ObservableProperty] private NxtLotViewMode _lotViewMode = NxtLotViewMode.All;
    [ObservableProperty] private WarehouseRow? _selectedWarehouse;

    public bool IsAllView => LotViewMode == NxtLotViewMode.All;
    public bool IsTxSscdView => LotViewMode == NxtLotViewMode.TxSscd;
    public bool IsIuuView => LotViewMode == NxtLotViewMode.Iuu;

    partial void OnLotViewModeChanged(NxtLotViewMode value)
    {
        OnPropertyChanged(nameof(IsAllView));
        OnPropertyChanged(nameof(IsTxSscdView));
        OnPropertyChanged(nameof(IsIuuView));
        if (!_loading)
            Reload();
    }

    partial void OnSelectedWarehouseChanged(WarehouseRow? value)
    {
        OnPropertyChanged(nameof(WarehouseLabel));
        if (!_loading)
            Reload();
    }

    [RelayCommand]
    private void ShowAllView() => LotViewMode = NxtLotViewMode.All;

    [RelayCommand]
    private void ShowTxSscdView() => LotViewMode = NxtLotViewMode.TxSscd;

    [RelayCommand]
    private void ShowIuuView() => LotViewMode = NxtLotViewMode.Iuu;

    [RelayCommand]
    private async Task ExportWord()
    {
        try
        {
            if (!TryPeriod(out var year, out var quarter))
                return;
            if (!TryQueryIds(out var ids))
                return;
            var mode = LotViewMode;
            var label = WarehouseLabel;
            var perWh = PerWarehouse;
            var title = PageTitle;
            var svc = Services.ExportUi.CreateService();
            var bytes = await Services.ExportUi.RunBusyAsync(
                $"Đang xuất {title} Word...",
                progress =>
                {
                    progress.Report(new Core.Models.DemoProgress($"Đang đọc {title}", 0, 2, 0, 2));
                    var sheet = LoadSheet(year, quarter, ids, mode, perWh);
                    progress.Report(new Core.Models.DemoProgress("Đang tạo Word", 1, 2, 1, 2));
                    var data = svc.ExportNxtTotalWord(sheet, year, quarter, label);
                    progress.Report(new Core.Models.DemoProgress("Đang tạo Word", 2, 2, 2, 2));
                    return data;
                });
            if (bytes is null) return;
            var file = perWh ? $"so_nxt_kho_Q{quarter}_{year}.docx" : $"so_nxt_tong_Q{quarter}_{year}.docx";
            Services.ExportUi.SaveBytes(bytes, file, "Word (*.docx)|*.docx", "docx");
            Ok($"Đã xuất {title} Word.");
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
            if (!TryQueryIds(out var ids))
                return;
            var mode = LotViewMode;
            var label = WarehouseLabel;
            var perWh = PerWarehouse;
            var title = PageTitle;
            var svc = Services.ExportUi.CreateService();
            var bytes = await Services.ExportUi.RunBusyAsync(
                $"Đang xuất {title} Excel...",
                progress =>
                {
                    progress.Report(new Core.Models.DemoProgress($"Đang đọc {title}", 0, 2, 0, 2));
                    var sheet = LoadSheet(year, quarter, ids, mode, perWh);
                    progress.Report(new Core.Models.DemoProgress("Đang tạo Excel", 1, 2, 1, 2));
                    var data = svc.ExportNxtTotalExcel(sheet, year, quarter, label);
                    progress.Report(new Core.Models.DemoProgress("Đang tạo Excel", 2, 2, 2, 2));
                    return data;
                });
            if (bytes is null) return;
            var file = perWh ? $"so_nxt_kho_Q{quarter}_{year}.xlsx" : $"so_nxt_tong_Q{quarter}_{year}.xlsx";
            Services.ExportUi.SaveBytes(bytes, file, "Excel (*.xlsx)|*.xlsx", "xlsx");
            Ok($"Đã xuất {title} Excel.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    private bool TryPeriod(out int year, out int quarter)
    {
        year = 0;
        quarter = 1;
        if (!int.TryParse(Year?.Trim(), out year))
        {
            Fail("Năm không hợp lệ.");
            return false;
        }
        quarter = Array.IndexOf(Quarters, Quarter) + 1;
        if (quarter < 1) quarter = 1;
        return true;
    }

    private bool TryQueryIds(out List<Guid> ids)
    {
        ids = [];
        if (PerWarehouse)
        {
            if (SelectedWarehouse is null)
            {
                Fail("Chọn kho để xem NXT.");
                return false;
            }

            ids = [SelectedWarehouse.Id];
            return true;
        }

        ids = _warehouseIds.Count > 0
            ? _warehouseIds.ToList()
            : System.GetWarehouses().Select(x => x.Id).ToList();
        return ids.Count > 0;
    }

    private NxtTotalSheet LoadSheet(int year, int quarter, IReadOnlyList<Guid> ids, NxtLotViewMode mode, bool perWarehouse) =>
        perWarehouse
            ? System.GetNxtWarehouseTotal(year, quarter, ids[0], mode)
            : System.GetNxtTotal(year, quarter, ids, mode);

    partial void OnYearChanged(string value)
    {
        if (!_loading)
            Reload();
    }

    partial void OnQuarterChanged(string value)
    {
        if (!_loading)
            Reload();
    }

    public override void Refresh() => Reload();

    public override async Task RefreshAsync(IProgress<DemoProgress>? progress = null)
    {
        progress?.Report(new DemoProgress($"Đang đọc {PageTitle}", 0, 1, 0, 1));
        _loading = true;
        if (string.IsNullOrWhiteSpace(Year))
            Year = DateTime.Today.Year.ToString();
        if (string.IsNullOrWhiteSpace(Quarter))
            Quarter = Quarters[(DateTime.Today.Month - 1) / 3];

        var rows = await Task.Run(() => System.GetWarehouses()
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .ToList());
        if (PerWarehouse)
        {
            var previousId = SelectedWarehouse?.Id;
            Warehouses.Clear();
            foreach (var row in rows)
                Warehouses.Add(row);
            SelectedWarehouse = Warehouses.FirstOrDefault(x => x.Id == previousId)
                ?? Warehouses.FirstOrDefault(x => x.Type == WarehouseType.Main)
                ?? Warehouses.FirstOrDefault();
            _warehouseIds = SelectedWarehouse is null ? [] : [SelectedWarehouse.Id];
        }
        else
            _warehouseIds = rows.Select(x => x.Id).ToList();

        _loading = false;
        OnPropertyChanged(nameof(WarehouseLabel));
        if (Rows.Count > 0 && CurrentSheetKey() == _loadedKey)
        {
            progress?.Report(new DemoProgress($"{PageTitle} sẵn sàng", 1, 1, 1, 1));
            return;
        }

        await ReloadAsync(progress);
    }

    private void Reload()
    {
        if (_loading)
            return;
        if (!int.TryParse(Year?.Trim(), out var year))
        {
            Rows.Clear();
            Fail("Năm không hợp lệ.");
            return;
        }

        var quarter = Array.IndexOf(Quarters, Quarter) + 1;
        if (quarter < 1)
            quarter = 1;
        _ = ReloadAsync(null, year, quarter);
    }

    private async Task ReloadAsync(IProgress<DemoProgress>? progress, int? year = null, int? quarter = null)
    {
        if (year is null || quarter is null)
        {
            if (!int.TryParse(Year?.Trim(), out var parsed))
            {
                Rows.Clear();
                Fail("Năm không hợp lệ.");
                return;
            }

            year = parsed;
            quarter = Array.IndexOf(Quarters, Quarter) + 1;
            if (quarter < 1)
                quarter = 1;
        }

        if (PerWarehouse)
        {
            if (SelectedWarehouse is null)
            {
                Rows.Clear();
                _loadedKey = "";
                return;
            }

            _warehouseIds = [SelectedWarehouse.Id];
        }
        else if (_warehouseIds.Count == 0)
            _warehouseIds = System.GetWarehouses().Select(x => x.Id).ToList();

        progress?.Report(new DemoProgress($"Đang đọc {PageTitle}", 0, 1, 0, 1));
        var capturedYear = year.Value;
        var capturedQuarter = quarter.Value;
        var capturedIds = _warehouseIds.ToList();
        var capturedMode = LotViewMode;
        var perWh = PerWarehouse;
        if (capturedIds.Count == 0)
        {
            Rows.Clear();
            _loadedKey = "";
            return;
        }

        var sheet = await Task.Run(() => LoadSheet(capturedYear, capturedQuarter, capturedIds, capturedMode, perWh));
        progress?.Report(new DemoProgress($"Đang vẽ {PageTitle}", 1, 1, 1, 1));
        Rows.Clear();
        foreach (var group in sheet.Rows.GroupBy(x => x.GroupName, StringComparer.Ordinal))
        {
            var members = group.ToList();
            if (members.Count == 0)
                continue;
            var label = FuelGroupOrder.Label(group.Key);
            var color = FuelGroupOrder.Rank(group.Key);
            Rows.Add(NxtTotalRowVm.Header(
                label,
                color,
                members.Sum(x => x.OpeningMain),
                members.Sum(x => x.OpeningMachine),
                members.Sum(x => x.OpeningVehicle),
                members.Sum(x => x.OpeningShip),
                members.Sum(x => x.OpeningTotal),
                members.Sum(x => x.InMain),
                members.Sum(x => x.InMachine),
                members.Sum(x => x.InVehicle),
                members.Sum(x => x.InShip),
                members.Sum(x => x.InTotal),
                members.Sum(x => x.OutMain),
                members.Sum(x => x.OutMachine),
                members.Sum(x => x.OutVehicle),
                members.Sum(x => x.OutShip),
                members.Sum(x => x.OutTotal),
                members.Sum(x => x.ClosingMain),
                members.Sum(x => x.ClosingMachine),
                members.Sum(x => x.ClosingVehicle),
                members.Sum(x => x.ClosingShip),
                members.Sum(x => x.ClosingTotal)));

            foreach (var typeGroup in members
                         .GroupBy(x => (x.LotTypeId, Code: string.IsNullOrWhiteSpace(x.LotTypeCode) ? "TX" : x.LotTypeCode))
                         .OrderBy(g => LotTypeRank(g.Key.Code))
                         .ThenBy(g => g.Key.Code, StringComparer.CurrentCultureIgnoreCase))
            {
                var typeMembers = typeGroup.ToList();
                Rows.Add(NxtTotalRowVm.LotTypeHeader(
                    typeGroup.Key.Code,
                    color,
                    typeMembers.Sum(x => x.OpeningMain),
                    typeMembers.Sum(x => x.OpeningMachine),
                    typeMembers.Sum(x => x.OpeningVehicle),
                    typeMembers.Sum(x => x.OpeningShip),
                    typeMembers.Sum(x => x.OpeningTotal),
                    typeMembers.Sum(x => x.InMain),
                    typeMembers.Sum(x => x.InMachine),
                    typeMembers.Sum(x => x.InVehicle),
                    typeMembers.Sum(x => x.InShip),
                    typeMembers.Sum(x => x.InTotal),
                    typeMembers.Sum(x => x.OutMain),
                    typeMembers.Sum(x => x.OutMachine),
                    typeMembers.Sum(x => x.OutVehicle),
                    typeMembers.Sum(x => x.OutShip),
                    typeMembers.Sum(x => x.OutTotal),
                    typeMembers.Sum(x => x.ClosingMain),
                    typeMembers.Sum(x => x.ClosingMachine),
                    typeMembers.Sum(x => x.ClosingVehicle),
                    typeMembers.Sum(x => x.ClosingShip),
                    typeMembers.Sum(x => x.ClosingTotal)));
                foreach (var row in typeMembers)
                    Rows.Add(NxtTotalRowVm.Item(row, color));
            }
        }

        _loadedKey = SheetKey(capturedYear, capturedQuarter, capturedMode, capturedIds, perWh);
        SheetRevision++;
        Ok("");
    }

    [ObservableProperty] private int _sheetRevision;

    private string CurrentSheetKey()
    {
        if (!int.TryParse(Year?.Trim(), out var year))
            return "";
        var quarter = Array.IndexOf(Quarters, Quarter) + 1;
        if (quarter < 1) quarter = 1;
        return SheetKey(year, quarter, LotViewMode, _warehouseIds, PerWarehouse);
    }

    private static string SheetKey(int year, int quarter, NxtLotViewMode mode, IReadOnlyList<Guid> ids, bool perWarehouse) =>
        $"{(perWarehouse ? "W" : "T")}|{year}|{quarter}|{(int)mode}|{string.Join(',', ids.OrderBy(x => x))}";

    private static int LotTypeRank(string code) => code.Trim().ToUpperInvariant() switch
    {
        "TX" => 0,
        "SSCĐ" or "SSCD" => 1,
        "IUU" => 2,
        _ => 10
    };
}

public sealed class NxtTotalRowVm
{
    private static readonly (string Header, string SubHeader, string Row, string Ink)[] Palette =
    [
        ("#2E5A88", "#4A7AA8", "#E8F0F8", "#1B2836"), // Dầu
        ("#8A5A12", "#B07A2E", "#FFF6E8", "#5C3A0A"), // Xăng
        ("#2F6B4F", "#4A8B6A", "#E8F5EE", "#1B3D2E"), // Nhớt
        ("#6B3A6B", "#8B5A8B", "#F5EAF5", "#3D2140"), // Mỡ
        ("#4A6678", "#6A86A0", "#EEF2F5", "#1B2836"), // Khác
        ("#8B3A2F", "#AB5A4F", "#F8EBE8", "#4A1F18"),
    ];

    private static string Fmt(decimal val) => val == 0 ? "" : val.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"));

    private NxtTotalRowVm(
        string lotText,
        string unitPriceText,
        string lotTypeText,
        decimal openingMain,
        decimal openingMachine,
        decimal openingVehicle,
        decimal openingShip,
        decimal openingTotal,
        decimal inMain,
        decimal inMachine,
        decimal inVehicle,
        decimal inShip,
        decimal inTotal,
        decimal outMain,
        decimal outMachine,
        decimal outVehicle,
        decimal outShip,
        decimal outTotal,
        decimal closingMain,
        decimal closingMachine,
        decimal closingVehicle,
        decimal closingShip,
        decimal closingTotal,
        bool isGroupHeader,
        bool isLotTypeHeader,
        int colorIndex,
        string note = "")
    {
        LotText = lotText;
        UnitPriceText = unitPriceText;
        LotTypeText = lotTypeText;

        OpeningMain = openingMain;
        OpeningMachine = openingMachine;
        OpeningVehicle = openingVehicle;
        OpeningShip = openingShip;
        OpeningTotal = openingTotal;

        InMain = inMain;
        InMachine = inMachine;
        InVehicle = inVehicle;
        InShip = inShip;
        InTotal = inTotal;

        OutMain = outMain;
        OutMachine = outMachine;
        OutVehicle = outVehicle;
        OutShip = outShip;
        OutTotal = outTotal;

        ClosingMain = closingMain;
        ClosingMachine = closingMachine;
        ClosingVehicle = closingVehicle;
        ClosingShip = closingShip;
        ClosingTotal = closingTotal;

        IsGroupHeader = isGroupHeader;
        IsLotTypeHeader = isLotTypeHeader;
        Note = note;
        var tone = Palette[ColorIndexOf(colorIndex)];
        HeaderBackground = tone.Header;
        if (isGroupHeader)
        {
            RowBackground = tone.Header;
            RowForeground = "#FFFFFF";
            FontWeight = FontWeights.SemiBold;
        }
        else if (isLotTypeHeader)
        {
            RowBackground = tone.SubHeader;
            RowForeground = "#FFFFFF";
            FontWeight = FontWeights.SemiBold;
        }
        else
        {
            RowBackground = tone.Row;
            RowForeground = tone.Ink;
            FontWeight = FontWeights.Normal;
        }
    }

    private static int ColorIndexOf(int colorIndex)
    {
        if (colorIndex is >= 0 and <= 3)
            return colorIndex;
        return 4 + (Math.Abs(colorIndex) % (Palette.Length - 4));
    }

    public static NxtTotalRowVm Header(
        string groupName,
        int colorIndex,
        decimal openMain, decimal openMach, decimal openVeh, decimal openShip, decimal openTotal,
        decimal inMain, decimal inMach, decimal inVeh, decimal inShip, decimal inTotal,
        decimal outMain, decimal outMach, decimal outVeh, decimal outShip, decimal outTotal,
        decimal closeMain, decimal closeMach, decimal closeVeh, decimal closeShip, decimal closeTotal) =>
        new(groupName, "", "",
            openMain, openMach, openVeh, openShip, openTotal,
            inMain, inMach, inVeh, inShip, inTotal,
            outMain, outMach, outVeh, outShip, outTotal,
            closeMain, closeMach, closeVeh, closeShip, closeTotal,
            true, false, colorIndex);

    public static NxtTotalRowVm LotTypeHeader(
        string lotTypeCode,
        int colorIndex,
        decimal openMain, decimal openMach, decimal openVeh, decimal openShip, decimal openTotal,
        decimal inMain, decimal inMach, decimal inVeh, decimal inShip, decimal inTotal,
        decimal outMain, decimal outMach, decimal outVeh, decimal outShip, decimal outTotal,
        decimal closeMain, decimal closeMach, decimal closeVeh, decimal closeShip, decimal closeTotal) =>
        new($"  {lotTypeCode}", "", lotTypeCode,
            openMain, openMach, openVeh, openShip, openTotal,
            inMain, inMach, inVeh, inShip, inTotal,
            outMain, outMach, outVeh, outShip, outTotal,
            closeMain, closeMach, closeVeh, closeShip, closeTotal,
            false, true, colorIndex);

    public static NxtTotalRowVm Item(NxtTotalRow row, int colorIndex) =>
        new(
            row.ItemName,
            row.UnitPrice > 0 ? row.UnitPrice.ToString("N0") : "",
            row.LotTypeCode,
            row.OpeningMain,
            row.OpeningMachine,
            row.OpeningVehicle,
            row.OpeningShip,
            row.OpeningTotal,
            row.InMain,
            row.InMachine,
            row.InVehicle,
            row.InShip,
            row.InTotal,
            row.OutMain,
            row.OutMachine,
            row.OutVehicle,
            row.OutShip,
            row.OutTotal,
            row.ClosingMain,
            row.ClosingMachine,
            row.ClosingVehicle,
            row.ClosingShip,
            row.ClosingTotal,
            false,
            false,
            colorIndex,
            row.Note);

    public string LotText { get; }
    public string UnitPriceText { get; }
    public string LotTypeText { get; }

    public decimal OpeningMain { get; }
    public decimal OpeningMachine { get; }
    public decimal OpeningVehicle { get; }
    public decimal OpeningShip { get; }
    public decimal OpeningTotal { get; }

    public decimal InMain { get; }
    public decimal InMachine { get; }
    public decimal InVehicle { get; }
    public decimal InShip { get; }
    public decimal InTotal { get; }

    public decimal OutMain { get; }
    public decimal OutMachine { get; }
    public decimal OutVehicle { get; }
    public decimal OutShip { get; }
    public decimal OutTotal { get; }

    public decimal ClosingMain { get; }
    public decimal ClosingMachine { get; }
    public decimal ClosingVehicle { get; }
    public decimal ClosingShip { get; }
    public decimal ClosingTotal { get; }

    public decimal Opening => OpeningTotal;
    public decimal In => InTotal;
    public decimal Out => OutTotal;
    public decimal Closing => ClosingTotal;

    public string OpeningMainText => Fmt(OpeningMain);
    public string OpeningMachineText => Fmt(OpeningMachine);
    public string OpeningVehicleText => Fmt(OpeningVehicle);
    public string OpeningShipText => Fmt(OpeningShip);
    public string OpeningTotalText => Fmt(OpeningTotal);

    public string InMainText => Fmt(InMain);
    public string InMachineText => Fmt(InMachine);
    public string InVehicleText => Fmt(InVehicle);
    public string InShipText => Fmt(InShip);
    public string InTotalText => Fmt(InTotal);

    public string OutMainText => Fmt(OutMain);
    public string OutMachineText => Fmt(OutMachine);
    public string OutVehicleText => Fmt(OutVehicle);
    public string OutShipText => Fmt(OutShip);
    public string OutTotalText => Fmt(OutTotal);

    public string ClosingMainText => Fmt(ClosingMain);
    public string ClosingMachineText => Fmt(ClosingMachine);
    public string ClosingVehicleText => Fmt(ClosingVehicle);
    public string ClosingShipText => Fmt(ClosingShip);
    public string ClosingTotalText => Fmt(ClosingTotal);

    public string Note { get; }
    public bool IsGroupHeader { get; }
    public bool IsLotTypeHeader { get; }
    public string HeaderBackground { get; }
    public string RowBackground { get; }
    public string RowForeground { get; }
    public FontWeight FontWeight { get; }
}
