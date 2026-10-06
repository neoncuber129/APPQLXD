using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using APPQLXD.Core;
using APPQLXD.Core.Calculations;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace APPQLXD.ViewModels;

public interface IRefreshable
{
    void Refresh();
}

public interface IDocumentEditor
{
    void EditDocument(Guid id);
}

public enum NumericKind
{
    None = 0,
    /// <summary>Số lượng nguyên — 1.234</summary>
    Qty = 1,
    /// <summary>Tiền / đơn giá nguyên — 1.234</summary>
    Money = 2,
    /// <summary>Số thập phân tối đa 4 chữ số — 1.234,56</summary>
    Decimal = 3,
    /// <summary>Hệ số (VCF, định mức…) tối đa 6 chữ số — 0,987654</summary>
    Factor = 4
}

public static class Numbers
{
    public static readonly CultureInfo Vietnam = CultureInfo.GetCultureInfo("vi-VN");

    public static bool Try(string? text, out decimal value)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            value = 0;
            return false;
        }

        if (text.Contains(',') && decimal.TryParse(text, NumberStyles.Number, Vietnam, out value))
            return true;
        if (LooksLikeGroupedThousands(text)
            && decimal.TryParse(text, NumberStyles.Number, Vietnam, out value))
            return true;
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            return true;
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
            return true;
        return decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool LooksLikeGroupedThousands(string text)
    {
        // 1.234 or 12.345.678 — groups of 3 digits separated by '.'
        var parts = text.Split('.');
        if (parts.Length < 2)
            return false;
        if (parts[0].Length is 0 or > 3 || !parts[0].All(char.IsDigit))
            return false;
        for (var i = 1; i < parts.Length; i++)
        {
            if (parts[i].Length != 3 || !parts[i].All(char.IsDigit))
                return false;
        }

        return true;
    }

    public static string Qty(decimal value) =>
        QuantityMath.Whole(value).ToString("N0", Vietnam);

    public static bool EndsWithSeparator(string? text)
    {
        var trimmed = text?.TrimEnd() ?? "";
        return trimmed.EndsWith(',') || trimmed.EndsWith('.');
    }

    public static string Decimal(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero).ToString("0.####", Vietnam);

    public static string Factor(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero).ToString("0.######", Vietnam);

    public static string Money(decimal value) =>
        decimal.Round(value, 0, MidpointRounding.AwayFromZero).ToString("N0", Vietnam);

    /// <summary>Số nguyên theo quy tắc nhập Việt Nam (cho phép 1.234).</summary>
    public static bool TryInt(string? text, out int value)
    {
        value = 0;
        if (!Try(text, out var parsed))
            return false;
        var whole = decimal.Truncate(parsed);
        if (whole != parsed || whole < int.MinValue || whole > int.MaxValue)
            return false;
        value = (int)whole;
        return true;
    }

    /// <summary>
    /// Định dạng tạm khi đang gõ: hàng nghìn bằng dấu chấm, thập phân bằng dấu phẩy.
    /// Giữ dấu phẩy/chấm đang dở để người dùng tiếp tục nhập.
    /// </summary>
    public static string FormatLive(string? text, NumericKind kind)
    {
        if (kind == NumericKind.None)
            return text ?? "";
        var raw = text ?? "";
        if (raw.Length == 0)
            return "";

        var allowFraction = kind is NumericKind.Decimal or NumericKind.Factor;
        var maxFrac = kind == NumericKind.Factor ? 6 : 4;

        // Chuẩn hoá: một dấu '.' đơn ở cuối (không phải nhóm nghìn) → ',' khi cho phép thập phân.
        if (allowFraction && !raw.Contains(',') && raw.EndsWith('.') && !LooksLikeGroupedThousands(raw.TrimEnd('.')))
            raw = raw[..^1] + ",";

        var negative = raw.StartsWith('-');
        var body = negative ? raw[1..] : raw;
        var trailingSep = body.EndsWith(',') || (!allowFraction && body.EndsWith('.'));
        if (trailingSep)
            body = body[..^1];

        string intDigits;
        string fracDigits = "";
        var hasComma = allowFraction && body.Contains(',');
        if (hasComma)
        {
            var idx = body.IndexOf(',');
            intDigits = DigitsOnly(body[..idx]);
            fracDigits = DigitsOnly(body[(idx + 1)..]);
            if (fracDigits.Length > maxFrac)
                fracDigits = fracDigits[..maxFrac];
        }
        else
            intDigits = DigitsOnly(body);

        if (intDigits.Length == 0 && fracDigits.Length == 0 && !hasComma && !trailingSep)
            return negative ? "-" : "";

        if (intDigits.Length == 0)
            intDigits = "0";

        // Bỏ số 0 đứng đầu nhưng giữ một số 0.
        intDigits = intDigits.TrimStart('0');
        if (intDigits.Length == 0)
            intDigits = "0";

        var grouped = GroupThousands(intDigits);
        var result = grouped;
        if (hasComma || (trailingSep && allowFraction))
            result += "," + fracDigits;
        else if (trailingSep && !allowFraction)
            result += ","; // Qty/Money: đang gõ dở — chờ chữ số tiếp

        if (negative && result != "0" && result != "0,")
            result = "-" + result;
        return result;
    }

    private static string DigitsOnly(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsDigit(ch))
                sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string GroupThousands(string digits)
    {
        if (digits.Length <= 3)
            return digits;
        var sb = new System.Text.StringBuilder(digits.Length + digits.Length / 3);
        var first = digits.Length % 3;
        if (first == 0)
            first = 3;
        sb.Append(digits, 0, first);
        for (var i = first; i < digits.Length; i += 3)
        {
            sb.Append('.');
            sb.Append(digits, i, 3);
        }

        return sb.ToString();
    }
}

