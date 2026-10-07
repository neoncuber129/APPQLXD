using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using APPQLXD.Core;
using APPQLXD.Core.Calculations;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using APPQLXD.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace APPQLXD.ViewModels;

public partial class QuotaVm : PageVm
{
    private bool _loading;
    private string _loadedKey = "";

    public QuotaVm(FuelSystem system) : base(system)
    {
        Year = DateTime.Today.Year.ToString(CultureInfo.InvariantCulture);
        Quarter = Quarters[(DateTime.Today.Month - 1) / 3];
    }

    public string PageTitle => "Hạn mức";
    public string PageHint =>
        "Hai bảng hạn mức riêng: TX + SSCĐ và IUU. Kỳ từ 01/01 đến cuối quý chọn. Cột Máy gồm máy + tàu. Hạn mức nhập theo từng bảng.";

    public string[] Quarters { get; } = ["Quý 1", "Quý 2", "Quý 3", "Quý 4"];
    public ObservableCollection<QuotaRowVm> Rows { get; } = [];

    [ObservableProperty] private string _year = "";
    [ObservableProperty] private string _quarter = "Quý 1";
    [ObservableProperty] private NxtLotViewMode _lotViewMode = NxtLotViewMode.TxSscd;
    [ObservableProperty] private string _periodLabel = "";
    [ObservableProperty] private int _sheetRevision;

    public bool IsTxSscdView => LotViewMode == NxtLotViewMode.TxSscd;
    public bool IsIuuView => LotViewMode == NxtLotViewMode.Iuu;

    partial void OnYearChanged(string value)
    {
        if (!_loading) Reload();
    }

    partial void OnQuarterChanged(string value)
    {
        if (!_loading) Reload();
    }

    partial void OnLotViewModeChanged(NxtLotViewMode value)
    {
        OnPropertyChanged(nameof(IsTxSscdView));
        OnPropertyChanged(nameof(IsIuuView));
        if (!_loading) Reload();
    }

    [RelayCommand] private void ShowTxSscdView() => LotViewMode = NxtLotViewMode.TxSscd;
    [RelayCommand] private void ShowIuuView() => LotViewMode = NxtLotViewMode.Iuu;

