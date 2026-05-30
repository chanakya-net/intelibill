namespace Intelibill.Application.Features.BankAccounts.DTOs;

public sealed record BankAccountDto(
    Guid Id,
    string BankName,
    string AccountNumber,
    string? AccountType,
    string? IfscCode,
    string? AccountHolderName);
