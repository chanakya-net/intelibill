// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'users_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(shopUserRemoteDataSource)
final shopUserRemoteDataSourceProvider = ShopUserRemoteDataSourceProvider._();

final class ShopUserRemoteDataSourceProvider
    extends
        $FunctionalProvider<
          ShopUserRemoteDataSource,
          ShopUserRemoteDataSource,
          ShopUserRemoteDataSource
        >
    with $Provider<ShopUserRemoteDataSource> {
  ShopUserRemoteDataSourceProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'shopUserRemoteDataSourceProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$shopUserRemoteDataSourceHash();

  @$internal
  @override
  $ProviderElement<ShopUserRemoteDataSource> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  ShopUserRemoteDataSource create(Ref ref) {
    return shopUserRemoteDataSource(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(ShopUserRemoteDataSource value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<ShopUserRemoteDataSource>(value),
    );
  }
}

String _$shopUserRemoteDataSourceHash() =>
    r'cc04b9bceaf9407148880759fc9fc3ba6384437a';

@ProviderFor(shopUserRepository)
final shopUserRepositoryProvider = ShopUserRepositoryProvider._();

final class ShopUserRepositoryProvider
    extends
        $FunctionalProvider<
          ShopUserRepository,
          ShopUserRepository,
          ShopUserRepository
        >
    with $Provider<ShopUserRepository> {
  ShopUserRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'shopUserRepositoryProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$shopUserRepositoryHash();

  @$internal
  @override
  $ProviderElement<ShopUserRepository> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  ShopUserRepository create(Ref ref) {
    return shopUserRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(ShopUserRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<ShopUserRepository>(value),
    );
  }
}

String _$shopUserRepositoryHash() =>
    r'1b4f0872fb4066ce37ce5065043bd3c35bc243f6';

@ProviderFor(getShopUsersUseCase)
final getShopUsersUseCaseProvider = GetShopUsersUseCaseProvider._();

final class GetShopUsersUseCaseProvider
    extends $FunctionalProvider<GetShopUsers, GetShopUsers, GetShopUsers>
    with $Provider<GetShopUsers> {
  GetShopUsersUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getShopUsersUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getShopUsersUseCaseHash();

  @$internal
  @override
  $ProviderElement<GetShopUsers> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  GetShopUsers create(Ref ref) {
    return getShopUsersUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetShopUsers value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetShopUsers>(value),
    );
  }
}

String _$getShopUsersUseCaseHash() =>
    r'1c0c1348920d6a26e1914778e0800e2947a9e6c6';

@ProviderFor(addShopUserUseCase)
final addShopUserUseCaseProvider = AddShopUserUseCaseProvider._();

final class AddShopUserUseCaseProvider
    extends $FunctionalProvider<AddShopUser, AddShopUser, AddShopUser>
    with $Provider<AddShopUser> {
  AddShopUserUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'addShopUserUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$addShopUserUseCaseHash();

  @$internal
  @override
  $ProviderElement<AddShopUser> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  AddShopUser create(Ref ref) {
    return addShopUserUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(AddShopUser value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<AddShopUser>(value),
    );
  }
}

String _$addShopUserUseCaseHash() =>
    r'2c0afa3e5641cd8917615123371930cd86b5e59e';

@ProviderFor(editShopUserUseCase)
final editShopUserUseCaseProvider = EditShopUserUseCaseProvider._();

final class EditShopUserUseCaseProvider
    extends $FunctionalProvider<EditShopUser, EditShopUser, EditShopUser>
    with $Provider<EditShopUser> {
  EditShopUserUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'editShopUserUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$editShopUserUseCaseHash();

  @$internal
  @override
  $ProviderElement<EditShopUser> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  EditShopUser create(Ref ref) {
    return editShopUserUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(EditShopUser value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<EditShopUser>(value),
    );
  }
}

String _$editShopUserUseCaseHash() =>
    r'd08c32784ec6f98228bdccc8203f8c81758aad82';

@ProviderFor(UsersController)
final usersControllerProvider = UsersControllerProvider._();

final class UsersControllerProvider
    extends $NotifierProvider<UsersController, UsersState> {
  UsersControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'usersControllerProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$usersControllerHash();

  @$internal
  @override
  UsersController create() => UsersController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(UsersState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<UsersState>(value),
    );
  }
}

String _$usersControllerHash() => r'a109cdcc8c2b2f1fe26ce76a5b937e80e261fa39';

abstract class _$UsersController extends $Notifier<UsersState> {
  UsersState build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<UsersState, UsersState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<UsersState, UsersState>,
              UsersState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}
