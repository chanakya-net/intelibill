import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/repositories/purchase_order_repository_impl.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/repositories/purchase_order_repository.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_orders.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'purchase_order_providers.g.dart';

@riverpod
PurchaseOrderRemoteDataSource purchaseOrderRemoteDataSource(Ref ref) {
  return PurchaseOrderRemoteDataSourceImpl(ref.watch(apiClientProvider));
}

@riverpod
PurchaseOrderRepository purchaseOrderRepository(Ref ref) {
  return PurchaseOrderRepositoryImpl(
    ref.watch(purchaseOrderRemoteDataSourceProvider),
  );
}

@riverpod
GetPurchaseOrders getPurchaseOrders(Ref ref) {
  return GetPurchaseOrders(ref.watch(purchaseOrderRepositoryProvider));
}
