using Microsoft.AspNetCore.Identity;

namespace FuelGuard.Infrastructure;

public sealed class FuelGuardUser : IdentityUser
{
    public bool IsActive { get; set; } = true;
}

public sealed class FuelGuardRole : IdentityRole
{
    public FuelGuardRole() { }
    public FuelGuardRole(string roleName) : base(roleName) { }
}
