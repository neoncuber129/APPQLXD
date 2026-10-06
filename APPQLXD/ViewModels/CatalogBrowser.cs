using System.Collections.ObjectModel;
using System.Windows.Threading;
using APPQLXD.Core;
using APPQLXD.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace APPQLXD.ViewModels;

public enum FinderKind
{
    Root,
    Section,
    Group,
    UnitFolder,
    Unit,
    UnitNew,
    LotTypeFolder,
    LotType,
    LotTypeNew,
    Item,
    ItemNew,
    WarehouseType,
    Warehouse,
    WarehouseNew,
    ConsumerType,
    Consumer,
    ConsumerNew,
    SampleFamily,
    SampleSet,
    SampleNew,
    FieldFamily,
    Field,
    FieldNew,
    MissionGroup,
    MissionGroupNew,
    MissionTask,
    MissionTaskNew
}

public readonly record struct FinderStep(FinderKind Kind, Guid Id, string Key)
{
    public static FinderStep Root { get; } = new(FinderKind.Root, Guid.Empty, "");
    public static FinderStep Section(string key) => new(FinderKind.Section, Guid.Empty, key);
    public static FinderStep IdOf(FinderKind kind, Guid id) => new(kind, id, "");
    public static FinderStep KeyOf(FinderKind kind, string key) => new(kind, Guid.Empty, key);
}

public sealed class FinderEntry
{
    public required string Title { get; init; }
    public string Subtitle { get; init; } = "";
    public string SampleOwner { get; init; } = "";
    public bool HasSubtitle => Subtitle.Length > 0;
    public bool CanSample => SampleOwner.Length > 0;
    public FinderStep Step { get; init; }
}

public partial class FinderColumnVm : ObservableObject
{
    public required string Title { get; init; }
    public FinderStep OpenedBy { get; init; }
    public double Width { get; init; } = 260;
    public bool ShowGroupFooter { get; set; }
    public bool ShowMissionGroupFooter { get; set; }
    public bool ShowPaperFilter { get; set; }
    public bool ShowExtraToggle { get; set; }
    public object? Editor { get; init; }
    public string EditorKind { get; init; } = "";
    public bool IsList => Editor is null;
    public bool IsEditor => Editor is not null;
    public bool GroupFooterOn => ShowGroupFooter && (Selected is null || Selected.Step.Kind == FinderKind.Group);
    public bool MissionGroupFooterOn => ShowMissionGroupFooter && (Selected is null || Selected.Step.Kind == FinderKind.MissionGroup);
    public ObservableCollection<FinderEntry> Entries { get; } = [];
    [ObservableProperty] private FinderEntry? _selected;

    partial void OnSelectedChanged(FinderEntry? value)
    {
        OnPropertyChanged(nameof(GroupFooterOn));
        OnPropertyChanged(nameof(MissionGroupFooterOn));
    }
}

public partial class CatalogHostVm
{
    private bool _busy;
    private List<FinderStep>? _overridePath;

    private void OnPaperFilter()
    {
        if (_busy) return;
        InvalidateFinder();
        Rebuild();
    }

    public void Open(FinderColumnVm column)
    {
        if (_busy) return;
        var entry = column.Selected;
        if (entry is null) return;
        var index = Columns.IndexOf(column);
        if (index < 0) return;
        if (index + 1 < Columns.Count && Columns[index + 1].OpenedBy.Equals(entry.Step))
            return;

        _busy = true;
        try
        {
            while (Columns.Count > index + 1)
                Columns.RemoveAt(Columns.Count - 1);
            if (entry.Step.Kind == FinderKind.Group)
                Items.GroupEditor.Selected = Items.GroupEditor.Rows.FirstOrDefault(x => x.Id == entry.Step.Id);
            if (entry.Step.Kind == FinderKind.MissionGroup)
                Missions.SelectedGroup = Missions.Groups.FirstOrDefault(x => x.Id == entry.Step.Id);
            if (entry.Step.Kind == FinderKind.UnitFolder)
                Items.NewGroupCommand.Execute(null);
            var next = MakeNext(column, entry);
            if (next is not null)
                Columns.Add(next);
        }
        finally
        {
            global::System.Windows.Application.Current.Dispatcher.BeginInvoke(() => _busy = false, DispatcherPriority.ApplicationIdle);
        }

        ColumnsBuilt?.Invoke();
    }

