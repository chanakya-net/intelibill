// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'customers_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(customerRemoteDataSource)
final customerRemoteDataSourceProvider = CustomerRemoteDataSourceProvider._();

final class CustomerRemoteDataSourceProvider
    extends
        $FunctionalProvider<
          CustomerRemoteDataSource,
          CustomerRemoteDataSource,
          CustomerRemoteDataSource
        >
    with $Provider<CustomerRemoteDataSource> {
  CustomerRemoteDataSourceProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'customerRemoteDataSourceProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$customerRemoteDataSourceHash();

  @$internal
  @override
  $ProviderElement<CustomerRemoteDataSource> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  CustomerRemoteDataSource create(Ref ref) {
    return customerRemoteDataSource(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(CustomerRemoteDataSource value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<CustomerRemoteDataSource>(value),
    );
  }
}

String _$customerRemoteDataSourceHash() =>
    r'd69c760a1c56c3ea3767f9215e2f9a856be2929f';

@ProviderFor(customerRepository)
final customerRepositoryProvider = CustomerRepositoryProvider._();

final class CustomerRepositoryProvider
    extends
        $FunctionalProvider<
          CustomerRepository,
          CustomerRepository,
          CustomerRepository
        >
    with $Provider<CustomerRepository> {
  CustomerRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'customerRepositoryProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$customerRepositoryHash();

  @$internal
  @override
  $ProviderElement<CustomerRepository> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  CustomerRepository create(Ref ref) {
    return customerRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(CustomerRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<CustomerRepository>(value),
    );
  }
}

String _$customerRepositoryHash() =>
    r'73fffc3ee9905528ea70e2ccd6c3960c9e6add8c';

@ProviderFor(getCustomersUseCase)
final getCustomersUseCaseProvider = GetCustomersUseCaseProvider._();

final class GetCustomersUseCaseProvider
    extends $FunctionalProvider<GetCustomers, GetCustomers, GetCustomers>
    with $Provider<GetCustomers> {
  GetCustomersUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getCustomersUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getCustomersUseCaseHash();

  @$internal
  @override
  $ProviderElement<GetCustomers> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  GetCustomers create(Ref ref) {
    return getCustomersUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetCustomers value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetCustomers>(value),
    );
  }
}

String _$getCustomersUseCaseHash() =>
    r'4195f8a9fab7f2d06ebd53bce214462ec6d5a668';

@ProviderFor(createCustomerUseCase)
final createCustomerUseCaseProvider = CreateCustomerUseCaseProvider._();

final class CreateCustomerUseCaseProvider
    extends $FunctionalProvider<CreateCustomer, CreateCustomer, CreateCustomer>
    with $Provider<CreateCustomer> {
  CreateCustomerUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'createCustomerUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$createCustomerUseCaseHash();

  @$internal
  @override
  $ProviderElement<CreateCustomer> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  CreateCustomer create(Ref ref) {
    return createCustomerUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(CreateCustomer value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<CreateCustomer>(value),
    );
  }
}

String _$createCustomerUseCaseHash() =>
    r'3dd1c0a62539bdf296fa5f19093643fb504b8504';

@ProviderFor(CustomersController)
final customersControllerProvider = CustomersControllerProvider._();

final class CustomersControllerProvider
    extends $NotifierProvider<CustomersController, CustomersState> {
  CustomersControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'customersControllerProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$customersControllerHash();

  @$internal
  @override
  CustomersController create() => CustomersController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(CustomersState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<CustomersState>(value),
    );
  }
}

String _$customersControllerHash() =>
    r'5be7e4e9a065317effd3d386771d333b0d4b3c56';

abstract class _$CustomersController extends $Notifier<CustomersState> {
  CustomersState build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<CustomersState, CustomersState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<CustomersState, CustomersState>,
              CustomersState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}