public sealed class OptionRow
{
    public OptionRow(Guid id, string label)
    {
        Id = id;
        Label = label;
    }

    public Guid Id { get; }
    public string Label { get; }
    public override string ToString() => Label;
}

public sealed record KindFilter(DocumentKind? Kind, string Label)
{
    public override string ToString() => Label;
}

public partial class FieldEditorVm : ObservableObject
{
    public Guid FieldId { get; init; }
    public string Name { get; init; } = "";
    public bool IsRequired { get; init; }
    public string DataTypeName { get; init; } = "";
    public IReadOnlyList<string> Options { get; init; } = [];
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private bool _hasError;
    public string Label => IsRequired ? $"{Name} *" : Name;

    partial void OnValueChanged(string value) => HasError = false;
}

public partial class FieldFormVm : ObservableObject
{
    private readonly FuelSystem _system;
    private readonly DocumentFamily _family;
    private bool _suppress;

    public FieldFormVm(FuelSystem system, DocumentFamily family)
    {
        _system = system;
        _family = family;
    }

    public ObservableCollection<FieldEditorVm> Rows { get; } = [];
    public ObservableCollection<OptionRow> SampleSets { get; } = [];
    [ObservableProperty] private OptionRow? _selectedSample;
    [ObservableProperty] private bool _addToSample;
    [ObservableProperty] private bool _pickSample = true;
    public bool HasRows => Rows.Count > 0;
    public bool ExtraFieldsEnabled { get; private set; }
    public bool PanelVisible => ExtraFieldsEnabled && HasRows;

    public void SetExtraFieldsEnabled(bool enabled)
    {
        ExtraFieldsEnabled = enabled;
        OnPropertyChanged(nameof(PanelVisible));
    }

    public void ReloadSets(Guid? preferred = null)
    {
        var keep = preferred ?? SelectedSample?.Id;
        SampleSets.Clear();
        foreach (var set in _system.GetSampleSets(_family))
            SampleSets.Add(new OptionRow(set.Id, set.Name));
        _suppress = true;
        SelectedSample = SampleSets.FirstOrDefault(x => x.Id == keep);
        _suppress = false;
    }

