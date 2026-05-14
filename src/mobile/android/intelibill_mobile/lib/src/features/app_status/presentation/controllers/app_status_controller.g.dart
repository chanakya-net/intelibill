// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'app_status_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(appStatusRemoteDataSource)
final appStatusRemoteDataSourceProvider = AppStatusRemoteDataSourceProvider._();

final class AppStatusRemoteDataSourceProvider
    extends
        $FunctionalProvider<
          AppStatusRemoteDataSource,
          AppStatusRemoteDataSource,
          AppStatusRemoteDataSource
        >
    with $Provider<AppStatusRemoteDataSource> {
  AppStatusRemoteDataSourceProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'appStatusRemoteDataSourceProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$appStatusRemoteDataSourceHash();

  @$internal
  @override
  $ProviderElement<AppStatusRemoteDataSource> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  AppStatusRemoteDataSource create(Ref ref) {
    return appStatusRemoteDataSource(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(AppStatusRemoteDataSource value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<AppStatusRemoteDataSource>(value),
    );
  }
}

String _$appStatusRemoteDataSourceHash() =>
    r'921dc6cf8138b1c72b794e2411eb32e12c6ae668';

@ProviderFor(appStatusRepository)
final appStatusRepositoryProvider = AppStatusRepositoryProvider._();

final class AppStatusRepositoryProvider
    extends
        $FunctionalProvider<
          AppStatusRepository,
          AppStatusRepository,
          AppStatusRepository
        >
    with $Provider<AppStatusRepository> {
  AppStatusRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'appStatusRepositoryProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$appStatusRepositoryHash();

  @$internal
  @override
  $ProviderElement<AppStatusRepository> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  AppStatusRepository create(Ref ref) {
    return appStatusRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(AppStatusRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<AppStatusRepository>(value),
    );
  }
}

String _$appStatusRepositoryHash() =>
    r'b9fdd4413de0e1e00e0c22dd0193a5ea791d2495';

@ProviderFor(getAppStatusUseCase)
final getAppStatusUseCaseProvider = GetAppStatusUseCaseProvider._();

final class GetAppStatusUseCaseProvider
    extends $FunctionalProvider<GetAppStatus, GetAppStatus, GetAppStatus>
    with $Provider<GetAppStatus> {
  GetAppStatusUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getAppStatusUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getAppStatusUseCaseHash();

  @$internal
  @override
  $ProviderElement<GetAppStatus> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  GetAppStatus create(Ref ref) {
    return getAppStatusUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetAppStatus value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetAppStatus>(value),
    );
  }
}

String _$getAppStatusUseCaseHash() =>
    r'b67daa57695f5a2ab177e5060746795ab5eccc3a';

@ProviderFor(AppStatusController)
final appStatusControllerProvider = AppStatusControllerProvider._();

final class AppStatusControllerProvider
    extends $AsyncNotifierProvider<AppStatusController, AppStatus> {
  AppStatusControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'appStatusControllerProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$appStatusControllerHash();

  @$internal
  @override
  AppStatusController create() => AppStatusController();
}

String _$appStatusControllerHash() =>
    r'd6c31e3c9ecd6db15a6768887fa5335302e4393e';

abstract class _$AppStatusController extends $AsyncNotifier<AppStatus> {
  FutureOr<AppStatus> build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<AsyncValue<AppStatus>, AppStatus>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<AsyncValue<AppStatus>, AppStatus>,
              AsyncValue<AppStatus>,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}

@ProviderFor(apiClient)
final apiClientProvider = ApiClientProvider._();

final class ApiClientProvider
    extends $FunctionalProvider<ApiClient, ApiClient, ApiClient>
    with $Provider<ApiClient> {
  ApiClientProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'apiClientProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$apiClientHash();

  @$internal
  @override
  $ProviderElement<ApiClient> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  ApiClient create(Ref ref) {
    return apiClient(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(ApiClient value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<ApiClient>(value),
    );
  }
}

String _$apiClientHash() => r'a0eb8837fc3cfc2ac414f9bdd23761e2186e929e';
