using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FuelGuard.Infrastructure;

public sealed class FuelGuardDesignTimeDbContextFactory : IDesignTimeDbContextFactory<FuelGuardDbContext>
{
    public FuelGuardDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__FuelGuard")
            ?? "Server=localhost;Database=ROMVEX_FuelGuard_DB;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<FuelGuardDbContext>().UseSqlServer(connectionString).Options;
        return new FuelGuardDbContext(options);
    }
}
