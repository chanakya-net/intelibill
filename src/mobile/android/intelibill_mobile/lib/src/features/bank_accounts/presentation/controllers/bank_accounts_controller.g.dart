// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'bank_accounts_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(bankAccountsRemoteDataSource)
final bankAccountsRemoteDataSourceProvider =
    BankAccountsRemoteDataSourceProvider._();

final class BankAccountsRemoteDataSourceProvider
    extends
        $FunctionalProvider<
          BankAccountsRemoteDataSource,
          BankAccountsRemoteDataSource,
          BankAccountsRemoteDataSource
        >
    with $Provider<BankAccountsRemoteDataSource> {
  BankAccountsRemoteDataSourceProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'bankAccountsRemoteDataSourceProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$bankAccountsRemoteDataSourceHash();

  @$internal
  @override
  $ProviderElement<BankAccountsRemoteDataSource> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  BankAccountsRemoteDataSource create(Ref ref) {
    return bankAccountsRemoteDataSource(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(BankAccountsRemoteDataSource value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<BankAccountsRemoteDataSource>(value),
    );
  }
}

String _$bankAccountsRemoteDataSourceHash() =>
    r'23b1234df203f147cdf1a72b8a06e6f90d0053ef';

@ProviderFor(bankAccountsRepository)
final bankAccountsRepositoryProvider = BankAccountsRepositoryProvider._();

final class BankAccountsRepositoryProvider
    extends
        $FunctionalProvider<
          BankAccountsRepository,
          BankAccountsRepository,
          BankAccountsRepository
        >
    with $Provider<BankAccountsRepository> {
  BankAccountsRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'bankAccountsRepositoryProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$bankAccountsRepositoryHash();

  @$internal
  @override
  $ProviderElement<BankAccountsRepository> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  BankAccountsRepository create(Ref ref) {
    return bankAccountsRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(BankAccountsRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<BankAccountsRepository>(value),
    );
  }
}

String _$bankAccountsRepositoryHash() =>
    r'fd7eb10e24c9978e6ae7f10284e95b5de222f0d9';

@ProviderFor(getBankAccountsUseCase)
final getBankAccountsUseCaseProvider = GetBankAccountsUseCaseProvider._();

final class GetBankAccountsUseCaseProvider
    extends
        $FunctionalProvider<GetBankAccounts, GetBankAccounts, GetBankAccounts>
    with $Provider<GetBankAccounts> {
  GetBankAccountsUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getBankAccountsUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getBankAccountsUseCaseHash();

  @$internal
  @override
  $ProviderElement<GetBankAccounts> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  GetBankAccounts create(Ref ref) {
    return getBankAccountsUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetBankAccounts value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetBankAccounts>(value),
    );
  }
}

String _$getBankAccountsUseCaseHash() =>
    r'9e557f2e070676ed763e449f2d2805f25c151a44';

@ProviderFor(addBankAccountUseCase)
final addBankAccountUseCaseProvider = AddBankAccountUseCaseProvider._();

final class AddBankAccountUseCaseProvider
    extends $FunctionalProvider<AddBankAccount, AddBankAccount, AddBankAccount>
    with $Provider<AddBankAccount> {
  AddBankAccountUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'addBankAccountUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$addBankAccountUseCaseHash();

  @$internal
  @override
  $ProviderElement<AddBankAccount> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  AddBankAccount create(Ref ref) {
    return addBankAccountUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(AddBankAccount value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<AddBankAccount>(value),
    );
  }
}

String _$addBankAccountUseCaseHash() =>
    r'b6fb34ef63014edd5adbc2d1bae1509b463d7186';

@ProviderFor(updateBankAccountUseCase)
final updateBankAccountUseCaseProvider = UpdateBankAccountUseCaseProvider._();

final class UpdateBankAccountUseCaseProvider
    extends
        $FunctionalProvider<
          UpdateBankAccount,
          UpdateBankAccount,
          UpdateBankAccount
        >
    with $Provider<UpdateBankAccount> {
  UpdateBankAccountUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'updateBankAccountUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$updateBankAccountUseCaseHash();

  @$internal
  @override
  $ProviderElement<UpdateBankAccount> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  UpdateBankAccount create(Ref ref) {
    return updateBankAccountUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(UpdateBankAccount value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<UpdateBankAccount>(value),
    );
  }
}

String _$updateBankAccountUseCaseHash() =>
    r'7a5e140a40266b2a44d7199233eb373bc9bf15d2';

@ProviderFor(deleteBankAccountUseCase)
final deleteBankAccountUseCaseProvider = DeleteBankAccountUseCaseProvider._();

final class DeleteBankAccountUseCaseProvider
    extends
        $FunctionalProvider<
          DeleteBankAccount,
          DeleteBankAccount,
          DeleteBankAccount
        >
    with $Provider<DeleteBankAccount> {
  DeleteBankAccountUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'deleteBankAccountUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$deleteBankAccountUseCaseHash();

  @$internal
  @override
  $ProviderElement<DeleteBankAccount> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  DeleteBankAccount create(Ref ref) {
    return deleteBankAccountUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(DeleteBankAccount value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<DeleteBankAccount>(value),
    );
  }
}

String _$deleteBankAccountUseCaseHash() =>
    r'1d5ab7f5bd12ee83bfa6ea17e4fbc038013995ac';

@ProviderFor(BankAccountsController)
final bankAccountsControllerProvider = BankAccountsControllerProvider._();

final class BankAccountsControllerProvider
    extends $NotifierProvider<BankAccountsController, BankAccountsState> {
  BankAccountsControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'bankAccountsControllerProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$bankAccountsControllerHash();

  @$internal
  @override
  BankAccountsController create() => BankAccountsController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(BankAccountsState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<BankAccountsState>(value),
    );
  }
}

String _$bankAccountsControllerHash() =>
    r'cd7f52e002ba40c197440e9b32143eb8f976c84e';

abstract class _$BankAccountsController extends $Notifier<BankAccountsState> {
  BankAccountsState build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<BankAccountsState, BankAccountsState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<BankAccountsState, BankAccountsState>,
              BankAccountsState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}
