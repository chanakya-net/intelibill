using System.Text.Json;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0608Tests
{
    [Fact]
    public void RefundRequestContractPinsHashStatesBoundsAndMigrationAction()
    {
        var contractPath = Path.Combine(ProjectPaths.Root, "tests/contracts/licensing/refund-request-v1.json");
        using var contract = JsonDocument.Parse(File.ReadAllText(contractPath));
        var root = contract.RootElement;

        Assert.Equal(RefundRequestsV1.Policy, root.GetProperty("policy").GetString());
        Assert.Equal(RefundRequestsV1.Create, root.GetProperty("actions").GetProperty("create").GetString());
        Assert.Equal(RefundRequestsV1.List, root.GetProperty("actions").GetProperty("list").GetString());
        Assert.Equal(RefundRequestsV1.States,
            root.GetProperty("states").EnumerateArray().Select(item => item.GetString()!).ToArray());

        var codes = root.GetProperty("errorCodes");
        Assert.Equal(RefundRequestsV1.Invalid, codes.GetProperty("invalid").GetString());
        Assert.Equal(RefundRequestsV1.AmountInvalid, codes.GetProperty("amountInvalid").GetString());
        Assert.Equal(RefundRequestsV1.PaymentNotFound, codes.GetProperty("paymentNotFound").GetString());

        var bounds = root.GetProperty("bounds");
        Assert.Equal(RefundRequestsV1.MaxReasonLength, bounds.GetProperty("maxReasonLength").GetInt32());
        Assert.Equal(RefundRequestsV1.PageSize, bounds.GetProperty("pageSize").GetInt32());
        Assert.Equal(RefundRequestsV1.MaxCreateBytes, bounds.GetProperty("maxCreateBytes").GetInt32());
        Assert.Equal(RefundRequestsV1.MaxRequestBytes, bounds.GetProperty("maxRequestBytes").GetInt32());
        Assert.Equal(RefundRequestsV1.MaxPageBytes, bounds.GetProperty("maxPageBytes").GetInt32());

        var vector = root.GetProperty("createHashVector");
        var actualHash = RefundRequestsV1.CreateHash(
            vector.GetProperty("orderId").GetGuid(),
            vector.GetProperty("confirmationId").GetGuid(),
            vector.GetProperty("amountPaise").GetInt64(),
            vector.GetProperty("reason").GetString()!);
        Assert.Equal(vector.GetProperty("expectedHash").GetString(), actualHash);

        // Pin customer DTO field names (provider-neutral: no gateway fields).
        var sample = new RefundRequestV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            RefundRequestsV1.Requested, 4500L, 12000L, "INR", "Sample reason",
            P0209Fixture.Utc("2028-01-31T04:30:00Z"));
        using var sampleDoc = JsonDocument.Parse(JsonSerializer.Serialize(sample, JsonSerializerOptions.Web));
        var actualFields = sampleDoc.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();
        var expectedFields = root.GetProperty("customerFields").EnumerateArray()
            .Select(item => item.GetString()!).Order().ToArray();
        Assert.Equal(expectedFields, actualFields);
        var settled = sample with { Status = RefundRequestsV1.Confirmed, ApprovedAmountPaise = 2000,
            AccessDisposition = "Applied", ProviderState = "Confirmed" };
        using var settledDoc = JsonDocument.Parse(JsonSerializer.Serialize(settled, JsonSerializerOptions.Web));
        var extra = settledDoc.RootElement.EnumerateObject().Select(field => field.Name).Except(actualFields).Order();
        Assert.Equal(root.GetProperty("optionalOutcomeFields").EnumerateArray().Select(value => value.GetString()!).Order(), extra);
    }

    [Fact]
    public void FullRefundRequestPageFitsRelayBoundAtMaximumContractValues()
    {
        var account = Guid.NewGuid();
        // Worst-case escaping in System.Text.Json: characters outside ASCII range (\uXXXX = 6 UTF-8 bytes each).
        var maxReason = new string('₹', RefundRequestsV1.MaxReasonLength);
        var items = Enumerable.Range(0, RefundRequestsV1.PageSize).Select(_ => new RefundRequestV1(
            Guid.NewGuid(), account, Guid.NewGuid(), Guid.NewGuid(), RefundRequestsV1.Pending,
            CheckoutOrdersV1.MaxPaise, CheckoutOrdersV1.MaxPaise, "INR", maxReason,
            P0209Fixture.Utc("9998-12-31T23:59:59.999999Z"))
            { ApprovedAmountPaise = CheckoutOrdersV1.MaxPaise, ProviderState = "Reserved", AccessDisposition = "AwaitingConfirmation" }).ToArray();
        var page = new RefundRequestPageV1(account, items, items[^1].RefundRequestId);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(page, JsonSerializerOptions.Web);
        Assert.InRange(bytes.Length, 1, RefundRequestsV1.MaxPageBytes);

        var create = new CreateRefundRequestV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            CheckoutOrdersV1.MaxPaise, maxReason);
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(create, JsonSerializerOptions.Web).Length,
            1, RefundRequestsV1.MaxCreateBytes);
    }

    [Fact]
    public void RefundRequestSliceDoesNotDependOnPaymentGateways()
    {
        var dirs = new[]
        {
            Path.Combine(ProjectPaths.Root, "src/backend/licensing/Intelibill.Licensing.Application/Refunds"),
            Path.Combine(ProjectPaths.Root, "src/backend/licensing/Intelibill.Licensing.Infrastructure/Refunds"),
        };
        foreach (var dir in dirs)
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(file);
                Assert.DoesNotContain("IPaymentGateway", source, StringComparison.Ordinal);
                Assert.DoesNotContain("RequestRefundAsync", source, StringComparison.Ordinal);
            }
        }
    }
}
