import type { ShopSetupAccess } from '../../../src/app/features/shops/services/shop-setup-access';

export function observeOnlyShopSetup(shopId: string, owner = true): ShopSetupAccess {
  return {
    shopId, contractVersion: 1, status: 'ObserveOnly', enforced: false,
    writableProfileFields: owner ? ['name', 'address', 'city', 'state', 'pincode', 'contactPerson', 'mobileNumber', 'gstNumber'] : [],
    canPrepareTax: owner, canSavePreferences: owner, canEditOperatingSettings: owner,
    hasOperatingAccess: true, canCreateAdditionalShop: true,
  };
}
