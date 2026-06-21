// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'services_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(servicesRemoteDataSource)
final servicesRemoteDataSourceProvider = ServicesRemoteDataSourceProvider._();

final class ServicesRemoteDataSourceProvider
    extends
        $FunctionalProvider<
          ServicesRemoteDataSource,
          ServicesRemoteDataSource,
          ServicesRemoteDataSource
        >
    with $Provider<ServicesRemoteDataSource> {
  ServicesRemoteDataSourceProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'servicesRemoteDataSourceProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$servicesRemoteDataSourceHash();

  @$internal
  @override
  $ProviderElement<ServicesRemoteDataSource> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  ServicesRemoteDataSource create(Ref ref) {
    return servicesRemoteDataSource(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(ServicesRemoteDataSource value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<ServicesRemoteDataSource>(value),
    );
  }
}

String _$servicesRemoteDataSourceHash() =>
    r'd44eb73773ba2ac554a51559b002b8241fd2b380';

@ProviderFor(servicesRepository)
final servicesRepositoryProvider = ServicesRepositoryProvider._();

final class ServicesRepositoryProvider
    extends
        $FunctionalProvider<
          ServicesRepository,
          ServicesRepository,
          ServicesRepository
        >
    with $Provider<ServicesRepository> {
  ServicesRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'servicesRepositoryProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$servicesRepositoryHash();

  @$internal
  @override
  $ProviderElement<ServicesRepository> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  ServicesRepository create(Ref ref) {
    return servicesRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(ServicesRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<ServicesRepository>(value),
    );
  }
}

String _$servicesRepositoryHash() =>
    r'1fed23717ac4fa60ca6072f05ddda149ae8b309c';

@ProviderFor(getServicesUseCase)
final getServicesUseCaseProvider = GetServicesUseCaseProvider._();

final class GetServicesUseCaseProvider
    extends $FunctionalProvider<GetServices, GetServices, GetServices>
    with $Provider<GetServices> {
  GetServicesUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getServicesUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getServicesUseCaseHash();

  @$internal
  @override
  $ProviderElement<GetServices> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  GetServices create(Ref ref) {
    return getServicesUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetServices value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetServices>(value),
    );
  }
}

String _$getServicesUseCaseHash() =>
    r'8cf4e191bb756e4a31e32f1518fefb672120973f';

@ProviderFor(createServiceUseCase)
final createServiceUseCaseProvider = CreateServiceUseCaseProvider._();

final class CreateServiceUseCaseProvider
    extends $FunctionalProvider<CreateService, CreateService, CreateService>
    with $Provider<CreateService> {
  CreateServiceUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'createServiceUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$createServiceUseCaseHash();

  @$internal
  @override
  $ProviderElement<CreateService> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  CreateService create(Ref ref) {
    return createServiceUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(CreateService value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<CreateService>(value),
    );
  }
}

String _$createServiceUseCaseHash() =>
    r'd757bdc477988f86748f7f37a0082893634ec24c';

@ProviderFor(updateServiceUseCase)
final updateServiceUseCaseProvider = UpdateServiceUseCaseProvider._();

final class UpdateServiceUseCaseProvider
    extends $FunctionalProvider<UpdateService, UpdateService, UpdateService>
    with $Provider<UpdateService> {
  UpdateServiceUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'updateServiceUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$updateServiceUseCaseHash();

  @$internal
  @override
  $ProviderElement<UpdateService> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  UpdateService create(Ref ref) {
    return updateServiceUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(UpdateService value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<UpdateService>(value),
    );
  }
}

String _$updateServiceUseCaseHash() =>
    r'aea73935e6a05a1c4e2f0ffade86645e2758603d';

@ProviderFor(activateServiceUseCase)
final activateServiceUseCaseProvider = ActivateServiceUseCaseProvider._();

final class ActivateServiceUseCaseProvider
    extends
        $FunctionalProvider<ActivateService, ActivateService, ActivateService>
    with $Provider<ActivateService> {
  ActivateServiceUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'activateServiceUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$activateServiceUseCaseHash();

  @$internal
  @override
  $ProviderElement<ActivateService> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  ActivateService create(Ref ref) {
    return activateServiceUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(ActivateService value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<ActivateService>(value),
    );
  }
}

String _$activateServiceUseCaseHash() =>
    r'9b92273c886acfbf2543e5c6c8a8f4abb751ae3f';

@ProviderFor(deactivateServiceUseCase)
final deactivateServiceUseCaseProvider = DeactivateServiceUseCaseProvider._();

final class DeactivateServiceUseCaseProvider
    extends
        $FunctionalProvider<
          DeactivateService,
          DeactivateService,
          DeactivateService
        >
    with $Provider<DeactivateService> {
  DeactivateServiceUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'deactivateServiceUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$deactivateServiceUseCaseHash();

  @$internal
  @override
  $ProviderElement<DeactivateService> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  DeactivateService create(Ref ref) {
    return deactivateServiceUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(DeactivateService value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<DeactivateService>(value),
    );
  }
}

String _$deactivateServiceUseCaseHash() =>
    r'168422cc17fe47505aab2b3290e17cc9be8d52da';

@ProviderFor(ServicesController)
final servicesControllerProvider = ServicesControllerProvider._();

final class ServicesControllerProvider
    extends $NotifierProvider<ServicesController, ServicesState> {
  ServicesControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'servicesControllerProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$servicesControllerHash();

  @$internal
  @override
  ServicesController create() => ServicesController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(ServicesState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<ServicesState>(value),
    );
  }
}

String _$servicesControllerHash() =>
    r'178015ee8611e73fc88788092892457a4a56cd76';

abstract class _$ServicesController extends $Notifier<ServicesState> {
  ServicesState build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<ServicesState, ServicesState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<ServicesState, ServicesState>,
              ServicesState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}
