using FMB.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FMB.Infrastructure.Tests;

public class DatabaseConnectionTests
{
    [Theory]
    [InlineData("Data Source=fmb_local.db", false)]
    [InlineData("Server=(localdb)\\MSSQLLocalDB;Database=FMB_TiffinTracker;Trusted_Connection=True;", true)]
    [InlineData("Server=sqlserver;Database=FMB_TiffinTracker;User Id=sa;Password=pass;TrustServerCertificate=True;", true)]
    public void IsSqlServerConnectionString_ReturnsExpectedValue(string connectionString, bool expected)
    {
        var result = DatabaseConnectionSettings.IsSqlServerConnectionString(connectionString);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ConfigureDbContext_ResolvesBothHostsToTheSameSqliteFile()
    {
        var mvcRoot = Path.Combine(Path.GetTempPath(), "fmb-tracker", "src", "FMB.WebMVC");
        var apiRoot = Path.Combine(Path.GetTempPath(), "fmb-tracker", "src", "FMB.WebAPI");
        var mvcDatabase = GetDataSource("Data Source=fmb_local.db", mvcRoot);
        var apiDatabase = GetDataSource("Data Source=../FMB.WebMVC/fmb_local.db", apiRoot);

        Assert.Equal(mvcDatabase, apiDatabase);
    }

    private static string GetDataSource(string connectionString, string contentRootPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString
            })
            .Build();
        var services = new ServiceCollection();
        DatabaseConnectionSettings.ConfigureDbContext(services, configuration, contentRootPath);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TiffinTrackerDbContext>();
        return context.Database.GetDbConnection().DataSource;
    }
}
