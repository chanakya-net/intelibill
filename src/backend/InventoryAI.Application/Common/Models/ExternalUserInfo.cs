namespace InventoryAI.Application.Common.Models;

public sealed record ExternalUserInfo(
    string ProviderKey,
    string? Email,
    string FirstName,
    string LastName);
