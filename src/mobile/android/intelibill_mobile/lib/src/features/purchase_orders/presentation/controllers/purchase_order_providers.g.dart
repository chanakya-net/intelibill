// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'purchase_order_providers.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(purchaseOrderDraftLocalDataSource)
final purchaseOrderDraftLocalDataSourceProvider =
    PurchaseOrderDraftLocalDataSourceProvider._();

final class PurchaseOrderDraftLocalDataSourceProvider
    extends
        $FunctionalProvider<
          AsyncValue<PurchaseOrderDraftLocalDataSource>,
          PurchaseOrderDraftLocalDataSource,
          FutureOr<PurchaseOrderDraftLocalDataSource>
        >
    with
        $FutureModifier<PurchaseOrderDraftLocalDataSource>,
        $FutureProvider<PurchaseOrderDraftLocalDataSource> {
  PurchaseOrderDraftLocalDataSourceProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'purchaseOrderDraftLocalDataSourceProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() =>
      _$purchaseOrderDraftLocalDataSourceHash();

  @$internal
  @override
  $FutureProviderElement<PurchaseOrderDraftLocalDataSource> $createElement(
    $ProviderPointer pointer,
  ) => $FutureProviderElement(pointer);

  @override
  FutureOr<PurchaseOrderDraftLocalDataSource> create(Ref ref) {
    return purchaseOrderDraftLocalDataSource(ref);
  }
}

String _$purchaseOrderDraftLocalDataSourceHash() =>
    r'0f4cda6d52e610c1a735135b1f32bfb38bafc5ff';

@ProviderFor(purchaseOrderDraftLocalKey)
final purchaseOrderDraftLocalKeyProvider = PurchaseOrderDraftLocalKeyFamily._();

final class PurchaseOrderDraftLocalKeyProvider
    extends
        $FunctionalProvider<
          PurchaseOrderDraftLocalKey?,
          PurchaseOrderDraftLocalKey?,
          PurchaseOrderDraftLocalKey?
        >
    with $Provider<PurchaseOrderDraftLocalKey?> {
  PurchaseOrderDraftLocalKeyProvider._({
    required PurchaseOrderDraftLocalKeyFamily super.from,
    required String super.argument,
  }) : super(
         retry: null,
         name: r'purchaseOrderDraftLocalKeyProvider',
         isAutoDispose: true,
         dependencies: null,
         $allTransitiveDependencies: null,
       );

  @override
  String debugGetCreateSourceHash() => _$purchaseOrderDraftLocalKeyHash();

  @override
  String toString() {
    return r'purchaseOrderDraftLocalKeyProvider'
        ''
        '($argument)';
  }

  @$internal
  @override
  $ProviderElement<PurchaseOrderDraftLocalKey?> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  PurchaseOrderDraftLocalKey? create(Ref ref) {
    final argument = this.argument as String;
    return purchaseOrderDraftLocalKey(ref, argument);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(PurchaseOrderDraftLocalKey? value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<PurchaseOrderDraftLocalKey?>(value),
    );
  }

  @override
  bool operator ==(Object other) {
    return other is PurchaseOrderDraftLocalKeyProvider &&
        other.argument == argument;
  }

  @override
  int get hashCode {
    return argument.hashCode;
  }
}

String _$purchaseOrderDraftLocalKeyHash() =>
    r'60146c923b3052a733e8f274626bff3fbe3cbcd0';

final class PurchaseOrderDraftLocalKeyFamily extends $Family
    with $FunctionalFamilyOverride<PurchaseOrderDraftLocalKey?, String> {
  PurchaseOrderDraftLocalKeyFamily._()
    : super(
        retry: null,
        name: r'purchaseOrderDraftLocalKeyProvider',
        dependencies: null,
        $allTransitiveDependencies: null,
        isAutoDispose: true,
      );

  PurchaseOrderDraftLocalKeyProvider call(String target) =>
      PurchaseOrderDraftLocalKeyProvider._(argument: target, from: this);

  @override
  String toString() => r'purchaseOrderDraftLocalKeyProvider';
}

@ProviderFor(purchaseOrderRemoteDataSource)
final purchaseOrderRemoteDataSourceProvider =
    PurchaseOrderRemoteDataSourceProvider._();

