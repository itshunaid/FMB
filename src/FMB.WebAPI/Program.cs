using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using FMB.Application.Commands;
using FMB.Application.Interfaces;
using FMB.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
var gateApiKey = builder.Configuration["Gate:ApiKey"];
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(gateApiKey))
{
    throw new InvalidOperationException("Gate:ApiKey must be configured outside Development.");
}
var configuredGateApiKey = Encoding.UTF8.GetBytes(gateApiKey ?? string.Empty);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});
builder.Services.AddProblemDetails();
DatabaseConnectionSettings.ConfigureDbContext(
    builder.Services,
    builder.Configuration,
    builder.Environment.ContentRootPath);

builder.Services.AddScoped<ITiffinTrackerDbContext>(provider =>
    provider.GetRequiredService<TiffinTrackerDbContext>());

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(ScanTiffinCommand).Assembly));

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

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
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api/gate")
        || app.Environment.IsDevelopment())
    {
        await next();
        return;
    }

    var suppliedKey = Encoding.UTF8.GetBytes(context.Request.Headers["X-Gate-Api-Key"].ToString());
    if (suppliedKey.Length != configuredGateApiKey.Length
        || !CryptographicOperations.FixedTimeEquals(suppliedKey, configuredGateApiKey))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "Unauthorized",
                    Detail = "A valid gate API key is required."
                }
            });
        return;
    }

    await next();
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TiffinTrackerDbContext>();
    await DbInitializer.SeedAsync(db, app.Environment.IsDevelopment());
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

app.Run();
