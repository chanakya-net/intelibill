// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'new_sale_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(salesRemoteDataSource)
final salesRemoteDataSourceProvider = SalesRemoteDataSourceProvider._();

final class SalesRemoteDataSourceProvider
    extends
        $FunctionalProvider<
          SalesRemoteDataSource,
          SalesRemoteDataSource,
          SalesRemoteDataSource
        >
    with $Provider<SalesRemoteDataSource> {
  SalesRemoteDataSourceProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'salesRemoteDataSourceProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$salesRemoteDataSourceHash();

  @$internal
  @override
  $ProviderElement<SalesRemoteDataSource> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  SalesRemoteDataSource create(Ref ref) {
    return salesRemoteDataSource(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(SalesRemoteDataSource value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<SalesRemoteDataSource>(value),
    );
  }
}

String _$salesRemoteDataSourceHash() =>
    r'5426be5eb24e443eb7af5f631786dc9c93d2d324';

@ProviderFor(salesRepository)
final salesRepositoryProvider = SalesRepositoryProvider._();

final class SalesRepositoryProvider
    extends
        $FunctionalProvider<SalesRepository, SalesRepository, SalesRepository>
    with $Provider<SalesRepository> {
  SalesRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'salesRepositoryProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$salesRepositoryHash();

  @$internal
  @override
  $ProviderElement<SalesRepository> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  SalesRepository create(Ref ref) {
    return salesRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(SalesRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<SalesRepository>(value),
    );
  }
}

String _$salesRepositoryHash() => r'c2c2fb52c66543ac7d42f0d0566b66a3135b3b99';

@ProviderFor(searchSellables)
final searchSellablesProvider = SearchSellablesProvider._();

final class SearchSellablesProvider
    extends
        $FunctionalProvider<SearchSellables, SearchSellables, SearchSellables>
    with $Provider<SearchSellables> {
  SearchSellablesProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'searchSellablesProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$searchSellablesHash();

  @$internal
  @override
  $ProviderElement<SearchSellables> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  SearchSellables create(Ref ref) {
    return searchSellables(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(SearchSellables value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<SearchSellables>(value),
    );
  }
}

String _$searchSellablesHash() => r'f8cb645e51f52ada62973041042cc5236c718229';

@ProviderFor(NewSaleController)
final newSaleControllerProvider = NewSaleControllerProvider._();

final class NewSaleControllerProvider
    extends $NotifierProvider<NewSaleController, NewSaleState> {
  NewSaleControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'newSaleControllerProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$newSaleControllerHash();

  @$internal
  @override
  NewSaleController create() => NewSaleController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(NewSaleState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<NewSaleState>(value),
    );
  }
}

String _$newSaleControllerHash() => r'dc78a84525319e10b44559fec2c4efdd88bd20a6';

abstract class _$NewSaleController extends $Notifier<NewSaleState> {
  NewSaleState build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<NewSaleState, NewSaleState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<NewSaleState, NewSaleState>,
              NewSaleState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}
