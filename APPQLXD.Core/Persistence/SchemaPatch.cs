using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace APPQLXD.Core.Persistence;

internal static class SchemaPatch
{
    public static int Apply(AppDbContext db)
    {
        var added = 0;
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened)
            connection.Open();
        try
        {
            var existing = ReadTables(connection);
            foreach (var statement in Split(db.Database.GenerateCreateScript()))
            {
                var name = TableName(statement);
                if (name is null || existing.Contains(name))
                    continue;
                Execute(connection, statement);
                existing.Add(name);
                added++;
            }

            foreach (var entity in db.Model.GetEntityTypes())
            {
                var table = entity.GetTableName();
                if (table is null || !existing.Contains(table))
                    continue;
                var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
                var have = ReadColumns(connection, table);
                foreach (var property in entity.GetProperties())
                {
                    var column = property.GetColumnName(store);
                    if (string.IsNullOrEmpty(column) || have.Contains(column))
                        continue;
                    var mapping = property.GetRelationalTypeMapping();
                    var defaultLiteral = column == "RollTransfersIntoQuarter"
                        || (table == "ItemGroups" && column == "Scope")
                        ? "1"
                        : table == "Lots" && column == "LotTypeId"
                        || ((table == "ShipQuarterBookLines" || table == "ConsumerQuarterBookLines") && column == "LotTypeId")
                        ? $"'{SeedIds.LotTypeTx}'"
                        : DefaultLiteral(property, mapping.StoreType);
                    var definition = property.IsNullable
                        ? $"{mapping.StoreType} NULL"
                        : $"{mapping.StoreType} NOT NULL DEFAULT {defaultLiteral}";
                    Execute(connection, $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}");
                    have.Add(column);
                    added++;
                }

                foreach (var index in entity.GetIndexes())
                {
                    var indexName = index.GetDatabaseName(store);
                    if (string.IsNullOrEmpty(indexName))
                        continue;
                    var columns = string.Join(", ", index.Properties.Select(x => $"\"{x.GetColumnName(store)}\""));
                    var unique = index.IsUnique ? "UNIQUE " : "";
                    Execute(connection, $"CREATE {unique}INDEX IF NOT EXISTS \"{indexName}\" ON \"{table}\" ({columns})");
                }
            }

            // Old unique key was (ItemNameKey, UnitPrice); new key includes LotTypeId.
            Execute(connection, "DROP INDEX IF EXISTS \"IX_Lots_ItemNameKey_UnitPrice\"");
            if (existing.Contains("Lots"))
            {
                Execute(connection,
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Lots_ItemNameKey_UnitPrice_LotTypeId\" ON \"Lots\" (\"ItemNameKey\", \"UnitPrice\", \"LotTypeId\")");
            }

            // Hạn mức tách TX+SSCĐ / IUU: unique (TaskId, Year, LotView).
            Execute(connection, "DROP INDEX IF EXISTS \"IX_MissionYearLimits_TaskId_Year\"");
            if (existing.Contains("MissionYearLimits"))
            {
                Execute(connection,
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_MissionYearLimits_TaskId_Year_LotView\" ON \"MissionYearLimits\" (\"TaskId\", \"Year\", \"LotView\")");
            }

            added += EnsureLotTypes(connection, existing);
            added += BackfillLotTypeSnapshots(connection, existing);
            added += BackfillShipType(connection, existing);
            added += CleanupUnusedDemoOtherConsumer(connection, existing);
        }
        finally
        {
            if (opened)
                connection.Close();
        }

