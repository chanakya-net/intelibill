using System.Net;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient : IMerchantRefundRequests
{
    public Task<ErrorOr<RefundRequestV1>> CreateRefundRequestAsync(MerchantBillingCaller caller, Guid account,
        CreateRefundRequestV1 request, CancellationToken ct)
    {
        if (!TrySerializeCreateRefundRequest(request, out var body))
            return Task.FromResult<ErrorOr<RefundRequestV1>>(InvalidRefundRequestError());

        return RunAsync<RefundRequestV1>(caller, new(RefundRequestsV1.Create, HttpMethod.Post, RefundRequestsV1.Path(account),
            account, body), RefundRequestsV1.MaxRequestBytes,
            async (response, token) =>
            {
                var created = await ReadJsonAsync<RefundRequestV1>(response, token);
                var normalized = RefundRequestsV1.NormalizeReason(request.Reason);
                return IsRefundRequestValid(created, account) &&
                    created!.OrderId == request.OrderId && created.ConfirmationId == request.ConfirmationId &&
                    created.AmountPaise == request.AmountPaise && normalized is not null && created.Reason == normalized
                    ? created : Unavailable("InvalidRefundRequest");
            }, ct);
    }

    internal static bool TrySerializeCreateRefundRequest(CreateRefundRequestV1 request, out byte[] body)
    {
        body = JsonSerializer.SerializeToUtf8Bytes(request, Json);
        return body.Length <= RefundRequestsV1.MaxCreateBytes;
    }

    public Task<ErrorOr<RefundRequestPageV1>> ListRefundRequestsAsync(MerchantBillingCaller caller, Guid account,
        Guid? after, CancellationToken ct) =>
        RunAsync<RefundRequestPageV1>(caller, new(RefundRequestsV1.List, HttpMethod.Get,
            RefundRequestsV1.ListPath(account, after), account, []), RefundRequestsV1.MaxPageBytes,
            async (response, token) =>
            {
                var page = await ReadJsonAsync<RefundRequestPageV1>(response, token);
                return IsRefundPageValid(page, account, after) ? page! : Unavailable("InvalidRefundRequestPage");
            }, ct);

    /// <summary>Structural trust boundary for a relayed refund request: must belong to this account and stay valid.</summary>
    internal static bool IsRefundRequestValid(RefundRequestV1? r, Guid account) =>
        r is not null && r.SchemaVersion == 1 && r.RefundRequestId != Guid.Empty && r.AccountId == account &&
        r.OrderId != Guid.Empty && r.ConfirmationId != Guid.Empty && RefundRequestsV1.States.Contains(r.Status) &&
        r.PaymentAmountPaise is > 0 and <= CheckoutOrdersV1.MaxPaise &&
        r.AmountPaise is > 0 && r.AmountPaise <= r.PaymentAmountPaise &&
        ValidRefundOutcome(r) && r.Currency == "INR" && r.Reason is not null && RefundRequestsV1.NormalizeReason(r.Reason) == r.Reason &&
        EntitlementSnapshotsV1.IsInstant(r.RequestedAt);

    private static bool ValidRefundOutcome(RefundRequestV1 row)
    {
        if (row is { ApprovedAmountPaise: null, ProviderState: null, AccessDisposition: null }) return true;
        if (row.ApprovedAmountPaise is not > 0 || row.ApprovedAmountPaise > row.AmountPaise) return false;
        return row.ProviderState switch
        {
            "Manual" or "Historical" => row.Status == "Pending" && row.AccessDisposition == "Applied",
            "Reserved" or "Pending" => row.Status == "Pending" && row.AccessDisposition == "AwaitingConfirmation",
            "Failed" or "Unknown" => row.Status == row.ProviderState && row.AccessDisposition == "AwaitingConfirmation",
            "Confirmed" => row.Status == "Confirmed" && row.AccessDisposition is "Applied" or "NeedsReview",
            _ => false,
        };
    }

    internal static bool IsRefundPageValid(RefundRequestPageV1? page, Guid account, Guid? after)
    {
        if (page is null || page.SchemaVersion != 1 || page.AccountId != account ||
            page.Items is not { Length: <= RefundRequestsV1.PageSize } items)
            return false;
        DateTimeOffset? previousAt = null;
        Guid? previousId = null;
        var seen = new HashSet<Guid>(items.Length);
        foreach (var item in items)
        {
            if (!IsRefundRequestValid(item, account) || !seen.Add(item.RefundRequestId) || item.RefundRequestId == after)
                return false;
            if (previousAt is { } lastAt && (item.RequestedAt < lastAt || (item.RequestedAt == lastAt &&
                    string.CompareOrdinal(item.RefundRequestId.ToString("D"), previousId!.Value.ToString("D")) <= 0)))
                return false;
            previousAt = item.RequestedAt;
            previousId = item.RefundRequestId;
        }
        return page.NextCursor is null || (items.Length == RefundRequestsV1.PageSize && page.NextCursor == previousId);
    }

    private static Error InvalidRefundRequestError() => Error.Validation(RefundRequestsV1.Invalid,
        "Provide a valid operation key, payment confirmation reference and refund reason.");

    private Error MapRefundRequestFailure(HttpStatusCode status, string? code) => (status, code) switch
    {
        (HttpStatusCode.BadRequest, RefundRequestsV1.Invalid) => InvalidRefundRequestError(),
        (HttpStatusCode.BadRequest, RefundRequestsV1.AmountInvalid) => Error.Validation(code,
            "Enter a positive whole-paise refund amount up to the verified payment amount."),
        (HttpStatusCode.NotFound, RefundRequestsV1.PaymentNotFound) => Error.NotFound(code,
            "The verified payment confirmation was not found for this billing account."),
        (HttpStatusCode.Conflict, "Billing.OperationPayloadMismatch") => MerchantBillingErrors.PayloadMismatch,
        _ => Unavailable("UpstreamStatus"),
    };
}
