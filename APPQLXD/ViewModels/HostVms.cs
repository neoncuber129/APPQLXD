using System.Collections.ObjectModel;
using APPQLXD.Core;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace APPQLXD.ViewModels;

public partial class CatalogHostVm : PageVm
{
    private bool _finderReady;
    private int _seenCatalogGeneration = int.MinValue;
    private WarehouseScope? _seenScope;

    public CatalogHostVm(FuelSystem system) : base(system)
    {
        Items = new ItemsVm(system);
        LotTypes = new LotTypesVm(system);
        Warehouses = new WarehousesVm(system);
        Consumers = new ConsumersVm(system);
        Missions = new MissionsVm(system);
        Fields = new FieldsVm(system);
        Samples = new SamplesVm(system);
        Fields.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FieldsVm.PaperOnly))
                OnPaperFilter();
        };
        // Không đánh dấu ready ở đây — lần Refresh/Rebuild đầu mới nạp dữ liệu danh mục.
    }

    public ItemsVm Items { get; }
    public LotTypesVm LotTypes { get; }
    public WarehousesVm Warehouses { get; }
    public ConsumersVm Consumers { get; }
    public MissionsVm Missions { get; }
    public FieldsVm Fields { get; }
    public SamplesVm Samples { get; }
    public ObservableCollection<FinderColumnVm> Columns { get; } = [];
    public event Action? ColumnsBuilt;

    public override void Refresh()
    {
        var generation = System.CatalogGeneration;
        var scope = System.GetWarehouseScope();
        if (_finderReady && Columns.Count > 0
            && _seenCatalogGeneration == generation
            && _seenScope == scope)
            return;
        _seenCatalogGeneration = generation;
        _seenScope = scope;
        Rebuild();
    }

    public override Task RefreshAsync(IProgress<DemoProgress>? progress = null)
    {
        Refresh();
        return Task.CompletedTask;
    }

    internal void InvalidateFinder() => _finderReady = false;
}

public partial class OpsHostVm : PageVm
{
    public OpsHostVm(FuelSystem system, HistoryVm history) : base(system)
    {
        Stock = new StockVm(system);
        History = history;
        Nxt = new NxtVm(system);
        NxtTotal = new NxtTotalVm(system);
        NxtWarehouse = new NxtTotalVm(system, perWarehouse: true);
        Quota = new QuotaVm(system);
    }

    public StockVm Stock { get; }
    public HistoryVm History { get; }
    public NxtVm Nxt { get; }
    public NxtTotalVm NxtTotal { get; }
    public NxtTotalVm NxtWarehouse { get; }
    public QuotaVm Quota { get; }
    [ObservableProperty] private int _selectedIndex;

    public Func<string, Func<IProgress<DemoProgress>, Task>, Task>? RunBusy { get; set; }

    public override void Refresh() => Current().Refresh();

    public override Task RefreshAsync(IProgress<DemoProgress>? progress = null) => Current().RefreshAsync(progress);

    partial void OnSelectedIndexChanged(int value)
    {
        var page = Current();
        var title = page switch
        {
            NxtTotalVm total => total.PageTitle,
            QuotaVm => "Hạn mức",
            _ => "Sổ NXT"
        };
        if (RunBusy is { } run)
            _ = run(title, page.RefreshAsync);
        else
            page.Refresh();
    }

    private PageVm Current() => SelectedIndex switch
    {
        1 => NxtTotal,
        2 => NxtWarehouse,
        3 => Quota,
        _ => Nxt
    };
}
