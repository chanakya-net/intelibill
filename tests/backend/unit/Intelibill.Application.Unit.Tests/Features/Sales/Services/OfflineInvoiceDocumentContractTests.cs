using System.Globalization;
using System.Text.Json;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Services;

/// <summary>
/// The device prints an offline sale from a document it builds itself; the server issues its own on
/// sync. tests/contracts/offline-invoice-document-v2.json pins both builders to the same answers.
/// </summary>
public class OfflineInvoiceDocumentContractTests
{
    private const string ContractFile = "offline-invoice-document-v2.json";

    public static TheoryData<string> CaseNames()
    {
        using var contract = LoadContract();
        var names = new TheoryData<string>();
        foreach (var testCase in contract.RootElement.GetProperty("cases").EnumerateArray())
            names.Add(testCase.GetProperty("name").GetString()!);
        return names;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Build_MatchesTheSharedOfflineContract(string caseName)
    {
        using var contract = LoadContract();
        var root = contract.RootElement;
        var testCase = root.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("name").GetString() == caseName);
        var issuedAt = DateTimeOffset.Parse(root.GetProperty("issuedAt").GetString()!, CultureInfo.InvariantCulture);
        var profile = Profile(root.GetProperty("sellers").GetProperty(testCase.GetProperty("seller").GetString()!));
        var shopId = Guid.NewGuid();
        var customer = Customer(shopId, testCase.GetProperty("customer"));
        var lines = testCase.GetProperty("lines").EnumerateArray().Select(line => Line(shopId, line)).ToList();

        var sale = Sale.Record(
            shopId,
            Guid.NewGuid(),
            "key",
            "hash",
            "A/2627/000001",
            lines,
            customer?.Id,
            OptionalString(testCase, "saleCustomerName"),
            OptionalString(testCase, "saleCustomerPhone"),
            PaymentMethod.Cash,
            lines.Sum(l => l.TotalAmount!.Value),
            0m,
            issuedAt);
        Assert.False(sale.IsError, sale.IsError ? sale.FirstError.Code : null);

        var result = InvoiceDocumentBuilder.Build(
            new InvoiceIssueContext(profile, customer, PlaceOfSupplyStateCode: null, issuedAt, new FiscalYear(root.GetProperty("fiscalYearStart").GetInt32())),
            sale.Value);

        if (testCase.TryGetProperty("expectedError", out var expectedError))
        {
            Assert.True(result.IsError);
            Assert.Equal(expectedError.GetString(), result.FirstError.Code);
            return;
        }

        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        var document = result.Value;
        var expected = testCase.GetProperty("expected");

        Assert.Equal(DateOnly.Parse(root.GetProperty("businessDate").GetString()!, CultureInfo.InvariantCulture), document.BusinessDate);
        Assert.Equal(expected.GetProperty("documentType").GetString(), document.DocumentType.ToString());
        Assert.Equal(expected.GetProperty("isInterstate").GetBoolean(), document.IsInterstate);
        Assert.Equal(expected.GetProperty("placeOfSupplyStateCode").GetString(), document.PlaceOfSupplyStateCode);
        Assert.Equal(expected.GetProperty("placeOfSupplyStateName").GetString(), document.PlaceOfSupplyStateName);
        Assert.Equal(profile.Seller, document.Seller);
        Assert.Equal(profile.SeriesCode, document.SeriesCode);

        var buyer = expected.GetProperty("buyer");
        Assert.Equal(
            (OptionalString(buyer, "name"), OptionalString(buyer, "phone"), OptionalString(buyer, "address"), OptionalString(buyer, "gstin"), OptionalString(buyer, "stateCode"), OptionalString(buyer, "stateName")),
            (document.Buyer.Name, document.Buyer.Phone, document.Buyer.Address, document.Buyer.Gstin, document.Buyer.StateCode, document.Buyer.StateName));

        Assert.Equal(
            expected.GetProperty("declarations").EnumerateArray().Select(d => d.GetString()),
            document.Declarations.Select(d => d.ToString()));

        var expectedLines = expected.GetProperty("lines").EnumerateArray().ToList();
        var inputLines = testCase.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(expectedLines.Count, document.Lines.Count);
        for (var i = 0; i < expectedLines.Count; i++)
        {
            var line = document.Lines[i];
            var input = inputLines[i];
            Assert.Equal(i + 1, line.LineNumber);
            Assert.Equal(input.GetProperty("description").GetString(), line.Description);
            Assert.Equal(OptionalString(input, "hsn"), line.HsnOrSac);
            Assert.Equal(expectedLines[i].GetProperty("uom").GetString(), line.Uom);
            Assert.Equal(input.GetProperty("quantity").GetDecimal(), line.Quantity);
            Assert.Equal(input.GetProperty("unitPrice").GetDecimal(), line.UnitPrice);
            Assert.Equal(input.GetProperty("discountAmount").GetDecimal(), line.DiscountAmount);
            Assert.Equal(input.GetProperty("taxableAmount").GetDecimal(), line.TaxableValue);
            Assert.Equal(input.GetProperty("lineTotal").GetDecimal(), line.LineTotal);
            Assert.Equal(expectedLines[i].GetProperty("isExempt").GetBoolean(), line.IsExempt);
            Assert.Equal(Components(expectedLines[i].GetProperty("components")), Components(line.TaxComponents));
        }

        var totals = expected.GetProperty("totals");
        Assert.Equal(totals.GetProperty("taxableValue").GetDecimal(), document.Totals.TaxableValue);
        Assert.Equal(totals.GetProperty("exemptValue").GetDecimal(), document.Totals.ExemptValue);
        Assert.Equal(totals.GetProperty("totalTax").GetDecimal(), document.Totals.TotalTax);
        Assert.Equal(totals.GetProperty("invoiceTotal").GetDecimal(), document.Totals.InvoiceTotal);
        Assert.Equal(0m, document.Totals.RoundingAdjustment);
        Assert.Equal(Components(totals.GetProperty("components")), Components(document.Totals.Components));
    }