    private void Rebuild()
    {
        var path = Capture();
        _busy = true;
        try
        {
            Items.Refresh();
            LotTypes.Refresh();
            Warehouses.Refresh();
            Consumers.Refresh();
            Missions.Refresh();
            Fields.Refresh();
            Samples.Refresh();
            Columns.Clear();
            var column = BuildRoot();
            Columns.Add(column);
            foreach (var step in path)
            {
                var entry = column.Entries.FirstOrDefault(x => x.Step.Equals(step));
                if (entry is null || !column.IsList) break;
                column.Selected = entry;
                var next = MakeNext(column, entry);
                if (next is null) break;
                Columns.Add(next);
                column = next;
            }

            _finderReady = true;
        }
        finally
        {
            global::System.Windows.Application.Current.Dispatcher.BeginInvoke(() => _busy = false, DispatcherPriority.ApplicationIdle);
        }

        ColumnsBuilt?.Invoke();
    }

    private List<FinderStep> Capture()
    {
        if (_overridePath is not null)
        {
            var forced = _overridePath;
            _overridePath = null;
            return forced;
        }

        var path = new List<FinderStep>();
        foreach (var column in Columns)
        {
            if (!column.IsList || column.Selected is null) break;
            path.Add(column.Selected.Step);
        }

        return path;
    }

    private void StartBlank(FinderKind kind)
    {
        var column = Columns.FirstOrDefault(c => c.IsList && c.Entries.Any(e => e.Step.Kind == kind));
        if (column is null) return;
        var add = column.Entries.First(e => e.Step.Kind == kind);
        if (ReferenceEquals(column.Selected, add))
            Open(column);
        else
            column.Selected = add;
    }

    private FinderColumnVm BuildRoot()
    {
        var column = ListColumn("Danh mục", FinderStep.Root, 240);
        column.Entries.Add(Entry("Mặt hàng", "Nhóm, đơn vị, mặt hàng", FinderStep.Section("items")));
        column.Entries.Add(Entry("Kho XD", "Kho XD, máy, phương tiện, tàu", FinderStep.Section("warehouses")));
        column.Entries.Add(Entry("Kho PTKT-VTXD", "Kho PTKT-VTXD độc lập", FinderStep.Section("ptkt")));
        column.Entries.Add(Entry("Nhiệm vụ", "Nhóm và nhiệm vụ con", FinderStep.Section("missions")));
        column.Entries.Add(Entry("Dữ liệu mẫu", "Giá trị trên phiếu", FinderStep.Section("samples")));
        column.Entries.Add(Entry("Trường khác", "Thêm hoặc ẩn trường", FinderStep.Section("fields")));
        return column;
    }

    private FinderColumnVm? MakeNext(FinderColumnVm source, FinderEntry entry)
    {
        var step = entry.Step;
        return step.Kind switch
        {
            FinderKind.Section when step.Key == "items" => BuildGroups(step),
            FinderKind.Section when step.Key is "warehouses" or "consumers" => BuildWarehouseTypes(step),
            FinderKind.Section when step.Key == "ptkt" => BuildWarehouses(FinderStep.KeyOf(FinderKind.WarehouseType, Labels.Warehouse(WarehouseType.Ptkt))),
            FinderKind.Section when step.Key == "missions" => BuildMissionGroups(step),
            FinderKind.Section when step.Key == "samples" => BuildSampleFamilies(step),
            FinderKind.Section when step.Key == "fields" => BuildFieldFamilies(step),
            FinderKind.Group => BuildItems(step, entry.Title),
            FinderKind.UnitFolder => BuildUnits(step),
            FinderKind.Unit or FinderKind.UnitNew => BuildUnitEditor(step),
            FinderKind.LotTypeFolder => BuildLotTypes(step),
            FinderKind.LotType or FinderKind.LotTypeNew => BuildLotTypeEditor(step),
            FinderKind.Item or FinderKind.ItemNew => BuildItemEditor(source, step),
            FinderKind.WarehouseType => BuildWarehouses(step),
            FinderKind.Warehouse or FinderKind.WarehouseNew => BuildWarehouseEditor(source, step),
            FinderKind.ConsumerType => BuildConsumers(step),
            FinderKind.Consumer or FinderKind.ConsumerNew => BuildConsumerEditor(source, step),
            FinderKind.MissionGroup => BuildMissionTasks(step, entry.Title),
            FinderKind.MissionTask or FinderKind.MissionTaskNew => BuildMissionTaskEditor(source, step),
            FinderKind.SampleFamily => BuildCatalogSample(step),
            FinderKind.SampleSet or FinderKind.SampleNew => BuildSampleEditor(source, step),
            FinderKind.FieldFamily => BuildFields(step),
            FinderKind.Field or FinderKind.FieldNew => BuildFieldEditor(source, step),
            _ => null
        };
    }

