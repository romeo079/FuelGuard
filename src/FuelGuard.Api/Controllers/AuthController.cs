using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FuelGuard.Contracts;
using FuelGuard.Domain;
using FuelGuard.Infrastructure;
using FuelGuard.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace FuelGuard.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    UserManager<FuelGuardUser> userManager,
    SignInManager<FuelGuardUser> signInManager,
    FuelGuardDbContext db,
    AccessTokenIssuer tokenIssuer,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost("bootstrap-administrator")]
    [AllowAnonymous]
    public async Task<IActionResult> BootstrapAdministrator([FromBody] BootstrapAdministratorRequest request, CancellationToken cancellationToken)
    {
        if (await userManager.Users.AnyAsync(cancellationToken)) return Conflict(new ProblemDetails { Title = "Bootstrap is closed." });
        var expected = configuration["InitialAdmin:BootstrapKey"];
        if (string.IsNullOrWhiteSpace(expected) || !FixedEquals(request.BootstrapKey, expected))
            return NotFound();
        var user = new FuelGuardUser { UserName = request.Email, Email = request.Email, EmailConfirmed = true, IsActive = true };
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(x => x.Description) });
        if (!await db.Roles.AnyAsync(x => x.Name == RoleCatalog.SuperAdministrator, cancellationToken))
            await db.Roles.AddAsync(new FuelGuardRole(RoleCatalog.SuperAdministrator), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await userManager.AddToRoleAsync(user, RoleCatalog.SuperAdministrator);
        return CreatedAtAction(nameof(Profile), new { }, new { message = "Initial administrator created. Remove the bootstrap key from server configuration." });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
            return Unauthorized(new ProblemDetails { Title = "Invalid credentials." });
        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded) return Unauthorized(new ProblemDetails { Title = "Invalid credentials." });
        return Ok(await CreateTokenPair(user, cancellationToken));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var hash = HashToken(request.RefreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash && x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (stored is null) return Unauthorized(new ProblemDetails { Title = "Invalid refresh token." });
        var user = await userManager.FindByIdAsync(stored.UserId);
        if (user is null || !user.IsActive) return Unauthorized(new ProblemDetails { Title = "Invalid refresh token." });
        stored.RevokedAtUtc = DateTimeOffset.UtcNow;
        var response = await CreateTokenPair(user, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(response);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken cancellationToken)
    {
        var hash = HashToken(request.RefreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash && x.RevokedAtUtc == null, cancellationToken);
        if (stored is not null)
        {
            stored.RevokedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }

    [HttpGet("profile")]
    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var user = await userManager.GetUserAsync(User);
        return user is null ? Unauthorized() : Ok(new { user.Id, user.Email, user.IsActive, Roles = await userManager.GetRolesAsync(user), StationIds = User.FindAll("station_id").Select(x => x.Value) });
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(x => x.Description) });
        db.AuditLogs.Add(new AuditLog { ActorUserId = user.Id, Action = "Account.PasswordChanged", EntityType = nameof(FuelGuardUser), EntityId = user.Id, DetailsJson = "{}" });
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<LoginResponse> CreateTokenPair(FuelGuardUser user, CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(user);
        var assignments = await db.UserStationAssignments.AsNoTracking().Where(x => x.UserId == user.Id && x.IsActive).Select(x => x.StationId).ToListAsync(cancellationToken);
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, user.Id), new(JwtRegisteredClaimNames.Email, user.Email ?? ""), new(ClaimTypes.NameIdentifier, user.Id), new(ClaimTypes.Name, user.UserName ?? user.Id) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(assignments.Select(id => new Claim("station_id", id.ToString())));
        var (accessToken, expires) = tokenIssuer.Create(claims);
        var refresh = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        db.RefreshTokens.Add(new RefreshToken { UserId = user.Id, TokenHash = HashToken(refresh), ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(8) });
        await db.SaveChangesAsync(cancellationToken);
        return new LoginResponse(accessToken, refresh, expires);
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool FixedEquals(string supplied, string expected)
    {
        var suppliedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(supplied ?? ""));
        var expectedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }
}

public sealed record BootstrapAdministratorRequest(string BootstrapKey, string Email, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