    private static JsonDocument LoadContract() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", ContractFile)));

    private static InvoiceSellerProfile Profile(JsonElement element)
    {
        var seller = element.GetProperty("seller");
        return new InvoiceSellerProfile(
            new InvoiceSeller(
                seller.GetProperty("legalName").GetString()!,
                seller.GetProperty("tradeName").GetString()!,
                seller.GetProperty("address").GetString()!,
                seller.GetProperty("stateCode").GetString()!,
                seller.GetProperty("stateName").GetString()!,
                OptionalString(seller, "gstin"),
                Enum.Parse<TaxRegime>(seller.GetProperty("taxRegime").GetString()!)),
            Enum.Parse<EInvoiceStatus>(element.GetProperty("eInvoiceStatus").GetString()!),
            element.GetProperty("seriesCode").GetString()!);
    }

    private static Customer? Customer(Guid shopId, JsonElement element) =>
        element.ValueKind == JsonValueKind.Null
            ? null
            : Domain.Entities.Customer.Create(
                shopId,
                element.GetProperty("name").GetString()!,
                element.GetProperty("phone").GetString()!,
                OptionalString(element, "address"),
                gstin: OptionalString(element, "gstin"),
                stateCode: OptionalString(element, "stateCode"));

    private static SaleLineInput Line(Guid shopId, JsonElement line)
    {
        var isService = line.GetProperty("lineType").GetString() == "service";
        var quantity = line.GetProperty("quantity").GetDecimal();
        var unitPrice = line.GetProperty("unitPrice").GetDecimal();
        var discount = line.GetProperty("discountAmount").GetDecimal();
        return new SaleLineInput(
            shopId,
            isService ? SaleLineType.Service : SaleLineType.Goods,
            isService ? null : Guid.NewGuid(),
            isService ? null : Guid.NewGuid(),
            isService ? Guid.NewGuid() : null,
            line.GetProperty("description").GetString()!,
            "CODE",
            quantity,
            CostPrice: 0m,
            unitPrice,
            Mrp: unitPrice,
            line.GetProperty("taxRatePercent").GetDecimal(),
            IsPriceIncludingTax: false,
            HasPriceMismatch: false,
            PreTaxAmountBeforeDiscount: quantity * unitPrice,
            ItemDiscountAmount: discount,
            TaxableAmount: line.GetProperty("taxableAmount").GetDecimal(),
            TaxAmount: line.GetProperty("taxAmount").GetDecimal(),
            TotalAmount: line.GetProperty("lineTotal").GetDecimal(),
            HsnCode: OptionalString(line, "hsn"));
    }

    private static List<(string Kind, decimal Rate, decimal Base, decimal Amount)> Components(JsonElement components) =>
        components.EnumerateArray()
            .Select(c => (c[0].GetString()!, c[1].GetDecimal(), c[2].GetDecimal(), c[3].GetDecimal()))
            .ToList();

    private static List<(string Kind, decimal Rate, decimal Base, decimal Amount)> Components(IEnumerable<InvoiceTaxComponent> components) =>
        components.Select(c => (c.Kind.ToString(), c.RatePercent, c.TaxableBase, c.Amount)).ToList();

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
