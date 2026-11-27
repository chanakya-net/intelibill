using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;

namespace Intelibill.Licensing.Integration.Tests;

internal sealed partial class FakeRazorpay
{
    internal HttpStatusCode RefundGetFailure { get; set; } = HttpStatusCode.ServiceUnavailable;

    internal List<JsonObject> Refunds { get; } = [];

    private HttpResponseMessage? RefundRoute(HttpRequestMessage request, string[] parts, string? body)
    {
        if (parts is ["v1", "payments", var payment, "refund"] && request.Method == HttpMethod.Post)
        {
            var command = JsonNode.Parse(body!)!;
            Assert.Equal("normal", command["speed"]!.GetValue<string>());
            var row = RefundRecord(payment, command["receipt"]!.GetValue<string>(), command["amount"]!.GetValue<long>());
            Refunds.Add(row);
            return Json(HttpStatusCode.OK, Copy(row));
        }
        if (request.Method != HttpMethod.Get) return null;
        if (parts is ["v1", "payments", var original, "refunds"])
        {
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            var skip = int.Parse(query["skip"]!, System.Globalization.CultureInfo.InvariantCulture);
            var count = int.Parse(query["count"]!, System.Globalization.CultureInfo.InvariantCulture);
            return Collection(Refunds.Where(r => r["payment_id"]!.GetValue<string>() == original).Skip(skip).Take(count));
        }
        if (parts is ["v1", "payments", var pay, "refunds", var reference])
        {
            if (FailRefundGets > 0)
            {
                FailRefundGets--;
                return Json(RefundGetFailure, new JsonObject());
            }
            if (Refunds.SingleOrDefault(r => r["id"]!.GetValue<string>() == reference && r["payment_id"]!.GetValue<string>() == pay) is { } found)
                return Json(HttpStatusCode.OK, Copy(found));
        }
        return null;
    }

    internal JsonObject RefundRecord(string payment, string receipt, long amount) => new()
    {
        ["entity"] = "refund",
        ["id"] = $"rfnd_fixture{++sequence:D6}",
        ["amount"] = amount,
        ["currency"] = "INR",
        ["payment_id"] = payment,
        ["receipt"] = receipt,
        ["status"] = "processed",
    };
}
