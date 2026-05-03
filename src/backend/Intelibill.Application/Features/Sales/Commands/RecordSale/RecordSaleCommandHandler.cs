using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

public sealed class RecordSaleCommandHandler(
    ISaleLineValidator saleLineValidator,
    ISaleInventoryMutator saleInventoryMutator,
    ICustomerResolver customerResolver,
    ISaleAggregator saleAggregator,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<SaleDto>> HandleAsync(RecordSaleCommand command, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        var validationResultOrError = await saleLineValidator.ValidateLinesAsync(
            command.ShopId, command.Items, warnings, cancellationToken);

        if (validationResultOrError.IsError)
            return validationResultOrError.Errors;

        var (validatedLines, itemNameById) = validationResultOrError.Value;

        var invoiceNumber = $"INV-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

        var mutatedLines = new List<MutatedSaleLine>();
        foreach (var validatedLine in validatedLines)
        {
            var result = await saleInventoryMutator.MutateAsync(
                command.ShopId, invoiceNumber, validatedLine, command.ActorUserId, cancellationToken);

            if (result.IsError)
                return result.Errors;

            mutatedLines.Add(result.Value);
        }

        var normalizedCustomerPhone = string.IsNullOrWhiteSpace(command.CustomerPhone) ? null : command.CustomerPhone.Trim();

        var resolvedCustomerOrError = await customerResolver.ResolveAsync(
            command.ShopId,
            command.CustomerId,
            command.CustomerPhone,
            command.DueAmount > 0,
            command.PaymentMethod,
            cancellationToken);

        if (resolvedCustomerOrError.IsError)
            return resolvedCustomerOrError.Errors;

        var aggregationOrError = await saleAggregator.AggregateAsync(
            invoiceNumber,
            command.ShopId,
            command.PaidAmount,
            command.DueAmount,
            command.ActorUserId,
            resolvedCustomerOrError.Value,
            command.CustomerName,
            normalizedCustomerPhone,
            command.PaymentMethod,
            mutatedLines,
            warnings,
            itemNameById,
            cancellationToken);

        if (aggregationOrError.IsError)
            return aggregationOrError.Errors;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return aggregationOrError.Value.Dto;
    }
}
