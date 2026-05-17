import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/shops/data/data_sources/shop_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/shops/data/repositories/shop_repository_impl.dart';
import 'package:intelibill_mobile/src/features/shops/domain/repositories/shop_repository.dart';
import 'package:intelibill_mobile/src/features/shops/domain/use_cases/add_bank_account_use_case.dart';
import 'package:intelibill_mobile/src/features/shops/domain/use_cases/create_shop_use_case.dart';
import 'package:intelibill_mobile/src/features/shops/domain/use_cases/get_shop_use_case.dart';
import 'package:intelibill_mobile/src/features/shops/domain/use_cases/update_shop_use_case.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'shops_providers.g.dart';

@riverpod
ShopRemoteDataSource shopRemoteDataSource(Ref ref) {
  final apiClient = ref.watch(apiClientProvider);
  return ShopRemoteDataSourceImpl(apiClient);
}

@riverpod
ShopRepository shopRepository(Ref ref) {
  final remoteDataSource = ref.watch(shopRemoteDataSourceProvider);
  return ShopRepositoryImpl(remoteDataSource);
}

@riverpod
GetShopUseCase getShopUseCase(Ref ref) {
  final repository = ref.watch(shopRepositoryProvider);
  return GetShopUseCase(repository);
}

@riverpod
CreateShopUseCase createShopUseCase(Ref ref) {
  final repository = ref.watch(shopRepositoryProvider);
  return CreateShopUseCase(repository);
}

@riverpod
UpdateShopUseCase updateShopUseCase(Ref ref) {
  final repository = ref.watch(shopRepositoryProvider);
  return UpdateShopUseCase(repository);
}

@riverpod
AddBankAccountUseCase addBankAccountUseCase(Ref ref) {
  final repository = ref.watch(shopRepositoryProvider);
  return AddBankAccountUseCase(repository);
}