    public void ApplySample(Guid? sampleSetId)
    {
        PickSample = true;
        _suppress = true;
        SelectedSample = sampleSetId is null ? null : SampleSets.FirstOrDefault(x => x.Id == sampleSetId);
        _suppress = false;
        Fill(sampleSetId);
    }

    public void ShowValues(Guid? sampleSetId, IReadOnlyDictionary<string, string> values)
    {
        PickSample = false;
        if (sampleSetId is Guid id)
            ReloadSets(id);
        _suppress = true;
        SelectedSample = sampleSetId is null ? null : SampleSets.FirstOrDefault(x => x.Id == sampleSetId);
        _suppress = false;
        Rows.Clear();
        if (values.Count > 0)
        {
            foreach (var row in _system.BuildFieldForm(_family, sampleSetId))
            {
                if (!values.TryGetValue(row.Name, out var value) || string.IsNullOrWhiteSpace(value))
                    continue;
                Rows.Add(new FieldEditorVm
                {
                    FieldId = row.FieldId,
                    Name = row.Name,
                    IsRequired = row.IsRequired,
                    DataTypeName = row.DataTypeName,
                    Options = row.Options,
                    Value = value
                });
            }
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(PanelVisible));
    }

    partial void OnSelectedSampleChanged(OptionRow? value)
    {
        if (_suppress)
            return;
        Fill(value?.Id);
    }

    public void LoadSnapshot(IReadOnlyList<FieldSnapshotRow> fields)
    {
        foreach (var editor in Rows)
        {
            var snap = fields.FirstOrDefault(x => x.Name == editor.Name);
            if (snap is not null)
                editor.Value = snap.Value;
        }
    }

    public List<FieldInput> Inputs() => Rows.Select(x => new FieldInput { FieldId = x.FieldId, Value = x.Value ?? "" }).ToList();

    public Guid? SampleToUpdate => AddToSample ? SelectedSample?.Id : null;

    public void ClearErrors()
    {
        foreach (var row in Rows)
            row.HasError = false;
    }

    /// <summary>Đánh dấu trường bắt buộc còn trống. Trả về tên các trường thiếu.</summary>
    public IReadOnlyList<string> MarkMissingRequired(Func<FieldEditorVm, bool>? treatAsFilled = null)
    {
        var missing = new List<string>();
        foreach (var row in Rows)
        {
            if (!row.IsRequired)
                continue;
            if (!string.IsNullOrWhiteSpace(row.Value))
                continue;
            if (treatAsFilled?.Invoke(row) == true)
                continue;
            row.HasError = true;
            missing.Add(row.Name);
        }

        return missing;
    }

    public void MarkErrorByName(string name)
    {
        foreach (var row in Rows.Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            row.HasError = true;
    }

    private void Fill(Guid? sampleSetId)
    {
        Rows.Clear();
        foreach (var row in _system.BuildFieldForm(_family, sampleSetId))
        {
            Rows.Add(new FieldEditorVm
            {
                FieldId = row.FieldId,
                Name = row.Name,
                IsRequired = row.IsRequired,
                DataTypeName = row.DataTypeName,
                Options = row.Options,
                Value = row.Value
            });
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(PanelVisible));
    }
}

public abstract partial class PageVm : ObservableObject, IRefreshable
{
    protected PageVm(FuelSystem system) => System = system;
    protected FuelSystem System { get; }
    [ObservableProperty] private string _banner = "";
    [ObservableProperty] private bool _bannerIsError;
    public Guid? LastSavedId { get; private set; }
    protected void Ok(string text) { Banner = text; BannerIsError = false; }
    protected void Fail(string text) { Banner = text; BannerIsError = true; LastSavedId = null; }
    protected void Show(FuelResult result)
    {
        LastSavedId = result.Ok ? result.Id : null;
        if (result.Ok) Ok(result.Message);
        else Fail(result.Message);
    }
    public abstract void Refresh();

