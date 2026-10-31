using System.Security.Cryptography;
using ErrorOr;
using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Auth.Services;

/// <summary>
/// The verified phone lifecycle, built the same way the email ownership flow is: one advisory lock
/// per transition, a single-use proof, and a generic answer to any request that would otherwise
/// disclose whether a number has an account behind it.
/// </summary>
public sealed class PhoneOwnershipService(
    IPhoneChallengeRepository repository,
    IUserRepository users,
    IIdentitySmsOutbox sms,
    IAuthSessionService sessions,
    IPhoneAuthPolicy policy,
    IPhoneCodeHasher codes,
    ReauthenticationProofs reauthentication,
    TimeProvider clock) : IPhoneOwnershipService
{
    public Task<ErrorOr<PhoneChallengeReceipt>> RequestEnrollmentAsync(string phone, CancellationToken ct) =>
        RequestAsync(phone, PhoneChallengePurposes.Enrollment, ct);

    public Task<ErrorOr<PhoneChallengeReceipt>> RequestLoginAsync(string phone, CancellationToken ct) =>
        RequestAsync(phone, PhoneChallengePurposes.Login, ct);

    private async Task<ErrorOr<PhoneChallengeReceipt>> RequestAsync(string phone, string purpose, CancellationToken ct)
    {
        if (!policy.Enabled) return Errors.Phone.AuthenticationDisabled;
        if (!sms.Enabled || !codes.Available) return Errors.Phone.DeliveryUnavailable;
        if (!PhoneNumberFormat.TryNormalize(phone, policy.DefaultCountryCode, out var destination))
            return Errors.Phone.InvalidPhoneNumber;

        await using var transaction = await repository.BeginAsync(ct);

        var occupant = await repository.LockOccupantAsync(destination, ct);

        // An account that carries business history and never proved this number is not recoverable
        // by proving it now: carriers recycle numbers, so the proof says nothing about having owned
        // that account. This is the one case worth telling the caller about, because the answer is
        // a support process rather than a retry.
        if (occupant is { RequiresOwnershipReview: true })
            return Errors.Phone.RecoverySupportRequired;

        var eligible = purpose switch
        {
            // Enrollment is for a number that is not already a credential. A proved holder gets the
            // generic answer rather than a hint that they should sign in instead.
            PhoneChallengePurposes.Enrollment => occupant is null or { IsProvedCredential: false },
            PhoneChallengePurposes.Login => occupant is { IsProvedCredential: true },
            _ => false,
        };

        if (!eligible || !await repository.HasSendBudgetAsync(destination, clock.GetUtcNow(), policy.DestinationSendsPerHour, policy.ResendSpacing, ct))
            return Unissued(destination);

        var actor = purpose == PhoneChallengePurposes.Login ? occupant!.UserId : (Guid?)null;
        var version = purpose == PhoneChallengePurposes.Login ? occupant!.SecurityVersion : 0;

        var challenge = await IssueAsync(destination, purpose, actor, version, ct);
        return await repository.CommitAsync(ct)
            ? new PhoneChallengeReceipt(challenge.Id, PhoneNumberFormat.Mask(destination), challenge.ResendAllowedAt, challenge.ExpiresAt)
            : Errors.Phone.PhoneUnavailable;
    }

    public async Task<ErrorOr<AuthResult>> ConfirmEnrollmentAsync(
        Guid challengeId,
        string code,
        string firstName,
        string lastName,
        CancellationToken ct)
    {
        if (!policy.Enabled) return Errors.Phone.AuthenticationDisabled;

        var validation = new PhoneEnrollmentValidator().Validate(new PhoneEnrollmentInput(challengeId, code, firstName, lastName));
        if (!validation.IsValid)
            return validation.Errors.Select(x => Error.Validation(x.PropertyName, x.ErrorMessage)).ToList();

        await using var transaction = await repository.BeginAsync(ct);

        var spent = await SpendAsync(challengeId, code, PhoneChallengePurposes.Enrollment, ct);
        if (spent.IsError) return spent.Errors;
        var challenge = spent.Value;

        // Enrollment has no actor, so every occupant is somebody else. An unproved contact number
        // is released to whoever proved it rather than merged into the account that listed it.
        var blocked = await BlockingOccupantAsync(challenge.Destination, Guid.Empty, ct);
        if (blocked is not null) return blocked.Value;

        var user = User.CreateWithPhone(challenge.Destination, firstName.Trim(), lastName.Trim());
        user.VerifyPhone(challenge.Destination, clock.GetUtcNow());
        await users.AddAsync(user, ct);

        // Inside the transaction that inserted the account and consumed the proof, which is the
        // precondition this overload documents.
        var result = await sessions.IssueForProvedAccountAsync(
            user,
            AuthenticationProof.FromRegistration(user.Id, clock.GetUtcNow()),
            preferredShopId: null,
            ct);

        return await repository.CommitAsync(ct) ? result : Errors.Phone.PhoneUnavailable;
    }

    public async Task<ErrorOr<AuthResult>> ConfirmLoginAsync(Guid challengeId, string code, CancellationToken ct)
    {
        if (!policy.Enabled) return Errors.Phone.AuthenticationDisabled;

        await using var transaction = await repository.BeginAsync(ct);

        var spent = await SpendAsync(challengeId, code, PhoneChallengePurposes.Login, ct);
        if (spent.IsError) return spent.Errors;
        var challenge = spent.Value;

        if (challenge.UserId is not Guid actor) return Errors.Phone.InvalidChallenge;

        // Re-checked under the account lock against the version the code was issued at: a number
        // that moved, or an account disabled or reset in the meantime, is no longer this proof.
        if (!await repository.LockVerifiedPhoneAsync(actor, challenge.SecurityVersion, challenge.Destination, ct))
            return Errors.Phone.InvalidChallenge;

        var user = await users.GetByIdWithDetailsAsync(actor, ct);
        if (user is null) return Errors.Phone.InvalidChallenge;

        var result = await sessions.IssueForProvedAccountAsync(
            user,
            AuthenticationProof.FromPhoneOwnership(user.Id, challenge.SecurityVersion, challenge.Id, clock.GetUtcNow()),
            preferredShopId: null,
            ct);

        return await repository.CommitAsync(ct) ? result : Errors.Phone.InvalidChallenge;
    }

    public async Task<ErrorOr<PhoneChallengeReceipt>> RequestPhoneChangeAsync(
        Guid actor,
        long version,
        string phone,
        string? reauthProof,
        CancellationToken ct)
    {
        if (!policy.Enabled) return Errors.Phone.AuthenticationDisabled;
        if (!sms.Enabled || !codes.Available) return Errors.Phone.DeliveryUnavailable;
        if (!PhoneNumberFormat.TryNormalize(phone, policy.DefaultCountryCode, out var destination))
            return Errors.Phone.InvalidPhoneNumber;

        await using var transaction = await repository.BeginAsync(ct);

        var user = await ActorAsync(actor, version, ct);
        if (user is null) return Errors.Phone.ReauthenticationRequired;

        // Where there is a password, a recent check of it is required on top of the session. An
        // account that signs in by phone alone has no password to check, so the live session it
        // asked from is the authority, and proving the new number is the rest of it.
        if (user.PasswordHash is not null && !await reauthentication.ConsumeAsync(actor, version, reauthProof, ct))
            return Errors.Phone.ReauthenticationRequired;

        // An established account that never proved a number does not get to bind one through a
        // self-service route. Whoever is holding the session may not be the person whose business
        // this is, and binding a number here would hand them the recovery factor as well.
        if (!user.IsPhoneVerified && await repository.HasBusinessHistoryAsync(actor, ct))
            return Errors.Phone.RecoverySupportRequired;

        var blocked = await BlockingOccupantAsync(destination, actor, ct);
        if (blocked is not null) return blocked.Value;

        if (!await repository.HasSendBudgetAsync(destination, clock.GetUtcNow(), policy.DestinationSendsPerHour, policy.ResendSpacing, ct))
        {
            // Committed so the reauthentication proof stays spent: a throttled retry must not be a
            // way to hold a proof open.
            await repository.CommitAsync(ct);
            return Unissued(destination);
        }

        var challenge = await IssueAsync(destination, PhoneChallengePurposes.PhoneChange, actor, version, ct);
        return await repository.CommitAsync(ct)
            ? new PhoneChallengeReceipt(challenge.Id, PhoneNumberFormat.Mask(destination), challenge.ResendAllowedAt, challenge.ExpiresAt)
            : Errors.Phone.PhoneUnavailable;
    }

    public async Task<ErrorOr<bool>> ConfirmPhoneChangeAsync(
        Guid actor,
        long version,
        Guid challengeId,
        string code,
        CancellationToken ct)
    {
        if (!policy.Enabled) return Errors.Phone.AuthenticationDisabled;

        await using var transaction = await repository.BeginAsync(ct);

        var spent = await SpendAsync(challengeId, code, PhoneChallengePurposes.PhoneChange, ct);
        if (spent.IsError) return spent.Errors;
        var challenge = spent.Value;

        if (challenge.UserId != actor)
            return Error.Forbidden("Auth.WrongChallengeActor", "Sign in to the account that requested this phone change.");
        if (challenge.SecurityVersion != version) return Errors.Phone.InvalidChallenge;

        var user = await ActorAsync(actor, version, ct);
        if (user is null) return Errors.Phone.InvalidChallenge;
        if (!user.IsPhoneVerified && await repository.HasBusinessHistoryAsync(actor, ct))
            return Errors.Phone.RecoverySupportRequired;

        var blocked = await BlockingOccupantAsync(challenge.Destination, actor, ct);
        if (blocked is not null) return blocked.Value;

        var previous = user.IsPhoneVerified ? user.PhoneNumber : null;
        user.VerifyPhone(challenge.Destination, clock.GetUtcNow());
        await repository.InvalidateCredentialsAsync(actor, ct);

        // Both numbers hear about it: the one that lost the factor is the only place somebody who
        // did not do this would notice.
        if (previous is not null) sms.QueueSecurityNotice(previous, bound: false);
        sms.QueueSecurityNotice(challenge.Destination, bound: true);

        return await repository.CommitAsync(ct) ? true : Errors.Phone.PhoneUnavailable;
    }

    /// <summary>
    /// Why a number cannot be taken, or null when it can. An unproved contact number on an account
    /// with no business history is released to whoever proves it; anything else is somebody's
    /// identity and is refused.
    /// </summary>
    private async Task<Error?> BlockingOccupantAsync(string destination, Guid actor, CancellationToken ct)
    {
        var occupant = await repository.LockOccupantAsync(destination, ct);
        if (occupant is null || occupant.UserId == actor) return null;
        if (occupant.RequiresOwnershipReview) return Errors.Phone.RecoverySupportRequired;
        if (occupant.IsProvedCredential) return Errors.Phone.PhoneUnavailable;

        await repository.DetachUnverifiedPhoneAsync(occupant.UserId, ct);
        return null;
    }

    private async Task<User?> ActorAsync(Guid actor, long version, CancellationToken ct)
    {
        var user = await users.GetByIdWithDetailsAsync(actor, ct);
        return user is { IsLoginEnabled: true } && user.SecurityVersion == version ? user : null;
    }

    /// <summary>
    /// Consumes one guess against a locked challenge. A wrong code costs an attempt and that cost
    /// is committed, so guessing cannot be made free by abandoning the request; a right code is
    /// spent here and cannot be spent again.
    /// </summary>
    private async Task<ErrorOr<PhoneChallenge>> SpendAsync(Guid challengeId, string code, string purpose, CancellationToken ct)
    {
        if (!IsWellFormed(code)) return Errors.Phone.InvalidChallenge;

        var challenge = await repository.FindForUpdateAsync(challengeId, ct);
        if (challenge is null ||
            !string.Equals(challenge.Purpose, purpose, StringComparison.Ordinal) ||
            !challenge.IsOpen(clock.GetUtcNow(), policy.MaxAttempts))
            return Errors.Phone.InvalidChallenge;

        if (!codes.Matches(code, challenge.CodeHash, challenge.KeyId))
        {
            challenge.RegisterFailedAttempt(policy.MaxAttempts);
            await repository.CommitAsync(ct);
            return Errors.Phone.InvalidChallenge;
        }

        challenge.Consume(clock.GetUtcNow());
        return challenge;
    }

    private async Task<PhoneChallenge> IssueAsync(string destination, string purpose, Guid? actor, long version, CancellationToken ct)
    {
        // A resend closes its predecessor, so exactly one code for a destination and purpose can
        // ever succeed and "which code is live" is never ambiguous.
        await repository.SupersedeAsync(destination, purpose, actor, clock.GetUtcNow(), ct);

        var code = NewCode();
        var now = clock.GetUtcNow();
        var challenge = PhoneChallenge.Create(
            purpose,
            destination,
            codes.Hash(code),
            codes.KeyId,
            now + policy.CodeLifetime,
            now + policy.ResendSpacing,
            actor,
            version);

        repository.Add(challenge);
        sms.QueueCode(challenge.Id, destination, purpose, code, challenge.ExpiresAt);
        return challenge;
    }

    /// <summary>
    /// The answer to a request that will not produce a code. It carries the same fields, and the
    /// same plausible deadlines, as one that did.
    /// </summary>
    private PhoneChallengeReceipt Unissued(string destination)
    {
        var now = clock.GetUtcNow();
        return new PhoneChallengeReceipt(Guid.Empty, PhoneNumberFormat.Mask(destination), now + policy.ResendSpacing, now + policy.CodeLifetime);
    }

    private string NewCode()
    {
        var digits = new char[policy.CodeLength];
        for (var index = 0; index < digits.Length; index++)
            digits[index] = (char)('0' + RandomNumberGenerator.GetInt32(10));

        return new string(digits);
    }

    private bool IsWellFormed(string? code)
    {
        if (code is null || code.Length != policy.CodeLength)
            return false;

        foreach (var character in code)
        {
            if (character is < '0' or > '9')
                return false;
        }

        return true;
    }
}

internal sealed record PhoneEnrollmentInput(Guid ChallengeId, string Code, string FirstName, string LastName);

internal sealed class PhoneEnrollmentValidator : AbstractValidator<PhoneEnrollmentInput>
{
    public PhoneEnrollmentValidator()
    {
        RuleFor(x => x.ChallengeId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(10);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
    }
}
