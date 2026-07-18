import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/shop_details.dart';
import 'package:intelibill_mobile/src/features/shops/shops_providers.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'purchase_order_preview_controller.g.dart';

final activeShopIdProvider = Provider<String?>((ref) {
  return ref.watch(authControllerProvider).value?.session?.activeShopId;
});

class PurchaseOrderPreviewState {
  const PurchaseOrderPreviewState({
    this.purchaseOrder,
    this.shop,
    this.failure,
    this.isLoading = false,
  });

  final PurchaseOrder? purchaseOrder;
  final ShopDetails? shop;
  final Failure? failure;
  final bool isLoading;

  PurchaseOrderPreviewState copyWith({
    PurchaseOrder? purchaseOrder,
    ShopDetails? shop,
    Failure? failure,
    bool? isLoading,
    bool clearShop = false,
    bool clearFailure = false,
  }) => PurchaseOrderPreviewState(
    purchaseOrder: purchaseOrder ?? this.purchaseOrder,
    shop: clearShop ? null : (shop ?? this.shop),
    failure: clearFailure ? null : (failure ?? this.failure),
    isLoading: isLoading ?? this.isLoading,
  );
}

@riverpod
class PurchaseOrderPreviewController extends _$PurchaseOrderPreviewController {
  late final String _purchaseOrderId;

  @override
  PurchaseOrderPreviewState build(String purchaseOrderId) {
    _purchaseOrderId = purchaseOrderId;
    unawaited(Future.microtask(_load));
    return const PurchaseOrderPreviewState(isLoading: true);
  }

  Future<void> retry() async {
    state = state.copyWith(isLoading: true, clearFailure: true);
    await _load();
  }

  Future<void> _load() async {
    final orderFuture = ref.read(getPurchaseOrderProvider)(_purchaseOrderId);
    final shopFuture = _loadShop(ref.read(activeShopIdProvider));
    try {
      final results = await Future.wait<Object?>([orderFuture, shopFuture]);
      if (!ref.mounted) return;
      state = state.copyWith(
        purchaseOrder: results.first! as PurchaseOrder,
        shop: results.last as ShopDetails?,
        isLoading: false,
        clearFailure: true,
        clearShop: results.last == null,
      );
    } on AppException catch (error) {
      _finishFailure(error.failure);
    } on Object {
      _finishFailure(const Failure.unknown());
    }
  }

  Future<ShopDetails?> _loadShop(String? shopId) async {
    if (shopId == null) return null;
    try {
      return await ref.read(getShopUseCaseProvider)(shopId);
    } on Object {
      return null;
    }
  }

  void _finishFailure(Failure failure) {
    if (!ref.mounted) return;
    state = state.copyWith(isLoading: false, failure: failure);
  }
}
