using System.Text;
using FuelGuard.Api.Middleware;
using FuelGuard.Api.Security;
using FuelGuard.Hardware.Abstractions;
using FuelGuard.Hardware.Simulator;
using FuelGuard.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "ROMVEX FuelGuard API",
        Version = "v1",
        Description = "Fuel and exit authorisation API for ROMVEX FuelGuard stations."
    });

    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT bearer token issued by the FuelGuard auth endpoint."
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var connectionString = builder.Configuration.GetConnectionString("FuelGuard") ??
    "Server=(localdb)\\MSSQLLocalDB;Database=FuelGuard;Trusted_Connection=True;TrustServerCertificate=True;";

builder.Services.AddFuelGuardInfrastructure(builder.Configuration);
builder.Services.AddDbContext<FuelGuardDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddScoped<AccessTokenIssuer>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    var issuer = builder.Configuration["Jwt:Issuer"] ?? "ROMVEX.FuelGuard";
    var audience = builder.Configuration["Jwt:Audience"] ?? "ROMVEX.FuelGuard.Clients";
    var signingKeyValue = builder.Configuration["Jwt:SigningKey"] ?? "ROMVEX_FUELGUARD_SIGNING_KEY_2026_CHANGE_ME_!";

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKeyValue)),
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    options.Events = new JwtBearerEvents
    {
        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return context.Response.WriteAsJsonAsync(new { title = "Authentication required." });
        }
    };
});

builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();

if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddSingleton<SimulatedReceiptPrinterService>();
    builder.Services.AddSingleton<SimulatedExitScannerService>();
    builder.Services.AddSingleton<SimulatedAnprService>();
    builder.Services.AddSingleton<SimulatedVehicleDetectionService>();
    builder.Services.AddSingleton<SimulatedAccessControllerService>();
    builder.Services.AddSingleton<SimulatedPosIntegrationService>();
    builder.Services.AddSingleton<SimulatedHardwareHealthService>();

    builder.Services.AddScoped<IReceiptPrinterService>(sp => sp.GetRequiredService<SimulatedReceiptPrinterService>());
    builder.Services.AddScoped<IExitScannerService>(sp => sp.GetRequiredService<SimulatedExitScannerService>());
    builder.Services.AddScoped<IANPRService>(sp => sp.GetRequiredService<SimulatedAnprService>());
    builder.Services.AddScoped<IVehicleDetectionService>(sp => sp.GetRequiredService<SimulatedVehicleDetectionService>());
    builder.Services.AddScoped<IAccessControllerService>(sp => sp.GetRequiredService<SimulatedAccessControllerService>());
    builder.Services.AddScoped<IPOSIntegrationService>(sp => sp.GetRequiredService<SimulatedPosIntegrationService>());
    builder.Services.AddScoped<ISimulatedPOSIntegrationService>(sp => sp.GetRequiredService<SimulatedPosIntegrationService>());
    builder.Services.AddScoped<IHardwareHealthService>(sp => sp.GetRequiredService<SimulatedHardwareHealthService>());
}
else
{
    builder.Services.AddScoped<IReceiptPrinterService, UnsupportedHardwareAdapter>();
    builder.Services.AddScoped<IExitScannerService, UnsupportedHardwareAdapter>();
    builder.Services.AddScoped<IANPRService, UnsupportedHardwareAdapter>();
    builder.Services.AddScoped<IVehicleDetectionService, UnsupportedHardwareAdapter>();
    builder.Services.AddScoped<IAccessControllerService, UnsupportedHardwareAdapter>();
    builder.Services.AddScoped<IPOSIntegrationService, UnsupportedHardwareAdapter>();
    builder.Services.AddScoped<IHardwareHealthService, UnsupportedHardwareAdapter>();
}

var app = builder.Build();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ProblemDetailsExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