    private FinderColumnVm BuildGroups(FinderStep openedBy)
    {
        var column = ListColumn("Mặt hàng", openedBy, 260);
        column.ShowGroupFooter = true;
        foreach (var group in Items.GroupEditor.Rows.OrderBy(x => x.Name))
        {
            var count = Items.Rows.Count(x => x.GroupId == group.Id);
            column.Entries.Add(Entry(group.Name, $"{count} mặt hàng", FinderStep.IdOf(FinderKind.Group, group.Id)));
        }

        column.Entries.Add(Entry("Đơn vị tính", $"{Items.UnitEditor.Rows.Count} đơn vị", FinderStep.KeyOf(FinderKind.UnitFolder, "units")));
        column.Entries.Add(Entry("Loại lô", $"{LotTypes.Rows.Count} loại", FinderStep.KeyOf(FinderKind.LotTypeFolder, "lottypes")));
        return column;
    }

    private FinderColumnVm BuildItems(FinderStep group, string title)
    {
        Items.GroupEditor.Selected = Items.GroupEditor.Rows.FirstOrDefault(x => x.Id == group.Id);
        var column = ListColumn(title, group, 280);
        foreach (var item in Items.Rows.Where(x => x.GroupId == group.Id).OrderBy(x => x.Name))
        {
            var subtitle = string.IsNullOrWhiteSpace(item.Code) ? item.UnitName : $"{item.Code} · {item.UnitName}";
            column.Entries.Add(Entry(item.Name, subtitle, FinderStep.IdOf(FinderKind.Item, item.Id)));
        }

        column.Entries.Add(Entry("Thêm mặt hàng", "", FinderStep.KeyOf(FinderKind.ItemNew, "")));
        return column;
    }

    private FinderColumnVm BuildItemEditor(FinderColumnVm source, FinderStep step)
    {
        if (step.Kind == FinderKind.ItemNew)
        {
            Items.NewCommand.Execute(null);
            Items.Group = Items.Groups.FirstOrDefault(x => x.Id == source.OpenedBy.Id);
            Items.Unit = Items.Units.FirstOrDefault();
        }
        else
            Items.Selected = Items.Rows.FirstOrDefault(x => x.Id == step.Id);

        return Editor("Mặt hàng", Items, "item", 460, step);
    }

    private FinderColumnVm BuildUnits(FinderStep openedBy)
    {
        var column = ListColumn("Đơn vị tính", openedBy, 240);
        foreach (var unit in Items.UnitEditor.Rows.OrderBy(x => x.Name))
            column.Entries.Add(Entry(unit.Name, "", FinderStep.IdOf(FinderKind.Unit, unit.Id)));
        column.Entries.Add(Entry("Thêm đơn vị", "", FinderStep.KeyOf(FinderKind.UnitNew, "")));
        return column;
    }

    private FinderColumnVm BuildUnitEditor(FinderStep step)
    {
        if (step.Kind == FinderKind.UnitNew)
            Items.NewUnitCommand.Execute(null);
        else
            Items.UnitEditor.Selected = Items.UnitEditor.Rows.FirstOrDefault(x => x.Id == step.Id);
        return Editor("Đơn vị", Items.UnitEditor, "unit", 360, step);
    }

    private FinderColumnVm BuildLotTypes(FinderStep openedBy)
    {
        var column = ListColumn("Loại lô", openedBy, 240);
        foreach (var row in LotTypes.Rows.OrderBy(x => x.SortOrder).ThenBy(x => x.Code))
            column.Entries.Add(Entry($"{row.Code} — {row.Name}", "", FinderStep.IdOf(FinderKind.LotType, row.Id)));
        column.Entries.Add(Entry("Thêm loại lô", "", FinderStep.KeyOf(FinderKind.LotTypeNew, "")));
        return column;
    }

    private FinderColumnVm BuildLotTypeEditor(FinderStep step)
    {
        if (step.Kind == FinderKind.LotTypeNew)
            LotTypes.NewCommand.Execute(null);
        else
            LotTypes.Selected = LotTypes.Rows.FirstOrDefault(x => x.Id == step.Id);
        return Editor("Loại lô", LotTypes, "lottype", 400, step);
    }

