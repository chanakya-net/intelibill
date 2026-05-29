using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Services.Commands.ActivateService;
using Intelibill.Application.Features.Services.Commands.AddService;
using Intelibill.Application.Features.Services.Commands.DeactivateService;
using Intelibill.Application.Features.Services.Commands.UpdateService;
using Intelibill.Application.Features.Services.DTOs;
using Intelibill.Application.Features.Services.Queries.GetServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class ServicesControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly ServicesController _controller;

    public ServicesControllerTests()
    {
        _controller = new ServicesController(_bus);
    }

    [Fact]
    public async Task GetServices_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.GetServices(cancellationToken: CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetServices_WhenValid_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var services = new List<ServiceDto>
        {
            new(Guid.NewGuid(), "SRV-000001", "Consulting", null, 120m, null, 18m, false, true),
        };

        _bus.InvokeAsync<ErrorOr<IReadOnlyList<ServiceDto>>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<IReadOnlyList<ServiceDto>>>(services));

        var result = await _controller.GetServices(includeInactive: true, search: null, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(services, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<IReadOnlyList<ServiceDto>>>(
            Arg.Is<GetServicesQuery>(q =>
                q.UserId == userId &&
                q.ActiveShopId == shopId &&
                q.IncludeInactive == true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddService_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.AddService(CreateAddRequest(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task AddService_WhenValid_ReturnsCreated()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var request = CreateAddRequest();
        var dto = new ServiceDto(Guid.NewGuid(), "SRV-000001", request.Name, request.Description, request.Price, request.HsnCode, request.TaxRatePercent, request.TaxIncluded, request.IsActive);

        _bus.InvokeAsync<ErrorOr<ServiceDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<ServiceDto>>(dto));

        var result = await _controller.AddService(request, CancellationToken.None);

        var createdAtAction = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(dto, createdAtAction.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<ServiceDto>>(
            Arg.Is<AddServiceCommand>(c =>
                c.ActorUserId == userId &&
                c.ActiveShopId == shopId &&
                c.Name == request.Name &&
                c.Price == request.Price),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddService_WhenNameConflict_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<ServiceDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<ServiceDto>>(Errors.Service.NameAlreadyExists));

        var result = await _controller.AddService(CreateAddRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
    }

    [Fact]
    public async Task UpdateService_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.UpdateService(Guid.NewGuid(), CreateUpdateRequest(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task UpdateService_WhenValid_ReturnsNoContent()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var request = CreateUpdateRequest();

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Success>>(Result.Success));

        var result = await _controller.UpdateService(serviceId, request, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await _bus.Received(1).InvokeAsync<ErrorOr<Success>>(
            Arg.Is<UpdateServiceCommand>(c =>
                c.ActorUserId == userId &&
                c.ActiveShopId == shopId &&
                c.ServiceId == serviceId &&
                c.Name == request.Name &&
                c.Price == request.Price),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateService_WhenNotFound_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Success>>(Errors.Service.NotFound));

        var result = await _controller.UpdateService(Guid.NewGuid(), CreateUpdateRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task ActivateService_WhenValid_ReturnsNoContent()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Success>>(Result.Success));

        var result = await _controller.ActivateService(serviceId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await _bus.Received(1).InvokeAsync<ErrorOr<Success>>(
            Arg.Is<ActivateServiceCommand>(c =>
                c.ActorUserId == userId &&
                c.ActiveShopId == shopId &&
                c.ServiceId == serviceId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ActivateService_WhenNotFound_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Success>>(Errors.Service.NotFound));

        var result = await _controller.ActivateService(Guid.NewGuid(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task DeactivateService_WhenValid_ReturnsNoContent()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Success>>(Result.Success));

        var result = await _controller.DeactivateService(serviceId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await _bus.Received(1).InvokeAsync<ErrorOr<Success>>(
            Arg.Is<DeactivateServiceCommand>(c =>
                c.ActorUserId == userId &&
                c.ActiveShopId == shopId &&
                c.ServiceId == serviceId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateService_WhenNotFound_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Success>>(Errors.Service.NotFound));

        var result = await _controller.DeactivateService(Guid.NewGuid(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    private void SetUserClaims(params Claim[] claims)
    {
        var identity = claims.Length == 0
            ? new ClaimsIdentity()
            : new ClaimsIdentity(claims, "TestAuthType");

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
            },
        };
    }

    private static AddServiceRequest CreateAddRequest() =>
        new(
            Name: "Consulting",
            Description: "Professional support",
            Price: 120m,
            HsnCode: "1001",
            TaxRatePercent: 18m,
            TaxIncluded: false,
            IsActive: true);

    private static UpdateServiceRequest CreateUpdateRequest() =>
        new(
            Name: "Updated Consulting",
            Description: "Updated support",
            Price: 150m,
            HsnCode: "1001",
            TaxRatePercent: 18m,
            TaxIncluded: false);
}
