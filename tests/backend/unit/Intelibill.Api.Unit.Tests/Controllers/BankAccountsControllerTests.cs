using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.BankAccounts.Commands.AddBankAccount;
using Intelibill.Application.Features.BankAccounts.DTOs;
using Intelibill.Application.Features.BankAccounts.Queries.GetBankAccounts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class BankAccountsControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly BankAccountsController _controller;

    public BankAccountsControllerTests()
    {
        _controller = new BankAccountsController(_bus);
    }

    [Fact]
    public async Task GetBankAccounts_WhenUserMissing_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.GetBankAccounts(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetBankAccounts_WhenSuccessful_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()));

        IReadOnlyList<BankAccountDto> accounts = [
            new(Guid.NewGuid(), "SBI", "1234567890", "Savings", "SBIN0001234", "Test")
        ];
        _bus.InvokeAsync<ErrorOr<IReadOnlyList<BankAccountDto>>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<IReadOnlyList<BankAccountDto>>>(accounts.ToList()));

        var result = await _controller.GetBankAccounts(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(accounts, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<IReadOnlyList<BankAccountDto>>>(
            Arg.Is<GetBankAccountsQuery>(q => q.OwnerUserId == userId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddBankAccount_WhenUserMissing_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.AddBankAccount(
            new AddBankAccountRequest(null, null, null, null, null),
            CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task AddBankAccount_WhenSuccessful_ReturnsOkAndDispatchesCommand()
    {
        var userId = Guid.NewGuid();
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()));
        var request = new AddBankAccountRequest("SBI", "123456789012", "Savings", "SBIN0001234", "Chandra Kumar");
        var bankAccount = new BankAccountDto(Guid.NewGuid(), request.BankName!, request.AccountNumber!, request.AccountType, request.IfscCode, request.AccountHolderName);
        ArrangeBusResponse<BankAccountDto>(bankAccount);

        var result = await _controller.AddBankAccount(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(bankAccount, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<BankAccountDto>>(
            Arg.Is<AddBankAccountCommand>(c =>
                c.OwnerUserId == userId
                && c.BankName == request.BankName
                && c.AccountNumber == request.AccountNumber
                && c.AccountType == request.AccountType
                && c.IfscCode == request.IfscCode
                && c.AccountHolderName == request.AccountHolderName),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddBankAccount_WhenValidationError_ReturnsBadRequest()
    {
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));
        ArrangeBusResponse<BankAccountDto>(Errors.BankAccount.IfscCodeInvalid);

        var result = await _controller.AddBankAccount(
            new AddBankAccountRequest(null, null, null, "INVALID", null),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    private void ArrangeBusResponse<T>(ErrorOr<T> response)
    {
        _bus.InvokeAsync<ErrorOr<T>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));
    }

    private void SetUserClaims(params Claim[] claims)
    {
        var identity = claims.Length == 0 ? new ClaimsIdentity() : new ClaimsIdentity(claims, "test");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }
}