    [RelayCommand]
    private void ClearLimits()
    {
        if (!int.TryParse(Year?.Trim(), out var year))
        {
            Fail("Năm không hợp lệ.");
            return;
        }

        var viewLabel = LotViewMode == NxtLotViewMode.Iuu ? "IUU" : "TX + SSCĐ";
        var result = MessageBox.Show(
            $"Bạn có chắc chắn muốn xóa toàn bộ các ô hạn mức đã điền của năm {year} (bảng {viewLabel}) không?",
            "Xác nhận xóa hạn mức",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        var r = System.ClearMissionYearLimits(year, LotViewMode);
        if (!r.Ok)
        {
            Fail(r.Message);
            return;
        }

        Ok($"Đã xóa toàn bộ hạn mức {viewLabel} năm {year}.");
        Reload(force: true);
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
            if (Rows.Count == 0)
                Reload(force: true);
            if (Rows.Count == 0)
            {
                Fail("Chưa có dữ liệu hạn mức để xuất.");
                return;
            }

            var lotView = LotViewMode is NxtLotViewMode.Iuu ? NxtLotViewMode.Iuu : NxtLotViewMode.TxSscd;
            var sheet = System.GetQuotaSheet(year, quarter, lotView);
            var viewTag = lotView == NxtLotViewMode.Iuu ? "IUU" : "TX_SSCD";
            var svc = Services.ExportUi.CreateService();
            var bytes = await Services.ExportUi.RunBusyAsync(
                "Đang xuất sổ hạn mức Excel...",
                progress =>
                {
                    progress.Report(new DemoProgress("Đang tạo Excel", 0, 1, 0, 1));
                    var data = svc.ExportQuotaExcel(sheet);
                    progress.Report(new DemoProgress("Đang tạo Excel", 1, 1, 1, 1));
                    return data;
                });
            if (bytes is null) return;
            Services.ExportUi.SaveBytes(
                bytes,
                $"so_han_muc_{viewTag}_Q{quarter}_{year}.xlsx",
                "Excel (*.xlsx)|*.xlsx",
                "xlsx");
            Ok("Đã xuất sổ hạn mức Excel.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    public override void Refresh() => Reload(force: true);

    public override async Task RefreshAsync(IProgress<DemoProgress>? progress = null)
    {
        progress?.Report(new DemoProgress("Đang tải hạn mức", 0, 1, 0, 1));
        await Task.Yield();
        Reload(force: true);
        progress?.Report(new DemoProgress("Đã tải hạn mức", 1, 1, 1, 1));
    }

    private void Reload(bool force = false)
    {
        if (!int.TryParse(Year?.Trim(), out var year))
        {
            Fail("Năm không hợp lệ.");
            return;
        }

        var quarter = Array.IndexOf(Quarters, Quarter) + 1;
        if (quarter < 1) quarter = 1;
        var key = $"{year}|{quarter}|{LotViewMode}";
        if (!force && key == _loadedKey && Rows.Count > 0)
            return;

        _loading = true;
        try
        {
            var sheet = System.GetQuotaSheet(year, quarter, LotViewMode);
            var viewLabel = LotViewMode == NxtLotViewMode.Iuu ? "IUU" : "TX + SSCĐ";
            PeriodLabel =
                $"Quý {quarter}: {sheet.FromDate:dd/MM/yyyy} – {sheet.ToDate:dd/MM/yyyy} (Lũy tích từ 01/01/{year}) · {viewLabel}";
            Rows.Clear();
            foreach (var r in sheet.Rows)
                Rows.Add(QuotaRowVm.From(r, year, SaveLimit));
            _loadedKey = key;
            SheetRevision++;
            Ok($"Hạn mức {viewLabel} — năm {year} — {Quarters[quarter - 1]}.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
        finally
        {
            _loading = false;
        }
    }

    private void SaveLimit(QuotaRowVm row)
    {
        if (row.TaskId is not Guid taskId)
            return;
        if (!int.TryParse(Year?.Trim(), out var year))
            return;
        var gas = ParseQty(row.GasolineLimitText);
        var die = ParseQty(row.DieselLimitText);
        var result = System.SaveMissionYearLimit(taskId, year, LotViewMode, gas, die);
        if (!result.Ok)
        {
            Fail(result.Message);
            return;
        }

        Reload(force: true);
    }

    private static decimal ParseQty(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        return Numbers.Try(text, out var v) ? v : 0;
    }
}

public partial class QuotaRowVm : ObservableObject
{
    private readonly Action<QuotaRowVm>? _save;
    private bool _suppress;

    private QuotaRowVm(Action<QuotaRowVm>? save) => _save = save;

    public string Stt { get; init; } = "";
    public string Name { get; init; } = "";
    public Guid? TaskId { get; init; }
    public bool IsHeader { get; init; }
    public bool CanEditLimit => !IsHeader && TaskId is not null;
    public FontWeight RowFontWeight => IsHeader ? FontWeights.SemiBold : FontWeights.Normal;
    public Brush RowForeground => IsHeader
        ? new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C))
        : new SolidColorBrush(Color.FromRgb(0x1B, 0x28, 0x36));

    [ObservableProperty] private string _gasolineLimitText = "";
    [ObservableProperty] private string _dieselLimitText = "";
    public string LimitTotal { get; init; } = "";
    public string GasolineKm { get; init; } = "";
    public string GasolineHours { get; init; } = "";
    public string DieselKm { get; init; } = "";
    public string DieselHours { get; init; } = "";
    public string GasolineVehicle { get; init; } = "";
    public string GasolineMachine { get; init; } = "";
    public string GasolineFuelTotal { get; init; } = "";
    public string DieselVehicle { get; init; } = "";
    public string DieselMachine { get; init; } = "";
    public string DieselFuelTotal { get; init; } = "";
    public string FuelTotal { get; init; } = "";

    public string CumGasoline { get; init; } = "";
    public string CumDiesel { get; init; } = "";
    public string CumTotal { get; init; } = "";

    public string RemainGasoline { get; init; } = "";
    public string RemainDiesel { get; init; } = "";
    public string RemainTotal { get; init; } = "";

    public string ExcessGasoline { get; init; } = "";
    public string ExcessDiesel { get; init; } = "";
    public string ExcessTotal { get; init; } = "";

    // Backwards-compatible aliases
    public string Cumulative => CumTotal;
    public string Remaining => RemainTotal;
    public string Excess => ExcessTotal;

    partial void OnGasolineLimitTextChanged(string value)
    {
        if (!_suppress && CanEditLimit)
            _save?.Invoke(this);
    }

    partial void OnDieselLimitTextChanged(string value)
    {
        if (!_suppress && CanEditLimit)
            _save?.Invoke(this);
    }

    public static QuotaRowVm From(QuotaRow row, int year, Action<QuotaRowVm> save)
    {
        var vm = new QuotaRowVm(save)
        {
            Stt = row.Stt,
            Name = row.Name,
            TaskId = row.TaskId,
            IsHeader = row.IsHeader,
            LimitTotal = Fmt(row.LimitTotal),
            GasolineKm = Fmt(row.GasolineKm),
            GasolineHours = Fmt(row.GasolineHours),
            DieselKm = Fmt(row.DieselKm),
            DieselHours = Fmt(row.DieselHours),
            GasolineVehicle = Fmt(row.GasolineVehicle),
            GasolineMachine = Fmt(row.GasolineMachine),
            GasolineFuelTotal = Fmt(row.GasolineFuelTotal),
            DieselVehicle = Fmt(row.DieselVehicle),
            DieselMachine = Fmt(row.DieselMachine),
            DieselFuelTotal = Fmt(row.DieselFuelTotal),
            FuelTotal = Fmt(row.FuelTotal),
            CumGasoline = Fmt(row.CumGasoline),
            CumDiesel = Fmt(row.CumDiesel),
            CumTotal = Fmt(row.CumTotal),
            RemainGasoline = Fmt(row.RemainGasoline),
            RemainDiesel = Fmt(row.RemainDiesel),
            RemainTotal = Fmt(row.RemainTotal),
            ExcessGasoline = Fmt(row.ExcessGasoline),
            ExcessDiesel = Fmt(row.ExcessDiesel),
            ExcessTotal = Fmt(row.ExcessTotal)
        };
        vm._suppress = true;
        vm.GasolineLimitText = Fmt(row.GasolineLimit);
        vm.DieselLimitText = Fmt(row.DieselLimit);
        vm._suppress = false;
        _ = year;
        return vm;
    }

    private static string Fmt(decimal? v)
    {
        if (v is null or 0)
            return "";
        var val = QuantityMath.RoundQty(v.Value);
        return val == QuantityMath.Whole(val) ? Numbers.Qty(val) : Numbers.Decimal(val);
    }
}
