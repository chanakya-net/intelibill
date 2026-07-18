// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'purchase_order_providers.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

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
