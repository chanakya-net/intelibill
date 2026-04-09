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
    string? BankName,
    string? BankAccountNumber,
    string? BankAccountType,
    string? IfscCode,
    string? AccountHolderName);