    public virtual Task RefreshAsync(IProgress<DemoProgress>? progress = null)
    {
        progress?.Report(new DemoProgress("Đang tải", 0, 1, 0, 1));
        Refresh();
        progress?.Report(new DemoProgress("Đang tải", 1, 1, 1, 1));
        return Task.CompletedTask;
    }
}

public sealed class ShellTab
{
    public required string Title { get; init; }
    public required PageVm Page { get; init; }
}

public partial class MainVm : ObservableObject
{
    private readonly FuelSystem _system;

    public MainVm(FuelSystem system)
    {
        _system = system;
        DatabasePath = system.DatabasePath;
        var history = new HistoryVm(system, OpenEditor);
        var ops = new OpsHostVm(system, history) { RunBusy = Track };
        Tabs = new ObservableCollection<ShellTab>
        {
            new() { Title = "Tổng quan", Page = new DashboardVm(system) },
            new() { Title = "Danh mục", Page = new CatalogHostVm(system) },
            new() { Title = "Phiếu nhập", Page = new VoucherDeskVm(system, exportDesk: false) },
            new() { Title = "Phiếu xuất", Page = new VoucherDeskVm(system, exportDesk: true) },
            new() { Title = "Tiêu thụ quý", Page = new ConsumptionVm(system) },
            new() { Title = "Kho", Page = ops },
            new() { Title = "Tồn đầu kỳ", Page = new OpeningVm(system) }
        };
        SelectedTab = Tabs[0];
        RefreshScopeUi();
    }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyText = "";
    [ObservableProperty] private int _busyDone;
    [ObservableProperty] private int _busyTotal;
    [ObservableProperty] private string _warehouseScopeText = "Kho XD";
    [ObservableProperty] private bool _isXdScope = true;
    [ObservableProperty] private bool _isPtktScope;

    public string DatabasePath { get; }
    public ObservableCollection<ShellTab> Tabs { get; }
    [ObservableProperty] private ShellTab? _selectedTab;

    private void RefreshScopeUi()
    {
        var scope = _system.GetWarehouseScope();
        IsXdScope = scope == WarehouseScope.Xd;
        IsPtktScope = scope == WarehouseScope.Ptkt;
        WarehouseScopeText = Labels.WarehouseScopeLabel(scope);
    }

    [RelayCommand]
    private async Task UseXdScope()
    {
        if (_system.GetWarehouseScope() == WarehouseScope.Xd)
            return;
        _system.SetWarehouseScope(WarehouseScope.Xd);
        RefreshScopeUi();
        await RefreshTabs();
    }

    [RelayCommand]
    private async Task UsePtktScope()
    {
        if (_system.GetWarehouseScope() == WarehouseScope.Ptkt)
            return;
        _system.SetWarehouseScope(WarehouseScope.Ptkt);
        RefreshScopeUi();
        await RefreshTabs();
    }

    private int _loadVersion;

    partial void OnSelectedTabChanged(ShellTab? value)
    {
        if (value is null || _refreshingAll || _openingEditor)
            return;
        _ = Track("Đang mở " + value.Title, value.Page.RefreshAsync);
    }

    public async Task Track(string title, Func<IProgress<DemoProgress>, Task> work)
    {
        var version = ++_loadVersion;
        IsBusy = true;
        BusyText = title;
        BusyDone = 0;
        BusyTotal = 0;
        var progress = new Progress<DemoProgress>(step =>
        {
            if (version != _loadVersion)
                return;
            if (!string.IsNullOrWhiteSpace(step.Phase))
                BusyText = step.Phase;
            BusyDone = step.Done;
            BusyTotal = step.Total;
        });
        try
        {
            await Task.Yield();
            await work(progress);
        }
        catch (Exception ex)
        {
            ErrorLog.Record(ex, title);
        }
        finally
        {
            if (version == _loadVersion)
                IsBusy = false;
        }
    }

    private bool _refreshingAll;
    private bool _openingEditor;

