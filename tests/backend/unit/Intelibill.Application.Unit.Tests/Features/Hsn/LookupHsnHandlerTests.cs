using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Hsn.Queries.LookupHsn;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Hsn;

public sealed class LookupHsnHandlerTests
{
    private readonly IHsnCacheRepository _hsnCacheRepository = Substitute.For<IHsnCacheRepository>();
    private readonly IExternalHsnLookupService _externalHsnLookupService = Substitute.For<IExternalHsnLookupService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Handle_EmptyProductName_ReturnsValidationError()
    {
        var sut = CreateSut();

        var result = await sut.HandleAsync(
            new LookupHsnQuery("   ", Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Hsn.EmptyProductName.Code, result.FirstError.Code);

        await _hsnCacheRepository.DidNotReceive().GetByProductNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _externalHsnLookupService.DidNotReceive().LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CacheHit_ReturnsCachedResultWithoutCallingApi()
    {
        var cached = Intelibill.Domain.Entities.HsnCache.Create(
            "Paracetamol",
            ["30049069"],
            [new Intelibill.Domain.Entities.HsnTaxScenario("Standard", "5%")]);

        _hsnCacheRepository.GetByProductNameAsync("Paracetamol", Arg.Any<CancellationToken>())
            .Returns(cached);

        var sut = CreateSut();
        var result = await sut.HandleAsync(
            new LookupHsnQuery("  Paracetamol  ", Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(["30049069"], result.Value.HsnCodes);
        Assert.Single(result.Value.TaxScenarios);
        Assert.Equal("Standard", result.Value.TaxScenarios[0].Condition);
        Assert.Equal("5%", result.Value.TaxScenarios[0].TaxPercentage);

        await _externalHsnLookupService.DidNotReceive().LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CacheMiss_CallsExternalApiAndReturnsResult()
    {
        _hsnCacheRepository.GetByProductNameAsync("Paracetamol", Arg.Any<CancellationToken>())
            .Returns((Intelibill.Domain.Entities.HsnCache?)null);

        _externalHsnLookupService.LookupAsync("Paracetamol", Arg.Any<CancellationToken>())
            .Returns(new ApiResponse<ExternalHsnLookupResponse>(
                Success: true,
                Data: new ExternalHsnLookupResponse(
                    Name: "Paracetamol",
                    HsnCodes: ["30049069"],
                    TaxScenarios: [new ExternalHsnTaxScenario("Standard", "5%")]),
                Error: null));

        var sut = CreateSut();
        var result = await sut.HandleAsync(
            new LookupHsnQuery("Paracetamol", Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(["30049069"], result.Value.HsnCodes);
        Assert.Single(result.Value.TaxScenarios);

        await _externalHsnLookupService.Received(1).LookupAsync("Paracetamol", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CacheMiss_SavesResultToCache()
    {
        Intelibill.Domain.Entities.HsnCache? saved = null;

        _hsnCacheRepository.GetByProductNameAsync("Paracetamol", Arg.Any<CancellationToken>())
            .Returns((Intelibill.Domain.Entities.HsnCache?)null);

        _externalHsnLookupService.LookupAsync("Paracetamol", Arg.Any<CancellationToken>())
            .Returns(new ApiResponse<ExternalHsnLookupResponse>(
                Success: true,
                Data: new ExternalHsnLookupResponse(
                    Name: "Paracetamol",
                    HsnCodes: ["30049069"],
                    TaxScenarios: [new ExternalHsnTaxScenario("Standard", "5%")]),
                Error: null));

        _hsnCacheRepository.SaveAsync(
                Arg.Do<Intelibill.Domain.Entities.HsnCache>(c => saved = c),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        var result = await sut.HandleAsync(
            new LookupHsnQuery("  Paracetamol  ", Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(saved);
        Assert.Equal("Paracetamol", saved!.ProductName);
        Assert.Equal(["30049069"], saved.HsnCodes);
        Assert.Single(saved.TaxScenarios);
        Assert.Equal("Standard", saved.TaxScenarios[0].Condition);
        Assert.Equal("5%", saved.TaxScenarios[0].TaxPercentage);

        await _hsnCacheRepository.Received(1).SaveAsync(Arg.Any<Intelibill.Domain.Entities.HsnCache>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CacheMiss_ApiReturnsMultipleHsnCodes_ReturnsAll()
    {
        _hsnCacheRepository.GetByProductNameAsync("Paracetamol", Arg.Any<CancellationToken>())
            .Returns((Intelibill.Domain.Entities.HsnCache?)null);

        _externalHsnLookupService.LookupAsync("Paracetamol", Arg.Any<CancellationToken>())
            .Returns(new ApiResponse<ExternalHsnLookupResponse>(
                Success: true,
                Data: new ExternalHsnLookupResponse(
                    Name: "Paracetamol",
                    HsnCodes: ["30049069", "30049071"],
                    TaxScenarios: [new ExternalHsnTaxScenario("Standard", "5%")]),
                Error: null));

        var sut = CreateSut();
        var result = await sut.HandleAsync(
            new LookupHsnQuery("Paracetamol", Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(["30049069", "30049071"], result.Value.HsnCodes);
    }

    [Fact]
    public async Task Handle_CacheMiss_ApiReturnsMultipleTaxScenarios_ReturnsAll()
    {
        _hsnCacheRepository.GetByProductNameAsync("Paracetamol", Arg.Any<CancellationToken>())
            .Returns((Intelibill.Domain.Entities.HsnCache?)null);

        _externalHsnLookupService.LookupAsync("Paracetamol", Arg.Any<CancellationToken>())
            .Returns(new ApiResponse<ExternalHsnLookupResponse>(
                Success: true,
                Data: new ExternalHsnLookupResponse(
                    Name: "Paracetamol",
                    HsnCodes: ["30049069"],
                    TaxScenarios: [
                        new ExternalHsnTaxScenario("Standard", "5%"),
                        new ExternalHsnTaxScenario("Luxury", "18%")
                    ]),
                Error: null));

        var sut = CreateSut();
        var result = await sut.HandleAsync(
            new LookupHsnQuery("Paracetamol", Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.TaxScenarios.Length);
        Assert.Equal("Standard", result.Value.TaxScenarios[0].Condition);
        Assert.Equal("5%", result.Value.TaxScenarios[0].TaxPercentage);
        Assert.Equal("Luxury", result.Value.TaxScenarios[1].Condition);
        Assert.Equal("18%", result.Value.TaxScenarios[1].TaxPercentage);
    }

    [Fact]
    public async Task Handle_ApiError_ReturnsError()
    {
        _hsnCacheRepository.GetByProductNameAsync("Paracetamol", Arg.Any<CancellationToken>())
            .Returns((Intelibill.Domain.Entities.HsnCache?)null);

        _externalHsnLookupService.LookupAsync("Paracetamol", Arg.Any<CancellationToken>())
            .Returns(Error.Failure("hsn.http_error", "Downstream failed"));

        var sut = CreateSut();
        var result = await sut.HandleAsync(
            new LookupHsnQuery("Paracetamol", Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("hsn.http_error", result.FirstError.Code);

        await _hsnCacheRepository.DidNotReceive().SaveAsync(Arg.Any<Intelibill.Domain.Entities.HsnCache>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ApiReturnsSuccessFalse_ReturnsError()
    {
        _hsnCacheRepository.GetByProductNameAsync("Paracetamol", Arg.Any<CancellationToken>())
            .Returns((Intelibill.Domain.Entities.HsnCache?)null);

        _externalHsnLookupService.LookupAsync("Paracetamol", Arg.Any<CancellationToken>())
            .Returns(new ApiResponse<ExternalHsnLookupResponse>(
                Success: false,
                Data: null,
                Error: "No match"));

        var sut = CreateSut();
        var result = await sut.HandleAsync(
            new LookupHsnQuery("Paracetamol", Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Hsn.LookupFailed.Code, result.FirstError.Code);

        await _hsnCacheRepository.DidNotReceive().SaveAsync(Arg.Any<Intelibill.Domain.Entities.HsnCache>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private LookupHsnHandler CreateSut() => new(_hsnCacheRepository, _externalHsnLookupService, _unitOfWork);
}
