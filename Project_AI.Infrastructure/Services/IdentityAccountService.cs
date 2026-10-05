using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Auth;
using Project_AI.Application.Interfaces;
using Project_AI.Domain.Constants;
using Project_AI.Infrastructure.Data;
using Project_AI.Infrastructure.Data.Identity;

namespace Project_AI.Infrastructure.Services;

public sealed class IdentityAccountService(UserManager<ApplicationUser> users, AuthDbContext db, TimeProvider clock)
    : IIdentityAccounts
{
    private static readonly ApplicationUser DummyUser = new();
    private static readonly PasswordHasher<ApplicationUser> DummyHasher = new();
    private static readonly string DummyHash = DummyHasher.HashPassword(DummyUser, Guid.NewGuid().ToString());

    public async Task<AuthAccount?> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        // Validate passwords even on the duplicate path; never return duplicate-email errors.
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = request.Email.Trim(), Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(), LockoutEnabled = true
        };
        if (user.DisplayName.Length == 0)
            throw new AppException(ErrorCode.InvalidRegistration, "Display name is required.");
        foreach (var validator in users.PasswordValidators)
            Ensure(await validator.ValidateAsync(users, user, request.Password), ErrorCode.InvalidPassword);
        if (await users.FindByEmailAsync(user.Email) is not null) return null;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await users.CreateAsync(user, request.Password);
            if (!result.Succeeded && result.Errors.Any(x => x.Code is "DuplicateEmail" or "DuplicateUserName"))
                return null;
            Ensure(result, ErrorCode.InvalidRegistration);
            Ensure(await users.AddToRoleAsync(user, AuthRoles.User), ErrorCode.RegistrationFailed);
            await transaction.CommitAsync(ct);
            return await ToAccountAsync(user);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            return null;
        }
    }

    public async Task<AuthAccount?> FindByEmailAsync(string email, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await users.FindByEmailAsync(email.Trim());
        return user is null ? null : await ToAccountAsync(user);
    }

    public async Task<AuthAccount?> FindByIdAsync(Guid userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(userId.ToString());
        return user is null ? null : await ToAccountAsync(user);
    }

    public async Task<AuthAccount> CheckPasswordAsync(LoginRequest request, CancellationToken ct)
    {
        var normalized = users.NormalizeEmail(request.Email.Trim());
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = (await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"NormalizedEmail\" = {normalized} FOR UPDATE")
            .ToListAsync(ct)).SingleOrDefault();
        if (user is null)
        {
            DummyHasher.VerifyHashedPassword(DummyUser, DummyHash, request.Password);
            throw InvalidLogin();
        }
        if (await users.IsLockedOutAsync(user)) throw InvalidLogin();
        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            Ensure(await users.AccessFailedAsync(user), ErrorCode.LoginFailed);
            await transaction.CommitAsync(ct);
            throw InvalidLogin();
        }
        // Use the same error for a wrong password, lockout or unconfirmed email.
        if (!user.EmailConfirmed) throw InvalidLogin();
        Ensure(await users.ResetAccessFailedCountAsync(user), ErrorCode.LoginFailed);
        await transaction.CommitAsync(ct);
        return await ToAccountAsync(user);
    }

    public async Task<string> GenerateConfirmationTokenAsync(Guid id, CancellationToken ct) =>
        Encode(await users.GenerateEmailConfirmationTokenAsync(await GetUserAsync(id)));

    public async Task<string> GenerateResetTokenAsync(Guid id, CancellationToken ct) =>
        Encode(await users.GeneratePasswordResetTokenAsync(await GetUserAsync(id)));

    public async Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await LockUserAsync(request.UserId, ct);
        if (user is null || user.EmailConfirmed) throw InvalidLink();
        Ensure(await users.ConfirmEmailAsync(user, Decode(request.Token)), ErrorCode.InvalidLink, true);
        await transaction.CommitAsync(ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await LockUserAsync(request.UserId, ct);
        if (user is null || !user.EmailConfirmed) throw InvalidLink();
        var result = await users.ResetPasswordAsync(user, Decode(request.Token), request.NewPassword);
        Ensure(result, ErrorCode.InvalidReset, true);
        // Identity changes the security stamp; also revoke every refresh-token family atomically.
        var now = clock.GetUtcNow();
        await db.AuthSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
        Ensure(await users.SetLockoutEndDateAsync(user, null), ErrorCode.ResetFailed);
        Ensure(await users.ResetAccessFailedCountAsync(user), ErrorCode.ResetFailed);
        await transaction.CommitAsync(ct);
    }

    private async Task<ApplicationUser?> LockUserAsync(Guid id, CancellationToken ct) =>
        (await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();

    private async Task<ApplicationUser> GetUserAsync(Guid id) =>
        await users.FindByIdAsync(id.ToString()) ?? throw InvalidLink();

    private async Task<AuthAccount> ToAccountAsync(ApplicationUser user) => new(
        new UserProfile(user.Id, user.Email!, user.DisplayName, (await users.GetRolesAsync(user)).ToArray()),
        user.SecurityStamp!, user.EmailConfirmed);

    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    private static string Decode(string token)
    {
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
        catch (FormatException) { throw InvalidLink(); }
    }

    private static AppException InvalidLogin() => new(ErrorCode.InvalidCredentials,
        "Unable to sign in. Check your credentials and confirm your email, or try again later.");
    private static AppException InvalidLink() => new(ErrorCode.InvalidLink, "The link is invalid or expired.");
    internal static void Ensure(IdentityResult result, ErrorCode code, bool hideErrors = false)
    {
        if (!result.Succeeded)
            throw new AppException(code, hideErrors ? "The link or password is invalid."
                : string.Join(" ", result.Errors.Select(x => x.Description)));
    }
}
