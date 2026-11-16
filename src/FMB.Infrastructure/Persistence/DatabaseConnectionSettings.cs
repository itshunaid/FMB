using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace FMB.Infrastructure.Persistence;

public static class DatabaseConnectionSettings
{
    public static bool IsSqlServerConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        return connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("User ID=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("User Id=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Trusted_Connection=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Integrated Security=", StringComparison.OrdinalIgnoreCase);
    }

    public static void ConfigureDbContext(
        IServiceCollection services,
        IConfiguration configuration,
        string? contentRootPath = null)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=fmb_local.db";

        if (IsSqlServerConnectionString(connectionString))
        {
            services.AddDbContext<TiffinTrackerDbContext>(options =>
                options.UseSqlServer(connectionString));
            return;
        }

        if (!string.IsNullOrWhiteSpace(contentRootPath))
        {
            var sqliteConnection = new SqliteConnectionStringBuilder(connectionString);
            if (!string.IsNullOrWhiteSpace(sqliteConnection.DataSource)
                && !string.Equals(sqliteConnection.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
                && !Path.IsPathRooted(sqliteConnection.DataSource)
                && !sqliteConnection.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                sqliteConnection.DataSource = Path.GetFullPath(sqliteConnection.DataSource, contentRootPath);
                connectionString = sqliteConnection.ConnectionString;
            }
        }

        services.AddDbContext<TiffinTrackerDbContext>(options =>
            options.UseSqlite(connectionString));
    }
}
