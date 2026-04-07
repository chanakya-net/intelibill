using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.Queries.GetProductDetails;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Intelibill.Application.Unit.Tests.Features.Items.Queries.GetProductDetails {
    public class GetProductDetailsByNameOrBarcodeQueryHandlerTests {
        [Fact]
        public async Task HandleAsync_WhenBatchHasActiveSupplier_ReturnsSupplierDetails() {
            var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
            var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
            owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

            var supplier = Supplier.Create(owner.Id, "Acme Foods", null, null, "Street", "City", "State", "560001", true, false);
            // ...rest of the test...
        }
        [Fact]
        public async Task HandleAsync_WhenBatchHasInactiveSupplier_ReturnsNullSupplier() {
            var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
            var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
            owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

            var inactiveSupplier = Supplier.Create(owner.Id, "Old Supplier", null, null, "Street", "City", "State", "560001", false, false);
            // ...rest of the test...
        }
    }
}