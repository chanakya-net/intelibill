using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Features.Customers.DTOs;
using Intelibill.Application.Features.Customers.Queries.GetCustomers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class CustomersControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly CustomersController _controller;

    private readonly Guid _userId = Guid.NewGuid();

    public CustomersControllerTests()
    {
        _controller = new CustomersController(_bus);
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, _userId.ToString()));
    }

    // --- GetCustomers ---

    [Fact]
    public async Task GetCustomers_WhenSuccessful_ReturnsOkWithList()
    {
        var customers = new List<CustomerDto>
        {
            new(Guid.NewGuid(), "Alice", "+919876543210", null, true)
        };
        ArrangeBusResponse<IReadOnlyList<CustomerDto>>(customers);

        var result = await _controller.GetCustomers(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(customers, ok.Value);
    }

    [Fact]
    public async Task GetCustomers_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims(); // no claims

        var result = await _controller.GetCustomers(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetCustomers_WhenBusReturnsError_ReturnsProblem()
    {
        ArrangeBusResponse<IReadOnlyList<CustomerDto>>(Error.NotFound("Customer.NotFound", "Not found"));

        var result = await _controller.GetCustomers(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    // --- AddCustomer ---

    [Fact]
    public async Task AddCustomer_WhenSuccessful_ReturnsCreated()
    {
        var dto = new CustomerDto(Guid.NewGuid(), "Alice", "+919876543210", null, true);
        ArrangeBusResponse<CustomerDto>(dto);
        var request = new AddCustomerRequest("Alice", "+919876543210", null, true);

        var result = await _controller.AddCustomer(request, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result);
    }

    [Fact]
    public async Task AddCustomer_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.AddCustomer(new AddCustomerRequest("A", "+9198", null, true), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task AddCustomer_WhenBusReturnsValidationError_ReturnsBadRequest()
    {
        ArrangeBusResponse<CustomerDto>(Error.Validation("Customer.NameRequired", "Name is required"));

        var result = await _controller.AddCustomer(new AddCustomerRequest("", "+9198", null, true), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    // --- EditCustomer ---

    [Fact]
    public async Task EditCustomer_WhenSuccessful_ReturnsOk()
    {
        var dto = new CustomerDto(Guid.NewGuid(), "Alice", "+919876543210", null, true);
        ArrangeBusResponse<CustomerDto>(dto);
        var request = new EditCustomerRequest("Alice", "+919876543210", null, true);

        var result = await _controller.EditCustomer(Guid.NewGuid(), request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(dto, ok.Value);
    }

    [Fact]
    public async Task EditCustomer_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.EditCustomer(Guid.NewGuid(), new EditCustomerRequest("A", "+9198", null, true), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task EditCustomer_WhenBusReturnsNotFoundError_ReturnsNotFound()
    {
        ArrangeBusResponse<CustomerDto>(Error.NotFound("Customer.NotFound", "Customer not found"));

        var result = await _controller.EditCustomer(Guid.NewGuid(), new EditCustomerRequest("A", "+9198", null, true), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
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