    private FinderColumnVm BuildWarehouseTypes(FinderStep openedBy)
    {
        var column = ListColumn("Kho XD", openedBy, 240);
        var main = Labels.Warehouse(WarehouseType.Main);
        column.Entries.Add(Entry(main, $"{Warehouses.Rows.Count(x => x.TypeName == main && !x.IsConsumerLocation)} kho", FinderStep.KeyOf(FinderKind.WarehouseType, main)));
        foreach (var type in Consumers.Types)
        {
            var count = Consumers.Rows.Count(x => x.TypeName == type);
            column.Entries.Add(Entry(type, $"{count} {(type == "Tàu" ? "tàu" : "đối tượng")}", FinderStep.KeyOf(FinderKind.ConsumerType, type)));
        }

        return column;
    }

    private FinderColumnVm BuildWarehouses(FinderStep type)
    {
        var column = ListColumn(type.Key, type, 280);
        foreach (var row in Warehouses.Rows.Where(x => x.TypeName == type.Key && !x.IsConsumerLocation).OrderBy(x => x.Name))
            column.Entries.Add(Entry(row.Name, row.Code, FinderStep.IdOf(FinderKind.Warehouse, row.Id), "warehouse"));
        column.Entries.Add(Entry("Thêm kho", "", FinderStep.KeyOf(FinderKind.WarehouseNew, "")));
        return column;
    }

    private FinderColumnVm BuildWarehouseEditor(FinderColumnVm source, FinderStep step)
    {
        if (step.Kind == FinderKind.WarehouseNew)
        {
            Warehouses.NewCommand.Execute(null);
            Warehouses.Type = source.OpenedBy.Key;
        }
        else
            Warehouses.Selected = Warehouses.Rows.FirstOrDefault(x => x.Id == step.Id);
        return Editor("Kho", Warehouses, "warehouse", 440, step);
    }

    private FinderColumnVm BuildConsumers(FinderStep type)
    {
        var column = ListColumn(type.Key, type, 280);
        foreach (var row in Consumers.Rows.Where(x => x.TypeName == type.Key).OrderBy(x => x.Name))
            column.Entries.Add(Entry(row.Name, row.Code, FinderStep.IdOf(FinderKind.Consumer, row.Id), "consumer"));
        column.Entries.Add(Entry("Thêm đối tượng", "", FinderStep.KeyOf(FinderKind.ConsumerNew, "")));
        return column;
    }

    private FinderColumnVm BuildConsumerEditor(FinderColumnVm source, FinderStep step)
    {
        if (step.Kind == FinderKind.ConsumerNew)
        {
            Consumers.NewCommand.Execute(null);
            Consumers.Type = source.OpenedBy.Key;
        }
        else
            Consumers.Selected = Consumers.Rows.FirstOrDefault(x => x.Id == step.Id);
        return Editor("Đối tượng", Consumers, "consumer", 460, step);
    }

    private FinderColumnVm BuildMissionGroups(FinderStep openedBy)
    {
        var column = ListColumn("Nhiệm vụ", openedBy, 280);
        column.ShowMissionGroupFooter = true;
        foreach (var group in Missions.Groups.OrderBy(x => x.SortOrder).ThenBy(x => x.Code))
        {
            var sub = group.IsLossGroup
                ? $"{group.TaskCount} nhiệm vụ · Hao hụt"
                : $"{group.TaskCount} nhiệm vụ";
            column.Entries.Add(Entry($"{group.Code}. {group.Name}", sub, FinderStep.IdOf(FinderKind.MissionGroup, group.Id)));
        }
        return column;
    }

