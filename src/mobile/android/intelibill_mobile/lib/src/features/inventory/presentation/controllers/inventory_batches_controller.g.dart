// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'inventory_batches_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(getInventoryBatches)
final getInventoryBatchesProvider = GetInventoryBatchesProvider._();

final class GetInventoryBatchesProvider
    extends
        $FunctionalProvider<
          GetInventoryBatches,
          GetInventoryBatches,
          GetInventoryBatches
        >
    with $Provider<GetInventoryBatches> {
  GetInventoryBatchesProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getInventoryBatchesProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getInventoryBatchesHash();

  @$internal
  @override
  $ProviderElement<GetInventoryBatches> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  GetInventoryBatches create(Ref ref) {
    return getInventoryBatches(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetInventoryBatches value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetInventoryBatches>(value),
    );
  }
}

String _$getInventoryBatchesHash() =>
    r'136c1dc4d01407ecf4ef1fb0814ecbcc99040cb8';

@ProviderFor(adjustInventoryBatch)
final adjustInventoryBatchProvider = AdjustInventoryBatchProvider._();

final class AdjustInventoryBatchProvider
    extends
        $FunctionalProvider<
          AdjustInventoryBatch,
          AdjustInventoryBatch,
          AdjustInventoryBatch
        >
    with $Provider<AdjustInventoryBatch> {
  AdjustInventoryBatchProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'adjustInventoryBatchProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$adjustInventoryBatchHash();

  @$internal
  @override
  $ProviderElement<AdjustInventoryBatch> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  AdjustInventoryBatch create(Ref ref) {
    return adjustInventoryBatch(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(AdjustInventoryBatch value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<AdjustInventoryBatch>(value),
    );
  }
}

String _$adjustInventoryBatchHash() =>
    r'8bdb17aa8b2777313c561bdc02a136575bdddb36';

@ProviderFor(InventoryBatchesController)
final inventoryBatchesControllerProvider =
    InventoryBatchesControllerProvider._();

final class InventoryBatchesControllerProvider
    extends
        $NotifierProvider<InventoryBatchesController, InventoryBatchesState> {
  InventoryBatchesControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'inventoryBatchesControllerProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$inventoryBatchesControllerHash();

  @$internal
  @override
  InventoryBatchesController create() => InventoryBatchesController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(InventoryBatchesState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<InventoryBatchesState>(value),
    );
  }
}

String _$inventoryBatchesControllerHash() =>
    r'90ad7e58607043850aec9951e358258476600e62';

abstract class _$InventoryBatchesController
    extends $Notifier<InventoryBatchesState> {
  InventoryBatchesState build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<InventoryBatchesState, InventoryBatchesState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<InventoryBatchesState, InventoryBatchesState>,
              InventoryBatchesState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}
