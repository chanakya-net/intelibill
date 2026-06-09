namespace Intelibill.Application.Features.Dashboard.DTOs;

public sealed record PaymentMixTrendPointDto(
    DateOnly Date,
    decimal Cash,
    decimal Upi,
    decimal Card,
    decimal Credit);
