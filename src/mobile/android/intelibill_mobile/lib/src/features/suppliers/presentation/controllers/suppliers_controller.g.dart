// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'suppliers_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(supplierRemoteDataSource)
final supplierRemoteDataSourceProvider = SupplierRemoteDataSourceProvider._();

final class SupplierRemoteDataSourceProvider
    extends
        $FunctionalProvider<
          SupplierRemoteDataSource,
          SupplierRemoteDataSource,
          SupplierRemoteDataSource
        >
    with $Provider<SupplierRemoteDataSource> {
  SupplierRemoteDataSourceProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'supplierRemoteDataSourceProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$supplierRemoteDataSourceHash();

  @$internal
  @override
  $ProviderElement<SupplierRemoteDataSource> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  SupplierRemoteDataSource create(Ref ref) {
    return supplierRemoteDataSource(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(SupplierRemoteDataSource value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<SupplierRemoteDataSource>(value),
    );
  }
}

String _$supplierRemoteDataSourceHash() =>
    r'749d335393117fbcedd122d05ec28e5df6f479f5';

@ProviderFor(supplierRepository)
final supplierRepositoryProvider = SupplierRepositoryProvider._();

final class SupplierRepositoryProvider
    extends
        $FunctionalProvider<
          SupplierRepository,
          SupplierRepository,
          SupplierRepository
        >
    with $Provider<SupplierRepository> {
  SupplierRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'supplierRepositoryProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$supplierRepositoryHash();

  @$internal
  @override
  $ProviderElement<SupplierRepository> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  SupplierRepository create(Ref ref) {
    return supplierRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(SupplierRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<SupplierRepository>(value),
    );
  }
}

String _$supplierRepositoryHash() =>
    r'd35b71fef7a18e70afbd58c30d328d68bc9b9af6';

@ProviderFor(getSuppliersUseCase)
final getSuppliersUseCaseProvider = GetSuppliersUseCaseProvider._();

final class GetSuppliersUseCaseProvider
    extends $FunctionalProvider<GetSuppliers, GetSuppliers, GetSuppliers>
    with $Provider<GetSuppliers> {
  GetSuppliersUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getSuppliersUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getSuppliersUseCaseHash();

  @$internal
  @override
  $ProviderElement<GetSuppliers> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  GetSuppliers create(Ref ref) {
    return getSuppliersUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetSuppliers value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetSuppliers>(value),
    );
  }
}

String _$getSuppliersUseCaseHash() =>
    r'8601379a6b9c107830a631b01618d5a886fe638f';

@ProviderFor(createSupplierUseCase)
final createSupplierUseCaseProvider = CreateSupplierUseCaseProvider._();

final class CreateSupplierUseCaseProvider
    extends $FunctionalProvider<CreateSupplier, CreateSupplier, CreateSupplier>
    with $Provider<CreateSupplier> {
  CreateSupplierUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'createSupplierUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$createSupplierUseCaseHash();

  @$internal
  @override
  $ProviderElement<CreateSupplier> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  CreateSupplier create(Ref ref) {
    return createSupplierUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(CreateSupplier value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<CreateSupplier>(value),
    );
  }
}

String _$createSupplierUseCaseHash() =>
    r'9f05b23433b0a46f6abde5d4169c67d3f103a127';

@ProviderFor(SuppliersController)
final suppliersControllerProvider = SuppliersControllerProvider._();

final class SuppliersControllerProvider
    extends $NotifierProvider<SuppliersController, SuppliersState> {
  SuppliersControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'suppliersControllerProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$suppliersControllerHash();

  @$internal
  @override
  SuppliersController create() => SuppliersController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(SuppliersState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<SuppliersState>(value),
    );
  }
}

String _$suppliersControllerHash() =>
    r'513d8fd425baea373b29d7293e5e4c658a658fdb';

abstract class _$SuppliersController extends $Notifier<SuppliersState> {
  SuppliersState build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<SuppliersState, SuppliersState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<SuppliersState, SuppliersState>,
              SuppliersState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}