    private async Task RefreshTabs()
    {
        _refreshingAll = true;
        try
        {
            foreach (var tab in Tabs)
                await Track(tab.Title, tab.Page.RefreshAsync);
        }
        finally
        {
            _refreshingAll = false;
        }
    }

    [RelayCommand]
    private void ShowPlaceholdersPhieuNhap() =>
        Services.ExportUi.ShowPlaceholders(Core.Export.ExportDocumentKind.PhieuNhap, _system.GetFields(Core.Domain.DocumentFamily.Import));

    [RelayCommand]
    private void ShowPlaceholdersPhieuXuat() =>
        Services.ExportUi.ShowPlaceholders(Core.Export.ExportDocumentKind.PhieuXuat, _system.GetFields(Core.Domain.DocumentFamily.Export));

    [RelayCommand]
    private void ShowPlaceholdersSoMayXe() =>
        Services.ExportUi.ShowPlaceholders(Core.Export.ExportDocumentKind.SoTtMayXe);

    [RelayCommand]
    private void ShowPlaceholdersSoTau() =>
        Services.ExportUi.ShowPlaceholders(Core.Export.ExportDocumentKind.SoTtTau);

    [RelayCommand]
    private void ShowPlaceholdersNxt() =>
        Services.ExportUi.ShowPlaceholders(Core.Export.ExportDocumentKind.SoNxt);

    [RelayCommand]
    private void ShowPlaceholdersNxtTong() =>
        Services.ExportUi.ShowPlaceholders(Core.Export.ExportDocumentKind.SoNxtTong);

    [RelayCommand]
    private void OpenTemplateFolder() => Services.ExportUi.OpenTemplateFolder();

    [RelayCommand]
    private void Backup() => SaveBackup("Sao lưu toàn bộ dữ liệu", $"appqlxd-{DateTime.Now:yyyyMMdd-HHmm}.db", _system.Backup);

    [RelayCommand]
    private void BackupCatalog() => SaveBackup("Sao lưu danh mục", $"appqlxd-danhmuc-{DateTime.Now:yyyyMMdd-HHmm}.db", _system.BackupCatalog);

