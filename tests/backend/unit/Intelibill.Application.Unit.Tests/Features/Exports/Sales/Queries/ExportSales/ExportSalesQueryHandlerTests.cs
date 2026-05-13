using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Application.Features.Exports.Sales.Queries.ExportSales;
using Intelibill.Application.Features.Exports.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Exports.Sales.Queries.ExportSales;

public class ExportSalesQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISalesExportDatasetBuilder _datasetBuilder = Substitute.For<ISalesExportDatasetBuilder>();
    private readonly ISalesExcelExportRenderer _excelRenderer = Substitute.For<ISalesExcelExportRenderer>();
    private readonly ISalesPdfExportRenderer _pdfRenderer = Substitute.For<ISalesPdfExportRenderer>();
    private readonly ISalesTallyXmlExportRenderer _tallyRenderer = Substitute.For<ISalesTallyXmlExportRenderer>();
    private readonly IExportFileNameBuilder _fileNameBuilder = Substitute.For<IExportFileNameBuilder>();

    private ExportSalesQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _datasetBuilder, _excelRenderer, _pdfRenderer, _tallyRenderer, _fileNameBuilder);

    private static User MakeUser() =>
        User.CreateWithEmail("export@test.com", "hash", "Export", "User");

    private static Shop MakeShop() =>
        Shop.Create("Test Shop", "123 Main St", "City", "State", "560001", null, null, null);

    private static ShopMembership MakeMembership(Guid shopId, Guid userId, ShopRole role) =>
        ShopMembership.Create(shopId, userId, role, true);

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsUserNotFoundError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var handler = CreateHandler();
        var query = new ExportSalesQuery(
            userId,
            shopId,
            "xlsx",
            "summary",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("user.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenShopNotFound_ReturnsShopNotFoundError()
    {
        var user = MakeUser();
        var shopId = Guid.NewGuid();

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns((Shop?)null);

        var handler = CreateHandler();
        var query = new ExportSalesQuery(
            user.Id,
            shopId,
            "xlsx",
            "summary",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.ShopNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenMembershipNotFound_ReturnsMembershipNotFoundError()
    {
        var user = MakeUser();
        var shop = MakeShop();

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns((ShopMembership?)null);

        var handler = CreateHandler();
        var query = new ExportSalesQuery(
            user.Id,
            shop.Id,
            "xlsx",
            "summary",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenUserIsStaff_ReturnsForbiddenError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var staffMembership = MakeMembership(shop.Id, user.Id, ShopRole.Staff);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(staffMembership);

        var handler = CreateHandler();
        var query = new ExportSalesQuery(
            user.Id,
            shop.Id,
            "xlsx",
            "summary",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Export.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenUserIsOwner_PassesAuthorizationCheck()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var ownerMembership = MakeMembership(shop.Id, user.Id, ShopRole.Owner);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(ownerMembership);
        _fileNameBuilder.BuildFileName(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns("test-shop-sales-summary.xlsx");
        _fileNameBuilder.GetContentType(Arg.Any<string>())
            .Returns("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        _excelRenderer.RenderAsync(Arg.Any<SalesExportDatasetDto>(), Arg.Any<CancellationToken>())
            .Returns(new SalesExportResult(Array.Empty<byte>(), "ct", "fn"));

        var handler = CreateHandler();
        var query = new ExportSalesQuery(
            user.Id,
            shop.Id,
            "xlsx",
            "summary",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        var result = await handler.Handle(query, CancellationToken.None);

        // Should not return authorization error
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task Handle_WhenUserIsManager_PassesAuthorizationCheck()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var managerMembership = MakeMembership(shop.Id, user.Id, ShopRole.Manager);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(managerMembership);
        _fileNameBuilder.BuildFileName(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns("test-shop-sales-summary.xlsx");
        _fileNameBuilder.GetContentType(Arg.Any<string>())
            .Returns("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        _excelRenderer.RenderAsync(Arg.Any<SalesExportDatasetDto>(), Arg.Any<CancellationToken>())
            .Returns(new SalesExportResult(Array.Empty<byte>(), "ct", "fn"));

        var handler = CreateHandler();
        var query = new ExportSalesQuery(
            user.Id,
            shop.Id,
            "xlsx",
            "summary",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        var result = await handler.Handle(query, CancellationToken.None);

        // Should not return authorization error
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task Handle_WhenFormatHasMixedCase_TallyXml_UsesTallyRendererWithLineItemDataset()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var ownerMembership = MakeMembership(shop.Id, user.Id, ShopRole.Owner);
        var dataset = new SalesExportDatasetDto(
            new SalesExportMetadataDto(
                "Test Shop",
                null,
                null,
                "Export User",
                DateTimeOffset.UtcNow,
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
                DateOnly.FromDateTime(DateTime.UtcNow),
                "summary"),
            Array.Empty<SalesExportSummaryRowDto>(),
            Array.Empty<SalesExportLineItemRowDto>(),
            Array.Empty<SalesExportTaxBreakupDto>(),
            []);
        var expectedResult = new SalesExportResult(Array.Empty<byte>(), "application/xml", "sales.xml");

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(ownerMembership);
        _datasetBuilder.BuildAsync(
            shop,
            user,
            Arg.Any<DateOnly>(),
            Arg.Any<DateOnly>(),
            SalesExportLevel.LineItems,
            Arg.Any<CancellationToken>())
            .Returns(dataset);
        _fileNameBuilder.BuildFileName(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns("test-shop-sales-tally.xml");
        _fileNameBuilder.GetContentType(Arg.Any<string>())
            .Returns("application/xml");
        _tallyRenderer.RenderAsync(Arg.Any<SalesExportDatasetDto>(), Arg.Any<CancellationToken>())
            .Returns(expectedResult);

        var handler = CreateHandler();
        var query = new ExportSalesQuery(
            user.Id,
            shop.Id,
            "TaLlYxMl",
            "summary",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.False(result.IsError);
        await _tallyRenderer.Received(1).RenderAsync(dataset, Arg.Any<CancellationToken>());
        await _datasetBuilder.Received(1).BuildAsync(
            shop,
            user,
            Arg.Any<DateOnly>(),
            Arg.Any<DateOnly>(),
            SalesExportLevel.LineItems,
            Arg.Any<CancellationToken>());
        await _pdfRenderer.DidNotReceive().RenderAsync(Arg.Any<SalesExportDatasetDto>(), Arg.Any<CancellationToken>());
        await _excelRenderer.DidNotReceive().RenderAsync(Arg.Any<SalesExportDatasetDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenFormatIsPdf_Summary_UsesPdfRenderer()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var ownerMembership = MakeMembership(shop.Id, user.Id, ShopRole.Owner);
        var dataset = new SalesExportDatasetDto(
            new SalesExportMetadataDto(
                shop.Name,
                null,
                null,
                "Export User",
                DateTimeOffset.UtcNow,
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
                DateOnly.FromDateTime(DateTime.UtcNow),
                SalesExportLevel.Summary),
            Array.Empty<SalesExportSummaryRowDto>(),
            Array.Empty<SalesExportLineItemRowDto>(),
            Array.Empty<SalesExportTaxBreakupDto>(),
            []);
        var expectedResult = new SalesExportResult([1, 2, 3], "application/pdf", "sales.pdf");

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(ownerMembership);
        _datasetBuilder.BuildAsync(
            shop,
            user,
            Arg.Any<DateOnly>(),
            Arg.Any<DateOnly>(),
            SalesExportLevel.Summary,
            Arg.Any<CancellationToken>()).Returns(dataset);
        _fileNameBuilder.BuildFileName(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns("test-shop-sales-summary.pdf");
        _fileNameBuilder.GetContentType(Arg.Any<string>()).Returns("application/pdf");
        _pdfRenderer.RenderAsync(Arg.Any<SalesExportDatasetDto>(), Arg.Any<CancellationToken>()).Returns(expectedResult);

        var handler = CreateHandler();
        var query = new ExportSalesQuery(
            user.Id,
            shop.Id,
            "pdf",
            "summary",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.False(result.IsError);
        await _pdfRenderer.Received(1).RenderAsync(dataset, Arg.Any<CancellationToken>());
        await _excelRenderer.DidNotReceive().RenderAsync(Arg.Any<SalesExportDatasetDto>(), Arg.Any<CancellationToken>());
        await _tallyRenderer.DidNotReceive().RenderAsync(Arg.Any<SalesExportDatasetDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenFormatIsPdf_LineItems_UsesPdfRenderer()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var ownerMembership = MakeMembership(shop.Id, user.Id, ShopRole.Owner);
        var dataset = new SalesExportDatasetDto(
            new SalesExportMetadataDto(
                shop.Name,
                null,
                null,
                "Export User",
                DateTimeOffset.UtcNow,
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
                DateOnly.FromDateTime(DateTime.UtcNow),
                SalesExportLevel.LineItems),
            Array.Empty<SalesExportSummaryRowDto>(),
            [new SalesExportLineItemRowDto("INV-001", "Alice", "Apple", 1m, 10m, 0m, 5m, 10m, 0.5m, 10.5m, false, 0m, null, null)],
            Array.Empty<SalesExportTaxBreakupDto>(),
            []);
        var expectedResult = new SalesExportResult([1, 2, 3], "application/pdf", "sales.pdf");

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(ownerMembership);
        _datasetBuilder.BuildAsync(
            shop,
            user,
            Arg.Any<DateOnly>(),
            Arg.Any<DateOnly>(),
            SalesExportLevel.LineItems,
            Arg.Any<CancellationToken>()).Returns(dataset);
        _fileNameBuilder.BuildFileName(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns("test-shop-sales-lineitems.pdf");
        _fileNameBuilder.GetContentType(Arg.Any<string>()).Returns("application/pdf");
        _pdfRenderer.RenderAsync(Arg.Any<SalesExportDatasetDto>(), Arg.Any<CancellationToken>()).Returns(expectedResult);

        var handler = CreateHandler();
        var query = new ExportSalesQuery(
            user.Id,
            shop.Id,
            "pdf",
            "lineItems",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.False(result.IsError);
        await _pdfRenderer.Received(1).RenderAsync(dataset, Arg.Any<CancellationToken>());
        await _excelRenderer.DidNotReceive().RenderAsync(Arg.Any<SalesExportDatasetDto>(), Arg.Any<CancellationToken>());
        await _tallyRenderer.DidNotReceive().RenderAsync(Arg.Any<SalesExportDatasetDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenPdfDatasetExceedsRowLimit_ReturnsValidationError_AndDoesNotRender()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var ownerMembership = MakeMembership(shop.Id, user.Id, ShopRole.Owner);

        var rows = Enumerable.Range(1, 2001)
            .Select(i => new SalesExportSummaryRowDto($"INV-{i:D4}", DateTimeOffset.UtcNow, null, "Cash", 0m, 0m, 0m, 0m, 0m, 0m, 0m, null, 0m, 0m, 0m, 0m, false, 0))
            .ToArray();

        var dataset = new SalesExportDatasetDto(
            new SalesExportMetadataDto(
                shop.Name,
                null,
                null,
                "Export User",
                DateTimeOffset.UtcNow,
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
                DateOnly.FromDateTime(DateTime.UtcNow),
                SalesExportLevel.Summary),
            rows,
            Array.Empty<SalesExportLineItemRowDto>(),
            Array.Empty<SalesExportTaxBreakupDto>(),
            []);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(ownerMembership);
        _datasetBuilder.BuildAsync(
            shop,
            user,
            Arg.Any<DateOnly>(),
            Arg.Any<DateOnly>(),
            SalesExportLevel.Summary,
            Arg.Any<CancellationToken>()).Returns(dataset);

        var handler = CreateHandler();
        var query = new ExportSalesQuery(
            user.Id,
            shop.Id,
            "pdf",
            "summary",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Export.PdfRowLimitExceeded", result.FirstError.Code);
        await _pdfRenderer.DidNotReceive().RenderAsync(Arg.Any<SalesExportDatasetDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenPdfLineItemDatasetExceedsRowLimit_ReturnsValidationError_AndDoesNotRender()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var ownerMembership = MakeMembership(shop.Id, user.Id, ShopRole.Owner);

        var rows = Enumerable.Range(1, 2001)
            .Select(i => new SalesExportLineItemRowDto($"INV-{i:D4}", null, "Item", 1m, 1m, 0m, 0m, 1m, 0m, 1m, false, 0m, null, null))
            .ToArray();

        var dataset = new SalesExportDatasetDto(
            new SalesExportMetadataDto(
                shop.Name,
                null,
                null,
                "Export User",
                DateTimeOffset.UtcNow,
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
                DateOnly.FromDateTime(DateTime.UtcNow),
                SalesExportLevel.LineItems),
            Array.Empty<SalesExportSummaryRowDto>(),
            rows,
            Array.Empty<SalesExportTaxBreakupDto>(),
            []);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(ownerMembership);
        _datasetBuilder.BuildAsync(
            shop,
            user,
            Arg.Any<DateOnly>(),
            Arg.Any<DateOnly>(),
            SalesExportLevel.LineItems,
            Arg.Any<CancellationToken>()).Returns(dataset);

        var handler = CreateHandler();
        var query = new ExportSalesQuery(
            user.Id,
            shop.Id,
            "pdf",
            "lineItems",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Export.PdfRowLimitExceeded", result.FirstError.Code);
        await _pdfRenderer.DidNotReceive().RenderAsync(Arg.Any<SalesExportDatasetDto>(), Arg.Any<CancellationToken>());
    }
}
