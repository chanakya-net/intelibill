using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.OfflineReconciliation.Commands.ResolveReconciliationCase;
using Intelibill.Application.Features.OfflineReconciliation.DTOs;
using Intelibill.Application.Features.OfflineReconciliation.Queries.GetReconciliationCases;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public sealed class OfflineReconciliationControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly OfflineReconciliationController _controller;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _shopId = Guid.NewGuid();

    public OfflineReconciliationControllerTests()
    {
        _controller = new OfflineReconciliationController(_bus);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(JwtRegisteredClaimNames.Sub, _userId.ToString()),
                        new Claim("active_shop_id", _shopId.ToString()),
                    ],
                    "test")),
            },
        };
    }

    [Fact]
    public async Task GetCases_PassesFilterAndPaging()
    {
        var page = new ReconciliationCasePageDto([], 2, 50, 0, 0);
        _bus.InvokeAsync<ErrorOr<ReconciliationCasePageDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<ReconciliationCasePageDto>>(page));

        var result = await _controller.GetCases("resolved", 2, 50, CancellationToken.None);

        Assert.Same(page, Assert.IsType<OkObjectResult>(result).Value);
        await _bus.Received(1).InvokeAsync<ErrorOr<ReconciliationCasePageDto>>(
            new GetReconciliationCasesQuery(_userId, _shopId, "resolved", 2, 50),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveCase_MapsRouteAndBodyToCommand()
    {
        var caseId = Guid.NewGuid();
        var linked = Guid.NewGuid();
        _bus.InvokeAsync<ErrorOr<ReconciliationCaseDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<ReconciliationCaseDto>>(Errors.Reconciliation.CaseVersionConflict));

        var result = await _controller.ResolveCase(
            caseId,
            new ResolveReconciliationCaseRequest("op-1", 42u, "LinkStockCorrection", "Found stock", linked),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ObjectResult>(result).StatusCode);
        await _bus.Received(1).InvokeAsync<ErrorOr<ReconciliationCaseDto>>(
            new ResolveReconciliationCaseCommand(_userId, _shopId, caseId, "op-1", 42u, "LinkStockCorrection", "Found stock", linked),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveCase_WithoutBody_IsBadRequest()
    {
        var result = await _controller.ResolveCase(Guid.NewGuid(), null, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(result).StatusCode);
        await _bus.DidNotReceive().InvokeAsync<ErrorOr<ReconciliationCaseDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }
}
