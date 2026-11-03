using System.Text.Json;
using System.Text.Json.Serialization;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.SyncOfflineSales;

public class OfflineInvoiceArithmeticValidatorTests
{
    // The hand-written contract cases plus a random corpus, both with expected amounts from an independent exact-rational reference.
    private static readonly Lazy<IReadOnlyList<JsonElement>> Version1Cases = new(() =>
        [.. ContractCases("offline-invoice-arithmetic-v1.json"), .. ContractCases("offline-invoice-arithmetic-v1-corpus.json")]);

    private static readonly JsonSerializerOptions CorpusOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly decimal[] OnePaiseOverThreeLines = [0.01m, 0m, 0m];
    private static readonly decimal[] SixPaiseOverTenLines = [0.01m, 0.01m, 0.01m, 0.01m, 0.01m, 0.01m, 0m, 0m, 0m, 0m];

    public static TheoryData<string> FixtureCaseNames()
    {
        var names = new TheoryData<string>();
        foreach (var testCase in Version1Cases.Value)
            names.Add(testCase.GetProperty("name").GetString()!);
        return names;
    }

    [Theory]
    [MemberData(nameof(FixtureCaseNames))]
    public void Fixture_case_is_consistent_under_version1(string caseName)
    {
        var verdict = OfflineInvoiceArithmeticValidator.Validate(FromFixture(caseName, calculationVersion: 1));

        Assert.Equal("v1", verdict.Contract);
        Assert.Empty(verdict.Mismatches);
    }

