using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using FluentValidation;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
namespace Intelibill.Application.Features.Auth.Services;

public sealed class EmailOwnershipService(IEmailOwnershipRepository repository, IUserRepository users,
    IRefreshTokenRepository refreshTokens, IIdentityMailOutbox mail, IPasswordHasher hasher,
    ITokenService tokens, TimeProvider clock) : IEmailOwnershipService
{
    private static readonly Error Invalid = Error.Validation("Auth.InvalidChallenge", "The proof is invalid, expired, superseded, or already used.");
    private static readonly Error Conflict = Error.Conflict("Auth.EmailUnavailable", "This email is unavailable. Request a new verification or sign in to your existing account.");
    private static readonly Error Reauth = Error.Unauthorized("Auth.ReauthenticationRequired", "Please authenticate again.");
    private static readonly Error Unavailable = Error.Failure("Auth.EmailUnavailable", "Email verification delivery is unavailable. Please try again later.");
    private static readonly Error RecoverySupportRequired = Error.Conflict("Auth.RecoverySupportRequired", "This legacy account requires an ownership review. Contact support; your data has been preserved.");
    public Task<ErrorOr<Guid>> RequestAsync(string email, CancellationToken ct) => RequestCoreAsync(email, "enrollment", ct);
    public Task<ErrorOr<Guid>> RequestClaimAsync(string email, CancellationToken ct) => RequestCoreAsync(email, "claim", ct);
    private async Task<ErrorOr<Guid>> RequestCoreAsync(string email, string purpose, CancellationToken ct)
    {
        if (!ValidEmail(email)) return Error.Validation("Auth.InvalidEmail", "Enter a valid email address.");
        if (!mail.Enabled) return Unavailable;
        email = email.Trim().ToLowerInvariant();
        await using var transaction = await repository.BeginAsync(ct);
        var user = await users.GetByEmailAsync(email, ct);
        if (purpose == "enrollment" ? user is not null : user is null || user.IsEmailVerified) return Guid.Empty;
        if (!await repository.HasSendBudgetAsync(email, clock.GetUtcNow(), ct)) return Guid.Empty;
        var challenge = await CreateAsync(email, purpose, user, ct);
        return await repository.CommitAsync(ct) ? challenge.Id : Conflict;
    }
    public Task<ErrorOr<AuthResult>> ConfirmEnrollmentAsync(Guid challengeId, string token, string password, string firstName, string lastName, CancellationToken ct) =>
        ConfirmAccountAsync(challengeId, token, password, firstName, lastName, "enrollment", ct);
    public Task<ErrorOr<AuthResult>> ConfirmClaimAsync(Guid challengeId, string token, string password, string firstName, string lastName, CancellationToken ct) =>
        ConfirmAccountAsync(challengeId, token, password, firstName, lastName, "claim", ct);
    private async Task<ErrorOr<AuthResult>> ConfirmAccountAsync(Guid id, string token, string password, string firstName, string lastName, string purpose, CancellationToken ct)
    {
        var validation = new EnrollmentValidator().Validate(new EnrollmentInput(id, token, password, firstName, lastName));
        if (!validation.IsValid) return validation.Errors.Select(x => Error.Validation(x.PropertyName, x.ErrorMessage)).ToList();
        await using var transaction = await repository.BeginAsync(ct);
        var challenge = await ValidChallengeAsync(id, token, purpose, ct);
        if (challenge is null) return Invalid;
        var user = await users.GetByEmailAsync(challenge.Destination, ct);
        if (purpose == "claim")
        {
            if (user is null || user.IsEmailVerified || user.Id != challenge.UserId || user.SecurityVersion != challenge.SecurityVersion) return Invalid;
            if (!user.IsLoginEnabled || await repository.HasBusinessHistoryAsync(user.Id, ct))
                return Error.Conflict("Auth.RecoverySupportRequired", "This legacy account has business access. Contact support for an ownership review; your data has been preserved.");
            user.ClaimEmail(challenge.Destination, hasher.Hash(password), firstName, lastName);
            await repository.InvalidateCredentialsAsync(user.Id, ct);
        }
        else
        {
            if (user is not null) return Conflict;
            user = User.CreateWithEmail(challenge.Destination, hasher.Hash(password), firstName.Trim(), lastName.Trim());
            user.VerifyEmail(challenge.Destination);
            await users.AddAsync(user, ct);
        }
        challenge.Consume(clock.GetUtcNow());
        var (shopId, role, shops) = AuthShopSelection.Resolve(user);
        var (access, expiry) = await tokens.GenerateAccessTokenAsync(user, shopId, role, ct);
        var refresh = tokens.CreateRefreshToken(user.Id);
        refresh.BindSecurityVersion(user.SecurityVersion);
        await refreshTokens.AddAsync(refresh, ct);
        if (!await repository.CommitAsync(ct)) return Conflict;
        return new AuthResult(access, refresh.Token, expiry, refresh.ExpiresAt,
            new UserDto(user.Id, user.Email, user.PhoneNumber, user.FirstName, user.LastName, user.Language), shopId, shops);
    }
    public async Task<ErrorOr<ReauthenticationResult>> ReauthenticateAsync(Guid actor, long version, string? password, CancellationToken ct)
    {
        await using var transaction = await repository.BeginAsync(ct);
        var user = await ActorAsync(actor, version, ct);
        if (user is null) return Reauth;
        var valid = user.IsEmailVerified && user.PasswordHash is not null && password is not null && hasher.Verify(password, user.PasswordHash);
        if (!valid) return Reauth;
        var secret = NewToken();
        var proof = EmailOwnershipChallenge.Create("reauthentication", string.Empty, Hash(secret), clock.GetUtcNow().AddMinutes(5), actor, version);
        repository.Add(proof);
        if (!await repository.CommitAsync(ct)) return Reauth;
        return new ReauthenticationResult($"{proof.Id:N}.{secret}", proof.ExpiresAt);
    }
    public async Task<ErrorOr<Guid>> RequestEmailChangeAsync(Guid actor, long version, string email, string proof, CancellationToken ct)
    {
        if (!ValidEmail(email)) return Error.Validation("Auth.InvalidEmail", "Enter a valid email address.");
        if (!mail.Enabled) return Unavailable;
        email = email.Trim().ToLowerInvariant();
        await using var transaction = await repository.BeginAsync(ct);
        var user = await ActorAsync(actor, version, ct);
        if (user is null || !await ConsumeProofAsync(actor, version, proof, ct)) return Reauth;
        if (!user.IsEmailVerified && await repository.HasBusinessHistoryAsync(user.Id, ct))
            return RecoverySupportRequired;
        if (await users.ExistsByEmailAsync(email, ct) || !await repository.HasSendBudgetAsync(email, clock.GetUtcNow(), ct))
        { await repository.CommitAsync(ct); return Guid.Empty; }
        var challenge = await CreateAsync(email, "email-change", user, ct);
        return await repository.CommitAsync(ct) ? challenge.Id : Conflict;
    }
    public async Task<ErrorOr<bool>> ConfirmEmailChangeAsync(Guid actor, long version, Guid challengeId, string token, CancellationToken ct)
    {
        await using var transaction = await repository.BeginAsync(ct);
        var challenge = await ValidChallengeAsync(challengeId, token, "email-change", ct);
        if (challenge?.UserId is null) return Invalid;
        if (challenge.UserId != actor || challenge.SecurityVersion != version)
            return Error.Forbidden("Auth.WrongChallengeActor", "Sign in to the account that requested this email change.");
        var user = await ActorAsync(actor, version, ct);
        if (user is null) return Invalid;
        if (!user.IsEmailVerified && await repository.HasBusinessHistoryAsync(user.Id, ct))
            return RecoverySupportRequired;
        if (await users.ExistsByEmailAsync(challenge.Destination, ct)) return Conflict;
        var previousEmail = user.IsEmailVerified ? user.Email : null;
        user.VerifyEmail(challenge.Destination);
        challenge.Consume(clock.GetUtcNow());
        await repository.InvalidateCredentialsAsync(user.Id, ct);
        if (previousEmail is not null) mail.QueueSecurityNotice(previousEmail);
        mail.QueueSecurityNotice(challenge.Destination);
        return await repository.CommitAsync(ct) ? true : Conflict;
    }
    private async Task<User?> ActorAsync(Guid id, long version, CancellationToken ct)
    {
        var user = await users.GetByIdWithDetailsAsync(id, ct);
        return user is { IsLoginEnabled: true } && user.SecurityVersion == version ? user : null;
    }
    private async Task<bool> ConsumeProofAsync(Guid actor, long version, string proof, CancellationToken ct)
    {
        var parts = (proof ?? string.Empty).Split('.', 2);
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var id)) return false;
        var challenge = await ValidChallengeAsync(id, parts[1], "reauthentication", ct);
        if (challenge is null || challenge.UserId != actor || challenge.SecurityVersion != version) return false;
        challenge.Consume(clock.GetUtcNow()); return true;
    }
    private async Task<EmailOwnershipChallenge?> ValidChallengeAsync(Guid id, string token, string purpose, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        var challenge = await repository.FindAsync(id, ct);
        return challenge is not null && challenge.Purpose == purpose && challenge.ConsumedAt is null && challenge.ExpiresAt > clock.GetUtcNow() &&
            CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(challenge.TokenHash), Encoding.ASCII.GetBytes(Hash(token))) ? challenge : null;
    }
    private async Task<EmailOwnershipChallenge> CreateAsync(string email, string purpose, User? user, CancellationToken ct)
    {
        await repository.SupersedeAsync(email, purpose, user?.Id, clock.GetUtcNow(), ct);
        var token = NewToken();
        var challenge = EmailOwnershipChallenge.Create(purpose, email, Hash(token), clock.GetUtcNow().AddMinutes(30), user?.Id, user?.SecurityVersion ?? 0);
        repository.Add(challenge); mail.QueueChallenge(challenge.Id, email, purpose, token, challenge.ExpiresAt);
        return challenge;
    }
    private static bool ValidEmail(string email) => new EmailInputValidator().Validate(new EmailInput(email)).IsValid;
    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
internal sealed record EmailInput(string Email);
internal sealed class EmailInputValidator : AbstractValidator<EmailInput>
{
    public EmailInputValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
}
internal sealed record EnrollmentInput(Guid Id, string Token, string Password, string FirstName, string LastName);
internal sealed class EnrollmentValidator : AbstractValidator<EnrollmentInput>
{
    public EnrollmentValidator()
    {
        RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100); RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
    }
}
