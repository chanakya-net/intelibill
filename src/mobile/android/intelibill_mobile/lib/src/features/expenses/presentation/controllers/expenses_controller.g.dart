// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'expenses_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(expenseRemoteDataSource)
final expenseRemoteDataSourceProvider = ExpenseRemoteDataSourceProvider._();

final class ExpenseRemoteDataSourceProvider
    extends
        $FunctionalProvider<
          ExpenseRemoteDataSource,
          ExpenseRemoteDataSource,
          ExpenseRemoteDataSource
        >
    with $Provider<ExpenseRemoteDataSource> {
  ExpenseRemoteDataSourceProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'expenseRemoteDataSourceProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$expenseRemoteDataSourceHash();

  @$internal
  @override
  $ProviderElement<ExpenseRemoteDataSource> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  ExpenseRemoteDataSource create(Ref ref) {
    return expenseRemoteDataSource(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(ExpenseRemoteDataSource value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<ExpenseRemoteDataSource>(value),
    );
  }
}

String _$expenseRemoteDataSourceHash() =>
    r'1b9c8f99d89b8f3c52957a650ca90201af2545be';

@ProviderFor(expenseRepository)
final expenseRepositoryProvider = ExpenseRepositoryProvider._();

final class ExpenseRepositoryProvider
    extends
        $FunctionalProvider<
          ExpenseRepository,
          ExpenseRepository,
          ExpenseRepository
        >
    with $Provider<ExpenseRepository> {
  ExpenseRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'expenseRepositoryProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$expenseRepositoryHash();

  @$internal
  @override
  $ProviderElement<ExpenseRepository> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  ExpenseRepository create(Ref ref) {
    return expenseRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(ExpenseRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<ExpenseRepository>(value),
    );
  }
}

String _$expenseRepositoryHash() => r'37fba5e35fc50bb56440c6f46b373183ad32f486';

@ProviderFor(getExpensesUseCase)
final getExpensesUseCaseProvider = GetExpensesUseCaseProvider._();

final class GetExpensesUseCaseProvider
    extends $FunctionalProvider<GetExpenses, GetExpenses, GetExpenses>
    with $Provider<GetExpenses> {
  GetExpensesUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getExpensesUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getExpensesUseCaseHash();

  @$internal
  @override
  $ProviderElement<GetExpenses> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  GetExpenses create(Ref ref) {
    return getExpensesUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetExpenses value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetExpenses>(value),
    );
  }
}

String _$getExpensesUseCaseHash() =>
    r'f65983ccfb450ff97b2e0be5715916df9f2c2d31';

@ProviderFor(getExpenseDetailUseCase)
final getExpenseDetailUseCaseProvider = GetExpenseDetailUseCaseProvider._();

final class GetExpenseDetailUseCaseProvider
    extends
        $FunctionalProvider<
          GetExpenseDetail,
          GetExpenseDetail,
          GetExpenseDetail
        >
    with $Provider<GetExpenseDetail> {
  GetExpenseDetailUseCaseProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getExpenseDetailUseCaseProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getExpenseDetailUseCaseHash();

  @$internal
  @override
  $ProviderElement<GetExpenseDetail> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  GetExpenseDetail create(Ref ref) {
    return getExpenseDetailUseCase(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetExpenseDetail value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetExpenseDetail>(value),
    );
  }
}

String _$getExpenseDetailUseCaseHash() =>
    r'260556eca7b5066bea35b738856b9f2ff84e3d74';

@ProviderFor(ExpensesController)
final expensesControllerProvider = ExpensesControllerProvider._();

final class ExpensesControllerProvider
    extends $NotifierProvider<ExpensesController, ExpensesState> {
  ExpensesControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'expensesControllerProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$expensesControllerHash();

  @$internal
  @override
  ExpensesController create() => ExpensesController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(ExpensesState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<ExpensesState>(value),
    );
  }
}

String _$expensesControllerHash() =>
    r'29cc81763b9a7d26a6f96da8da3ad48b5180a55b';

abstract class _$ExpensesController extends $Notifier<ExpensesState> {
  ExpensesState build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<ExpensesState, ExpensesState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<ExpensesState, ExpensesState>,
              ExpensesState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}
