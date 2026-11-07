using ErrorOr;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.SupplierReturns.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Features.SupplierReturns.Services;

public sealed class SupplierReturnAccess(IUserRepository users,IShopRepository shops)
{
    public async Task<ErrorOr<Shop>> AuthorizeAsync(Guid actorId,Guid shopId,bool owner,CancellationToken ct)
    {
        var actor=await users.GetByIdWithDetailsAsync(actorId,ct);
        var membership=actor?.ShopMemberships.FirstOrDefault(x=>x.ShopId==shopId && x.IsActive);
        if(membership is null || (owner ? membership.Role!=ShopRole.Owner : membership.Role is not (ShopRole.Owner or ShopRole.Manager)))
            return Error.Forbidden("SupplierReturn.Forbidden",owner?"Only an owner may accept credit or reverse a receipt.":"Only an owner or manager may manage supplier returns.");
        var shop=await shops.GetByIdAsync(shopId,ct);
        return shop is null?Error.NotFound("Shop.NotFound","Shop was not found."):shop;
    }
}
