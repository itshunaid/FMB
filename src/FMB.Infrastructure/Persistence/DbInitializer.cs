using FMB.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FMB.Infrastructure.Persistence
{
    public static class DbInitializer
    {
        public static async Task SeedAsync(TiffinTrackerDbContext context, bool seedDevelopmentData = true)
        {
            await context.Database.EnsureCreatedAsync();
            await EnsureOperationalTablesAsync(context);
            await EnsureRoleDefinitionTableAsync(context);
            await EnsureHouseholdLoginColumnsAsync(context);
            await EnsureDailyCollectionUniquenessAsync(context);

            if (!seedDevelopmentData)
            {
                return;
            }

            await PrototypeDemoDataSeeder.SeedAsync(context);

            if (!context.Distributors.Any())
            {
                context.Distributors.AddRange(
                    new Distributor { Id = "DST-01", Name = "Shabbir Bhai", Phone = "+919849011223", Status = "Active" },
                    new Distributor { Id = "DST-02", Name = "Murtaza Ali", Phone = "+919849044556", Status = "Active" }
                );
                await context.SaveChangesAsync();
            }

            if (!context.Households.Any())
            {
                context.Households.AddRange(
                    new Household { Id = "30451201", ThaliNumber = 1, Name = "Mustafa Saifuddin", Address = "Hussaini Alam Lane 1", Zone = "North", Phone = "+919000011111", Size = 4, Status = "Active", DistributorId = "DST-01", TagId = "RFID-1001" },
                    new Household { Id = "30400007", ThaliNumber = 2, Name = "Taher Abbas", Address = "Hussaini Alam Main Rd", Zone = "South", Phone = "+919000022222", Size = 5, Status = "Active", DistributorId = "DST-01", TagId = "RFID-1002" },
                    new Household { Id = "30451203", ThaliNumber = 3, Name = "Burhanuddin Hakim", Address = "Pathergatti Cross", Zone = "East", Phone = "+919000033333", Size = 3, Status = "On Hold", DistributorId = "DST-02", TagId = "RFID-1003" }
                );
                await context.SaveChangesAsync();
            }

            if (!context.DailyMenus.Any())
            {
                context.DailyMenus.Add(new DailyMenu
                {
                    Date = DateTime.Today,
                    MenuName = "Chicken Biryani"
                });
                await context.SaveChangesAsync();
            }
        }

        private static Task<int> EnsureOperationalTablesAsync(TiffinTrackerDbContext context)
        {
            const string sqliteSql = """
                CREATE TABLE IF NOT EXISTS "FeedbackRecords" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_FeedbackRecords" PRIMARY KEY AUTOINCREMENT,
                    "Date" TEXT NOT NULL,
                    "MenuName" TEXT NOT NULL,
                    "ItsId" TEXT NOT NULL,
                    "MemberName" TEXT NOT NULL,
                    "Zone" TEXT NOT NULL,
                    "Rating" INTEGER NOT NULL,
                    "Quantity" TEXT NOT NULL,
                    "Quality" TEXT NOT NULL,
                    "Comment" TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS "SystemUsers" (
                    "ItsId" TEXT NOT NULL CONSTRAINT "PK_SystemUsers" PRIMARY KEY,
                    "Name" TEXT NOT NULL,
                    "Role" TEXT NOT NULL,
                    "Phone" TEXT NOT NULL,
                    "Status" TEXT NOT NULL,
                    "PinHash" TEXT NOT NULL,
                    "LastLogin" TEXT NULL
                );
                """;
            const string sqlServerSql = """
                IF OBJECT_ID(N'[dbo].[FeedbackRecords]', N'U') IS NULL
                CREATE TABLE [dbo].[FeedbackRecords] (
                    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_FeedbackRecords] PRIMARY KEY,
                    [Date] datetime2 NOT NULL,
                    [MenuName] nvarchar(200) NOT NULL,
                    [ItsId] nvarchar(20) NOT NULL,
                    [MemberName] nvarchar(150) NOT NULL,
                    [Zone] nvarchar(100) NOT NULL,
                    [Rating] int NOT NULL,
                    [Quantity] nvarchar(30) NOT NULL,
                    [Quality] nvarchar(30) NOT NULL,
                    [Comment] nvarchar(1000) NOT NULL
                );
                IF OBJECT_ID(N'[dbo].[SystemUsers]', N'U') IS NULL
                CREATE TABLE [dbo].[SystemUsers] (
                    [ItsId] nvarchar(20) NOT NULL CONSTRAINT [PK_SystemUsers] PRIMARY KEY,
                    [Name] nvarchar(150) NOT NULL,
                    [Role] nvarchar(50) NOT NULL,
                    [Phone] nvarchar(30) NOT NULL,
                    [Status] nvarchar(20) NOT NULL,
                    [PinHash] nvarchar(500) NOT NULL,
                    [LastLogin] datetime2 NULL
                );
                """;

            var sql = context.Database.IsSqlite() ? sqliteSql : sqlServerSql;
            return context.Database.ExecuteSqlRawAsync(sql);
        }

        private static Task<int> EnsureRoleDefinitionTableAsync(TiffinTrackerDbContext context)
        {
            const string sqliteSql = """
                CREATE TABLE IF NOT EXISTS "RoleDefinitions" (
                    "Name" TEXT NOT NULL CONSTRAINT "PK_RoleDefinitions" PRIMARY KEY,
                    "PermissionsJson" TEXT NOT NULL,
                    "DashboardWidgetsJson" TEXT NOT NULL
                );
                """;
            const string sqlServerSql = """
                IF OBJECT_ID(N'[dbo].[RoleDefinitions]', N'U') IS NULL
                CREATE TABLE [dbo].[RoleDefinitions] (
                    [Name] nvarchar(50) NOT NULL CONSTRAINT [PK_RoleDefinitions] PRIMARY KEY,
                    [PermissionsJson] nvarchar(max) NOT NULL,
                    [DashboardWidgetsJson] nvarchar(max) NOT NULL
                );
                """;

            return context.Database.ExecuteSqlRawAsync(context.Database.IsSqlite() ? sqliteSql : sqlServerSql);
        }

        private static Task<int> EnsureDailyCollectionUniquenessAsync(TiffinTrackerDbContext context)
        {
            const string sqliteIndexSql = """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_TiffinLogs_Date_HouseholdId"
            ON "TiffinLogs" ("Date", "HouseholdId");
            """;
            const string sqlServerIndexSql = """
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_TiffinLogs_Date_HouseholdId'
                  AND object_id = OBJECT_ID(N'[dbo].[TiffinLogs]'))
            CREATE UNIQUE INDEX [IX_TiffinLogs_Date_HouseholdId]
            ON [dbo].[TiffinLogs] ([Date], [HouseholdId]);
            """;

            var sql = context.Database.IsSqlite() ? sqliteIndexSql : sqlServerIndexSql;
            return context.Database.ExecuteSqlRawAsync(sql);
        }

        private static async Task EnsureHouseholdLoginColumnsAsync(TiffinTrackerDbContext context)
        {
            if (!context.Database.IsSqlite())
            {
                await context.Database.ExecuteSqlRawAsync("""
                    IF COL_LENGTH(N'dbo.Households', N'PasswordHash') IS NULL
                    ALTER TABLE [dbo].[Households] ADD [PasswordHash] nvarchar(500) NULL;
                    IF COL_LENGTH(N'dbo.Households', N'ThaliNumber') IS NULL
                    ALTER TABLE [dbo].[Households] ADD [ThaliNumber] int NOT NULL
                        CONSTRAINT [DF_Households_ThaliNumber] DEFAULT 0;
                    """);
            }
            else
            {
                var connection = context.Database.GetDbConnection();
                var closeConnection = connection.State != System.Data.ConnectionState.Open;
                if (closeConnection)
                {
                    await connection.OpenAsync();
                }

                try
                {
                    var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    await using var command = connection.CreateCommand();
                    command.CommandText = "PRAGMA table_info(\"Households\");";
                    await using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            columns.Add(reader.GetString(1));
                        }
                    }

                    if (!columns.Contains("PasswordHash"))
                    {
                        command.CommandText = "ALTER TABLE \"Households\" ADD COLUMN \"PasswordHash\" TEXT NULL;";
                        await command.ExecuteNonQueryAsync();
                    }
                    if (!columns.Contains("ThaliNumber"))
                    {
                        command.CommandText = "ALTER TABLE \"Households\" ADD COLUMN \"ThaliNumber\" INTEGER NOT NULL DEFAULT 0;";
                        await command.ExecuteNonQueryAsync();
                    }
                }
                finally
                {
                    if (closeConnection)
                    {
                        await connection.CloseAsync();
                    }
                }
            }

            foreach (var entry in context.ChangeTracker.Entries<Household>().ToList())
            {
                entry.State = EntityState.Detached;
            }

            var households = await context.Households.OrderBy(item => item.Id).ToListAsync();
            var assignedNumbers = households.Where(item => item.ThaliNumber > 0)
                .Select(item => item.ThaliNumber)
                .ToHashSet();
            var nextNumber = 1;
            foreach (var household in households.Where(item => item.ThaliNumber <= 0))
            {
                while (assignedNumbers.Contains(nextNumber))
                {
                    nextNumber++;
                }
                household.ThaliNumber = nextNumber++;
                assignedNumbers.Add(household.ThaliNumber);
            }
            if (context.ChangeTracker.HasChanges())
            {
                await context.SaveChangesAsync();
            }

            var indexSql = context.Database.IsSqlite()
                ? """
                  CREATE UNIQUE INDEX IF NOT EXISTS "IX_Households_ThaliNumber"
                  ON "Households" ("ThaliNumber");
                  """
                : """
                  IF NOT EXISTS (
                      SELECT 1 FROM sys.indexes
                      WHERE name = N'IX_Households_ThaliNumber'
                        AND object_id = OBJECT_ID(N'[dbo].[Households]'))
                  CREATE UNIQUE INDEX [IX_Households_ThaliNumber]
                  ON [dbo].[Households] ([ThaliNumber]);
                  """;
            await context.Database.ExecuteSqlRawAsync(indexSql);
        }
    }
}
