import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_draft_local_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/repositories/purchase_order_repository_impl.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/repositories/purchase_order_repository.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/cancel_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/close_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/create_purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/delete_purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_orders.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/place_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/receive_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/update_purchase_order_draft.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'purchase_order_providers.g.dart';

@riverpod
Future<PurchaseOrderDraftLocalDataSource> purchaseOrderDraftLocalDataSource(
  Ref ref,
) async {
  final storage = await ref.watch(preferencesStorageProvider.future);
  return PurchaseOrderDraftLocalDataSource(storage);
}

@riverpod
PurchaseOrderDraftLocalKey? purchaseOrderDraftLocalKey(
  Ref ref,
  String target,
) {
  final session = ref.watch(authControllerProvider).value?.session;
  final shopId = session?.activeShopId;
  if (session == null || shopId == null) return null;
  return PurchaseOrderDraftLocalKey(
    userId: session.user.id,
    shopId: shopId,
    target: target,
  );
}

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

@riverpod
GetPurchaseOrder getPurchaseOrder(Ref ref) {
  return GetPurchaseOrder(ref.watch(purchaseOrderRepositoryProvider));
}

@riverpod
CreatePurchaseOrderDraft createPurchaseOrderDraft(Ref ref) {
  return CreatePurchaseOrderDraft(ref.watch(purchaseOrderRepositoryProvider));
}

@riverpod
UpdatePurchaseOrderDraft updatePurchaseOrderDraft(Ref ref) {
  return UpdatePurchaseOrderDraft(ref.watch(purchaseOrderRepositoryProvider));
}

@riverpod
DeletePurchaseOrderDraft deletePurchaseOrderDraft(Ref ref) {
  return DeletePurchaseOrderDraft(ref.watch(purchaseOrderRepositoryProvider));
}

@riverpod
CancelPurchaseOrder cancelPurchaseOrder(Ref ref) {
  return CancelPurchaseOrder(ref.watch(purchaseOrderRepositoryProvider));
}

@riverpod
ClosePurchaseOrder closePurchaseOrder(Ref ref) {
  return ClosePurchaseOrder(ref.watch(purchaseOrderRepositoryProvider));
}

@riverpod
ReceivePurchaseOrder receivePurchaseOrder(Ref ref) {
  return ReceivePurchaseOrder(ref.watch(purchaseOrderRepositoryProvider));
}

@riverpod
PlacePurchaseOrder placePurchaseOrder(Ref ref) {
  return PlacePurchaseOrder(ref.watch(purchaseOrderRepositoryProvider));
}
