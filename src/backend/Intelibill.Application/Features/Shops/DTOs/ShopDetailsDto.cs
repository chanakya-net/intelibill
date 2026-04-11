namespace Intelibill.Application.Features.Shops.DTOs;

public sealed record ShopDetailsDto(
    Guid ShopId,
    string Name,
    string Address,
    string City,
    string State,
    string Pincode,
    string? ContactPerson,
    string? MobileNumber,
    string? GstNumber,
    IReadOnlyList<BankAccountDto> BankAccounts);

public sealed record BankAccountDto(
    Guid Id,
    string BankName,
    string AccountNumber,
    string? AccountType,
    string? IfscCode,
    string? AccountHolderName);
