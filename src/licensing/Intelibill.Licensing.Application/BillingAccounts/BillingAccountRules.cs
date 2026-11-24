using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.BillingAccounts;

public static class BillingAccountErrors
{
    public static Error NameInvalid => Error.Validation("Billing.AccountNameInvalid",
        $"Enter a billing account name of 1 to {MerchantBillingDelegationV1.MaximumAccountNameLength} characters.");
    public static Error OperationKeyInvalid => Error.Validation("Billing.OperationKeyInvalid", "The operation key is invalid.");
    public static Error PayloadMismatch => Error.Conflict("Billing.OperationPayloadMismatch",
        "This operation key was already used with different details.");
}

public static class BillingAccountRules
{
    // Fixed namespace for account identifiers derived from (actor, operation key).
    private static readonly byte[] Namespace = Encoding.ASCII.GetBytes("intelibill.billing-account.v1\0");

    public static string? NormalizeName(string? name) => MerchantBillingDelegationV1.NormalizeAccountName(name);

    public static byte[] RequestHash(string normalizedName) => SHA256.HashData(Encoding.UTF8.GetBytes(normalizedName));

    /// <summary>
    /// RFC 9562 version 8 identifier from SHA-256 of the actor and operation key. Two actors never share an
    /// account through a key, and a retry by the same actor always names the same account.
    /// </summary>
    public static Guid DeriveAccountId(Guid actorId, Guid operationKey)
    {
        Span<byte> input = stackalloc byte[Namespace.Length + 32];
        Namespace.CopyTo(input);
        actorId.TryWriteBytes(input[Namespace.Length..], bigEndian: true, out _);
        operationKey.TryWriteBytes(input[(Namespace.Length + 16)..], bigEndian: true, out _);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>Account-scoped actions each role may delegate. Membership never grants merchant shop access.</summary>
    public static bool Allows(BillingAccountRole role, string action) => action switch
    {
        ShopClosuresV1.AccountRead or ShopClosuresV1.Propose or ShopClosuresV1.Confirm or
        BillingOwnershipTransfersV1.Issue or BillingInvitationsV1.OwnerCheck or BillingInvitationsV1.Issue or BillingInvitationsV1.RemoveMember
        or CapacityChangeQuotesV1.Create or CapacityChangeQuotesV1.Shops => role == BillingAccountRole.Owner,
        MerchantBillingDelegationV1.ReadAccount or BillingInvitationsV1.Roster or ShopAffiliationsV1.Read or ShopAffiliationsV1.Request or SubscriptionStatusV1.AccountRead
        or SubscriptionCancellationsV1.Read or SubscriptionCancellationsV1.Schedule or SubscriptionCancellationsV1.Recover
        or SubscriptionCancellationsV1.Withdraw or SubscriptionCancellationsV1.WithdrawRecover => role is BillingAccountRole.Owner or BillingAccountRole.Manager,
        _ when CheckoutOrdersV1.IsAction(action) || CheckoutPaymentsV1.IsAction(action) =>
            role is BillingAccountRole.Owner or BillingAccountRole.Manager,
        _ => false,
    };

    // Verify appends a browser claim: its insert and commit share the account lock a proof-free resume decides under,
    // so a stale unpaid read can never commit ResumeConfirmed over a claim that is inserted but not yet committed.
    public static bool SerializesAccountMutation(string action) => action is ShopClosuresV1.Propose or ShopClosuresV1.Confirm
        or BillingOwnershipTransfersV1.Issue
        or BillingInvitationsV1.Issue or BillingInvitationsV1.RemoveMember or ShopAffiliationsV1.Request or SubscriptionCancellationsV1.Schedule
        or SubscriptionCancellationsV1.Withdraw or CheckoutOrdersV1.Create or CheckoutOrdersV1.Accept or CapacityChangeQuotesV1.Create
        or CheckoutPaymentsV1.Start or CheckoutPaymentsV1.Verify;

    public static string RoleName(BillingAccountRole role) => role switch
    {
        BillingAccountRole.Owner => MerchantBillingDelegationV1.OwnerRole,
        BillingAccountRole.Manager => MerchantBillingDelegationV1.ManagerRole,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    // PostgreSQL keeps microseconds; truncate so the first answer equals every later read.
    public static DateTimeOffset StorageInstant(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
}