    private static void SaveBackup(string title, string fileName, Func<string, FuelResult> save)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = "Cơ sở dữ liệu (*.db)|*.db",
            FileName = fileName,
            AddExtension = true,
            DefaultExt = ".db"
        };
        if (dialog.ShowDialog() != true)
            return;
        var result = save(dialog.FileName);
        MessageBox.Show(result.Message, result.Ok ? "Sao lưu" : "Lỗi", MessageBoxButton.OK, result.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    [RelayCommand]
    private async Task Restore()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Khôi phục dữ liệu",
            Filter = "Cơ sở dữ liệu (*.db)|*.db|Tất cả tệp (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
            return;
        if (MessageBox.Show(
                "Khôi phục sẽ thay toàn bộ dữ liệu hiện tại. Bản đang dùng được giữ lại thành tệp .before-restore cạnh cơ sở dữ liệu. Tiếp tục?",
                "Khôi phục",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var result = _system.Restore(dialog.FileName);
        if (!result.Ok)
        {
            MessageBox.Show(result.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await ReloadAfterDataChangeAsync(result.Message, "Khôi phục");
    }

    [RelayCommand]
    private async Task RestoreCatalog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Khôi phục danh mục",
            Filter = "Cơ sở dữ liệu (*.db)|*.db|Tất cả tệp (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
            return;
        if (MessageBox.Show(
                "Khôi phục danh mục sẽ thay mặt hàng, kho, đối tượng và phiếu mẫu. Chứng từ và tồn được giữ khi kho và mặt hàng vẫn còn trong tệp sao lưu. Tiếp tục?",
                "Khôi phục danh mục",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        var result = _system.RestoreCatalog(dialog.FileName);
        if (!result.Ok)
        {
            MessageBox.Show(result.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await ReloadAfterDataChangeAsync(result.Message, "Khôi phục danh mục");
    }

    [RelayCommand]
    private async Task LoadSampleCatalog()
    {
        if (MessageBox.Show(
                "Tạo/nạp danh mục mẫu từ Excel (mặt hàng, kho, đối tượng xe/máy/tàu, phiếu mẫu và hạn mức). Tiếp tục?",
                "Tạo danh mục mẫu",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var result = _system.LoadSampleCatalog();
        if (!result.Ok)
        {
            MessageBox.Show(result.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await ReloadAfterDataChangeAsync(result.Message, "Tạo danh mục mẫu");
    }

    [RelayCommand]
    private async Task ClearActivity()
    {
        if (MessageBox.Show(
                "Xoá toàn bộ chứng từ và tồn kho. Danh mục mặt hàng, kho, đối tượng và phiếu mẫu được giữ. Tiếp tục?",
                "Xoá chứng từ",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        var result = _system.ClearActivity();
        if (!result.Ok)
        {
            MessageBox.Show(result.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await ReloadAfterDataChangeAsync(result.Message, "Xoá chứng từ");
    }

    [RelayCommand]
    private async Task ClearAll()
    {
        if (MessageBox.Show(
                "Xoá toàn bộ chứng từ, tồn kho và danh mục. Không nạp lại danh mục từ Excel. Tiếp tục?",
                "Xoá toàn bộ",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        var result = _system.ClearAll();
        if (!result.Ok)
        {
            MessageBox.Show(result.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await ReloadAfterDataChangeAsync(result.Message, "Xoá toàn bộ");
    }

    /// <summary>Nạp lại phạm vi kho + mọi tab sau khôi phục/xoá; gợi ý khởi động lại nếu nạp lỗi.</summary>
    private async Task ReloadAfterDataChangeAsync(string message, string title)
    {
        try
        {
            RefreshScopeUi();
            await RefreshTabs();
            var askRestart = MessageBox.Show(
                message + "\n\nĐã nạp lại dữ liệu trên các tab.\nKhởi động lại ứng dụng nếu giao diện còn lệch?",
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (askRestart == MessageBoxResult.Yes)
                RestartApp();
        }
        catch (Exception ex)
        {
            ErrorLog.Record(ex, title);
            var askRestart = MessageBox.Show(
                message + "\n\nKhông nạp lại được giao diện: " + ex.Message + "\n\nKhởi động lại ứng dụng ngay?",
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (askRestart == MessageBoxResult.Yes)
                RestartApp();
        }
    }

    private static void RestartApp()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            MessageBox.Show("Không tìm thấy tệp ứng dụng để khởi động lại. Hãy đóng và mở lại app thủ công.", "Khởi động lại", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true
            });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Không khởi động lại được: " + ex.Message + "\nHãy đóng và mở lại app thủ công.", "Khởi động lại", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void OpenEditor(DocumentKind kind, Guid id)
    {
        var tab = kind switch
        {
            DocumentKind.Import => Tabs.FirstOrDefault(x => x.Page is VoucherDeskVm desk && !desk.ExportDesk),
            DocumentKind.Issue or DocumentKind.Transfer or DocumentKind.LotConvert
                => Tabs.FirstOrDefault(x => x.Page is VoucherDeskVm desk && desk.ExportDesk),
            DocumentKind.Consumption => _system.GetDocument(id)?.Distance is not null
                ? Tabs.FirstOrDefault(x => x.Page is VoucherDeskVm desk && desk.ExportDesk)
                : Tabs.FirstOrDefault(x => x.Page is ConsumptionVm),
            DocumentKind.Auxiliary => Tabs.FirstOrDefault(x => x.Page is ConsumptionVm),
            DocumentKind.Opening => Tabs.FirstOrDefault(x => x.Page is OpeningVm),
            _ => null
        };
        if (tab is null)
            return;
        _openingEditor = true;
        if (SelectedTab != tab)
            SelectedTab = tab;
        _openingEditor = false;
        _ = Track(tab.Title, async progress =>
        {
            await tab.Page.RefreshAsync(progress);
            if (tab.Page is IDocumentEditor editor)
                editor.EditDocument(id);
        });
    }
}
