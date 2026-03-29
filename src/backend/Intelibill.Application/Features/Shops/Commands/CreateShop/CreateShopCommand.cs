namespace Intelibill.Application.Features.Shops.Commands.CreateShop;

public sealed record CreateShopCommand(
	Guid UserId,
	string Name,
	string Address,
	string City,
	string State,
	string Pincode,
	string? ContactPerson,
	string? MobileNumber,
	string? GstNumber);