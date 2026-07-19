// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'purchase_orders_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(PurchaseOrdersController)
final purchaseOrdersControllerProvider = PurchaseOrdersControllerProvider._();

final class PurchaseOrdersControllerProvider
    extends $NotifierProvider<PurchaseOrdersController, PurchaseOrdersState> {
  PurchaseOrdersControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'purchaseOrdersControllerProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$purchaseOrdersControllerHash();

  @$internal
  @override
  PurchaseOrdersController create() => PurchaseOrdersController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(PurchaseOrdersState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<PurchaseOrdersState>(value),
    );
  }
}

String _$purchaseOrdersControllerHash() =>
    r'e705af145e77b7fdc90cecf0871e0d8ad5b78cb3';

abstract class _$PurchaseOrdersController
    extends $Notifier<PurchaseOrdersState> {
  PurchaseOrdersState build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<PurchaseOrdersState, PurchaseOrdersState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<PurchaseOrdersState, PurchaseOrdersState>,
              PurchaseOrdersState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}
