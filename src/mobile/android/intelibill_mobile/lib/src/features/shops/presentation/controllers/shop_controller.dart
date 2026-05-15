import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/add_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/create_shop_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/update_shop_request.dart';
import 'package:intelibill_mobile/src/features/shops/shops_providers.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'shop_controller.g.dart';

@riverpod
class ShopController extends _$ShopController {
  @override
  Future<void> build() async {}

  Future<void> createShop(CreateShopRequest request) async {
    state = const AsyncLoading();
    final useCase = ref.read(createShopUseCaseProvider);

    try {
      final session = await useCase(request);
      await ref.read(authControllerProvider.notifier).applySession(session);
      if (!ref.mounted) return;
      state = const AsyncData(null);
    } on AppException catch (error, stackTrace) {
      if (!ref.mounted) return;
      state = AsyncError(error, stackTrace);
    } on Object catch (error, stackTrace) {
      if (!ref.mounted) return;
      state = AsyncError(error, stackTrace);
    }
  }

  Future<void> updateShop(String shopId, UpdateShopRequest request) async {
    state = const AsyncLoading();
    final useCase = ref.read(updateShopUseCaseProvider);

    try {
      await useCase(shopId, request);
      if (!ref.mounted) return;
      state = const AsyncData(null);
    } on AppException catch (error, stackTrace) {
      if (!ref.mounted) return;
      state = AsyncError(error, stackTrace);
    } on Object catch (error, stackTrace) {
      if (!ref.mounted) return;
      state = AsyncError(error, stackTrace);
    }
  }

  Future<void> addBankAccount(
    String shopId,
    AddBankAccountRequest request,
  ) async {
    state = const AsyncLoading();
    final useCase = ref.read(addBankAccountUseCaseProvider);

    try {
      shopId.trim();
      await useCase(request);
      if (!ref.mounted) return;
      state = const AsyncData(null);
    } on AppException catch (error, stackTrace) {
      if (!ref.mounted) return;
      state = AsyncError(error, stackTrace);
    } on Object catch (error, stackTrace) {
      if (!ref.mounted) return;
      state = AsyncError(error, stackTrace);
    }
  }
}
