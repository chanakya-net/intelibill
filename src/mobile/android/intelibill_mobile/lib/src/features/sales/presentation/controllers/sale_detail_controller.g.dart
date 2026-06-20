// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'sale_detail_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(saleDetailRemoteDataSource)
final saleDetailRemoteDataSourceProvider =
    SaleDetailRemoteDataSourceProvider._();

final class SaleDetailRemoteDataSourceProvider
    extends
        $FunctionalProvider<
          SalesRemoteDataSource,
          SalesRemoteDataSource,
          SalesRemoteDataSource
        >
    with $Provider<SalesRemoteDataSource> {
  SaleDetailRemoteDataSourceProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'saleDetailRemoteDataSourceProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$saleDetailRemoteDataSourceHash();

  @$internal
  @override
  $ProviderElement<SalesRemoteDataSource> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  SalesRemoteDataSource create(Ref ref) {
    return saleDetailRemoteDataSource(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(SalesRemoteDataSource value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<SalesRemoteDataSource>(value),
    );
  }
}

String _$saleDetailRemoteDataSourceHash() =>
    r'14430bb3745e29256bbaf36728f22f7ddb96cb4e';

@ProviderFor(saleDetailRepository)
final saleDetailRepositoryProvider = SaleDetailRepositoryProvider._();

final class SaleDetailRepositoryProvider
    extends
        $FunctionalProvider<SalesRepository, SalesRepository, SalesRepository>
    with $Provider<SalesRepository> {
  SaleDetailRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'saleDetailRepositoryProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$saleDetailRepositoryHash();

  @$internal
  @override
  $ProviderElement<SalesRepository> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  SalesRepository create(Ref ref) {
    return saleDetailRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(SalesRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<SalesRepository>(value),
    );
  }
}

String _$saleDetailRepositoryHash() =>
    r'fde1316073572e58c82f3efca65da92dc2969d2d';

@ProviderFor(getSaleDetail)
final getSaleDetailProvider = GetSaleDetailProvider._();

final class GetSaleDetailProvider
    extends $FunctionalProvider<GetSaleDetail, GetSaleDetail, GetSaleDetail>
    with $Provider<GetSaleDetail> {
  GetSaleDetailProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getSaleDetailProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getSaleDetailHash();

  @$internal
  @override
  $ProviderElement<GetSaleDetail> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  GetSaleDetail create(Ref ref) {
    return getSaleDetail(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetSaleDetail value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetSaleDetail>(value),
    );
  }
}

String _$getSaleDetailHash() => r'301cfdec33e06064d35ece503454af24f65e7438';

@ProviderFor(SaleDetailController)
final saleDetailControllerProvider = SaleDetailControllerFamily._();

final class SaleDetailControllerProvider
    extends $NotifierProvider<SaleDetailController, SaleDetailState> {
  SaleDetailControllerProvider._({
    required SaleDetailControllerFamily super.from,
    required String super.argument,
  }) : super(
         retry: null,
         name: r'saleDetailControllerProvider',
         isAutoDispose: true,
         dependencies: null,
         $allTransitiveDependencies: null,
       );

  @override
  String debugGetCreateSourceHash() => _$saleDetailControllerHash();

  @override
  String toString() {
    return r'saleDetailControllerProvider'
        ''
        '($argument)';
  }

  @$internal
  @override
  SaleDetailController create() => SaleDetailController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(SaleDetailState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<SaleDetailState>(value),
    );
  }

  @override
  bool operator ==(Object other) {
    return other is SaleDetailControllerProvider && other.argument == argument;
  }

  @override
  int get hashCode {
    return argument.hashCode;
  }
}

String _$saleDetailControllerHash() =>
    r'9a10b378a54ad71465de1ba01a9631f7bb395982';

final class SaleDetailControllerFamily extends $Family
    with
        $ClassFamilyOverride<
          SaleDetailController,
          SaleDetailState,
          SaleDetailState,
          SaleDetailState,
          String
        > {
  SaleDetailControllerFamily._()
    : super(
        retry: null,
        name: r'saleDetailControllerProvider',
        dependencies: null,
        $allTransitiveDependencies: null,
        isAutoDispose: true,
      );

  SaleDetailControllerProvider call(String saleId) =>
      SaleDetailControllerProvider._(argument: saleId, from: this);

  @override
  String toString() => r'saleDetailControllerProvider';
}

abstract class _$SaleDetailController extends $Notifier<SaleDetailState> {
  late final _$args = ref.$arg as String;
  String get saleId => _$args;

  SaleDetailState build(String saleId);
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<SaleDetailState, SaleDetailState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<SaleDetailState, SaleDetailState>,
              SaleDetailState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, () => build(_$args));
  }
}
