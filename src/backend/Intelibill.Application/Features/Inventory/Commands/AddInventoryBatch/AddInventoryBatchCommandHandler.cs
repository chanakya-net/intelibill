using System.Globalization;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Features.Inventory.Commands.AddInventory;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;

/// <summary>
/// Receives up to 100 stock rows with partial success: each row commits on its own or not at all.
/// </summary>
/// <remarks>
/// <para>
/// A keyed request first commits a manifest receipt naming every row id and row hash in order, so
/// the same key can never stand for two different submissions. Each row then runs under a receipt
/// of its own: a resend after a lost response replays committed rows and runs only the rest.
/// </para>
/// <para>
/// A rejected row leaves nothing behind, so it is not recorded; a resend evaluates it again.
/// </para>
/// </remarks>
public sealed partial class AddInventoryBatchCommandHandler(
    IUserRepository userRepository,
    IInboundRowExecutor inboundRowExecutor,
    IFinancialOperationExecutor operationExecutor,
    ILogger<AddInventoryBatchCommandHandler> logger)
{
    private const int MaxBatchSize = 100;
    private const int MaxClientRowIdLength = 64;

    /// <summary>
    /// Leaves room in <see cref="Domain.Entities.OperationReceipt.MaxKeyLength"/> for the row key
    /// <c>{length}:{operationKey}:{clientRowId}</c>.
    /// </summary>
    private const int MaxOperationKeyLength = 60;

    private static readonly AddInventoryCommandValidator RowValidator = new();

    public async Task<ErrorOr<AddInventoryBatchResultDto>> HandleAsync(AddInventoryBatchCommand command, CancellationToken cancellationToken)
    {
        if (command.Items.Count == 0)
            return Error.Validation("Inventory.BatchEmpty", "At least one inventory row is required.");

        if (command.Items.Count > MaxBatchSize)
            return Error.Validation("Inventory.BatchLimitExceeded", "Only 100 items are allowed in a batch.");

        var shapeError = ValidateShape(command);
        if (shapeError is not null)
            return shapeError.Value;

        var auth = await AuthorizeAsync(command, cancellationToken);
        if (auth.IsError)
            return auth.Errors;

        var operationKey = FinancialOperationHasher.NormalizeKey(command.OperationKey);
        var rowHashes = command.Items.Select(row => FinancialOperationHasher.Compute(ToPayload(row))).ToArray();

        if (operationKey is null)
        {
            LogUnkeyedIntake(logger, command.ActiveShopId);
        }
        else
        {
            var manifest = await ReserveManifestAsync(command, operationKey, rowHashes, cancellationToken);
            if (manifest.IsError)
                return manifest.Errors;
        }

        var succeeded = new List<AddInventoryBatchSucceededRowDto>();
        var failed = new List<AddInventoryBatchFailedRowDto>();

        for (var index = 0; index < command.Items.Count; index++)
        {
            // A cancelled request answers nothing rather than a summary that looks complete;
            // resending under the same key finds every row already committed.
            cancellationToken.ThrowIfCancellationRequested();

            var row = command.Items[index];
            var rowErrors = ValidateRow(row);
            if (rowErrors.Count > 0)
            {
                failed.Add(ToFailedRow(row, rowErrors));
                continue;
            }

            var operation = operationKey is null
                ? null
                : new InboundRowOperation(RowKey(operationKey, row.ClientRowId), rowHashes[index]);

            var rowResult = await inboundRowExecutor.ExecuteAsync(
                command.ActiveShopId,
                command.ActorUserId,
                operation,
                row.ToInboundInput(),
                cancellationToken);

            if (rowResult.IsError)
            {
                failed.Add(ToFailedRow(row, rowResult.Errors));
                continue;
            }

            succeeded.Add(new AddInventoryBatchSucceededRowDto(row.ClientRowId, rowResult.Value));
        }

        return new AddInventoryBatchResultDto(
            command.Items.Count,
            succeeded.Count,
            failed.Count,
            succeeded,
            failed);
    }

    /// <summary>Length-prefixed so no operation key and row id pair can spell another pair's key.</summary>
    internal static string RowKey(string operationKey, string clientRowId) =>
        string.Create(CultureInfo.InvariantCulture, $"{operationKey.Length}:{operationKey}:{clientRowId}");

    private static Error? ValidateShape(AddInventoryBatchCommand command)
    {
        if (command.OperationKey is not null
            && FinancialOperationHasher.NormalizeKey(command.OperationKey) is not { Length: <= MaxOperationKeyLength })
            return Errors.Inventory.BatchOperationKeyInvalid;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in command.Items)
        {
            if (string.IsNullOrWhiteSpace(row.ClientRowId) || row.ClientRowId.Length > MaxClientRowIdLength)
                return Errors.Inventory.ClientRowIdInvalid;

            if (!seen.Add(row.ClientRowId))
                return Errors.Inventory.DuplicateClientRowId;
        }

        return null;
    }

    private async Task<ErrorOr<Success>> AuthorizeAsync(AddInventoryBatchCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Inventory.UserIsNotOwnerOrManager;

        return Result.Success;
    }

    /// <summary>
    /// Commits the manifest receipt, or replays it when this exact submission was seen before.
    /// Another submission under the same key answers <c>Operation.PayloadMismatch</c> before any
    /// row runs.
    /// </summary>
    private Task<ErrorOr<BulkIntakeManifestDto>> ReserveManifestAsync(
        AddInventoryBatchCommand command,
        string operationKey,
        string[] rowHashes,
        CancellationToken cancellationToken)
    {
        var manifestHash = FinancialOperationHasher.Compute(
            command.Items.Select((row, index) => new ManifestRow(row.ClientRowId, rowHashes[index])).ToArray());

        return operationExecutor.ExecuteAsync(
            new FinancialOperationScope(command.ActiveShopId, FinancialOperationType.BulkIntakeManifest, operationKey),
            command.ActorUserId,
            command.ActiveShopId,
            manifestHash,
            _ => Task.FromResult<ErrorOr<BulkIntakeManifestDto>>(new BulkIntakeManifestDto(command.Items.Count)),
            cancellationToken);
    }

    private static List<Error> ValidateRow(AddInventoryBatchRowCommand row)
    {
        var cmd = new AddInventoryCommand(
            ActorUserId: Guid.Empty,
            ActiveShopId: Guid.Empty,
            row.ItemName,
            row.Barcode,
            row.ItemDescription,
            row.HsnCode,
            row.Uom,
            row.BatchNumber,
            row.Quantity,
            row.TotalPurchaseCost,
            row.Mrp,
            row.SalesPrice,
            row.TaxRatePercent,
            row.TaxIncluded,
            row.PurchaseTaxIncluded,
            row.ExpiryDate,
            row.ManufacturingDate,
            row.SupplierId,
            row.ReferenceNumber,
            row.Notes,
            row.PerformedAt);

        var result = RowValidator.Validate(cmd);
        return result.Errors
            .Select(e => Error.Validation(e.ErrorCode, e.ErrorMessage))
            .ToList();
    }

    private static AddInventoryBatchFailedRowDto ToFailedRow(AddInventoryBatchRowCommand row, IReadOnlyList<Error> errors) =>
        new(
            row.ClientRowId,
            row.ItemName,
            row.Barcode,
            errors.Select(error => new AddInventoryBatchRowErrorDto(error.Code, error.Description)).ToArray(),
            Retryable: errors.Any(error => error.Code == Errors.Inventory.RowRetryable.Code));

    private static RowPayload ToPayload(AddInventoryBatchRowCommand row) =>
        new(
            Required(row.ItemName),
            Required(row.Barcode),
            Optional(row.ItemDescription),
            Required(row.Uom),
            Required(row.BatchNumber),
            FinancialOperationHasher.NormalizeAmount(row.Quantity),
            FinancialOperationHasher.NormalizeAmount(row.TotalPurchaseCost),
            FinancialOperationHasher.NormalizeAmount(row.Mrp),
            FinancialOperationHasher.NormalizeAmount(row.SalesPrice),
            FinancialOperationHasher.NormalizeAmount(row.TaxRatePercent),
            row.TaxIncluded,
            row.PurchaseTaxIncluded,
            row.ExpiryDate,
            row.ManufacturingDate,
            row.SupplierId,
            Optional(row.ReferenceNumber),
            Optional(row.Notes),
            row.PerformedAt?.ToUniversalTime(),
            Optional(row.HsnCode));

    // Rows are hashed before row validation, so a field the client left null must not throw.
    private static string Required(string? value) => value?.Trim() ?? string.Empty;

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Legacy unkeyed financial command: bulk stock intake without an operation key in shop {ShopId}.")]
    private static partial void LogUnkeyedIntake(ILogger logger, Guid shopId);

    /// <summary>The business content of one row; the hash a resend must match.</summary>
    private sealed record RowPayload(
        string ItemName,
        string Barcode,
        string? ItemDescription,
        string Uom,
        string BatchNumber,
        decimal Quantity,
        decimal TotalPurchaseCost,
        decimal Mrp,
        decimal SalesPrice,
        decimal TaxRatePercent,
        bool TaxIncluded,
        bool PurchaseTaxIncluded,
        DateOnly? ExpiryDate,
        DateOnly? ManufacturingDate,
        Guid? SupplierId,
        string? ReferenceNumber,
        string? Notes,
        DateTimeOffset? PerformedAt,
        string? HsnCode);

    private sealed record ManifestRow(string ClientRowId, string RowHash);

    private sealed record BulkIntakeManifestDto(int RowCount);
}
