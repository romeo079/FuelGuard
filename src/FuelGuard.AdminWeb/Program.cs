using FuelGuard.AdminWeb.Components;
using FuelGuard.Hardware.Simulator;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSingleton<SimulatedPosIntegrationService>();
builder.Services.AddSingleton(_ => new SimulatedExitScannerService { IsOnline = true });
builder.Services.AddSingleton<IFuelGuardSimulatorDemoService>(services =>
{
    var accounts = builder.Environment.IsDevelopment()
        ? builder.Configuration.GetSection("Simulator:DemoAccounts").Get<List<SimulatorDemoAccount>>() ?? []
        : [];
    return new FuelGuardSimulatorDemoService(
        accounts,
        services.GetRequiredService<SimulatedPosIntegrationService>(),
        services.GetRequiredService<SimulatedExitScannerService>(),
        TimeProvider.System,
        builder.Environment.IsDevelopment());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