    private FinderColumnVm BuildMissionTasks(FinderStep group, string title)
    {
        Missions.SelectedGroup = Missions.Groups.FirstOrDefault(x => x.Id == group.Id);
        var column = ListColumn(title, group, 300);
        foreach (var task in Missions.Tasks.OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
        {
            var subtitle = task.IsActive ? "" : "Ngừng dùng";
            column.Entries.Add(Entry(task.Name, subtitle, FinderStep.IdOf(FinderKind.MissionTask, task.Id)));
        }

        column.Entries.Add(Entry("Thêm nhiệm vụ", "", FinderStep.KeyOf(FinderKind.MissionTaskNew, "")));
        return column;
    }

    private FinderColumnVm BuildMissionTaskEditor(FinderColumnVm source, FinderStep step)
    {
        if (source.OpenedBy.Kind == FinderKind.MissionGroup)
            Missions.SelectedGroup = Missions.Groups.FirstOrDefault(x => x.Id == source.OpenedBy.Id);
        if (step.Kind == FinderKind.MissionTaskNew)
            Missions.NewCommand.Execute(null);
        else
            Missions.Selected = Missions.Tasks.FirstOrDefault(x => x.Id == step.Id);
        return Editor("Nhiệm vụ", Missions, "missiontask", 420, step);
    }

    private FinderColumnVm BuildSampleFamilies(FinderStep openedBy)
    {
        var column = ListColumn("Dữ liệu mẫu", openedBy, 240);
        column.Entries.Add(Entry("Phiếu nhập", "Sửa nội dung các trường", FinderStep.KeyOf(FinderKind.SampleFamily, "Phiếu nhập")));
        column.Entries.Add(Entry("Phiếu xuất", "Sửa nội dung các trường", FinderStep.KeyOf(FinderKind.SampleFamily, "Phiếu xuất")));
        return column;
    }

    private FinderColumnVm BuildCatalogSample(FinderStep family)
    {
        var export = family.Key == "Phiếu xuất";
        var ready = System.EnsureCatalogSample(export ? DocumentFamily.Export : DocumentFamily.Import);
        Samples.Refresh();
        Samples.IsOwnedEditor = false;
        Samples.IsCatalogSlip = true;
        Samples.SelectedSet = ready.Ok ? Samples.Sets.FirstOrDefault(x => x.Id == ready.Id) : null;
        Samples.Banner = ready.Ok
            ? "Sửa nội dung từng trường ngay tại phiếu. Số phiếu và ngày không nằm ở đây."
            : ready.Message;
        return Editor(family.Key, Samples, "sample", 860, family);
    }

    private FinderColumnVm BuildSampleSets(FinderStep family)
    {
        var column = ListColumn(family.Key, family, 280);
        foreach (var set in Samples.Sets.Where(x => x.FamilyName == family.Key).OrderBy(x => x.Name))
            column.Entries.Add(Entry(set.Name, "", FinderStep.IdOf(FinderKind.SampleSet, set.Id)));
        column.Entries.Add(Entry("Thêm bộ mẫu", "", FinderStep.KeyOf(FinderKind.SampleNew, "")));
        return column;
    }

    private FinderColumnVm BuildSampleEditor(FinderColumnVm source, FinderStep step)
    {
        Samples.IsOwnedEditor = false;
        Samples.IsCatalogSlip = false;
        if (step.Kind == FinderKind.SampleNew)
        {
            Samples.NewCommand.Execute(null);
            Samples.Family = source.OpenedBy.Key;
        }
        else
            Samples.SelectedSet = Samples.Sets.FirstOrDefault(x => x.Id == step.Id);
        return Editor("Bộ mẫu", Samples, "sample", 820, step);
    }

    private FinderColumnVm BuildFieldFamilies(FinderStep openedBy)
    {
        var column = ListColumn("Trường khác", openedBy, 280);
        column.ShowExtraToggle = true;
        foreach (var family in Fields.Families)
        {
            var count = Fields.VisibleRows.Count(x => x.FamilyName == family);
            column.Entries.Add(Entry(family, $"{count} trường", FinderStep.KeyOf(FinderKind.FieldFamily, family)));
        }

        return column;
    }

    private FinderColumnVm BuildFields(FinderStep family)
    {
        var column = ListColumn(family.Key, family, 280);
        column.ShowPaperFilter = true;
        foreach (var row in Fields.VisibleRows.Where(x => x.FamilyName == family.Key).OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
            column.Entries.Add(Entry(row.Name, row.DataTypeName, FinderStep.IdOf(FinderKind.Field, row.Id)));
        column.Entries.Add(Entry("Thêm trường", "", FinderStep.KeyOf(FinderKind.FieldNew, "")));
        return column;
    }

    private FinderColumnVm BuildFieldEditor(FinderColumnVm source, FinderStep step)
    {
        if (step.Kind == FinderKind.FieldNew)
        {
            Fields.NewCommand.Execute(null);
            Fields.Family = source.OpenedBy.Key;
        }
        else
            Fields.Selected = Fields.VisibleRows.FirstOrDefault(x => x.Id == step.Id) ?? Fields.Rows.FirstOrDefault(x => x.Id == step.Id);
        return Editor("Trường", Fields, "field", 420, step);
    }

    private static FinderColumnVm ListColumn(string title, FinderStep openedBy, double width) =>
        new() { Title = title, OpenedBy = openedBy, Width = width };

    private static FinderColumnVm Editor(string title, object editor, string kind, double width, FinderStep openedBy) =>
        new() { Title = title, OpenedBy = openedBy, Width = width, Editor = editor, EditorKind = kind };

    private static FinderEntry Entry(string title, string subtitle, FinderStep step, string sampleOwner = "") =>
        new() { Title = title, Subtitle = subtitle, Step = step, SampleOwner = sampleOwner };

    public void OpenExportSample(FinderColumnVm column, FinderEntry entry)
    {
        if (entry.SampleOwner is not ("warehouse" or "consumer"))
            return;
        var result = System.EnsureOwnedExportSample(entry.SampleOwner == "warehouse", entry.Step.Id);
        if (!result.Ok || result.Id is not Guid setId)
        {
            Fail(result.Message);
            return;
        }

        _busy = true;
        try
        {
            var index = Columns.IndexOf(column);
            if (index >= 0)
            {
                column.Selected = entry;
                while (Columns.Count > index + 1)
                    Columns.RemoveAt(Columns.Count - 1);
            }

            AppendOwnedSample(setId);
        }
        finally
        {
            global::System.Windows.Application.Current.Dispatcher.BeginInvoke(() => _busy = false, DispatcherPriority.ApplicationIdle);
        }

        ColumnsBuilt?.Invoke();
    }

    [RelayCommand]
    private void OpenWarehouseSample()
    {
        if (Warehouses.Selected is null)
        {
            Fail("Lưu kho trước khi tạo phiếu mẫu.");
            return;
        }

        OpenOwnedSample(true, Warehouses.Selected.Id);
    }

    [RelayCommand]
    private void OpenConsumerSample()
    {
        if (Consumers.Selected is null)
        {
            Fail("Lưu đối tượng trước khi tạo phiếu mẫu.");
            return;
        }

        OpenOwnedSample(false, Consumers.Selected.Id);
    }

    private void OpenOwnedSample(bool warehouse, Guid ownerId)
    {
        var result = System.EnsureOwnedExportSample(warehouse, ownerId);
        if (!result.Ok || result.Id is not Guid setId)
        {
            Fail(result.Message);
            return;
        }

        while (Columns.Count > 0 && Columns[^1].EditorKind == "sample")
            Columns.RemoveAt(Columns.Count - 1);
        AppendOwnedSample(setId);
        ColumnsBuilt?.Invoke();
    }

    private void AppendOwnedSample(Guid setId)
    {
        Samples.Refresh();
        var set = Samples.Sets.FirstOrDefault(x => x.Id == setId);
        if (set is null)
        {
            Fail("Không thấy phiếu mẫu.");
            return;
        }

        Samples.IsOwnedEditor = true;
        Samples.IsCatalogSlip = false;
        Samples.SelectedSet = set;
        Samples.LoadOwned();
        Samples.Banner = "Tự điền từng trường, hoặc chọn giá trị có sẵn từ phiếu xuất, rồi bấm Lưu phiếu mẫu.";
        Samples.BannerIsError = false;
        Columns.Add(Editor("Phiếu mẫu", Samples, "sample", 820, FinderStep.IdOf(FinderKind.SampleSet, setId)));
    }

    [RelayCommand]
    private void SaveGroup()
    {
        Items.SaveGroupCommand.Execute(null);
        var editor = Items.GroupEditor;
        if (editor.BannerIsError)
        {
            Fail(editor.Banner);
            return;
        }

        if (editor.LastSavedId is Guid id)
        {
            var path = Capture();
            if (path.Count == 0 || path[^1].Kind != FinderKind.Group)
                path.Add(FinderStep.IdOf(FinderKind.Group, id));
            else
                path[^1] = FinderStep.IdOf(FinderKind.Group, id);
            _overridePath = path;
        }

        var message = editor.Banner;
        Rebuild();
        Ok(message);
    }

    [RelayCommand]
    private void DeleteGroup()
    {
        var groups = Columns.FirstOrDefault(c => c.ShowGroupFooter);
        if (groups?.Selected?.Step.Kind != FinderKind.Group)
        {
            Fail("Chọn một nhóm trong danh sách rồi mới xóa.");
            return;
        }

        var id = groups.Selected.Step.Id;
        Items.GroupEditor.Selected = Items.GroupEditor.Rows.FirstOrDefault(x => x.Id == id);
        Items.DeleteGroupCommand.Execute(null);
        if (Items.GroupEditor.Rows.Any(x => x.Id == id))
        {
            if (Items.GroupEditor.BannerIsError)
                Fail(Items.GroupEditor.Banner);
            return;
        }

        Rebuild();
    }

    [RelayCommand]
    private void NewGroup()
    {
        Items.NewGroupCommand.Execute(null);
        var groups = Columns.FirstOrDefault(c => c.ShowGroupFooter);
        if (groups is null) return;
        _busy = true;
        groups.Selected = null;
        var index = Columns.IndexOf(groups);
        while (Columns.Count > index + 1)
            Columns.RemoveAt(Columns.Count - 1);
        global::System.Windows.Application.Current.Dispatcher.BeginInvoke(() => _busy = false, DispatcherPriority.ApplicationIdle);
    }

    [RelayCommand]
    private void SaveMissionGroup()
    {
        Missions.SaveGroupCommand.Execute(null);
        if (Missions.BannerIsError)
        {
            Fail(Missions.Banner);
            return;
        }

        if (Missions.LastSavedId is Guid id)
        {
            var path = Capture();
            if (path.Count == 0 || path[^1].Kind != FinderKind.MissionGroup)
                path.Add(FinderStep.IdOf(FinderKind.MissionGroup, id));
            else
                path[^1] = FinderStep.IdOf(FinderKind.MissionGroup, id);
            _overridePath = path;
        }

        var message = Missions.Banner;
        Rebuild();
        Ok(message);
    }

    [RelayCommand]
    private void DeleteMissionGroup()
    {
        var groups = Columns.FirstOrDefault(c => c.ShowMissionGroupFooter);
        if (groups?.Selected?.Step.Kind != FinderKind.MissionGroup)
        {
            Fail("Chọn một nhóm nhiệm vụ rồi mới xóa.");
            return;
        }

        var id = groups.Selected.Step.Id;
        Missions.SelectedGroup = Missions.Groups.FirstOrDefault(x => x.Id == id);
        Missions.DeleteGroupCommand.Execute(null);
        if (Missions.Groups.Any(x => x.Id == id))
        {
            if (Missions.BannerIsError)
                Fail(Missions.Banner);
            return;
        }

        Rebuild();
    }

    [RelayCommand]
    private void NewMissionGroup()
    {
        Missions.NewGroupCommand.Execute(null);
        var groups = Columns.FirstOrDefault(c => c.ShowMissionGroupFooter);
        if (groups is null) return;
        _busy = true;
        groups.Selected = null;
        var index = Columns.IndexOf(groups);
        while (Columns.Count > index + 1)
            Columns.RemoveAt(Columns.Count - 1);
        global::System.Windows.Application.Current.Dispatcher.BeginInvoke(() => _busy = false, DispatcherPriority.ApplicationIdle);
    }

    [RelayCommand]
    private void SaveMissionTask()
    {
        Missions.SaveCommand.Execute(null);
        FinishSave(Missions, FinderKind.MissionTaskNew, FinderKind.MissionTask);
    }

    [RelayCommand] private void DeleteMissionTask() => DeleteRow(Missions.Selected?.Id, Missions, id => Missions.Tasks.Any(x => x.Id == id));
    [RelayCommand] private void NewMissionTask() => StartBlank(FinderKind.MissionTaskNew);

    [RelayCommand]
    private void SaveItem()
    {
        Items.SaveCommand.Execute(null);
        FinishSave(Items, FinderKind.ItemNew, FinderKind.Item);
    }
    [RelayCommand] private void DeleteItem() => DeleteRow(Items.Selected?.Id, Items, id => Items.Rows.Any(x => x.Id == id));
    [RelayCommand] private void NewItem() => StartBlank(FinderKind.ItemNew);

    [RelayCommand]
    private void SaveUnit()
    {
        Items.SaveUnitCommand.Execute(null);
        FinishSave(Items.UnitEditor, FinderKind.UnitNew, FinderKind.Unit);
    }
    [RelayCommand] private void DeleteUnit() => DeleteRow(Items.UnitEditor.Selected?.Id, Items.UnitEditor, id => Items.UnitEditor.Rows.Any(x => x.Id == id));
    [RelayCommand] private void NewUnit() => StartBlank(FinderKind.UnitNew);

    [RelayCommand]
    private void SaveLotType()
    {
        LotTypes.SaveCommand.Execute(null);
        FinishSave(LotTypes, FinderKind.LotTypeNew, FinderKind.LotType);
    }
    [RelayCommand] private void DeleteLotType() => DeleteRow(LotTypes.Selected?.Id, LotTypes, id => LotTypes.Rows.Any(x => x.Id == id));
    [RelayCommand] private void NewLotType() => StartBlank(FinderKind.LotTypeNew);

    [RelayCommand]
    private void SaveWarehouse()
    {
        Warehouses.SaveCommand.Execute(null);
        FinishSave(Warehouses, FinderKind.WarehouseNew, FinderKind.Warehouse);
    }
    [RelayCommand] private void DeleteWarehouse() => DeleteRow(Warehouses.Selected?.Id, Warehouses, id => Warehouses.Rows.Any(x => x.Id == id));
    [RelayCommand] private void NewWarehouse() => StartBlank(FinderKind.WarehouseNew);

    [RelayCommand]
    private void SaveConsumer()
    {
        Consumers.SaveCommand.Execute(null);
        FinishSave(Consumers, FinderKind.ConsumerNew, FinderKind.Consumer);
    }
    [RelayCommand] private void DeleteConsumer() => DeleteRow(Consumers.Selected?.Id, Consumers, id => Consumers.Rows.Any(x => x.Id == id));
    [RelayCommand] private void NewConsumer() => StartBlank(FinderKind.ConsumerNew);

    [RelayCommand]
    private void SaveField()
    {
        Fields.SaveCommand.Execute(null);
        FinishSave(Fields, FinderKind.FieldNew, FinderKind.Field);
    }

    private void FinishSave(PageVm page, FinderKind blank, FinderKind saved)
    {
        if (page.BannerIsError)
        {
            Fail(page.Banner);
            return;
        }

        if (page.LastSavedId is Guid id)
        {
            var path = Capture();
            if (path.Count > 0 && path[^1].Kind == blank)
                path[^1] = FinderStep.IdOf(saved, id);
            _overridePath = path;
        }

        var message = page.Banner;
        Rebuild();
        Ok(message);
    }
    [RelayCommand] private void DeleteField() => DeleteRow(Fields.Selected?.Id, Fields, id => Fields.Rows.Any(x => x.Id == id));
    [RelayCommand] private void NewField() => StartBlank(FinderKind.FieldNew);

    private void DeleteRow(Guid? id, PageVm page, Func<Guid, bool> stillThere)
    {
        if (id is null)
        {
            Fail("Chọn một mục trong danh sách rồi mới xóa.");
            return;
        }

        switch (page)
        {
            case ItemsVm items:
                items.DeleteCommand.Execute(null);
                break;
            case UnitsVm units:
                units.DeleteCommand.Execute(null);
                break;
            case LotTypesVm lotTypes:
                lotTypes.DeleteCommand.Execute(null);
                break;
            case MissionsVm missions:
                missions.DeleteCommand.Execute(null);
                break;
            case WarehousesVm warehouses:
                warehouses.DeleteCommand.Execute(null);
                break;
            case ConsumersVm consumers:
                consumers.DeleteCommand.Execute(null);
                break;
            case FieldsVm fields:
                fields.DeleteCommand.Execute(null);
                break;
        }

        if (stillThere(id.Value))
        {
            if (page.BannerIsError)
                Fail(page.Banner);
            return;
        }

        Rebuild();
    }

    [RelayCommand]
    private void SaveSample()
    {
        var wasNew = Columns.Any(c => c.IsList && c.Selected?.Step.Kind == FinderKind.SampleNew);
        Samples.SaveSetCommand.Execute(null);
        if (Samples.BannerIsError) return;
        if (wasNew && Samples.SelectedSet is { } set)
        {
            _overridePath = Capture()
                .Select(step => step.Kind == FinderKind.SampleNew ? FinderStep.IdOf(FinderKind.SampleSet, set.Id) : step)
                .ToList();
        }

        Rebuild();
    }

    [RelayCommand] private void DeleteSample() { Samples.DeleteSetCommand.Execute(null); Rebuild(); }
    [RelayCommand] private void NewSample() => StartBlank(FinderKind.SampleNew);
}