    [Theory]
    [MemberData(nameof(FixtureCaseNames))]
    public void Fixture_case_rejects_a_single_paise_change(string caseName)
    {
        var sale = FromFixture(caseName, calculationVersion: 1);
        var lastIndex = sale.Items.Count - 1;
        var items = sale.Items.ToList();
        items[lastIndex] = items[lastIndex] with
        {
            TaxAmount = items[lastIndex].TaxAmount + 0.01m,
            TotalAmount = items[lastIndex].TotalAmount + 0.01m,
        };
        var tampered = sale with
        {
            Items = items,
            TotalTaxAmount = sale.TotalTaxAmount + 0.01m,
            TotalAmount = sale.TotalAmount + 0.01m,
            TotalBeforeDiscount = sale.TotalBeforeDiscount + 0.01m,
            DueAmount = sale.DueAmount + 0.01m,
        };

        var verdict = OfflineInvoiceArithmeticValidator.Validate(tampered);

        Assert.Contains(verdict.Mismatches, m => m.Path == $"items[{lastIndex}].taxAmount" && m.Code == "amount_mismatch");
        Assert.Contains(verdict.Mismatches, m => m.Path == "totalAmount" && m.Code == "amount_mismatch");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void Header_must_equal_lines(int? calculationVersion)
    {
        var sale = Sale([Line(1m, 100m, 18m, false, preTax: 100m, taxable: 100m, tax: 18m, total: 118m)], calculationVersion) with
        {
            PaidAmount = 1m,
            DueAmount = 0m,
            SubtotalBeforeDiscount = 1m,
            TotalBeforeDiscount = 1m,
            TotalTaxAmount = 0m,
            TotalAmount = 1m,
        };

        var verdict = OfflineInvoiceArithmeticValidator.Validate(sale);

        var total = Assert.Single(verdict.Mismatches, m => m.Path == "totalAmount");
        Assert.Equal(1m, total.Supplied);
        Assert.Equal(118m, total.Expected);
        Assert.Equal("amount_mismatch", total.Code);
        Assert.Contains(verdict.Mismatches, m => m.Path == "totalTaxAmount" && m.Expected == 18m);
        Assert.Contains(verdict.Mismatches, m => m.Path == "subtotalBeforeDiscount" && m.Expected == 100m);
        Assert.DoesNotContain(verdict.Mismatches, m => m.Path.StartsWith("items[", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void Inclusive_exclusive_fractional_parity(int? calculationVersion)
    {
        var inclusive = Sale([Line(0.25m, 118m, 18m, true, preTax: 25m, taxable: 25m, tax: 4.5m, total: 29.5m)], calculationVersion);
        var exclusive = Sale([Line(0.25m, 100m, 18m, false, preTax: 25m, taxable: 25m, tax: 4.5m, total: 29.5m)], calculationVersion);
        var zeroRate = Sale([Line(3m, 33.33m, 0m, true, preTax: 99.99m, taxable: 99.99m, tax: 0m, total: 99.99m)], calculationVersion);
        var service = Sale([Line(2m, 59m, 18m, true, preTax: 100m, taxable: 100m, tax: 18m, total: 118m) with { LineType = "Service", ServiceId = Guid.NewGuid() }], calculationVersion);

        Assert.Empty(OfflineInvoiceArithmeticValidator.Validate(inclusive).Mismatches);
        Assert.Empty(OfflineInvoiceArithmeticValidator.Validate(exclusive).Mismatches);
        Assert.Empty(OfflineInvoiceArithmeticValidator.Validate(zeroRate).Mismatches);
        Assert.Empty(OfflineInvoiceArithmeticValidator.Validate(service).Mismatches);
    }

    [Fact]
    public void Allocation_remainder_is_deterministic()
    {
        var onePaise = FromFixture("one_paise_over_three_equal_lines", calculationVersion: 1);
        Assert.Equal(OnePaiseOverThreeLines, onePaise.Items.Select(i => i.SaleDiscountAmount));
        Assert.Empty(OfflineInvoiceArithmeticValidator.Validate(onePaise).Mismatches);

        var sixPaise = FromFixture("six_paise_over_ten_equal_lines", calculationVersion: 1);
        Assert.Equal(SixPaiseOverTenLines, sixPaise.Items.Select(i => i.SaleDiscountAmount));
        Assert.Empty(OfflineInvoiceArithmeticValidator.Validate(sixPaise).Mismatches);

        var moved = onePaise with { Items = [Allocated(onePaise.Items[0], 0m), Allocated(onePaise.Items[1], 0.01m), onePaise.Items[2]] };
        var verdict = OfflineInvoiceArithmeticValidator.Validate(moved);

        Assert.Contains(verdict.Mismatches, m => m.Path == "items[0].saleDiscountAmount" && m.Supplied == 0m && m.Expected == 0.01m);
        Assert.Contains(verdict.Mismatches, m => m.Path == "items[1].saleDiscountAmount" && m.Supplied == 0.01m && m.Expected == 0m);
    }

    [Fact]
    public void Legacy_final_line_residual_is_accepted_only_for_legacy_bills()
    {
        // The unversioned browser rounded each share and gave the last line the residual: 0.04 over three equal lines.
        var legacyAllocation = ThreeEqualLinesWithSaleDiscount(0.04m, [0.01m, 0.01m, 0.02m], calculationVersion: null);
        var version1Allocation = ThreeEqualLinesWithSaleDiscount(0.04m, [0.02m, 0.01m, 0.01m], calculationVersion: 1);

        Assert.Empty(OfflineInvoiceArithmeticValidator.Validate(legacyAllocation).Mismatches);
        Assert.Empty(OfflineInvoiceArithmeticValidator.Validate(version1Allocation).Mismatches);
        Assert.NotEmpty(OfflineInvoiceArithmeticValidator.Validate(legacyAllocation with { CalculationVersion = 1 }).Mismatches);
        Assert.NotEmpty(OfflineInvoiceArithmeticValidator.Validate(version1Allocation with { CalculationVersion = null }).Mismatches);
    }

    [Fact]
    public void Legacy_corpus_from_the_unversioned_browser_calculator_replays_exactly()
    {
        // Payloads priced by the browser calculator as it was before calculation versions (commit 41015861565d).
        using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "offline-invoice-arithmetic-legacy-corpus.json")));
        var sales = corpus.RootElement.GetProperty("sales").Deserialize<List<OfflineSaleSyncCommand>>(CorpusOptions)!;

        Assert.Equal(40, sales.Count);
        Assert.All(sales, sale => Assert.Empty(OfflineInvoiceArithmeticValidator.Validate(sale).Mismatches));
        Assert.All(sales, sale =>
        {
            var items = sale.Items.ToList();
            items[^1] = items[^1] with { TaxAmount = items[^1].TaxAmount + 0.01m, TotalAmount = items[^1].TotalAmount + 0.01m };
            var tampered = sale with { Items = items, TotalTaxAmount = sale.TotalTaxAmount + 0.01m, TotalAmount = sale.TotalAmount + 0.01m };
            Assert.NotEmpty(OfflineInvoiceArithmeticValidator.Validate(tampered).Mismatches);
        });
    }

    [Fact]
    public void Version1_inclusive_line_totals_its_shelf_price()
    {
        // ₹100 inclusive at 18%: 84.75 taxable + 15.25 tax. Rounding the 15.255 tax up would bill ₹100.01, above the shelf price.
        var shelfPrice = Sale([Line(1m, 100m, 18m, true, preTax: 84.75m, taxable: 84.75m, tax: 15.25m, total: 100m)], calculationVersion: 1);
        var aboveShelfPrice = Sale([Line(1m, 100m, 18m, true, preTax: 84.75m, taxable: 84.75m, tax: 15.26m, total: 100.01m)], calculationVersion: 1);

        Assert.Empty(OfflineInvoiceArithmeticValidator.Validate(shelfPrice).Mismatches);
        Assert.Contains(OfflineInvoiceArithmeticValidator.Validate(aboveShelfPrice).Mismatches, m => m.Path == "items[0].totalAmount" && m.Expected == 100m);
    }

    [Fact]
    public void Legacy_tofixed_midpoints_are_replayed_exactly()
    {
        // 6.70 x 15% = 1.005 exactly; the browser's double sat below the midpoint and printed 1.00.
        var legacyTie = Sale([Line(1m, 6.7m, 15m, false, preTax: 6.7m, taxable: 6.7m, tax: 1m, total: 7.7m)], calculationVersion: null);
        var legacyInclusive = Sale([Line(1m, 100m, 18m, true, preTax: 84.75m, taxable: 84.75m, tax: 15.25m, total: 100m)], calculationVersion: null);
        var version1Tie = Sale([Line(1m, 6.7m, 15m, false, preTax: 6.7m, taxable: 6.7m, tax: 1.01m, total: 7.71m)], calculationVersion: 1);

        Assert.Empty(OfflineInvoiceArithmeticValidator.Validate(legacyTie).Mismatches);
        Assert.Empty(OfflineInvoiceArithmeticValidator.Validate(legacyInclusive).Mismatches);
        Assert.Empty(OfflineInvoiceArithmeticValidator.Validate(version1Tie).Mismatches);

        var legacyClaimingHalfAway = OfflineInvoiceArithmeticValidator.Validate(version1Tie with { CalculationVersion = null });
        Assert.Contains(legacyClaimingHalfAway.Mismatches, m => m.Path == "items[0].taxAmount" && m.Expected == 1m && m.Supplied == 1.01m);

        var version1ClaimingFloat = OfflineInvoiceArithmeticValidator.Validate(legacyTie with { CalculationVersion = 1 });
        Assert.Contains(version1ClaimingFloat.Mismatches, m => m.Path == "items[0].taxAmount" && m.Expected == 1.01m && m.Supplied == 1m);
    }

    [Fact]
    public void Legacy_accepts_either_known_total_before_discount_definition()
    {
        var preTaxDefinition = Sale([Line(1m, 100m, 18m, false, preTax: 100m, taxable: 100m, tax: 18m, total: 118m)], calculationVersion: null) with { TotalBeforeDiscount = 100m };
        var domainDefinition = preTaxDefinition with { TotalBeforeDiscount = 118m };

        Assert.Empty(OfflineInvoiceArithmeticValidator.Validate(preTaxDefinition).Mismatches);
        Assert.Empty(OfflineInvoiceArithmeticValidator.Validate(domainDefinition).Mismatches);
        Assert.Contains(
            OfflineInvoiceArithmeticValidator.Validate(preTaxDefinition with { CalculationVersion = 1 }).Mismatches,
            m => m.Path == "totalBeforeDiscount" && m.Expected == 118m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void Discount_exceeds_basis(int? calculationVersion)
    {
        var sale = Sale([Line(1m, 100m, 18m, false, preTax: 100m, taxable: 0m, tax: 0m, total: 0m) with { ItemDiscountAmount = 150m }], calculationVersion) with
        {
            TotalDiscountAmount = 150m,
        };

        var verdict = OfflineInvoiceArithmeticValidator.Validate(sale);

        var discount = Assert.Single(verdict.Mismatches, m => m.Path == "items[0].itemDiscountAmount");
        Assert.Equal("discount_exceeds_basis", discount.Code);
        Assert.Equal(150m, discount.Supplied);
        Assert.Equal(0m, discount.Expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void Tampered_discount_within_basis_is_amount_mismatch(int? calculationVersion)
    {
        var sale = Sale([Line(1m, 100m, 18m, false, preTax: 100m, taxable: 90m, tax: 16.2m, total: 106.2m) with { ItemDiscountAmount = 10m }], calculationVersion) with
        {
            TotalDiscountAmount = 10m,
        };

        var verdict = OfflineInvoiceArithmeticValidator.Validate(sale);

        Assert.Contains(verdict.Mismatches, m => m.Path == "items[0].itemDiscountAmount" && m.Code == "amount_mismatch" && m.Expected == 0m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public void Unknown_version_is_unsupported(int calculationVersion)
    {
        var verdict = OfflineInvoiceArithmeticValidator.Validate(FromFixture("exclusive_single_line", calculationVersion));

        Assert.Equal("unsupported", verdict.Contract);
        Assert.True(verdict.IsUnsupportedVersion);
        var mismatch = Assert.Single(verdict.Mismatches);
        Assert.Equal(new OfflineArithmeticMismatch("calculationVersion", calculationVersion, 1m, "unsupported_version"), mismatch);
    }

    [Fact]
    public void Version1_rejects_inputs_beyond_contract_scale()
    {
        var sale = FromFixture("exclusive_single_line", calculationVersion: 1);
        var tampered = sale with { Items = [sale.Items[0] with { SalesPrice = 100.004m, Quantity = 1.0004m }] };

        var verdict = OfflineInvoiceArithmeticValidator.Validate(tampered);

        Assert.Contains(verdict.Mismatches, m => m.Path == "items[0].salesPrice" && m.Code == "scale_exceeded");
        Assert.Contains(verdict.Mismatches, m => m.Path == "items[0].quantity" && m.Code == "scale_exceeded");
    }

    [Fact]
    public void Undefined_enum_and_out_of_range_inputs_are_unsupported_values()
    {
        var sale = FromFixture("exclusive_single_line", calculationVersion: null);
        var tampered = sale with
        {
            SaleDiscountOverrideType = (InstantDiscountType)7,
            Items = [sale.Items[0] with { ItemDiscountOverrideType = (InstantDiscountType)9, TaxRatePercent = -100m }],
        };

        var verdict = OfflineInvoiceArithmeticValidator.Validate(tampered);

        Assert.Contains(verdict.Mismatches, m => m.Path == "saleDiscountOverrideType" && m.Code == "unsupported_value");
        Assert.Contains(verdict.Mismatches, m => m.Path == "items[0].itemDiscountOverrideType" && m.Code == "unsupported_value");
        Assert.Contains(verdict.Mismatches, m => m.Path == "items[0].taxRatePercent" && m.Code == "unsupported_value");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void Threshold_rule_must_be_met_by_eligible_subtotal(int? calculationVersion)
    {
        var sale = FromFixture("sale_threshold_rule_partial_payment", calculationVersion) with { ConfiguredSaleRuleThresholdAmount = 600.01m };

        var verdict = OfflineInvoiceArithmeticValidator.Validate(sale);

        var threshold = Assert.Single(verdict.Mismatches);
        Assert.Equal("configuredSaleRuleThresholdAmount", threshold.Path);
        Assert.Equal("rule_threshold_not_met", threshold.Code);
    }

    [Fact]
    public void Rule_without_its_percentage_is_missing_evidence()
    {
        var sale = FromFixture("batch_rule_with_lower_override", calculationVersion: 1);
        var tampered = sale with { Items = [sale.Items[0] with { ConfiguredBatchRulePercentage = null }] };

        var verdict = OfflineInvoiceArithmeticValidator.Validate(tampered);

        Assert.Contains(verdict.Mismatches, m => m.Path == "items[0].configuredBatchRulePercentage" && m.Code == "rule_evidence_missing");
    }

    [Fact]
    public void Describe_summarises_mismatches_within_the_issue_message_limit()
    {
        var sale = FromFixture("six_paise_over_ten_equal_lines", calculationVersion: 1);
        var tampered = sale with { Items = sale.Items.Select(i => i with { SalesPrice = i.SalesPrice + 1m }).ToList() };

        var verdict = OfflineInvoiceArithmeticValidator.Validate(tampered);
        var summary = verdict.Describe(1000);

        Assert.True(summary.Length <= 1000);
        Assert.StartsWith("Offline invoice arithmetic (v1) does not reconcile:", summary, StringComparison.Ordinal);
        Assert.Contains("items[0].preTaxAmountBeforeDiscount supplied 10.00 expected 11.00", summary, StringComparison.Ordinal);
    }

    private static IEnumerable<JsonElement> ContractCases(string fileName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", fileName)));
        return [.. document.RootElement.GetProperty("cases").EnumerateArray().Select(c => c.Clone())];
    }

    private static OfflineSaleSyncLineCommand Allocated(OfflineSaleSyncLineCommand line, decimal saleDiscount)
    {
        var taxable = line.PreTaxAmountBeforeDiscount - line.ItemDiscountAmount - saleDiscount;
        return line with { SaleDiscountAmount = saleDiscount, TaxableAmount = taxable, TotalAmount = taxable + line.TaxAmount };
    }

    private static OfflineSaleSyncCommand ThreeEqualLinesWithSaleDiscount(decimal saleDiscount, decimal[] allocation, int? calculationVersion)
    {
        var lines = allocation
            .Select(share => Line(1m, 10m, 0m, false, preTax: 10m, taxable: 10m - share, tax: 0m, total: 10m - share) with { SaleDiscountAmount = share })
            .ToList();
        return Sale(lines, calculationVersion) with
        {
            SaleDiscountOverrideType = InstantDiscountType.Flat,
            SaleDiscountOverrideValue = saleDiscount,
            TotalDiscountAmount = saleDiscount,
            TotalBeforeDiscount = 30m,
        };
    }

    private static OfflineSaleSyncLineCommand Line(
        decimal quantity,
        decimal salesPrice,
        decimal taxRatePercent,
        bool isPriceIncludingTax,
        decimal preTax,
        decimal taxable,
        decimal tax,
        decimal total) =>
        new(
            "BC-001",
            "B-01",
            "Rice",
            quantity,
            0m,
            salesPrice,
            0m,
            taxRatePercent,
            isPriceIncludingTax,
            Guid.NewGuid(),
            preTax,
            0m,
            0m,
            taxable,
            tax,
            total,
            null,
            null,
            InstantDiscountType.None,
            0m,
            null);

    private static OfflineSaleSyncCommand Sale(IReadOnlyList<OfflineSaleSyncLineCommand> lines, int? calculationVersion)
    {
        var total = lines.Sum(l => l.TotalAmount);
        return new OfflineSaleSyncCommand(
            "client-1",
            "INV-2025-26-000001",
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            PaymentMethod.Cash,
            total,
            0m,
            lines.Sum(l => l.PreTaxAmountBeforeDiscount),
            lines.Sum(l => l.TotalAmount + l.ItemDiscountAmount + l.SaleDiscountAmount),
            lines.Sum(l => l.ItemDiscountAmount + l.SaleDiscountAmount),
            lines.Sum(l => l.TaxAmount),
            total,
            InstantDiscountType.None,
            0m,
            null,
            null,
            null,
            null,
            lines,
            calculationVersion);
    }

    private static OfflineSaleSyncCommand FromFixture(string caseName, int? calculationVersion)
    {
        var testCase = Version1Cases.Value.Single(c => c.GetProperty("name").GetString() == caseName);
        var input = testCase.GetProperty("input");
        var expected = testCase.GetProperty("expected");
        var expectedLines = expected.GetProperty("lines").EnumerateArray().ToList();
        var lines = input.GetProperty("lines").EnumerateArray()
            .Select((line, index) =>
            {
                var isService = line.GetProperty("lineType").GetString() == "service";
                var amounts = expectedLines[index];
                var itemDiscount = line.GetProperty("itemDiscount");
                var batchRule = line.GetProperty("batchRulePercentage");
                return new OfflineSaleSyncLineCommand(
                    $"BC-{index:000}",
                    isService ? string.Empty : $"B-{index:000}",
                    $"Line {index}",
                    line.GetProperty("quantity").GetDecimal(),
                    0m,
                    line.GetProperty("salesPrice").GetDecimal(),
                    0m,
                    line.GetProperty("taxRatePercent").GetDecimal(),
                    line.GetProperty("taxIncluded").GetBoolean(),
                    isService ? Guid.Empty : Guid.NewGuid(),
                    amounts.GetProperty("preTaxAmount").GetDecimal(),
                    amounts.GetProperty("itemDiscountAmount").GetDecimal(),
                    amounts.GetProperty("saleDiscountAmount").GetDecimal(),
                    amounts.GetProperty("taxableAmount").GetDecimal(),
                    amounts.GetProperty("taxAmount").GetDecimal(),
                    amounts.GetProperty("lineTotal").GetDecimal(),
                    batchRule.ValueKind == JsonValueKind.Null ? null : Guid.NewGuid(),
                    batchRule.ValueKind == JsonValueKind.Null ? null : batchRule.GetDecimal(),
                    isService ? InstantDiscountType.None : (InstantDiscountType)itemDiscount.GetProperty("type").GetInt32(),
                    isService ? 0m : itemDiscount.GetProperty("value").GetDecimal(),
                    null,
                    isService ? "Service" : "Goods",
                    isService ? Guid.NewGuid() : null);
            })
            .ToList();

        var totals = expected.GetProperty("totals");
        var saleDiscount = input.GetProperty("saleDiscount");
        var saleRule = input.GetProperty("saleRule");
        var hasSaleRule = saleRule.ValueKind != JsonValueKind.Null;
        return new OfflineSaleSyncCommand(
            $"client-{caseName}",
            "INV-2025-26-000001",
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            PaymentMethod.Cash,
            totals.GetProperty("paidAmount").GetDecimal(),
            totals.GetProperty("dueAmount").GetDecimal(),
            totals.GetProperty("subtotalBeforeDiscount").GetDecimal(),
            totals.GetProperty("totalBeforeDiscount").GetDecimal(),
            totals.GetProperty("totalDiscount").GetDecimal(),
            totals.GetProperty("totalTax").GetDecimal(),
            totals.GetProperty("grandTotal").GetDecimal(),
            (InstantDiscountType)saleDiscount.GetProperty("type").GetInt32(),
            saleDiscount.GetProperty("value").GetDecimal(),
            hasSaleRule ? Guid.NewGuid() : null,
            hasSaleRule ? Enum.Parse<DiscountRuleType>(saleRule.GetProperty("ruleType").GetString()!) : null,
            hasSaleRule ? saleRule.GetProperty("percentage").GetDecimal() : null,
            hasSaleRule && saleRule.GetProperty("thresholdAmount").ValueKind != JsonValueKind.Null ? saleRule.GetProperty("thresholdAmount").GetDecimal() : null,
            lines,
            calculationVersion);
    }
}
