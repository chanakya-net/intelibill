using ErrorOr;
using System.Text.Json;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient : IMerchantCoverageSlots
{
    public Task<ErrorOr<CoverageSlotTermV1>> SlotsAsync(MerchantBillingCaller caller, Guid account, Guid term, int page, CancellationToken ct) =>
        ReadAffiliationAsync<CoverageSlotTermV1>(caller, new(CoverageSlotsV1.Read, HttpMethod.Get, CoverageSlotsV1.ReadPath(account, term, page),
            account, []), row => row.TermId == term && row.UnusedSlots >= 0 && row.UnusedSlots <= row.Capacity &&
                row.EligibleShops is { Length: <= CoverageSlotsV1.PageSize } &&
                row.Assignments is { Length: <= CoverageSlotsV1.PageSize } && row.Assignments.All(r => r.TermId == term && r.AccountId == account), ct);

    public Task<ErrorOr<CoverageSlotAssignmentV1>> AssignAsync(MerchantBillingCaller caller, Guid account, Guid term,
        AssignUnusedCoverageSlotV1 command, CancellationToken ct) => ReadAffiliationAsync<CoverageSlotAssignmentV1>(caller,
            new(CoverageSlotsV1.Request, HttpMethod.Post, CoverageSlotsV1.Path(account, term), account, JsonSerializer.SerializeToUtf8Bytes(command, Json)),
            row => row.AccountId == account && row.TermId == term && row.ShopId == command.ShopId &&
                row.CoverageEndsAt == command.ObservedPaidThrough && row.Capacity == command.ObservedCapacity, ct);

    public Task<ErrorOr<CoverageSlotAssignmentV1>> RecoverSlotAsync(MerchantBillingCaller caller, Guid account, Guid term, Guid key,
        CancellationToken ct) => ReadAffiliationAsync<CoverageSlotAssignmentV1>(caller, new(CoverageSlotsV1.Recover, HttpMethod.Get,
            $"{CoverageSlotsV1.Path(account, term)}/operations/{key:D}", account, []), row => row.TermId == term && row.AccountId == account, ct);

    public async Task<ErrorOr<CoverageSlotOwnerV1>> SlotConsentAsync(MerchantBillingCaller caller, Guid account, Guid shop,
        Guid assignment, ShopAffiliationConsentAction action, Guid key, CancellationToken ct)
    {
        if (action != ShopAffiliationConsentAction.Read && key == Guid.Empty) return MerchantBillingErrors.OperationKeyInvalid;
        try
        {
            if (!await consents.IsOwnerAsync(caller, shop, ct)) return ShopAffiliationConsentErrors.OwnerRequired;
            var detail = await ReadAffiliationAsync<CoverageSlotAssignmentV1>(caller, new(CoverageSlotsV1.OwnerRead, HttpMethod.Get,
                CoverageSlotsV1.OwnerPath(account, shop, assignment), null, []), r => r.AccountId == account && r.ShopId == shop &&
                    r.AssignmentId == assignment, ct);
            return detail.IsError ? detail.Errors : await slotConsents.RunAsync(caller, detail.Value, action, key, ct);
        }
        catch (Exception error) when (error is Npgsql.NpgsqlException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return Unavailable("CoverageSlotStorageUnavailable", error);
        }
    }

    private static Error SlotError(string? code) => code switch
    {
        "Coverage.SlotInvalid" => Error.Validation(code),
        "Coverage.SlotNotFound" => Error.NotFound(code),
        "Coverage.SlotUnavailable" or "Coverage.SlotTermUnsupported" or "Coverage.SlotSourceChanged"
            or "Coverage.SlotShopIneligible" or "Coverage.SlotShopBusy" => Error.Conflict(code),
        "Billing.OperationPayloadMismatch" => MerchantBillingErrors.PayloadMismatch,
        _ => MerchantBillingErrors.Unavailable,
    };
}
