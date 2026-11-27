using Intelibill.Application.Features.Tills;
using Intelibill.Domain.ValueObjects;
namespace Intelibill.Application.Features.Payments.Commands.ReversePayment;

public sealed record ReversePaymentCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid ReceiptId,
    string Reason,
    string OperationKey, TillPostingContext? Till = null, TillSettlementEvidence? SettlementEvidence = null);
