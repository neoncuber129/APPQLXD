using APPQLXD.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace APPQLXD.Core.Persistence;

public sealed class AppDbContext : DbContext
{
    private readonly string _connectionString;

    public AppDbContext(string connectionString) => _connectionString = connectionString;

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<ItemGroup> ItemGroups => Set<ItemGroup>();
    public DbSet<MeasureUnit> MeasureUnits => Set<MeasureUnit>();
    public DbSet<LotType> LotTypes => Set<LotType>();
    public DbSet<LotOrigin> LotOrigins => Set<LotOrigin>();
    public DbSet<FuelItem> FuelItems => Set<FuelItem>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Consumer> Consumers => Set<Consumer>();
    public DbSet<ConsumerNormFactor> ConsumerNormFactors => Set<ConsumerNormFactor>();
    public DbSet<FieldDefinition> FieldDefinitions => Set<FieldDefinition>();
    public DbSet<SampleSet> SampleSets => Set<SampleSet>();
    public DbSet<SampleValue> SampleValues => Set<SampleValue>();
    public DbSet<Lot> Lots => Set<Lot>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<FuelDocument> Documents => Set<FuelDocument>();
    public DbSet<FuelDocumentLine> DocumentLines => Set<FuelDocumentLine>();
    public DbSet<FuelDocumentField> DocumentFields => Set<FuelDocumentField>();
    public DbSet<ShipQuarterBook> ShipQuarterBooks => Set<ShipQuarterBook>();
    public DbSet<ShipQuarterBookLine> ShipQuarterBookLines => Set<ShipQuarterBookLine>();
    public DbSet<ConsumerQuarterBook> ConsumerQuarterBooks => Set<ConsumerQuarterBook>();
    public DbSet<ConsumerQuarterBookLine> ConsumerQuarterBookLines => Set<ConsumerQuarterBookLine>();
    public DbSet<MissionGroup> MissionGroups => Set<MissionGroup>();
    public DbSet<MissionTask> MissionTasks => Set<MissionTask>();
    public DbSet<MissionYearLimit> MissionYearLimits => Set<MissionYearLimit>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(_connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSetting>().HasKey(x => x.Key);
        modelBuilder.Entity<AppSetting>().ToTable("AppSettings");
        modelBuilder.Entity<ItemGroup>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<MeasureUnit>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<LotType>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<LotOrigin>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<FuelItem>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<FuelItem>().HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<FuelItem>().HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Lot>().HasOne(x => x.LotType).WithMany().HasForeignKey(x => x.LotTypeId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Warehouse>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<Warehouse>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<Consumer>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<ConsumerNormFactor>().HasOne(x => x.Consumer).WithMany(x => x.NormFactors).HasForeignKey(x => x.ConsumerId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ConsumerNormFactor>().HasIndex(x => new { x.ConsumerId, x.SortOrder });
        modelBuilder.Entity<ConsumerNormFactor>().HasIndex(x => new { x.ConsumerId, x.GroupId });
        modelBuilder.Entity<ShipQuarterBook>().HasIndex(x => new { x.ConsumerId, x.Year, x.Quarter }).IsUnique();
        modelBuilder.Entity<ShipQuarterBookLine>().HasOne(x => x.Book).WithMany(x => x.Lines).HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ShipQuarterBookLine>().HasIndex(x => new { x.BookId, x.SortOrder });
        modelBuilder.Entity<ConsumerQuarterBook>().HasIndex(x => new { x.ConsumerId, x.Year, x.Quarter }).IsUnique();
        modelBuilder.Entity<ConsumerQuarterBookLine>().HasOne(x => x.Book).WithMany(x => x.Lines).HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ConsumerQuarterBookLine>().HasIndex(x => new { x.BookId, x.SortOrder });
        modelBuilder.Entity<MissionGroup>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<MissionTask>().HasOne(x => x.Group).WithMany(x => x.Tasks).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<MissionTask>().HasIndex(x => new { x.GroupId, x.SortOrder });
        modelBuilder.Entity<MissionYearLimit>().HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<MissionYearLimit>().HasIndex(x => new { x.TaskId, x.Year, x.LotView }).IsUnique();
        modelBuilder.Entity<FieldDefinition>().HasIndex(x => new { x.Family, x.Name }).IsUnique();
        modelBuilder.Entity<SampleValue>().HasOne(x => x.SampleSet).WithMany(x => x.Values).HasForeignKey(x => x.SampleSetId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SampleValue>().HasOne(x => x.Field).WithMany().HasForeignKey(x => x.FieldDefinitionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Lot>().HasIndex(x => new { x.ItemNameKey, x.UnitPrice, x.LotTypeId, x.Origin }).IsUnique();
        modelBuilder.Entity<StockBalance>().HasIndex(x => new { x.LotId, x.WarehouseId }).IsUnique();
        modelBuilder.Entity<StockBalance>().HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<StockBalance>().HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<FuelDocument>().HasIndex(x => x.Number).IsUnique();
        modelBuilder.Entity<FuelDocument>().HasIndex(x => x.Sequence).IsUnique();
        modelBuilder.Entity<FuelDocument>().HasIndex(x => new { x.Status, x.DocumentDate });
        modelBuilder.Entity<FuelDocument>().HasIndex(x => new { x.Kind, x.DocumentDate });
        modelBuilder.Entity<FuelDocument>().HasIndex(x => x.DestinationWarehouseId);
        modelBuilder.Entity<FuelDocument>().HasIndex(x => new { x.DestinationWarehouseId, x.DocumentDate });
        modelBuilder.Entity<FuelDocumentLine>().HasOne(x => x.Document).WithMany(x => x.Lines).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FuelDocumentLine>().HasIndex(x => x.WarehouseId);
        modelBuilder.Entity<FuelDocumentLine>().HasIndex(x => new { x.WarehouseId, x.DocumentId });
        modelBuilder.Entity<FuelDocumentField>().HasOne(x => x.Document).WithMany(x => x.Fields).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}
