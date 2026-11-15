using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient
{
    public async Task<ErrorOr<BillingInvitationAcknowledgementV1>> IssueAsync(MerchantBillingCaller caller,
        Guid accountId, MerchantBillingInvitation request, CancellationToken cancellationToken)
    {
        if (request.OperationKey == Guid.Empty)
            return MerchantBillingErrors.OperationKeyInvalid;
        if (NormalizeRecipient(request.RecipientKind, request.Recipient) is not { } recipient)
            return MerchantBillingInvitationErrors.InvalidRecipient;

        var path = BillingInvitationsV1.AccountInvitations(accountId);
        var owner = await ReadInvitationAsync<BillingOwnerRevisionV1>(caller,
            new(BillingInvitationsV1.OwnerCheck, HttpMethod.Get, path + "/owner", accountId, []),
            result => result.OwnerRevision > 0, cancellationToken);
        if (owner.IsError)
            return owner.Errors;
        if (request.ExpectedOwnerRevision != owner.Value.OwnerRevision)
            return MerchantBillingInvitationErrors.StaleRevision;

        var digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { accountId, request.OperationKey, request.ExpectedOwnerRevision, request.RecipientKind, recipient })));
        return await SendInvitationAsync(caller, accountId, path, request, recipient, digest, cancellationToken);
    }

    private async Task<ErrorOr<BillingInvitationAcknowledgementV1>> SendInvitationAsync(MerchantBillingCaller caller,
        Guid accountId, string path, MerchantBillingInvitation request, string recipient, string digest,
        CancellationToken cancellationToken)
    {
        var actor = await ResolveVerifiedRecipientAsync(request.RecipientKind, recipient, cancellationToken);
        if (actor.IsError)
            return actor.Errors;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new IssueBillingInvitationV1(request.OperationKey,
            request.ExpectedOwnerRevision, actor.Value, digest));
        return await ReadInvitationAsync<BillingInvitationAcknowledgementV1>(caller,
            new(BillingInvitationsV1.Issue, HttpMethod.Post, path, accountId, bytes),
            result => result.OperationKey == request.OperationKey, cancellationToken);
    }

    private async Task<ErrorOr<Guid?>> ResolveVerifiedRecipientAsync(string? kind, string recipient, CancellationToken ct)
    {
        try
        {
            var candidates = db.Users.AsNoTracking().Where(user => user.IsLoginEnabled);
            var verified = kind == "email"
                ? candidates.Where(user => user.IsEmailVerified && user.Email == recipient)
                : candidates.Where(user => user.IsPhoneVerified && user.PhoneNumber == recipient);
            var ids = await verified.Select(user => user.Id).Take(2).ToListAsync(ct);
            return ids is [var id] ? id : (Guid?)null;
        }
        catch (Exception exception) when (exception is Npgsql.NpgsqlException or TimeoutException)
        {
            return Unavailable("RecipientLookupUnavailable", exception);
        }
    }

    private string? NormalizeRecipient(string? kind, string? recipient)
    {
        if (recipient?.Trim() is not { Length: > 0 and <= 254 } value || value.Any(char.IsControl))
            return null;
        return kind switch
        {
            "email" when MailAddress.TryCreate(value, out var parsed) && parsed.Address == value => value.ToLowerInvariant(),
            "phone" when PhoneNumberFormat.TryNormalize(value, phones.DefaultCountryCode, out var e164) => e164,
            _ => null,
        };
    }

    public Task<ErrorOr<BillingInvitationInboxV1>> InboxAsync(MerchantBillingCaller caller, Guid? after,
        CancellationToken cancellationToken) => ReadInvitationAsync<BillingInvitationInboxV1>(caller,
        new(BillingInvitationsV1.Inbox, HttpMethod.Get, BillingInvitationsV1.InboxPage(after), null, []),
        result => result.Invitations is { Length: <= BillingInvitationsV1.PageSize } items &&
            items.All(item => item is { Revision: > 0 } && item.InvitationId != Guid.Empty && item.AccountId != Guid.Empty &&
                MerchantBillingDelegationV1.NormalizeAccountName(item.AccountName) == item.AccountName &&
                item.AccountName is not null), cancellationToken);

    public Task<ErrorOr<BillingRosterV1>> RosterAsync(MerchantBillingCaller caller, Guid accountId, Guid? after,
        CancellationToken cancellationToken) => ReadInvitationAsync<BillingRosterV1>(caller,
        new(BillingInvitationsV1.Roster, HttpMethod.Get, BillingInvitationsV1.Members(accountId, after), accountId, []),
        result => result.Members is { Length: <= BillingInvitationsV1.PageSize } items &&
            items.All(item => item is { Revision: > 0, Role: MerchantBillingDelegationV1.OwnerRole
                or MerchantBillingDelegationV1.ManagerRole } && item.MemberId != Guid.Empty) &&
            result.OwnerRevision is null or > 0 && result.AccountName is not null &&
            MerchantBillingDelegationV1.NormalizeAccountName(result.AccountName) == result.AccountName, cancellationToken);

    // The browser expresses consent for one revision. Only the merchant server sees the opaque proof.
    public async Task<ErrorOr<BillingAccountV1>> AcceptAsync(MerchantBillingCaller caller, Guid invitationId,
        int revision, CancellationToken cancellationToken)
    {
        if (revision < 1)
            return MerchantBillingInvitationErrors.UnavailableInvitation;

        var proof = await ReadInvitationAsync<BillingInvitationProofV1>(caller,
            new(BillingInvitationsV1.Proof, HttpMethod.Post, BillingInvitationsV1.Invitation(invitationId, "proof"), null, []),
            result => result.Token is { Length: BillingInvitationsV1.TokenLength } && result.Revision > 0, cancellationToken);
        if (proof.IsError)
            return proof.Errors;
        if (proof.Value.Revision != revision)
            return MerchantBillingInvitationErrors.UnavailableInvitation;

        var body = JsonSerializer.SerializeToUtf8Bytes(new AcceptBillingInvitationV1(proof.Value.Token, revision));
        return await ReadInvitationAsync<BillingAccountV1>(caller,
            new(BillingInvitationsV1.Accept, HttpMethod.Post, BillingInvitationsV1.Invitation(invitationId, "accept"), null, body),
            result => IsValidAccount(result) && result.Role == MerchantBillingDelegationV1.ManagerRole, cancellationToken);
    }

    private Task<ErrorOr<T>> ReadInvitationAsync<T>(MerchantBillingCaller caller, BillingDelegationIntent intent,
        Func<T, bool> valid, CancellationToken cancellationToken) where T : class =>
        RunAsync<T>(caller, intent, BillingInvitationsV1.MaximumResponseBytes, async (response, token) =>
        {
            var result = await ReadJsonAsync<T>(response, token);
            return result is not null && valid(result) ? result : Unavailable("InvalidInvitationContract");
        }, cancellationToken);
}
