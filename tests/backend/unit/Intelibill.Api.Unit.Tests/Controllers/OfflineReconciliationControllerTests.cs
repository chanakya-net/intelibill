using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.OfflineReconciliation.Commands.ResolveReconciliationCase;
using Intelibill.Application.Features.OfflineReconciliation.Commands.SubmitRecoveryBundle;
using Intelibill.Application.Features.OfflineReconciliation.DTOs;
using Intelibill.Application.Features.OfflineReconciliation.Queries.GetReconciliationCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
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
    public async Task SubmitRecoveryBundle_PassesTheInvoicesTextExactlyAsReceived()
    {
        // Spacing and escapes inside the array must survive: the checksum was computed over this text.
        const string invoices = "[ {\"clientSaleId\" : \"c\\u002d1\",\n  \"customerName\":\"Zo\u00eb\"} ]";
        var shopId = Guid.NewGuid();
        var exportedAt = new DateTimeOffset(2026, 11, 2, 18, 30, 0, TimeSpan.FromHours(5.5));
        var body = $$$"""{"format":"intelibill.offline-recovery","version":1,"shopId":"{{{shopId}}}","deviceId":"device-1","exportedAt":"2026-11-02T18:30:00+05:30","checksum":"ABC123","invoices":{{{invoices}}}}""";
        var response = new RecoverySubmissionResultDto("recovery-1", []);
        _bus.InvokeAsync<ErrorOr<RecoverySubmissionResultDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<RecoverySubmissionResultDto>>(response));

        var result = await _controller.SubmitRecoveryBundle(new SubmitRecoveryBundleRequest("recovery-1", Parse(body)), CancellationToken.None);

        Assert.Same(response, Assert.IsType<OkObjectResult>(result).Value);
        await _bus.Received(1).InvokeAsync<ErrorOr<RecoverySubmissionResultDto>>(
            new SubmitRecoveryBundleCommand(
                _userId,
                _shopId,
                "recovery-1",
                new RecoveryBundle("intelibill.offline-recovery", 1, shopId, "device-1", exportedAt, "ABC123", invoices)),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"version":1,"shopId":"6f1c2c9e-8f5e-4a51-9d6b-0b8e4f1d2a10","deviceId":"d","exportedAt":"2026-11-02T00:00:00Z","checksum":"c","invoices":[]}""")]
    [InlineData("""{"format":"intelibill.offline-recovery","version":"1","shopId":"6f1c2c9e-8f5e-4a51-9d6b-0b8e4f1d2a10","deviceId":"d","exportedAt":"2026-11-02T00:00:00Z","checksum":"c","invoices":[]}""")]
    [InlineData("""{"format":"intelibill.offline-recovery","version":1.5,"shopId":"6f1c2c9e-8f5e-4a51-9d6b-0b8e4f1d2a10","deviceId":"d","exportedAt":"2026-11-02T00:00:00Z","checksum":"c","invoices":[]}""")]
    [InlineData("""{"format":"intelibill.offline-recovery","version":1,"shopId":"shop-1","deviceId":"d","exportedAt":"2026-11-02T00:00:00Z","checksum":"c","invoices":[]}""")]
    [InlineData("""{"format":"intelibill.offline-recovery","version":1,"shopId":"6f1c2c9e-8f5e-4a51-9d6b-0b8e4f1d2a10","deviceId":7,"exportedAt":"2026-11-02T00:00:00Z","checksum":"c","invoices":[]}""")]
    [InlineData("""{"format":"intelibill.offline-recovery","version":1,"shopId":"6f1c2c9e-8f5e-4a51-9d6b-0b8e4f1d2a10","deviceId":"d","exportedAt":"yesterday","checksum":"c","invoices":[]}""")]
    [InlineData("""{"format":"intelibill.offline-recovery","version":1,"shopId":"6f1c2c9e-8f5e-4a51-9d6b-0b8e4f1d2a10","deviceId":"d","exportedAt":"2026-11-02T00:00:00Z","invoices":[]}""")]
    [InlineData("""{"format":"intelibill.offline-recovery","version":1,"shopId":"6f1c2c9e-8f5e-4a51-9d6b-0b8e4f1d2a10","deviceId":"d","exportedAt":"2026-11-02T00:00:00Z","checksum":"c","invoices":{}}""")]
    public async Task SubmitRecoveryBundle_MissingOrIllTypedMember_IsBundleInvalid(string bundle)
    {
        var result = await _controller.SubmitRecoveryBundle(new SubmitRecoveryBundleRequest("recovery-1", Parse(bundle)), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(Errors.Recovery.BundleInvalid.Code, Assert.IsType<ProblemDetails>(problem.Value).Title);
        await _bus.DidNotReceive().InvokeAsync<ErrorOr<RecoverySubmissionResultDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitRecoveryBundle_WithoutBodyOrBundle_IsBundleInvalid()
    {
        var noBody = await _controller.SubmitRecoveryBundle(null, CancellationToken.None);
        var noBundle = await _controller.SubmitRecoveryBundle(new SubmitRecoveryBundleRequest("recovery-1", default), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(noBody).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(noBundle).StatusCode);
        await _bus.DidNotReceive().InvokeAsync<ErrorOr<RecoverySubmissionResultDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SubmitRecoveryBundle_OwnerOrManagerOnly_AndCappedAtTwoMebibytes()
    {
        var method = typeof(OfflineReconciliationController).GetMethod(nameof(OfflineReconciliationController.SubmitRecoveryBundle))!;

        Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == "OwnerOrManager");
        // Kestrel refuses a larger body with 413 while model binding reads it, before the action runs.
        IRequestSizeLimitMetadata limit = method.GetCustomAttribute<RequestSizeLimitAttribute>()!;
        Assert.Equal(2_097_152, limit.MaxRequestBodySize);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task ResolveCase_WithoutBody_IsBadRequest()
    {
        var result = await _controller.ResolveCase(Guid.NewGuid(), null, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(result).StatusCode);
        await _bus.DidNotReceive().InvokeAsync<ErrorOr<ReconciliationCaseDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }
}
