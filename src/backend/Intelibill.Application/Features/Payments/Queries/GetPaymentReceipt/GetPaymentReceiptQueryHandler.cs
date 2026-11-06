using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Payments.DTOs;
using Intelibill.Application.Features.Payments.Services;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Payments.Queries.GetPaymentReceipt;

public sealed class GetPaymentReceiptQueryHandler(
    IUserRepository userRepository,
    IPaymentReceiptRepository receiptRepository,
    PaymentReceiptDtoBuilder dtoBuilder)
{
    public async Task<ErrorOr<PaymentReceiptDto>> HandleAsync(GetPaymentReceiptQuery query, CancellationToken cancellationToken)
    {
        var authorized = await PaymentAuthorization.RequireOwnerOrManagerAsync(
            userRepository, query.ActorUserId, query.ActiveShopId, cancellationToken);
        if (authorized.IsError)
            return authorized.Errors;

        var receipt = await receiptRepository.GetByShopAndIdAsync(query.ActiveShopId, query.ReceiptId, cancellationToken);
        if (receipt is null)
            return Errors.Payment.ReceiptNotFound;

        return await dtoBuilder.BuildAsync(receipt, cancellationToken);
    }
}
