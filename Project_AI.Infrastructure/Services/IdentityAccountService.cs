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

public sealed class IdentityAccountService : IIdentityAccountService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AuthDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public IdentityAccountService(
        UserManager<ApplicationUser> userManager,
        AuthDbContext dbContext,
        TimeProvider timeProvider)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    private static readonly ApplicationUser DummyUser = new();
    private static readonly PasswordHasher<ApplicationUser> DummyHasher = new();
    private static readonly string DummyHash = DummyHasher.HashPassword(DummyUser, Guid.NewGuid().ToString());

    public async Task<AuthAccount?> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        // Validate passwords even on the duplicate path; never return duplicate-email errors.
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = request.Email.Trim(), Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(), LockoutEnabled = true
        };
        if (user.DisplayName.Length == 0)
        {
            throw new AppException(ErrorCode.InvalidRegistration, "Display name is required.");
        }
        foreach (var validator in _userManager.PasswordValidators)
            Ensure(await validator.ValidateAsync(_userManager, user, request.Password), ErrorCode.InvalidPassword);
        if (await _userManager.FindByEmailAsync(user.Email) is not null)
        {
            return null;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded && result.Errors.Any(x => x.Code is "DuplicateEmail" or "DuplicateUserName"))
            {
                return null;
            }
            Ensure(result, ErrorCode.InvalidRegistration);
            Ensure(await _userManager.AddToRoleAsync(user, AuthRoles.User), ErrorCode.RegistrationFailed);
            await transaction.CommitAsync(cancellationToken);
            return await ToAccountAsync(user);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            return null;
        }
    }

    public async Task<AuthAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByEmailAsync(email.Trim());
        return user is null ? null : await ToAccountAsync(user);
    }

    public async Task<AuthAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : await ToAccountAsync(user);
    }

    public async Task<AuthAccount> CheckPasswordAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalized = _userManager.NormalizeEmail(request.Email.Trim());
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var user = (await _dbContext.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"NormalizedEmail\" = {normalized} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();
        if (user is null)
        {
            DummyHasher.VerifyHashedPassword(DummyUser, DummyHash, request.Password);
            throw InvalidLogin();
        }
        if (await _userManager.IsLockedOutAsync(user))
        {
            throw InvalidLogin();
        }
        if (!await _userManager.CheckPasswordAsync(user, request.Password))
        {
            Ensure(await _userManager.AccessFailedAsync(user), ErrorCode.LoginFailed);
            await transaction.CommitAsync(cancellationToken);
            throw InvalidLogin();
        }
        // Use the same error for a wrong password, lockout or unconfirmed email.
        if (!user.EmailConfirmed)
        {
            throw InvalidLogin();
        }
        Ensure(await _userManager.ResetAccessFailedCountAsync(user), ErrorCode.LoginFailed);
        await transaction.CommitAsync(cancellationToken);
        return await ToAccountAsync(user);
    }

    public async Task<string> GenerateConfirmationTokenAsync(Guid id, CancellationToken cancellationToken) =>
        Encode(await _userManager.GenerateEmailConfirmationTokenAsync(await GetUserAsync(id)));

    public async Task<string> GenerateResetTokenAsync(Guid id, CancellationToken cancellationToken) =>
        Encode(await _userManager.GeneratePasswordResetTokenAsync(await GetUserAsync(id)));

    public async Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(request.UserId, cancellationToken);
        if (user is null || user.EmailConfirmed)
        {
            throw InvalidLink();
        }
        Ensure(await _userManager.ConfirmEmailAsync(user, Decode(request.Token)), ErrorCode.InvalidLink, true);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(request.UserId, cancellationToken);
        if (user is null || !user.EmailConfirmed)
        {
            throw InvalidLink();
        }
        var result = await _userManager.ResetPasswordAsync(user, Decode(request.Token), request.NewPassword);
        Ensure(result, ErrorCode.InvalidReset, true);
        // Identity changes the security stamp; also revoke every refresh-token family atomically.
        var now = _timeProvider.GetUtcNow();
        await _dbContext.AuthSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);
        Ensure(await _userManager.SetLockoutEndDateAsync(user, null), ErrorCode.ResetFailed);
        Ensure(await _userManager.ResetAccessFailedCountAsync(user), ErrorCode.ResetFailed);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<ApplicationUser?> LockUserAsync(Guid id, CancellationToken cancellationToken) =>
        (await _dbContext.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault();

    private async Task<ApplicationUser> GetUserAsync(Guid id) =>
        await _userManager.FindByIdAsync(id.ToString()) ?? throw InvalidLink();

    private async Task<AuthAccount> ToAccountAsync(ApplicationUser user) => new(
        new UserProfile(user.Id, user.Email!, user.DisplayName, (await _userManager.GetRolesAsync(user)).ToArray()),
        user.SecurityStamp!, user.EmailConfirmed);

    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    private static string Decode(string token)
    {
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch (FormatException)
        {
            throw InvalidLink();
        }
    }

    private static AppException InvalidLogin() => new(ErrorCode.InvalidCredentials,
        "Unable to sign in. Check your credentials and confirm your email, or try again later.");
    private static AppException InvalidLink() => new(ErrorCode.InvalidLink, "The link is invalid or expired.");
    private static void Ensure(IdentityResult result, ErrorCode code, bool hideErrors = false)
    {
        if (!result.Succeeded)
            throw new AppException(code, hideErrors ? "The link or password is invalid."
                : string.Join(" ", result.Errors.Select(x => x.Description)));
    }
}
