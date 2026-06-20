// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'sales_providers.dart';

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

@ProviderFor(previewSaleReturn)
final previewSaleReturnProvider = PreviewSaleReturnProvider._();

final class PreviewSaleReturnProvider
    extends
        $FunctionalProvider<
          PreviewSaleReturn,
          PreviewSaleReturn,
          PreviewSaleReturn
        >
    with $Provider<PreviewSaleReturn> {
  PreviewSaleReturnProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'previewSaleReturnProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$previewSaleReturnHash();

  @$internal
  @override
  $ProviderElement<PreviewSaleReturn> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  PreviewSaleReturn create(Ref ref) {
    return previewSaleReturn(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(PreviewSaleReturn value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<PreviewSaleReturn>(value),
    );
  }
}

String _$previewSaleReturnHash() => r'187878c7d732c784a7a1c1054a848c1cdc79a634';

@ProviderFor(recordSaleReturn)
final recordSaleReturnProvider = RecordSaleReturnProvider._();

final class RecordSaleReturnProvider
    extends
        $FunctionalProvider<
          RecordSaleReturn,
          RecordSaleReturn,
          RecordSaleReturn
        >
    with $Provider<RecordSaleReturn> {
  RecordSaleReturnProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'recordSaleReturnProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$recordSaleReturnHash();

  @$internal
  @override
  $ProviderElement<RecordSaleReturn> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  RecordSaleReturn create(Ref ref) {
    return recordSaleReturn(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(RecordSaleReturn value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<RecordSaleReturn>(value),
    );
  }
}

String _$recordSaleReturnHash() => r'78fb666af9c0d4b19862f7002b6e3b37f3eaf222';
