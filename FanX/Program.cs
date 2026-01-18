using FanX.Services;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor.Services;
using ApexCharts;
using System.IO; // added for Path

LoggerService.Info("Application starting up...");

var builder = WebApplication.CreateBuilder(args);

// Add MudBlazor services
builder.Services.AddMudServices();
builder.Services.AddApexCharts();

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Add Authentication and Authorization
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

// Add Custom services
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<AppSettingService>();
builder.Services.AddSingleton<IpmiService>();
builder.Services.AddSingleton<SensorDataService>();
builder.Services.AddScoped<IpmiConfigService>();
builder.Services.AddScoped<FanControlService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<BrowserStorageService>();
builder.Services.AddScoped<LocalizationService>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthenticationStateProvider>();
builder.Services.AddHostedService<SensorLoggingService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddHostedService<MaintenanceService>();

// static file serving configuration
if (builder.Environment.IsDevelopment())
{
    // Use static assets from project during development
    builder.WebHost.UseStaticWebAssets();
}
else
{
    // In production/published deployment, ensure wwwroot is served from the output directory
    var contentRoot = AppContext.BaseDirectory;
    builder.WebHost
        .UseContentRoot(contentRoot)
        .UseWebRoot(Path.Combine(contentRoot, "wwwroot"));
}

// Add Http client factory
builder.Services.AddHttpClient();

// Binding port from environment variable or default to 5136
var port = Environment.GetEnvironmentVariable("FanX_PORT") ?? "5136";
// Configure Kestrel to listen on the specified port
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(int.Parse(port), listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
    });
});


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Only enable HTTPS redirection in development mode
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();
// Endpoint to download log files by relative path
app.MapGet("/download-log/{**filePath}", (string filePath) => {
    var logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
    // Convert URL path to OS path
    var relative = filePath.Replace("/", Path.DirectorySeparatorChar.ToString());
    var fullPath = Path.GetFullPath(Path.Combine(logsDir, relative));
    // Prevent directory traversal
    if (!fullPath.StartsWith(logsDir)) return Results.BadRequest();
    if (!File.Exists(fullPath)) return Results.NotFound();
    var fileName = Path.GetFileName(fullPath);
    return Results.File(fullPath, "application/octet-stream", fileName);
});
app.MapFallbackToPage("/_Host");

try
{
    app.Run();
}
catch (Exception ex)
{
    LoggerService.Fatal("Application terminated unexpectedly", ex);
}
finally
{
    LoggerService.Info("Application shutting down.");
    log4net.LogManager.Shutdown();
}