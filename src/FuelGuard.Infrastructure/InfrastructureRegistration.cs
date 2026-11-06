using FuelGuard.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FuelGuard.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddFuelGuardInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FuelGuard");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:FuelGuard must be configured using an environment variable or development user secrets.");

        services.AddDbContext<FuelGuardDbContext>(options => options.UseSqlServer(connectionString));
        services.AddIdentityCore<FuelGuardUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.SignIn.RequireConfirmedEmail = false;
        })
            .AddRoles<FuelGuardRole>()
            .AddEntityFrameworkStores<FuelGuardDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        services.AddDataProtection().SetApplicationName("ROMVEX.FuelGuard");
        services.AddScoped<IStationAccessService, StationAccessService>();
        services.AddScoped<IFuelGuardWorkflowService, FuelGuardWorkflowService>();
        return services;
    }
}

public sealed class StationAccessService(FuelGuardDbContext db) : IStationAccessService
{
    public Task<bool> HasStationAccessAsync(string userId, Guid stationId, CancellationToken cancellationToken) =>
        db.UserStationAssignments.AnyAsync(x => x.UserId == userId && x.StationId == stationId && x.IsActive, cancellationToken);
}