        return added;
    }

    /// <summary>
    /// Trước đây sổ tàu hiện Mã ở ô Loại tàu. Cột ShipType mới thêm — điền lại từ Code nếu đang trống.
    /// </summary>
    private static int BackfillShipType(System.Data.Common.DbConnection connection, HashSet<string> existing)
    {
        if (!existing.Contains("Consumers"))
            return 0;
        var columns = ReadColumns(connection, "Consumers");
        if (!columns.Contains("ShipType") || !columns.Contains("Code") || !columns.Contains("Type"))
            return 0;
        using var cmd = connection.CreateCommand();
        // ConsumerType.Ship = 4
        cmd.CommandText =
            """
            UPDATE "Consumers"
            SET "ShipType" = "Code"
            WHERE "Type" = 4
              AND ("ShipType" IS NULL OR TRIM("ShipType") = '')
              AND TRIM("Code") != ''
            """;
        return cmd.ExecuteNonQuery();
    }

    private static int CleanupUnusedDemoOtherConsumer(System.Data.Common.DbConnection connection, HashSet<string> existing)
    {
        if (!existing.Contains("Consumers") || !existing.Contains("Warehouses"))
            return 0;
        using var cmd = connection.CreateCommand();
        var stockBalancesFilter = existing.Contains("StockBalances")
            ? "AND \"Id\" NOT IN (SELECT DISTINCT \"WarehouseId\" FROM \"StockBalances\" WHERE \"WarehouseId\" IS NOT NULL)"
            : "";
        cmd.CommandText =
            $"""
            DELETE FROM "Warehouses"
            WHERE (LOWER("Id") = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' OR ("Code" = 'KH-01' AND "Name" = 'Công trình A'))
              AND "Id" NOT IN (SELECT DISTINCT "WarehouseId" FROM "Documents" WHERE "WarehouseId" IS NOT NULL)
              AND "Id" NOT IN (SELECT DISTINCT "DestinationWarehouseId" FROM "Documents" WHERE "DestinationWarehouseId" IS NOT NULL)
              {stockBalancesFilter};

            DELETE FROM "Consumers"
            WHERE (LOWER("Id") = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' OR ("Code" = 'KH-01' AND "Name" = 'Công trình A'))
              AND "Id" NOT IN (SELECT DISTINCT "ConsumerId" FROM "Documents" WHERE "ConsumerId" IS NOT NULL);
            """;
        return cmd.ExecuteNonQuery();
    }

    private static int EnsureLotTypes(System.Data.Common.DbConnection connection, HashSet<string> existing)
    {
        if (!existing.Contains("LotTypes"))
            return 0;
        var seeds = new (Guid Id, string Code, string Name, int Sort)[]
        {
            (SeedIds.LotTypeTx, "TX", "TX", 1),
            (SeedIds.LotTypeSscd, "SSCĐ", "SSCĐ", 2),
            (SeedIds.LotTypeIuu, "IUU", "IUU", 3)
        };
        var added = 0;
        foreach (var seed in seeds)
        {
            using var check = connection.CreateCommand();
            check.CommandText = "SELECT COUNT(1) FROM \"LotTypes\" WHERE \"Id\" = $id OR \"Code\" = $code";
            var idParam = check.CreateParameter();
            idParam.ParameterName = "$id";
            idParam.Value = seed.Id.ToString();
            check.Parameters.Add(idParam);
            var codeParam = check.CreateParameter();
            codeParam.ParameterName = "$code";
            codeParam.Value = seed.Code;
            check.Parameters.Add(codeParam);
            var count = Convert.ToInt32(check.ExecuteScalar());
            if (count > 0)
                continue;
            using var insert = connection.CreateCommand();
            insert.CommandText =
                "INSERT INTO \"LotTypes\" (\"Id\", \"Code\", \"Name\", \"SortOrder\", \"IsActive\") VALUES ($id, $code, $name, $sort, 1)";
            var p1 = insert.CreateParameter();
            p1.ParameterName = "$id";
            p1.Value = seed.Id.ToString();
            insert.Parameters.Add(p1);
            var p2 = insert.CreateParameter();
            p2.ParameterName = "$code";
            p2.Value = seed.Code;
            insert.Parameters.Add(p2);
            var p3 = insert.CreateParameter();
            p3.ParameterName = "$name";
            p3.Value = seed.Name;
            insert.Parameters.Add(p3);
            var p4 = insert.CreateParameter();
            p4.ParameterName = "$sort";
            p4.Value = seed.Sort;
            insert.Parameters.Add(p4);
            insert.ExecuteNonQuery();
            added++;
        }

        return added;
    }

    private static int BackfillLotTypeSnapshots(System.Data.Common.DbConnection connection, HashSet<string> existing)
    {
        var added = 0;
        var tx = SeedIds.LotTypeTx.ToString();
        if (existing.Contains("Lots"))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"UPDATE \"Lots\" SET \"LotTypeId\" = '{tx}' WHERE \"LotTypeId\" IS NULL OR \"LotTypeId\" = '' OR \"LotTypeId\" = '00000000-0000-0000-0000-000000000000'";
            added += cmd.ExecuteNonQuery();
        }

        if (existing.Contains("Documents"))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                $"UPDATE \"Documents\" SET \"LotTypeId\" = '{tx}', \"LotTypeCode\" = 'TX' WHERE (\"LotTypeId\" IS NULL OR \"LotTypeId\" = '' OR \"LotTypeId\" = '00000000-0000-0000-0000-000000000000') AND \"ItemName\" IS NOT NULL AND \"ItemName\" != ''";
            added += cmd.ExecuteNonQuery();
        }

        if (existing.Contains("DocumentLines"))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                $"UPDATE \"DocumentLines\" SET \"LotTypeId\" = (SELECT \"LotTypeId\" FROM \"Lots\" WHERE \"Lots\".\"Id\" = \"DocumentLines\".\"LotId\"), \"LotTypeCode\" = COALESCE((SELECT \"Code\" FROM \"LotTypes\" INNER JOIN \"Lots\" ON \"Lots\".\"LotTypeId\" = \"LotTypes\".\"Id\" WHERE \"Lots\".\"Id\" = \"DocumentLines\".\"LotId\"), 'TX') WHERE \"LotTypeId\" IS NULL OR \"LotTypeId\" = '' OR \"LotTypeId\" = '00000000-0000-0000-0000-000000000000'";
            added += cmd.ExecuteNonQuery();
        }

        return added;
    }

    private static void Execute(System.Data.Common.DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static HashSet<string> ReadTables(System.Data.Common.DbConnection connection)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
    }

    private static HashSet<string> ReadColumns(System.Data.Common.DbConnection connection, string table)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            names.Add(reader.GetString(1));
        return names;
    }

    private static IEnumerable<string> Split(string script)
    {
        var current = new List<string>();
        foreach (var raw in script.Split('\n'))
        {
            current.Add(raw);
            if (raw.TrimEnd().EndsWith(';'))
            {
                var statement = string.Join('\n', current).Trim();
                current.Clear();
                if (statement.Length > 0)
                    yield return statement;
            }
        }
    }

    private static string? TableName(string statement)
    {
        var match = Regex.Match(statement, "^CREATE TABLE\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string DefaultLiteral(IProperty property, string storeType)
    {
        var clr = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        var numeric = storeType.StartsWith("INT", StringComparison.OrdinalIgnoreCase)
            || storeType.StartsWith("REAL", StringComparison.OrdinalIgnoreCase)
            || storeType.StartsWith("NUM", StringComparison.OrdinalIgnoreCase);
        if (clr == typeof(bool) || clr.IsEnum || clr == typeof(int) || clr == typeof(long) || clr == typeof(short) || clr == typeof(byte))
            return "0";
        if (clr == typeof(decimal) || clr == typeof(double) || clr == typeof(float))
            return numeric ? "0" : "'0'";
        if (clr == typeof(Guid))
            return "'00000000-0000-0000-0000-000000000000'";
        if (clr == typeof(DateTime) || clr == typeof(DateTimeOffset))
            return "'0001-01-01 00:00:00'";
        return numeric ? "0" : "''";
    }
}
