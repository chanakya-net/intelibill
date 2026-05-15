import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/add_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/create_shop_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/shop_details.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/update_shop_request.dart';

interface class ShopRepository {
  Future<ShopDetails> getShop(String shopId) {
    throw UnimplementedError();
  }

  Future<AuthSession> createShop(CreateShopRequest request) {
    throw UnimplementedError();
  }

  Future<ShopDetails> updateShop(
    String shopId,
    UpdateShopRequest request,
  ) {
    throw UnimplementedError();
  }

  Future<void> addBankAccount(
    AddBankAccountRequest request,
  ) {
    throw UnimplementedError();
  }
}
