using FMB.Application.Interfaces;
using FMB.Application.Queries;
using FMB.Infrastructure.Persistence;
using FMB.WebMVC.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});
DatabaseConnectionSettings.ConfigureDbContext(
    builder.Services,
    builder.Configuration,
    builder.Environment.ContentRootPath);

builder.Services.AddScoped<ITiffinTrackerDbContext>(provider =>
    provider.GetRequiredService<TiffinTrackerDbContext>());

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(GetDashboardStatsQuery).Assembly));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TiffinTrackerDbContext>();
    await FMB.Infrastructure.Persistence.DbInitializer.SeedAsync(db, app.Environment.IsDevelopment());
    if (app.Environment.IsDevelopment())
    {
        await DevelopmentUserSeeder.SeedAsync(db);
    }
}

app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlationId)
        || correlationId.Length > 100
        || correlationId.Any(character => !char.IsLetterOrDigit(character) && character != '-' && character != '_'))
    {
        correlationId = Guid.NewGuid().ToString("N");
    }

    context.TraceIdentifier = correlationId;
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    using (context.RequestServices.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Correlation")
        .BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
    {
        await next();
    }
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
