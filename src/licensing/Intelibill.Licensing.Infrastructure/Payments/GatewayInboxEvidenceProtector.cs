using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Microsoft.AspNetCore.DataProtection;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed class GatewayInboxEvidenceProtector(IDataProtectionProvider protection) : IGatewayInboxEvidence
{
    public byte[] Protect(VerifiedGatewayEvent parsed) =>
        protection.CreateProtector("InteliBill.Licensing.GatewayInbox/1", parsed.Pin.ProviderId,
            parsed.Pin.ProviderAccountId, parsed.Pin.Environment, parsed.Pin.ConfigurationVersion, parsed.ProviderEventId)
            .Protect(parsed.Refund is { } hint
                ? JsonSerializer.SerializeToUtf8Bytes(new { type = parsed.ProviderKind, account = parsed.PayloadAccountId, refund = hint })
                : JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = parsed.ProviderKind,
                account = parsed.PayloadAccountId,
                occurredAt = parsed.OccurredAt,
                state = parsed.Payment?.State.ToString(),
                amountPaise = parsed.Payment?.AmountPaise,
                currency = parsed.Payment?.Currency
            }));
}
