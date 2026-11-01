using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Commands.AddInventoryBatch;

public class AddInventoryBatchCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IInboundRowExecutor _rowExecutor = Substitute.For<IInboundRowExecutor>();
    private readonly RecordingOperationExecutor _operationExecutor = new();
    private readonly User _owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
    private readonly Shop _shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);

    public AddInventoryBatchCommandHandlerTests()
    {
        _owner.AddShopMembership(ShopMembership.Create(_shop.Id, _owner.Id, ShopRole.Owner, true));
        _userRepository.GetByIdWithDetailsAsync(_owner.Id, Arg.Any<CancellationToken>()).Returns(_owner);

        _rowExecutor.ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<InboundRowOperation?>(), Arg.Any<InboundInventoryLineInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<ErrorOr<AddInventoryResultDto>>(Received(callInfo.Arg<InboundInventoryLineInput>())));
    }

    [Fact]
    public async Task HandleAsync_WhenMoreThanHundredRows_ReturnsValidationError()
    {
        var rows = Enumerable.Range(1, 101).Select(index => CreateRow($"row-{index}", $"Item-{index}", $"BC-{index}")).ToArray();

        var result = await CreateHandler().HandleAsync(new AddInventoryBatchCommand(_owner.Id, _shop.Id, rows), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Inventory.BatchLimitExceeded", result.FirstError.Code);
        Assert.Equal("Only 100 items are allowed in a batch.", result.FirstError.Description);
        await AssertNoRowRanAsync();
    }

    [Fact]
    public async Task HandleAsync_WhenEmptyBatch_ReturnsValidationError()
    {
        var result = await CreateHandler().HandleAsync(new AddInventoryBatchCommand(_owner.Id, _shop.Id, []), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Inventory.BatchEmpty", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenStaffRole_ReturnsForbidden()
    {
        var staff = User.CreateWithEmail("staff@test.com", "hash", "Staff", "User");
        staff.AddShopMembership(ShopMembership.Create(_shop.Id, staff.Id, ShopRole.Staff, true));
        _userRepository.GetByIdWithDetailsAsync(staff.Id, Arg.Any<CancellationToken>()).Returns(staff);

        var result = await CreateHandler().HandleAsync(
            new AddInventoryBatchCommand(staff.Id, _shop.Id, [CreateRow("row-1", "Rice", "111")], "intake-1"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
        Assert.Empty(_operationExecutor.Scopes);
        await AssertNoRowRanAsync();
    }

    [Fact]
    public async Task HandleAsync_WhenClientRowIdRepeats_RejectsBeforeAuthorizingOrRunningRows()
    {
        var result = await CreateHandler().HandleAsync(
            new AddInventoryBatchCommand(_owner.Id, _shop.Id, [CreateRow("row-1", "Rice", "111"), CreateRow("row-1", "Sugar", "222")], "intake-1"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.DuplicateClientRowId.Code, result.FirstError.Code);
        await _userRepository.DidNotReceive().GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.Empty(_operationExecutor.Scopes);
        await AssertNoRowRanAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345678901234567890123456789012345678901234567890123456789012345")]
    public async Task HandleAsync_WhenClientRowIdBlankOrTooLong_RejectsBatch(string clientRowId)
    {
        var result = await CreateHandler().HandleAsync(
            new AddInventoryBatchCommand(_owner.Id, _shop.Id, [CreateRow(clientRowId, "Rice", "111")]),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.ClientRowIdInvalid.Code, result.FirstError.Code);
        await AssertNoRowRanAsync();
    }

    [Theory]
    [InlineData("  ")]
    [InlineData("1234567890123456789012345678901234567890123456789012345678901")]
    public async Task HandleAsync_WhenOperationKeyBlankOrTooLong_RejectsBatch(string operationKey)
    {
        var result = await CreateHandler().HandleAsync(
            new AddInventoryBatchCommand(_owner.Id, _shop.Id, [CreateRow("row-1", "Rice", "111")], operationKey),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.BatchOperationKeyInvalid.Code, result.FirstError.Code);
        await AssertNoRowRanAsync();
    }

    [Fact]
    public async Task HandleAsync_WhenOneRowFails_ReportsItAndKeepsTheOthers()
    {
        _rowExecutor.ExecuteAsync(_shop.Id, _owner.Id, Arg.Any<InboundRowOperation?>(), Arg.Is<InboundInventoryLineInput>(r => r.Barcode == "222"), Arg.Any<CancellationToken>())
            .Returns(Errors.Inventory.ItemNameBarcodeMismatch);

        var result = await CreateHandler().HandleAsync(
            new AddInventoryBatchCommand(_owner.Id, _shop.Id,
            [
                CreateRow("row-1", "Rice", "111"),
                CreateRow("row-2", "Different Name", "222"),
                CreateRow("row-3", "Sugar", "333"),
            ]),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(3, result.Value.RequestedCount);
        Assert.Equal(2, result.Value.SuccessCount);
        Assert.Equal(["row-1", "row-3"], result.Value.Succeeded.Select(r => r.ClientRowId));
        var failed = Assert.Single(result.Value.Failed);
        Assert.Equal("row-2", failed.ClientRowId);
        Assert.Equal(Errors.Inventory.ItemNameBarcodeMismatch.Code, failed.Errors[0].Code);
        Assert.False(failed.Retryable);
    }

    [Fact]
    public async Task HandleAsync_WhenRowFailsTransiently_MarksItRetryable()
    {
        _rowExecutor.ExecuteAsync(_shop.Id, _owner.Id, Arg.Any<InboundRowOperation?>(), Arg.Is<InboundInventoryLineInput>(r => r.Barcode == "111"), Arg.Any<CancellationToken>())
            .Returns(Errors.Inventory.RowRetryable);

        var result = await CreateHandler().HandleAsync(
            new AddInventoryBatchCommand(_owner.Id, _shop.Id, [CreateRow("row-1", "Rice", "111"), CreateRow("row-2", "Sugar", "222")], "intake-1"),
            CancellationToken.None);

        var failed = Assert.Single(result.Value.Failed);
        Assert.True(failed.Retryable);
        Assert.Equal("row-2", Assert.Single(result.Value.Succeeded).ClientRowId);
    }

    [Fact]
    public async Task HandleAsync_WhenRowIsInvalid_NeverRunsIt()
    {
        var invalid = CreateRow("row-1", "Rice", "111") with { Quantity = 0m };

        var result = await CreateHandler().HandleAsync(
            new AddInventoryBatchCommand(_owner.Id, _shop.Id, [invalid, CreateRow("row-2", "Sugar", "222")]),
            CancellationToken.None);

        Assert.Equal("row-1", Assert.Single(result.Value.Failed).ClientRowId);
        await _rowExecutor.DidNotReceive().ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<InboundRowOperation?>(),
            Arg.Is<InboundInventoryLineInput>(r => r.Barcode == "111"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenUnkeyed_RunsRowsWithoutReceipts()
    {
        var result = await CreateHandler().HandleAsync(
            new AddInventoryBatchCommand(_owner.Id, _shop.Id, [CreateRow("row-1", "Rice", "111")]),
            CancellationToken.None);

        Assert.Equal(1, result.Value.SuccessCount);
        Assert.Empty(_operationExecutor.Scopes);
        await _rowExecutor.Received(1).ExecuteAsync(_shop.Id, _owner.Id, null, Arg.Any<InboundInventoryLineInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenKeyed_CommitsManifestThenKeysEveryRow()
    {
        var operations = new List<InboundRowOperation?>();
        _rowExecutor.ExecuteAsync(_shop.Id, _owner.Id, Arg.Do<InboundRowOperation?>(operations.Add), Arg.Any<InboundInventoryLineInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<ErrorOr<AddInventoryResultDto>>(Received(callInfo.Arg<InboundInventoryLineInput>())));

        var result = await CreateHandler().HandleAsync(
            new AddInventoryBatchCommand(_owner.Id, _shop.Id, [CreateRow("row-1", "Rice", "111"), CreateRow("row-2", "Sugar", "222")], " intake-1 "),
            CancellationToken.None);

        Assert.Equal(2, result.Value.SuccessCount);
        var manifest = Assert.Single(_operationExecutor.Scopes);
        Assert.Equal(new FinancialOperationScope(_shop.Id, FinancialOperationType.BulkIntakeManifest, "intake-1"), manifest);
        Assert.Equal(
            [AddInventoryBatchCommandHandler.RowKey("intake-1", "row-1"), AddInventoryBatchCommandHandler.RowKey("intake-1", "row-2")],
            operations.Select(o => o!.Key));
        Assert.NotEqual(operations[0]!.RequestHash, operations[1]!.RequestHash);
    }

    [Fact]
    public async Task HandleAsync_WhenRowHashIsComputed_IgnoresFormattingButNotBusinessContent()
    {
        var hashes = new List<string>();
        _rowExecutor.ExecuteAsync(_shop.Id, _owner.Id, Arg.Do<InboundRowOperation?>(o => hashes.Add(o!.RequestHash)), Arg.Any<InboundInventoryLineInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<ErrorOr<AddInventoryResultDto>>(Received(callInfo.Arg<InboundInventoryLineInput>())));
        var handler = CreateHandler();
        var row = CreateRow("row-1", "Rice", "111");

        await handler.HandleAsync(new AddInventoryBatchCommand(_owner.Id, _shop.Id, [row], "a"), CancellationToken.None);
        await handler.HandleAsync(new AddInventoryBatchCommand(_owner.Id, _shop.Id, [row with { ItemName = " Rice ", Quantity = 10.000m }], "b"), CancellationToken.None);
        await handler.HandleAsync(new AddInventoryBatchCommand(_owner.Id, _shop.Id, [row with { Quantity = 11m }], "c"), CancellationToken.None);

        Assert.Equal(hashes[0], hashes[1]);
        Assert.NotEqual(hashes[0], hashes[2]);
    }

    [Fact]
    public async Task HandleAsync_WhenManifestConflicts_RunsNoRow()
    {
        _operationExecutor.Result = Errors.Operation.PayloadMismatch;

        var result = await CreateHandler().HandleAsync(
            new AddInventoryBatchCommand(_owner.Id, _shop.Id, [CreateRow("row-1", "Rice", "111")], "intake-1"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Operation.PayloadMismatch.Code, result.FirstError.Code);
        await AssertNoRowRanAsync();
    }

    [Fact]
    public async Task HandleAsync_WhenCancelledMidBatch_ThrowsInsteadOfSummarising()
    {
        using var cancellation = new CancellationTokenSource();
        _rowExecutor.ExecuteAsync(_shop.Id, _owner.Id, Arg.Any<InboundRowOperation?>(), Arg.Is<InboundInventoryLineInput>(r => r.Barcode == "111"), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                cancellation.Cancel();
                return Task.FromResult<ErrorOr<AddInventoryResultDto>>(Received(callInfo.Arg<InboundInventoryLineInput>()));
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateHandler().HandleAsync(
            new AddInventoryBatchCommand(_owner.Id, _shop.Id, [CreateRow("row-1", "Rice", "111"), CreateRow("row-2", "Sugar", "222")], "intake-1"),
            cancellation.Token));

        await _rowExecutor.DidNotReceive().ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<InboundRowOperation?>(),
            Arg.Is<InboundInventoryLineInput>(r => r.Barcode == "222"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RowKey_IsUnambiguousAcrossOperationAndRowBoundaries()
    {
        Assert.NotEqual(
            AddInventoryBatchCommandHandler.RowKey("a:b", "c"),
            AddInventoryBatchCommandHandler.RowKey("a", "b:c"));
        Assert.Equal(128, AddInventoryBatchCommandHandler.RowKey(new string('k', 60), new string('r', 64)).Length);
    }

    private AddInventoryBatchCommandHandler CreateHandler() =>
        new(_userRepository, _rowExecutor, _operationExecutor, NullLogger<AddInventoryBatchCommandHandler>.Instance);

    private async Task AssertNoRowRanAsync() =>
        await _rowExecutor.DidNotReceive().ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<InboundRowOperation?>(),
            Arg.Any<InboundInventoryLineInput>(), Arg.Any<CancellationToken>());

    private static AddInventoryResultDto Received(InboundInventoryLineInput row) =>
        new(Guid.NewGuid(), row.ItemName, row.Barcode, Guid.NewGuid(), row.BatchNumber, row.Quantity, row.Quantity, null, Guid.NewGuid(), DateTimeOffset.UtcNow);

    private static AddInventoryBatchRowCommand CreateRow(string clientRowId, string itemName, string barcode) =>
        new(
            clientRowId,
            itemName,
            barcode,
            ItemDescription: "Description",
            Uom: "kg",
            BatchNumber: "B-1",
            Quantity: 10m,
            TotalPurchaseCost: 800m,
            Mrp: 120m,
            SalesPrice: 110m,
            TaxRatePercent: 5m,
            TaxIncluded: false,
            PurchaseTaxIncluded: false,
            ExpiryDate: null,
            ManufacturingDate: null,
            SupplierId: null,
            ReferenceNumber: "PO-1",
            Notes: "Initial",
            PerformedAt: null,
            HsnCode: null);

    /// <summary>Runs the action like the real executor's first commit, or answers a configured error.</summary>
    private sealed class RecordingOperationExecutor : IFinancialOperationExecutor
    {
        public List<FinancialOperationScope> Scopes { get; } = [];

        public Error? Result { get; set; }

        public Task<ErrorOr<T>> ExecuteAsync<T>(
            FinancialOperationScope scope,
            Guid actorUserId,
            Guid targetId,
            string requestHash,
            Func<CancellationToken, Task<ErrorOr<T>>> action,
            CancellationToken cancellationToken)
        {
            Scopes.Add(scope);
            return Result is { } error ? Task.FromResult<ErrorOr<T>>(error) : action(cancellationToken);
        }
    }
}
