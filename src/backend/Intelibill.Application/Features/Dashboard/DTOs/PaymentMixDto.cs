namespace Intelibill.Application.Features.Dashboard.DTOs;

public sealed record PaymentMixDto(decimal Cash, decimal Upi, decimal Card, decimal Credit);