final class PurchaseOrderRemoteDataSourceProvider
    extends
        $FunctionalProvider<
          PurchaseOrderRemoteDataSource,
          PurchaseOrderRemoteDataSource,
          PurchaseOrderRemoteDataSource
        >
    with $Provider<PurchaseOrderRemoteDataSource> {
  PurchaseOrderRemoteDataSourceProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'purchaseOrderRemoteDataSourceProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$purchaseOrderRemoteDataSourceHash();

  @$internal
  @override
  $ProviderElement<PurchaseOrderRemoteDataSource> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  PurchaseOrderRemoteDataSource create(Ref ref) {
    return purchaseOrderRemoteDataSource(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(PurchaseOrderRemoteDataSource value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<PurchaseOrderRemoteDataSource>(
        value,
      ),
    );
  }
}

String _$purchaseOrderRemoteDataSourceHash() =>
    r'16baae3476f5f140e35f26174fa3b2d44ed1a356';

@ProviderFor(purchaseOrderRepository)
final purchaseOrderRepositoryProvider = PurchaseOrderRepositoryProvider._();

final class PurchaseOrderRepositoryProvider
    extends
        $FunctionalProvider<
          PurchaseOrderRepository,
          PurchaseOrderRepository,
          PurchaseOrderRepository
        >
    with $Provider<PurchaseOrderRepository> {
  PurchaseOrderRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'purchaseOrderRepositoryProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$purchaseOrderRepositoryHash();

  @$internal
  @override
  $ProviderElement<PurchaseOrderRepository> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  PurchaseOrderRepository create(Ref ref) {
    return purchaseOrderRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(PurchaseOrderRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<PurchaseOrderRepository>(value),
    );
  }
}

String _$purchaseOrderRepositoryHash() =>
    r'36f5cffccea6cbff2a309a57637b7932c3e95e8c';

@ProviderFor(getPurchaseOrders)
final getPurchaseOrdersProvider = GetPurchaseOrdersProvider._();

final class GetPurchaseOrdersProvider
    extends
        $FunctionalProvider<
          GetPurchaseOrders,
          GetPurchaseOrders,
          GetPurchaseOrders
        >
    with $Provider<GetPurchaseOrders> {
  GetPurchaseOrdersProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getPurchaseOrdersProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getPurchaseOrdersHash();

  @$internal
  @override
  $ProviderElement<GetPurchaseOrders> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  GetPurchaseOrders create(Ref ref) {
    return getPurchaseOrders(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetPurchaseOrders value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetPurchaseOrders>(value),
    );
  }
}

String _$getPurchaseOrdersHash() => r'be03ee8b9e5c2836bcb39ad0bd0dc1200e40e0e7';

@ProviderFor(getPurchaseOrder)
final getPurchaseOrderProvider = GetPurchaseOrderProvider._();

final class GetPurchaseOrderProvider
    extends
        $FunctionalProvider<
          GetPurchaseOrder,
          GetPurchaseOrder,
          GetPurchaseOrder
        >
    with $Provider<GetPurchaseOrder> {
  GetPurchaseOrderProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getPurchaseOrderProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getPurchaseOrderHash();

  @$internal
  @override
  $ProviderElement<GetPurchaseOrder> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  GetPurchaseOrder create(Ref ref) {
    return getPurchaseOrder(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetPurchaseOrder value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetPurchaseOrder>(value),
    );
  }
}

String _$getPurchaseOrderHash() => r'a6a115ce2ce5c6324aed82e3fa79efbc2407ae56';

@ProviderFor(createPurchaseOrderDraft)
final createPurchaseOrderDraftProvider = CreatePurchaseOrderDraftProvider._();

final class CreatePurchaseOrderDraftProvider
    extends
        $FunctionalProvider<
          CreatePurchaseOrderDraft,
          CreatePurchaseOrderDraft,
          CreatePurchaseOrderDraft
        >
    with $Provider<CreatePurchaseOrderDraft> {
  CreatePurchaseOrderDraftProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'createPurchaseOrderDraftProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$createPurchaseOrderDraftHash();

  @$internal
  @override
  $ProviderElement<CreatePurchaseOrderDraft> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  CreatePurchaseOrderDraft create(Ref ref) {
    return createPurchaseOrderDraft(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(CreatePurchaseOrderDraft value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<CreatePurchaseOrderDraft>(value),
    );
  }
}

String _$createPurchaseOrderDraftHash() =>
    r'1f50a5ce07b573d211a8f2ec67d0981608a2e873';

@ProviderFor(updatePurchaseOrderDraft)
final updatePurchaseOrderDraftProvider = UpdatePurchaseOrderDraftProvider._();

final class UpdatePurchaseOrderDraftProvider
    extends
        $FunctionalProvider<
          UpdatePurchaseOrderDraft,
          UpdatePurchaseOrderDraft,
          UpdatePurchaseOrderDraft
        >
    with $Provider<UpdatePurchaseOrderDraft> {
  UpdatePurchaseOrderDraftProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'updatePurchaseOrderDraftProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$updatePurchaseOrderDraftHash();

  @$internal
  @override
  $ProviderElement<UpdatePurchaseOrderDraft> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  UpdatePurchaseOrderDraft create(Ref ref) {
    return updatePurchaseOrderDraft(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(UpdatePurchaseOrderDraft value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<UpdatePurchaseOrderDraft>(value),
    );
  }
}

String _$updatePurchaseOrderDraftHash() =>
    r'9d38c8ff5cfb2fa8e17e04dce88155d5cc933665';

@ProviderFor(deletePurchaseOrderDraft)
final deletePurchaseOrderDraftProvider = DeletePurchaseOrderDraftProvider._();

final class DeletePurchaseOrderDraftProvider
    extends
        $FunctionalProvider<
          DeletePurchaseOrderDraft,
          DeletePurchaseOrderDraft,
          DeletePurchaseOrderDraft
        >
    with $Provider<DeletePurchaseOrderDraft> {
  DeletePurchaseOrderDraftProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'deletePurchaseOrderDraftProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$deletePurchaseOrderDraftHash();

  @$internal
  @override
  $ProviderElement<DeletePurchaseOrderDraft> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  DeletePurchaseOrderDraft create(Ref ref) {
    return deletePurchaseOrderDraft(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(DeletePurchaseOrderDraft value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<DeletePurchaseOrderDraft>(value),
    );
  }
}

String _$deletePurchaseOrderDraftHash() =>
    r'9de06f19ffa72fae62f59730aa59cde4be21567f';

@ProviderFor(cancelPurchaseOrder)
final cancelPurchaseOrderProvider = CancelPurchaseOrderProvider._();

final class CancelPurchaseOrderProvider
    extends
        $FunctionalProvider<
          CancelPurchaseOrder,
          CancelPurchaseOrder,
          CancelPurchaseOrder
        >
    with $Provider<CancelPurchaseOrder> {
  CancelPurchaseOrderProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'cancelPurchaseOrderProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$cancelPurchaseOrderHash();

  @$internal
  @override
  $ProviderElement<CancelPurchaseOrder> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  CancelPurchaseOrder create(Ref ref) {
    return cancelPurchaseOrder(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(CancelPurchaseOrder value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<CancelPurchaseOrder>(value),
    );
  }
}

String _$cancelPurchaseOrderHash() =>
    r'829a343d7251c510006ae1b463931403fc0ab65f';

@ProviderFor(closePurchaseOrder)
final closePurchaseOrderProvider = ClosePurchaseOrderProvider._();

final class ClosePurchaseOrderProvider
    extends
        $FunctionalProvider<
          ClosePurchaseOrder,
          ClosePurchaseOrder,
          ClosePurchaseOrder
        >
    with $Provider<ClosePurchaseOrder> {
  ClosePurchaseOrderProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'closePurchaseOrderProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$closePurchaseOrderHash();

  @$internal
  @override
  $ProviderElement<ClosePurchaseOrder> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  ClosePurchaseOrder create(Ref ref) {
    return closePurchaseOrder(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(ClosePurchaseOrder value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<ClosePurchaseOrder>(value),
    );
  }
}

String _$closePurchaseOrderHash() =>
    r'ae7d56a255fe28adc6677c2df13edd3d59f469a8';

@ProviderFor(receivePurchaseOrder)
final receivePurchaseOrderProvider = ReceivePurchaseOrderProvider._();

final class ReceivePurchaseOrderProvider
    extends
        $FunctionalProvider<
          ReceivePurchaseOrder,
          ReceivePurchaseOrder,
          ReceivePurchaseOrder
        >
    with $Provider<ReceivePurchaseOrder> {
  ReceivePurchaseOrderProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'receivePurchaseOrderProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$receivePurchaseOrderHash();

  @$internal
  @override
  $ProviderElement<ReceivePurchaseOrder> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  ReceivePurchaseOrder create(Ref ref) {
    return receivePurchaseOrder(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(ReceivePurchaseOrder value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<ReceivePurchaseOrder>(value),
    );
  }
}

String _$receivePurchaseOrderHash() =>
    r'4cac46c8d8e9396ae1fe7a9a3eb3513bcece3003';

@ProviderFor(placePurchaseOrder)
final placePurchaseOrderProvider = PlacePurchaseOrderProvider._();

final class PlacePurchaseOrderProvider
    extends
        $FunctionalProvider<
          PlacePurchaseOrder,
          PlacePurchaseOrder,
          PlacePurchaseOrder
        >
    with $Provider<PlacePurchaseOrder> {
  PlacePurchaseOrderProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'placePurchaseOrderProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$placePurchaseOrderHash();

  @$internal
  @override
  $ProviderElement<PlacePurchaseOrder> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  PlacePurchaseOrder create(Ref ref) {
    return placePurchaseOrder(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(PlacePurchaseOrder value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<PlacePurchaseOrder>(value),
    );
  }
}

String _$placePurchaseOrderHash() =>
    r'c9aaa114a9c3bf9a15e45769aa0d6a7e52e27f5f';
