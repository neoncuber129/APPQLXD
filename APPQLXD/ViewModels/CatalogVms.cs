using System.Collections.ObjectModel;
using System.Windows;
using APPQLXD.Core;
using APPQLXD.Core.Calculations;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using APPQLXD.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace APPQLXD.ViewModels;

public partial class DashboardVm : PageVm
{
    public DashboardVm(Core.FuelSystem system) : base(system) { }
    public ObservableCollection<DocumentRow> Recent { get; } = [];
    [ObservableProperty] private int _itemCount;
    [ObservableProperty] private int _warehouseCount;
    [ObservableProperty] private int _consumerCount;
    [ObservableProperty] private decimal _totalStock;

    public override void Refresh()
    {
        var summary = System.GetDashboard();
        ItemCount = summary.ItemCount;
        WarehouseCount = summary.WarehouseCount;
        ConsumerCount = summary.ConsumerCount;
        TotalStock = summary.TotalStock;
        Recent.Clear();
        foreach (var row in summary.RecentDocuments)
            Recent.Add(row);
    }

    [RelayCommand]
    private void LoadDemo()
    {
        if (MessageBox.Show(
                "Tạo dữ liệu thử số lượng lớn, đủ xăng, dầu, nhớt và mỡ, từ danh mục và phiếu mẫu sẵn có. Số lượng là số nguyên. Tiếp tục?",
                "Dữ liệu thử",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        var window = new DemoProgressWindow();
        if (Application.Current.MainWindow is { IsLoaded: true } owner)
            window.Owner = owner;
        window.Start(progress => System.LoadDemoActivity(progress));
        if (window.ShowDialog() == true && window.Result is { } result)
        {
            Show(result);
            if (result.Ok)
                Refresh();
        }
    }
}

public partial class GroupsVm : PageVm
{
    public GroupsVm(Core.FuelSystem system) : base(system) { }
    public ObservableCollection<GroupRow> Rows { get; } = [];
    [ObservableProperty] private GroupRow? _selected;
    [ObservableProperty] private string _name = "";
    private Guid? _editingId;
    private bool _suppress;

    partial void OnSelectedChanged(GroupRow? value)
    {
        if (_suppress || value is null) return;
        _editingId = value.Id;
        Name = value.Name;
    }

    public override void Refresh()
    {
        var keep = _editingId;
        _suppress = true;
        Rows.Clear();
        foreach (var row in System.GetGroups()) Rows.Add(row);
        Selected = Rows.FirstOrDefault(x => x.Id == keep);
        _suppress = false;
    }

    [RelayCommand]
    private void New() { _editingId = null; Selected = null; Name = ""; Banner = ""; }

    [RelayCommand]
    private void Save()
    {
        var result = System.SaveGroup(_editingId, Name);
        Show(result);
        if (!result.Ok || result.Id is not Guid id)
            return;
        _editingId = id;
        Refresh();
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        if (!Confirm("Xóa nhóm này? Các mặt hàng chưa phát sinh chứng từ trong nhóm cũng bị xóa.")) return;
        Show(System.DeleteGroup(Selected.Id));
        if (BannerIsError) return;
        New();
        Refresh();
    }

    internal static bool Confirm(string text)
    {
        var owner = Application.Current?.MainWindow;
        var result = owner is null
            ? MessageBox.Show(text, "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            : MessageBox.Show(owner, text, "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }
}

public partial class UnitsVm : PageVm
{
    public UnitsVm(Core.FuelSystem system) : base(system) { }
    public ObservableCollection<UnitRow> Rows { get; } = [];
    [ObservableProperty] private UnitRow? _selected;
    [ObservableProperty] private string _name = "";
    private Guid? _editingId;
    private bool _suppress;

    partial void OnSelectedChanged(UnitRow? value)
    {
        if (_suppress || value is null) return;
        _editingId = value.Id;
        Name = value.Name;
    }

    public override void Refresh()
    {
        var keep = _editingId;
        _suppress = true;
        Rows.Clear();
        foreach (var row in System.GetUnits()) Rows.Add(row);
        Selected = Rows.FirstOrDefault(x => x.Id == keep);
        _suppress = false;
    }

    [RelayCommand] private void New() { _editingId = null; Selected = null; Name = ""; Banner = ""; }

    [RelayCommand]
    private void Save()
    {
        var result = System.SaveUnit(_editingId, Name);
        Show(result);
        if (!result.Ok || result.Id is not Guid id)
            return;
        _editingId = id;
        Refresh();
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        if (!GroupsVm.Confirm("Xóa đơn vị tính này?")) return;
        Show(System.DeleteUnit(Selected.Id));
        if (BannerIsError) return;
        New();
        Refresh();
    }
}

public partial class LotTypesVm : PageVm
{
    public LotTypesVm(Core.FuelSystem system) : base(system) { }
    public ObservableCollection<LotTypeRow> Rows { get; } = [];
    [ObservableProperty] private LotTypeRow? _selected;
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _name = "";
    private Guid? _editingId;
    private bool _suppress;

    partial void OnSelectedChanged(LotTypeRow? value)
    {
        if (_suppress || value is null) return;
        _editingId = value.Id;
        Code = value.Code;
        Name = value.Name;
    }

    public override void Refresh()
    {
        var keep = _editingId;
        _suppress = true;
        Rows.Clear();
        foreach (var row in System.GetLotTypes(activeOnly: false)) Rows.Add(row);
        Selected = Rows.FirstOrDefault(x => x.Id == keep);
        _suppress = false;
    }

    [RelayCommand] private void New() { _editingId = null; Selected = null; Code = ""; Name = ""; Banner = ""; }

    [RelayCommand]
    private void Save()
    {
        var result = System.SaveLotType(_editingId, Code, Name);
        Show(result);
        if (!result.Ok || result.Id is not Guid id)
            return;
        _editingId = id;
        Refresh();
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        if (!GroupsVm.Confirm("Xóa loại lô này?")) return;
        Show(System.DeleteLotType(Selected.Id));
        if (BannerIsError) return;
        New();
        Refresh();
    }
}

public partial class ItemsVm : PageVm
{
    public ItemsVm(Core.FuelSystem system) : base(system)
    {
        GroupEditor = new GroupsVm(system);
        UnitEditor = new UnitsVm(system);
    }

    public GroupsVm GroupEditor { get; }
    public UnitsVm UnitEditor { get; }
    public ObservableCollection<ItemRow> Rows { get; } = [];
    public ObservableCollection<OptionRow> Groups { get; } = [];
    public ObservableCollection<OptionRow> Units { get; } = [];
    [ObservableProperty] private ItemRow? _selected;
    [ObservableProperty] private OptionRow? _group;
    [ObservableProperty] private OptionRow? _unit;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _density = "";
    [ObservableProperty] private string _quality = "";
    [ObservableProperty] private string _temperature = "";
    [ObservableProperty] private string _measurement = "";
    [ObservableProperty] private string _vcf = "";
    [ObservableProperty] private string _rule = "";
    private Guid? _editingId;
    private bool _suppress;

    partial void OnSelectedChanged(ItemRow? value)
    {
        if (_suppress || value is null) return;
        _editingId = value.Id;
        _suppress = true;
        Group = Groups.FirstOrDefault(x => x.Id == value.GroupId);
        Unit = Units.FirstOrDefault(x => x.Id == value.UnitId);
        _suppress = false;
        Name = value.Name;
        Code = value.Code;
        Density = value.Density is decimal density ? Numbers.Factor(density) : "";
        Quality = value.QualityInfo;
        Temperature = value.Temperature is decimal temperature ? Numbers.Decimal(temperature) : "";
        Measurement = value.MeasurementNote;
        Vcf = Numbers.Factor(value.Vcf);
        Rule = value.ConversionRule;
    }

    public override void Refresh()
    {
        var keep = _editingId;
        GroupEditor.Refresh();
        UnitEditor.Refresh();
        ReloadLookups();
        _suppress = true;
        Rows.Clear();
        foreach (var row in System.GetItems()) Rows.Add(row);
        Selected = Rows.FirstOrDefault(x => x.Id == keep);
        _suppress = false;
    }

    private void ReloadLookups()
    {
        var groupId = Group?.Id;
        var unitId = Unit?.Id;
        Groups.Clear();
        foreach (var row in System.GetGroups()) Groups.Add(new OptionRow(row.Id, row.Name));
        Units.Clear();
        foreach (var row in System.GetUnits()) Units.Add(new OptionRow(row.Id, row.Name));
        Group = Groups.FirstOrDefault(x => x.Id == groupId);
        Unit = Units.FirstOrDefault(x => x.Id == unitId);
    }

    [RelayCommand]
    private void SaveGroup()
    {
        GroupEditor.SaveCommand.Execute(null);
        ReloadLookups();
    }

    [RelayCommand]
    private void DeleteGroup()
    {
        GroupEditor.DeleteCommand.Execute(null);
        ReloadLookups();
    }

    [RelayCommand]
    private void NewGroup() => GroupEditor.NewCommand.Execute(null);

    [RelayCommand]
    private void SaveUnit()
    {
        UnitEditor.SaveCommand.Execute(null);
        ReloadLookups();
    }

    [RelayCommand]
    private void DeleteUnit()
    {
        UnitEditor.DeleteCommand.Execute(null);
        ReloadLookups();
    }

    [RelayCommand]
    private void NewUnit() => UnitEditor.NewCommand.Execute(null);

    [RelayCommand]
    private void New()
    {
        _editingId = null; Selected = null; Name = ""; Code = ""; Density = ""; Quality = ""; Temperature = ""; Measurement = ""; Vcf = "1"; Unit = null; Rule = Core.Labels.DefaultConversionRule; Banner = "";
    }

    [RelayCommand]
    private void Save()
    {
        if (Group is null || Unit is null) { Fail("Chọn nhóm và đơn vị tính."); return; }
        if (!Numbers.Try(Vcf, out var vcf)) { Fail("VCF không hợp lệ."); return; }
        decimal? density = null;
        if (!string.IsNullOrWhiteSpace(Density))
        {
            if (!Numbers.Try(Density, out var parsedDensity)) { Fail("Tỉ trọng không hợp lệ."); return; }
            density = parsedDensity;
        }
        decimal? temperature = null;
        if (!string.IsNullOrWhiteSpace(Temperature))
        {
            if (!Numbers.Try(Temperature, out var temp)) { Fail("Nhiệt độ không hợp lệ."); return; }
            temperature = temp;
        }

        var result = System.SaveItem(new ItemEdit
        {
            Id = _editingId,
            GroupId = Group.Id,
            UnitId = Unit.Id,
            Name = Name,
            Code = Code,
            Density = density,
            QualityInfo = Quality,
            Temperature = temperature,
            MeasurementNote = Measurement,
            Vcf = vcf,
            ConversionRule = Rule
        });
        Show(result);
        if (!result.Ok || result.Id is not Guid id)
            return;
        _editingId = id;
        Refresh();
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        if (!GroupsVm.Confirm("Xóa mặt hàng này?")) return;
        Show(System.DeleteItem(Selected.Id));
        if (BannerIsError) return;
        New();
        Refresh();
    }
}

public partial class WarehousesVm : PageVm
{
    public WarehousesVm(Core.FuelSystem system) : base(system) { }
    public ObservableCollection<WarehouseRow> Rows { get; } = [];
    public ObservableCollection<OptionRow> ExportSamples { get; } = [];
    public ObservableCollection<string> Types { get; } = [];
    [ObservableProperty] private WarehouseRow? _selected;
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _type = "Kho XD";
    [ObservableProperty] private OptionRow? _exportSample;
    private Guid? _editingId;
    private bool _suppress;

    partial void OnSelectedChanged(WarehouseRow? value)
    {
        if (_suppress || value is null) return;
        _editingId = value.Id;
        Code = value.Code;
        Name = value.Name;
        Type = value.TypeName is "Kho XD" or "Kho PTKT-VTXD" ? value.TypeName : Type;
        ExportSample = ExportSamples.FirstOrDefault(x => x.Id == value.DefaultExportSampleSetId);
    }

    public override void Refresh()
    {
        var keep = _editingId;
        Types.Clear();
        Types.Add("Kho XD");
        Types.Add("Kho PTKT-VTXD");
        if (!Types.Contains(Type))
            Type = "Kho XD";
        ExportSamples.Clear();
        foreach (var set in System.GetSampleSets(DocumentFamily.Export)) ExportSamples.Add(new OptionRow(set.Id, set.Name));
        _suppress = true;
        Rows.Clear();
        foreach (var row in System.GetCatalogWarehouses().Where(x => !x.IsConsumerLocation))
            Rows.Add(row);
        Selected = Rows.FirstOrDefault(x => x.Id == keep);
        _suppress = false;
    }

    [RelayCommand]
    private void New()
    {
        _editingId = null;
        Selected = null;
        Code = "";
        Name = "";
        if (!Types.Contains(Type))
            Type = "Kho XD";
        ExportSample = null;
        Banner = "";
    }

    [RelayCommand]
    private void Save()
    {
        var result = System.SaveWarehouse(new WarehouseEdit
        {
            Id = _editingId,
            Code = Code,
            Name = Name,
            Type = Type == "Kho PTKT-VTXD" ? WarehouseType.Ptkt : WarehouseType.Main,
            DefaultImportSampleSetId = null,
            DefaultExportSampleSetId = ExportSample?.Id
                ?? (_editingId is Guid existing
                    ? System.GetCatalogWarehouses().FirstOrDefault(x => x.Id == existing)?.DefaultExportSampleSetId
                    : null)
        });
        Show(result);
        if (!result.Ok || result.Id is not Guid id)
            return;
        _editingId = id;
        Refresh();
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        if (!GroupsVm.Confirm("Xóa kho này?")) return;
        Show(System.DeleteWarehouse(Selected.Id));
        if (BannerIsError) return;
        New();
        Refresh();
    }
}

public partial class NormFactorRowVm : ObservableObject
{
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private OptionRow? _group;
    [ObservableProperty] private string _value = "1";
    public Guid? Id { get; set; }
}

public partial class ConsumersVm : PageVm
{
    public ConsumersVm(Core.FuelSystem system) : base(system) { }
    public ObservableCollection<ConsumerRow> Rows { get; } = [];
    public ObservableCollection<OptionRow> Groups { get; } = [];
    public ObservableCollection<OptionRow> Items { get; } = [];
    public ObservableCollection<OptionRow> ExportSamples { get; } = [];
    public ObservableCollection<NormFactorRowVm> NormFactors { get; } = [];
    public ObservableCollection<string> Types { get; } = ["Máy", "Phương tiện", "Tàu"];
    [ObservableProperty] private ConsumerRow? _selected;
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _type = "Máy";
    [ObservableProperty] private OptionRow? _group;
    [ObservableProperty] private OptionRow? _item;
    [ObservableProperty] private string _norm = "";
    [ObservableProperty] private OptionRow? _exportSample;
    [ObservableProperty] private bool _isVehicle;
    [ObservableProperty] private bool _isShip;
    [ObservableProperty] private bool _showRollTransfers;
    [ObservableProperty] private bool _rollTransfersIntoQuarter = true;
    [ObservableProperty] private string _effectiveNormText = "";
    [ObservableProperty] private string _shipType = "";
    [ObservableProperty] private string _mainMachineCount = "1";
    [ObservableProperty] private string _auxMachineCount = "1";
    private Guid? _editingId;
    private bool _suppress;

    partial void OnTypeChanged(string value)
    {
        IsVehicle = value == "Phương tiện";
        IsShip = value == "Tàu";
        ShowRollTransfers = value is "Máy" or "Phương tiện";
        if (IsShip)
            EnsureShipNormSlots();
        RefreshEffectiveNorm();
    }

    partial void OnGroupChanged(OptionRow? value)
    {
        if (_suppress || !IsShip)
            return;
        foreach (var row in NormFactors)
            row.Group = value;
        RefreshEffectiveNorm();
    }

    partial void OnSelectedChanged(ConsumerRow? value)
    {
        if (_suppress || value is null) return;
        _editingId = value.Id;
        Code = value.Code;
        Name = value.Name;
        Type = value.TypeName;
        Group = Groups.FirstOrDefault(x => x.Id == value.DefaultGroupId);
        Norm = value.Norm is decimal norm ? Numbers.Factor(norm) : "";
        ExportSample = ExportSamples.FirstOrDefault(x => x.Id == value.DefaultExportSampleSetId);
        RollTransfersIntoQuarter = value.RollTransfersIntoQuarter;
        ShipType = value.ShipType;
        MainMachineCount = value.MainMachineCount > 0
            ? Numbers.Qty(value.MainMachineCount)
            : "1";
        AuxMachineCount = value.AuxMachineCount > 0
            ? Numbers.Qty(value.AuxMachineCount)
            : "1";
        LoadFactors(value.NormFactors);
    }

    public override void Refresh()
    {
        var keep = _editingId;
        Groups.Clear();
        Items.Clear();
        foreach (var row in System.GetGroups()) Groups.Add(new OptionRow(row.Id, row.Name));
        ExportSamples.Clear();
        foreach (var row in System.GetItems()) Items.Add(new OptionRow(row.Id, row.Name));
        foreach (var row in System.GetSampleSets(DocumentFamily.Export)) ExportSamples.Add(new OptionRow(row.Id, row.Name));
        _suppress = true;
        Rows.Clear();
        foreach (var row in System.GetConsumers()) Rows.Add(row);
        Selected = Rows.FirstOrDefault(x => x.Id == keep);
        _suppress = false;
        if (Selected is null && keep is Guid id)
        {
            var row = Rows.FirstOrDefault(x => x.Id == id);
            if (row is not null)
                LoadFactors(row.NormFactors);
        }
    }

    [RelayCommand]
    private void New()
    {
        _editingId = null; Selected = null; Code = ""; Name = ""; Type = "Máy"; Group = null; Item = null; Norm = ""; ExportSample = null; Banner = "";
        RollTransfersIntoQuarter = true;
        ShowRollTransfers = true;
        ShipType = "";
        MainMachineCount = "1";
        AuxMachineCount = "1";
        NormFactors.Clear();
        EffectiveNormText = "";
    }

    [RelayCommand]
    private void AddNormFactor() => EnsureShipNormSlots();

    [RelayCommand]
    private void RemoveNormFactor(NormFactorRowVm? row)
    {
        // Bảng định mức tàu cố định 6 bậc — không xóa dòng.
    }

    [RelayCommand]
    private void Save()
    {
        decimal? norm = null;
        if (IsVehicle)
        {
            if (!Numbers.Try(Norm, out var parsed)) { Fail("Định mức không hợp lệ."); return; }
            norm = parsed;
        }

        var factors = new List<ConsumerNormFactorEdit>();
        if (IsShip)
        {
            if (Group is null)
            {
                Fail("Chọn nhóm nhiên liệu mặc định (Xăng hoặc Dầu).");
                return;
            }

            if (!ShipNormSlots.IsFuelGroup(Group.Label))
            {
                Fail("Nhóm nhiên liệu tàu phải là Xăng hoặc Dầu.");
                return;
            }

            EnsureShipNormSlots();
            if (!Numbers.Try(MainMachineCount, out var mainMachines) || mainMachines < 0)
            {
                Fail("Số máy chính không hợp lệ.");
                return;
            }

            if (!Numbers.Try(AuxMachineCount, out var auxMachines) || auxMachines < 0)
            {
                Fail("Số máy phụ không hợp lệ.");
                return;
            }

            var order = 0;
            foreach (var row in NormFactors)
            {
                order++;
                if (!Numbers.Try(row.Value, out var value) || value < 0)
                {
                    Fail($"Định mức '{row.Label}' không hợp lệ.");
                    return;
                }

                factors.Add(new ConsumerNormFactorEdit
                {
                    Id = row.Id,
                    GroupId = Group.Id,
                    Name = row.Label,
                    Value = value,
                    SortOrder = order
                });
            }

            var result = System.SaveConsumer(new ConsumerEdit
            {
                Id = _editingId,
                Code = Code,
                Name = Name,
                Type = Type switch
                {
                    "Phương tiện" => ConsumerType.Vehicle,
                    "Tàu" => ConsumerType.Ship,
                    "Đối tượng khác" => ConsumerType.Other,
                    _ => ConsumerType.Machine
                },
                DefaultGroupId = Group?.Id,
                DefaultItemId = _editingId is Guid existingId ? Rows.FirstOrDefault(x => x.Id == existingId)?.DefaultItemId : null,
                Norm = norm,
                NormFactors = factors,
                ShipType = ShipType,
                MainMachineCount = mainMachines,
                AuxMachineCount = auxMachines,
                RollTransfersIntoQuarter = ShowRollTransfers && RollTransfersIntoQuarter,
                DefaultImportSampleSetId = null,
                DefaultExportSampleSetId = ExportSample?.Id
                    ?? (_editingId is Guid existing
                        ? System.GetConsumers().FirstOrDefault(x => x.Id == existing)?.DefaultExportSampleSetId
                        : null)
            });
            Show(result);
            if (!result.Ok || result.Id is not Guid id)
                return;
            _editingId = id;
            Refresh();
            return;
        }

        var nonShipResult = System.SaveConsumer(new ConsumerEdit
        {
            Id = _editingId,
            Code = Code,
            Name = Name,
            Type = Type switch
            {
                "Phương tiện" => ConsumerType.Vehicle,
                "Tàu" => ConsumerType.Ship,
                "Đối tượng khác" => ConsumerType.Other,
                _ => ConsumerType.Machine
            },
            DefaultGroupId = Group?.Id,
            DefaultItemId = _editingId is Guid existingId2 ? Rows.FirstOrDefault(x => x.Id == existingId2)?.DefaultItemId : null,
            Norm = norm,
            NormFactors = factors,
            RollTransfersIntoQuarter = ShowRollTransfers && RollTransfersIntoQuarter,
            DefaultImportSampleSetId = null,
            DefaultExportSampleSetId = ExportSample?.Id
                ?? (_editingId is Guid existing2
                    ? System.GetConsumers().FirstOrDefault(x => x.Id == existing2)?.DefaultExportSampleSetId
                    : null)
        });
        Show(nonShipResult);
        if (!nonShipResult.Ok || nonShipResult.Id is not Guid nonShipId)
            return;
        _editingId = nonShipId;
        Refresh();
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        if (!GroupsVm.Confirm("Xóa đối tượng này?")) return;
        Show(System.DeleteConsumer(Selected.Id));
        if (BannerIsError) return;
        New();
        Refresh();
    }

    private void LoadFactors(IReadOnlyList<ConsumerNormFactorRow> factors)
    {
        NormFactors.Clear();
        var byName = factors
            .GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.SortOrder).First(), StringComparer.OrdinalIgnoreCase);
        foreach (var label in ShipNormSlots.Labels)
        {
            byName.TryGetValue(label, out var factor);
            var row = new NormFactorRowVm
            {
                Id = factor?.Id,
                Label = label,
                Group = Group ?? Groups.FirstOrDefault(x => x.Id == factor?.GroupId),
                Value = factor is null ? "0" : Numbers.Factor(factor.Value)
            };
            row.PropertyChanged += (_, _) => RefreshEffectiveNorm();
            NormFactors.Add(row);
        }

        RefreshEffectiveNorm();
    }

    private void EnsureShipNormSlots()
    {
        if (!IsShip)
            return;
        if (NormFactors.Count == ShipNormSlots.Labels.Length
            && NormFactors.Select(x => x.Label).SequenceEqual(ShipNormSlots.Labels))
        {
            foreach (var row in NormFactors)
                row.Group ??= Group;
            return;
        }

        var existing = NormFactors
            .GroupBy(x => x.Label.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        NormFactors.Clear();
        foreach (var label in ShipNormSlots.Labels)
        {
            existing.TryGetValue(label, out var old);
            var formatted = "0";
            if (old is not null && Numbers.Try(old.Value, out var parsed))
                formatted = Numbers.Factor(parsed);
            else if (!string.IsNullOrWhiteSpace(old?.Value))
                formatted = old.Value;
            var row = new NormFactorRowVm
            {
                Id = old?.Id,
                Label = label,
                Group = Group ?? old?.Group,
                Value = formatted
            };
            row.PropertyChanged += (_, _) => RefreshEffectiveNorm();
            NormFactors.Add(row);
        }

        RefreshEffectiveNorm();
    }

    private void RefreshEffectiveNorm()
    {
        if (!IsShip)
        {
            EffectiveNormText = "";
            return;
        }

        var parts = new List<string>();
        foreach (var row in NormFactors)
        {
            if (!Numbers.Try(row.Value, out var value) || value <= 0)
                continue;
            parts.Add($"{row.Label}: {Numbers.Factor(value)} L/giờ");
        }

        EffectiveNormText = parts.Count > 0
            ? $"Định mức theo bậc công suất (L/giờ): {string.Join("; ", parts)}. Tiêu thụ = giờ HĐ × định mức từng bậc."
            : "Nhập định mức L/giờ cho từng bậc: Tại bến, 25/50/75/100% CX, Máy phụ. Nhóm nhiên liệu = Xăng hoặc Dầu.";
    }
}

public partial class FieldsVm : PageVm
{
    public FieldsVm(Core.FuelSystem system) : base(system) { }
    public ObservableCollection<FieldRow> Rows { get; } = [];
    public ObservableCollection<FieldRow> VisibleRows { get; } = [];
    public ObservableCollection<string> Families { get; } = ["Phiếu nhập", "Phiếu xuất"];
    public ObservableCollection<string> DataTypes { get; } = ["Chữ", "Số", "Ngày", "Có/Không"];
    [ObservableProperty] private FieldRow? _selected;
    [ObservableProperty] private string _family = "Phiếu nhập";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _dataType = "Chữ";
    [ObservableProperty] private bool _isRequired;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private string _sort = "0";
    [ObservableProperty] private bool _paperOnly = true;
    [ObservableProperty] private bool _showExtraOnSlip;
    private Guid? _editingId;
    private bool _suppress;

    partial void OnSelectedChanged(FieldRow? value)
    {
        if (_suppress || value is null) return;
        _editingId = value.Id;
        Family = value.FamilyName;
        Name = value.Name;
        DataType = value.DataTypeName;
        IsRequired = value.IsRequired;
        IsVisible = value.IsVisible;
        Sort = Numbers.Qty(value.SortOrder);
    }

    partial void OnShowExtraOnSlipChanged(bool value)
    {
        if (_suppress)
            return;
        System.SetExtraFieldsOnSlip(value);
    }

    partial void OnPaperOnlyChanged(bool value)
    {
        if (_suppress) return;
        _suppress = true;
        ApplyFilter(Selected?.Id);
        _suppress = false;
    }

    public override void Refresh()
    {
        var keep = _editingId;
        _suppress = true;
        ShowExtraOnSlip = System.GetExtraFieldsOnSlip();
        Rows.Clear();
        foreach (var row in System.GetFields()) Rows.Add(row);
        ApplyFilter(keep);
        _suppress = false;
    }

    private void ApplyFilter(Guid? keep)
    {
        VisibleRows.Clear();
        foreach (var row in Rows.Where(x => !PaperOnly || Core.Persistence.SlipFieldCatalog.IsPaperPriority(x.Name)))
            VisibleRows.Add(row);
        Selected = VisibleRows.FirstOrDefault(x => x.Id == keep);
    }

    [RelayCommand]
    private void New() { _editingId = null; Selected = null; Name = ""; DataType = "Chữ"; IsRequired = false; IsVisible = true; Sort = "0"; Banner = ""; }

    [RelayCommand]
    private void Save()
    {
        if (!Numbers.TryInt(Sort, out var sort) || sort < 0) { Fail("Thứ tự phải là số nguyên không âm."); return; }
        var result = System.SaveField(new FieldEdit
        {
            Id = _editingId,
            Family = Family == "Phiếu xuất" ? DocumentFamily.Export : DocumentFamily.Import,
            Name = Name,
            DataType = DataType switch { "Số" => FieldDataType.Number, "Ngày" => FieldDataType.Date, "Có/Không" => FieldDataType.Boolean, _ => FieldDataType.Text },
            IsRequired = IsRequired,
            IsVisible = IsVisible,
            SortOrder = sort
        });
        Show(result);
        if (!result.Ok || result.Id is not Guid id)
            return;
        _editingId = id;
        Refresh();
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        if (!GroupsVm.Confirm("Xóa trường dữ liệu này?")) return;
        Show(System.DeleteField(Selected.Id));
        if (BannerIsError) return;
        New();
        Refresh();
    }
}

public partial class SamplesVm : PageVm
{
    private static readonly (string Title, bool Open, string[] Names)[] ImportLayout =
    [
        ("Đầu phiếu", true, ["Cơ quan", "Đơn vị"]),
        ("Cột trái", true, ["Đơn vị nhận hàng", "Đơn vị giao hàng", "Tính chất nhập", "Theo hợp đồng số", "Đơn vị vận chuyển"]),
        ("Cột phải", true, ["Có giá đến ngày", "Người giao hàng", "Giấy giới thiệu và CMT", "Số xe", "Dung tích kiểm định"]),
        ("Ghi chú", true, ["Ghi chú"]),
        ("Chữ ký", true, ["Chữ ký người giao", "Chữ ký người nhận", "Chữ ký tài chính", "Chữ ký người viết phiếu", "Chữ ký trưởng ban HC-KT", "Chữ ký chỉ huy đơn vị"])
    ];

    private static readonly (string Title, bool Open, string[] Names)[] ExportLayout =
    [
        ("Đầu phiếu", true, ["Cơ quan", "Đơn vị"]),
        ("Cột trái", true, ["Đơn vị giao", "Đơn vị nhận", "Tính chất xuất", "Theo lệnh (KH)", "Người nhận", "Giấy giới thiệu và CMT"]),
        ("Cột phải", true, ["Có giá đến ngày", "Đơn vị vận chuyển", "Số xe", "Số km", "Dung tích kiểm định", "Dung tích nhận hàng", "Số lượng bao bì"]),
        ("Ghi chú", true, ["Ghi chú"]),
        ("Chữ ký", true, ["Chữ ký người nhận", "Chữ ký người giao", "Chữ ký tài chính", "Chữ ký người viết phiếu", "Chữ ký trưởng ban HC-KT", "Chữ ký chỉ huy đơn vị"])
    ];

    public SamplesVm(Core.FuelSystem system) : base(system) { }
    public ObservableCollection<SampleSetRow> Sets { get; } = [];
    public ObservableCollection<SampleSectionVm> Sections { get; } = [];
    public ObservableCollection<string> Families { get; } = ["Phiếu nhập", "Phiếu xuất"];
    [ObservableProperty] private SampleSetRow? _selectedSet;
    [ObservableProperty] private string _family = "Phiếu nhập";
    [ObservableProperty] private string _name = "";
    private Guid? _editingId;
    private bool _suppress;

    public bool HasSet => SelectedSet is not null;
    [ObservableProperty] private bool _isOwnedEditor;
    [ObservableProperty] private bool _isCatalogSlip;
    public ObservableCollection<OwnedFieldVm> OwnedFields { get; } = [];
    public bool ShowAddHint => !IsCatalogSlip && !IsOwnedEditor;
    public bool ShowSetTools => !IsCatalogSlip && !IsOwnedEditor;
    public bool ShowValueLists => HasSet && !IsOwnedEditor;

    partial void OnIsOwnedEditorChanged(bool value) => NotifySampleMode();

    partial void OnIsCatalogSlipChanged(bool value) => NotifySampleMode();

    private void NotifySampleMode()
    {
        OnPropertyChanged(nameof(ShowAddHint));
        OnPropertyChanged(nameof(ShowSetTools));
        OnPropertyChanged(nameof(ShowValueLists));
    }
    public bool IsImportFamily => Family != "Phiếu xuất";
    public bool IsExportFamily => Family == "Phiếu xuất";

    partial void OnFamilyChanged(string value)
    {
        OnPropertyChanged(nameof(IsImportFamily));
        OnPropertyChanged(nameof(IsExportFamily));
    }

    partial void OnSelectedSetChanged(SampleSetRow? value)
    {
        OnPropertyChanged(nameof(HasSet));
        OnPropertyChanged(nameof(ShowValueLists));
        if (_suppress) return;
        if (value is null)
        {
            Sections.Clear();
            OwnedFields.Clear();
            return;
        }

        _editingId = value.Id;
        Name = value.Name;
        Family = value.FamilyName;
        LoadCards(value);
        if (IsOwnedEditor)
            LoadOwned();
        else
            OwnedFields.Clear();
    }

    public override void Refresh()
    {
        var keep = _editingId ?? SelectedSet?.Id;
        _suppress = true;
        Sets.Clear();
        foreach (var row in System.GetSampleSets()) Sets.Add(row);
        SelectedSet = Sets.FirstOrDefault(x => x.Id == keep) ?? Sets.FirstOrDefault();
        _suppress = false;
        OnPropertyChanged(nameof(HasSet));
        if (SelectedSet is null)
        {
            Sections.Clear();
            return;
        }

        _editingId = SelectedSet.Id;
        Name = SelectedSet.Name;
        Family = SelectedSet.FamilyName;
        LoadCards(SelectedSet);
    }

    private void LoadCards(SampleSetRow set)
    {
        var fields = System.GetFields(set.Family)
            .Where(x => x.IsVisible && !Core.Persistence.SlipFieldCatalog.IsDocumentKey(x.Name))
            .ToList();
        var values = System.GetSampleValues(set.Id);
        var layout = set.Family == DocumentFamily.Export ? ExportLayout : ImportLayout;
        var used = new HashSet<string>();
        Sections.Clear();
        foreach (var (title, _, names) in layout)
        {
            var section = new SampleSectionVm(title, true);
            foreach (var name in names)
            {
                var field = fields.FirstOrDefault(x => x.Name == name);
                if (field is null) continue;
                used.Add(name);
                section.Fields.Add(MakeCard(field, values));
            }

            if (section.Fields.Count > 0)
                Sections.Add(section);
        }

        var extra = new SampleSectionVm("Trường khác", true);
        foreach (var field in fields.Where(x => !used.Contains(x.Name)).OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
            extra.Fields.Add(MakeCard(field, values));
        if (extra.Fields.Count > 0)
            Sections.Add(extra);
    }

    public void LoadOwned()
    {
        OwnedFields.Clear();
        if (!IsOwnedEditor || SelectedSet is null)
            return;

        var fields = System.GetFields(DocumentFamily.Export)
            .Where(x => x.IsVisible && !Core.Persistence.SlipFieldCatalog.IsDocumentKey(x.Name))
            .ToList();
        var owned = System.GetSampleValues(SelectedSet.Id)
            .GroupBy(x => x.FieldId)
            .ToDictionary(g => g.Key, g => g.First().Value);
        var choices = System.GetSharedExportValues()
            .GroupBy(x => x.FieldId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Value).Distinct().ToList());
        var used = new HashSet<string>();
        foreach (var (title, _, names) in ExportLayout)
        {
            var first = true;
            foreach (var name in names)
            {
                var field = fields.FirstOrDefault(x => x.Name == name);
                if (field is null)
                    continue;
                used.Add(name);
                OwnedFields.Add(MakeOwned(field, owned, choices, first ? title : ""));
                first = false;
            }
        }

        var extraFirst = true;
        foreach (var field in fields.Where(x => !used.Contains(x.Name)).OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
        {
            OwnedFields.Add(MakeOwned(field, owned, choices, extraFirst ? "Trường khác" : ""));
            extraFirst = false;
        }
    }

    private static OwnedFieldVm MakeOwned(FieldRow field, Dictionary<Guid, string> owned, Dictionary<Guid, List<string>> choices, string header)
    {
        var row = new OwnedFieldVm(field.Id, field.Name, header);
        if (choices.TryGetValue(field.Id, out var list))
        {
            foreach (var value in list)
                row.Choices.Add(value);
        }

        if (owned.TryGetValue(field.Id, out var text))
            row.Text = text;
        return row;
    }

    [RelayCommand]
    private void SaveOwned()
    {
        if (SelectedSet is null)
        {
            Fail("Chưa có phiếu mẫu.");
            return;
        }

        var result = System.SaveOwnedSample(SelectedSet.Id, OwnedFields.Select(x => (x.FieldId, x.Text)).ToList());
        Show(result);
        if (result.Ok)
            LoadOwned();
    }

    [RelayCommand]
    private void FillFromExport()
    {
        foreach (var field in OwnedFields)
        {
            if (field.Text.Trim().Length > 0 || field.Choices.Count == 0)
                continue;
            field.Text = field.Choices[0];
        }
    }

    private SampleFieldCardVm MakeCard(FieldRow field, IReadOnlyList<SampleValueRow> values)
    {
        var isNumber = field.DataType == FieldDataType.Number;
        var card = new SampleFieldCardVm(field.Id, field.Name, isNumber);
        foreach (var row in values.Where(x => x.FieldId == field.Id))
            card.Values.Add(new SampleValueItemVm(this, row.Id, FormatSampleNumber(row.Value, isNumber), isNumber));
        card.NotifyValues();
        return card;
    }

    private void FillCard(SampleFieldCardVm card)
    {
        if (SelectedSet is null) return;
        card.Values.Clear();
        foreach (var row in System.GetSampleValues(SelectedSet.Id).Where(x => x.FieldId == card.FieldId))
            card.Values.Add(new SampleValueItemVm(this, row.Id, FormatSampleNumber(row.Value, card.IsNumber), card.IsNumber));
        card.NotifyValues();
    }

    private static string FormatSampleNumber(string text, bool isNumber)
    {
        if (!isNumber || string.IsNullOrWhiteSpace(text))
            return text ?? "";
        return Numbers.Try(text, out var value) ? Numbers.Factor(value) : text.Trim();
    }

    private static bool TryNormalizeSampleNumber(string? text, bool isNumber, out string normalized)
    {
        normalized = text?.Trim() ?? "";
        if (!isNumber)
            return normalized.Length > 0;
        if (!Numbers.Try(normalized, out var value))
            return false;
        normalized = Numbers.Factor(value);
        return true;
    }

    [RelayCommand]
    private void ChooseFamily(string? family)
    {
        if (string.IsNullOrWhiteSpace(family)) return;
        Family = family;
        var match = Sets.FirstOrDefault(x => x.FamilyName == family);
        if (match is not null)
            SelectedSet = match;
    }

    [RelayCommand]
    private void New()
    {
        _editingId = null;
        SelectedSet = null;
        Name = "";
        Sections.Clear();
        Banner = "";
    }

    [RelayCommand]
    private void SaveSet()
    {
        Show(System.SaveSampleSet(_editingId, Name, Family == "Phiếu xuất" ? DocumentFamily.Export : DocumentFamily.Import));
        if (!BannerIsError && System.GetSampleSets().Count > 0)
        {
            _editingId ??= System.GetSampleSets().FirstOrDefault(x => x.Name == Name.Trim())?.Id;
            Refresh();
        }
    }

    [RelayCommand]
    private void DeleteSet()
    {
        if (SelectedSet is null) return;
        if (!GroupsVm.Confirm("Xóa bộ dữ liệu mẫu này?")) return;
        Show(System.DeleteSampleSet(SelectedSet.Id));
        if (BannerIsError) return;
        New();
        Refresh();
    }

    [RelayCommand]
    private void AddDraft(SampleFieldCardVm? card)
    {
        if (SelectedSet is null || card is null)
        {
            Fail("Chọn bộ mẫu.");
            return;
        }

        if (!TryNormalizeSampleNumber(card.Draft, card.IsNumber, out var value))
        {
            Fail(card.IsNumber ? "Giá trị số không hợp lệ (dùng dấu phẩy thập phân, ví dụ 1.234,5)." : "Giá trị trống.");
            return;
        }

        Show(System.AddSampleValue(SelectedSet.Id, card.FieldId, value));
        if (BannerIsError) return;
        card.Draft = "";
        FillCard(card);
    }

    public void CommitValue(SampleValueItemVm item)
    {
        if (!TryNormalizeSampleNumber(item.Value, item.IsNumber, out var value))
        {
            Fail(item.IsNumber ? "Giá trị số không hợp lệ (dùng dấu phẩy thập phân, ví dụ 1.234,5)." : "Giá trị trống.");
            var stored = SelectedSet is null
                ? null
                : System.GetSampleValues(SelectedSet.Id).FirstOrDefault(x => x.Id == item.Id);
            if (stored is not null)
                item.Restore(FormatSampleNumber(stored.Value, item.IsNumber));
            return;
        }

        if (value != item.Value)
            item.Restore(value);

        var result = System.UpdateSampleValue(item.Id, value);
        Show(result);
        if (result.Ok)
            return;
        var again = SelectedSet is null
            ? null
            : System.GetSampleValues(SelectedSet.Id).FirstOrDefault(x => x.Id == item.Id);
        if (again is not null)
            item.Restore(FormatSampleNumber(again.Value, item.IsNumber));
    }

    [RelayCommand]
    private void RemoveValue(SampleValueItemVm? item)
    {
        if (item is null || SelectedSet is null) return;
        Show(System.DeleteSampleValue(item.Id));
        if (BannerIsError) return;
        foreach (var section in Sections)
        {
            foreach (var card in section.Fields)
            {
                var found = card.Values.FirstOrDefault(x => x.Id == item.Id);
                if (found is null) continue;
                card.Values.Remove(found);
                card.NotifyValues();
                return;
            }
        }
    }
}

public partial class OwnedFieldVm : ObservableObject
{
    public OwnedFieldVm(Guid fieldId, string name, string header)
    {
        FieldId = fieldId;
        Name = name;
        Header = header;
    }

    public Guid FieldId { get; }
    public string Name { get; }
    public string Header { get; }
    public bool HasHeader => Header.Length > 0;
    public ObservableCollection<string> Choices { get; } = [];
    public bool HasChoices => Choices.Count > 0;
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string? _picked;

    partial void OnPickedChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            Text = value;
    }
}

public partial class SampleSectionVm : ObservableObject
{
    public SampleSectionVm(string title, bool open)
    {
        Title = title;
        IsOpen = open;
    }

    public string Title { get; }
    public ObservableCollection<SampleFieldCardVm> Fields { get; } = [];
    [ObservableProperty] private bool _isOpen;
}

public partial class SampleFieldCardVm : ObservableObject
{
    public SampleFieldCardVm(Guid fieldId, string name, bool isNumber = false)
    {
        FieldId = fieldId;
        Name = name;
        IsNumber = isNumber;
    }

    public Guid FieldId { get; }
    public string Name { get; }
    public bool IsNumber { get; }
    public ObservableCollection<SampleValueItemVm> Values { get; } = [];
    [ObservableProperty] private string _draft = "";
    public bool HasValues => Values.Count > 0;
    public void NotifyValues() => OnPropertyChanged(nameof(HasValues));
}

public partial class SampleValueItemVm : ObservableObject
{
    private readonly SamplesVm _owner;
    private bool _restoring;

    public SampleValueItemVm(SamplesVm owner, Guid id, string value, bool isNumber = false)
    {
        _owner = owner;
        Id = id;
        IsNumber = isNumber;
        _restoring = true;
        Value = value;
        _restoring = false;
    }

    public Guid Id { get; }
    public bool IsNumber { get; }
    [ObservableProperty] private string _value;

    partial void OnValueChanged(string value)
    {
        if (_restoring)
            return;
        _owner.CommitValue(this);
    }

    public void Restore(string value)
    {
        if (Value == value)
            return;
        _restoring = true;
        Value = value;
        _restoring = false;
    }
}

public partial class MissionsVm : PageVm
{
    public MissionsVm(Core.FuelSystem system) : base(system) { }

    public ObservableCollection<MissionGroupRow> Groups { get; } = [];
    public ObservableCollection<MissionTaskRow> Tasks { get; } = [];
    [ObservableProperty] private MissionGroupRow? _selectedGroup;
    [ObservableProperty] private MissionTaskRow? _selected;
    [ObservableProperty] private string _groupCode = "";
    [ObservableProperty] private string _groupName = "";
    [ObservableProperty] private bool _groupIsLoss;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _isActive = true;
    private Guid? _editingGroupId;
    private Guid? _editingId;
    private bool _suppress;

    partial void OnSelectedGroupChanged(MissionGroupRow? value)
    {
        if (_suppress || value is null) return;
        _editingGroupId = value.Id;
        GroupCode = value.Code;
        GroupName = value.Name;
        GroupIsLoss = value.IsLossGroup;
        ReloadTasks(value.Id);
    }

    partial void OnSelectedChanged(MissionTaskRow? value)
    {
        if (_suppress || value is null) return;
        _editingId = value.Id;
        Name = value.Name;
        IsActive = value.IsActive;
    }

    public override void Refresh()
    {
        var keepGroup = _editingGroupId;
        var keepTask = _editingId;
        _suppress = true;
        Groups.Clear();
        foreach (var row in System.GetMissionGroups())
            Groups.Add(row);
        SelectedGroup = Groups.FirstOrDefault(x => x.Id == keepGroup) ?? Groups.FirstOrDefault();
        if (SelectedGroup is not null)
        {
            _editingGroupId = SelectedGroup.Id;
            GroupCode = SelectedGroup.Code;
            GroupName = SelectedGroup.Name;
            GroupIsLoss = SelectedGroup.IsLossGroup;
            ReloadTasks(SelectedGroup.Id);
            Selected = Tasks.FirstOrDefault(x => x.Id == keepTask);
            if (Selected is not null)
            {
                _editingId = Selected.Id;
                Name = Selected.Name;
                IsActive = Selected.IsActive;
            }
        }
        else
        {
            Tasks.Clear();
            Selected = null;
        }

        _suppress = false;
    }

    private void ReloadTasks(Guid groupId)
    {
        Tasks.Clear();
        foreach (var row in System.GetMissionTasks(groupId, activeOnly: false))
            Tasks.Add(row);
    }

    [RelayCommand]
    private void NewGroup()
    {
        _editingGroupId = null;
        SelectedGroup = null;
        GroupCode = "";
        GroupName = "";
        GroupIsLoss = false;
        Banner = "";
    }

    [RelayCommand]
    private void SaveGroup()
    {
        var result = System.SaveMissionGroup(_editingGroupId, GroupCode, GroupName, GroupIsLoss);
        Show(result);
        if (!result.Ok || result.Id is not Guid id)
            return;
        _editingGroupId = id;
        Refresh();
    }

    [RelayCommand]
    private void DeleteGroup()
    {
        if (_editingGroupId is not Guid id) return;
        if (!GroupsVm.Confirm("Xóa nhóm nhiệm vụ này?")) return;
        Show(System.DeleteMissionGroup(id));
        if (BannerIsError) return;
        NewGroup();
        Refresh();
    }

    [RelayCommand]
    private void New()
    {
        _editingId = null;
        Selected = null;
        Name = "";
        IsActive = true;
        Banner = "";
    }

    [RelayCommand]
    private void Save()
    {
        if (SelectedGroup is null && _editingGroupId is null)
        {
            Fail("Chọn nhóm nhiệm vụ trước.");
            return;
        }

        var groupId = SelectedGroup?.Id ?? _editingGroupId!.Value;
        var result = System.SaveMissionTask(_editingId, groupId, Name, isActive: IsActive);
        Show(result);
        if (!result.Ok || result.Id is not Guid id)
            return;
        _editingId = id;
        Refresh();
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        if (!GroupsVm.Confirm("Xóa nhiệm vụ này?")) return;
        Show(System.DeleteMissionTask(Selected.Id));
        if (BannerIsError) return;
        New();
        Refresh();
    }
}
