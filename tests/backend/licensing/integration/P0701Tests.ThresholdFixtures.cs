using System.Net;
using System.Net.Http.Json;
using Intelibill.Domain.Enums;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0701Tests
{
    internal static async Task ExtendTrialAsync(
        P0110CookieBrowser catalogueManager,
        Guid accountId,
        Guid shopId,
        DateTimeOffset extendedEnd)
    {
        var extPath = $"/api/admin/accounts/{accountId:D}/shops/{shopId:D}/trial-extensions";
        using var previewResp = await P0212Fixture.PostAsync(
            catalogueManager,
            extPath + "/preview",
            new { endsAt = extendedEnd.ToString("O"), reason = "Extended pilot", reviewReference = "PILOT-924" });
        Assert.Equal(HttpStatusCode.OK, previewResp.StatusCode);
        var extPreview = await P0206Fixture.BodyAsync(previewResp);
        using var extSaved = await P0212Fixture.PostAsync(
            catalogueManager,
            extPath,
            new
            {
                operationKey = Guid.NewGuid(),
                endsAt = extPreview.GetProperty("endsAt").GetString(),
                reason = "Extended pilot",
                reviewReference = "PILOT-924",
                currentVersion = extPreview.GetProperty("currentVersion").GetInt64(),
                sourceHash = extPreview.GetProperty("sourceHash").GetString(),
                confirmed = true,
            });
        Assert.Equal(HttpStatusCode.Created, extSaved.StatusCode);
    }

    internal static async Task ReleaseRestrictionAsync(
        P0110CookieBrowser secAdmin,
        Guid accountId,
        Guid restrictionId)
    {
        var path = $"/api/admin/accounts/{accountId:D}/restrictions/{restrictionId:D}/release";
        var command = await P0404Tests.PreviewReleaseAsync(secAdmin, path);
        using var released = await P0212Fixture.PostAsync(secAdmin, path, command);
        Assert.Equal(HttpStatusCode.Created, released.StatusCode);
    }

    internal static async Task ApproveShortenedRefundAsync(
        P0308Fixture paid,
        P0308Fixture.Package package,
        Guid orderId,
        DateTimeOffset shortenedEnd)
    {
        var confirmation = await P0608Tests.ConfirmationIdAsync(package.Payer.Client, package.Account, orderId);
        var refundRequest = await P0609Tests.RequestRefundAsync(package, orderId, confirmation, 6000L);
        using var billingAdmin = await P0306Tests.SignInBillingAsync(paid.Checkout);
        var preview = await P0609Tests.PreviewAsync(billingAdmin, package.Account, refundRequest, 4500L, "EndAt", shortenedEnd);
        using var approved = await P0609Tests.ApproveAsync(
            billingAdmin, package.Account, refundRequest, P0609Tests.Command(preview, 4500L, "EndAt", shortenedEnd));
        Assert.Equal(HttpStatusCode.Created, approved.StatusCode);
        await P0610Tests.PumpRefundsAsync(paid);
        await P0608Tests.DrainAsync(paid);
    }

    private static async Task GrantComplimentaryAsync(
        P0110CookieBrowser compManager,
        Guid accountId,
        Guid shopId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt)
    {
        var compBase = P0212Fixture.Base(accountId);
        using var prevResp = await P0212Fixture.PostAsync(
            compManager,
            compBase + "/preview",
            new { shopIds = new[] { shopId }, startsAt = startsAt.ToString("O"), endsAt = endsAt.ToString("O"), memberAllowance = 3, reason = "Pilot courtesy" });
        Assert.Equal(HttpStatusCode.OK, prevResp.StatusCode);
        var prev = await P0206Fixture.BodyAsync(prevResp);
        using var saved = await P0212Fixture.PostAsync(
            compManager,
            compBase,
            new
            {
                operationKey = Guid.NewGuid(),
                shopIds = new[] { shopId },
                startsAt = startsAt.ToString("O"),
                endsAt = endsAt.ToString("O"),
                memberAllowance = 3,
                reason = "Pilot courtesy",
                expectedVersion = prev.GetProperty("expectedVersion").GetInt64(),
                sourceHash = prev.GetProperty("sourceHash").GetString(),
                confirmed = true,
            });
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
    }
}
