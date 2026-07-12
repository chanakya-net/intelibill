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
    r'4f85bd87eb20e86f65e836e9b0c02fd540a2b7b7';

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
