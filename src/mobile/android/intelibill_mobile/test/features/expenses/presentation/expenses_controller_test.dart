import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_category.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_list_item.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expenses_page.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/correct_expense.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/get_expense_categories.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/get_expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/get_expenses.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/record_expense.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/controllers/expenses_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetExpenses extends Mock implements GetExpenses {}

class MockGetExpenseDetail extends Mock implements GetExpenseDetail {}

class MockGetExpenseCategories extends Mock implements GetExpenseCategories {}

class MockRecordExpense extends Mock implements RecordExpense {}

class MockCorrectExpense extends Mock implements CorrectExpense {}

AuthSession _authSession(String? activeShopId) => AuthSession(
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  accessTokenExpiresAt: DateTime.utc(2026, 5, 15, 10),
  refreshTokenExpiresAt: DateTime.utc(2026, 6, 15, 10),
  user: const AuthUser(
    id: 'user-1',
    email: 'test@example.com',
    phoneNumber: null,
    firstName: 'Alex',
    lastName: 'Doe',
    language: 'en-IN',
  ),
  activeShopId: activeShopId,
  shops: const [],
  rememberMe: false,
);

class _TestAuthController extends AuthController {
  _TestAuthController(this._activeShopId);

  final String? _activeShopId;

  @override
  Future<AuthControllerState> build() async =>
      AuthControllerState(session: _authSession(_activeShopId));
}

class _TestExpensesController extends ExpensesController {
  _TestExpensesController(this._initialState);

  final ExpensesState _initialState;

  @override
  ExpensesState build() => _initialState;
}

Future<void> _waitForAuthSession(ProviderContainer container) async {
  await container.read(authControllerProvider.future);
  await Future<void>.delayed(Duration.zero);
}

Future<void> _setAuthActiveShop(
  ProviderContainer container,
  String shopId,
) async {
  await _setAuthControllerState(
    container,
    AuthControllerState(session: _authSession(shopId)),
  );
}

Future<void> _setAuthControllerState(
  ProviderContainer container,
  AuthControllerState state,
) async {
  container.read(authControllerProvider.notifier).state = AsyncData(state);
  await Future<void>.delayed(Duration.zero);
}

ExpenseDetail _detail(String id) => ExpenseDetail(
  id: id,
  shopId: 'shop-1',
  categoryId: 'category-1',
  categoryName: 'Rent',
  amount: 100,
  paidTo: 'Payee',
  expenseDate: DateTime(2026, 7),
  actorUserId: 'user-1',
  isVoided: false,
  createdAt: DateTime(2026, 7, 1, 8, 30),
);

ExpensePage _makePage({
  required List<ExpenseListItem> items,
  int pageNumber = 1,
  int pageSize = 20,
  int? totalCount,
}) => ExpensePage(
  items: items,
  totalCount: totalCount ?? items.length,
  pageNumber: pageNumber,
  pageSize: pageSize,
);

ExpenseListItem _item(String id) => ExpenseListItem(
  id: id,
  amount: 100,
  categoryName: 'Travel',
  paidTo: 'Taxi',
  expenseDate: DateTime(2026, 7),
  isVoided: false,
);

final _page = ExpensePage(
  items: [
    ExpenseListItem(
      id: 'expense-1',
      amount: 100,
      categoryName: 'Travel',
      paidTo: 'Taxi',
      expenseDate: DateTime(2026, 7),
      isVoided: false,
    ),
  ],
  totalCount: 1,
  pageNumber: 1,
  pageSize: 20,
);

final _pageWithStatuses = ExpensePage(
  items: [
    ExpenseListItem(
      id: 'active-expense',
      amount: 100,
      categoryName: 'Travel',
      paidTo: 'Taxi',
      expenseDate: DateTime(2026, 7),
      isVoided: false,
    ),
    ExpenseListItem(
      id: 'voided-expense',
      amount: 200,
      categoryName: 'Supplies',
      paidTo: 'Vendor',
      expenseDate: DateTime(2026, 7, 2),
      isVoided: true,
    ),
  ],
  totalCount: 2,
  pageNumber: 1,
  pageSize: 20,
);

void main() {
  ProviderContainer detailContainer(MockGetExpenseDetail getDetail) {
    return ProviderContainer(
      overrides: [
        getExpenseDetailUseCaseProvider.overrideWithValue(getDetail),
        expensesControllerProvider.overrideWith(
          () => _TestExpensesController(ExpensesState(page: _page)),
        ),
      ],
    );
  }

  test(
    'resets full shop-scoped state before loading new-shop page one',
    () async {
      final getExpenses = MockGetExpenses();
      final staleFirstShopPage = Completer<ExpensePage>();
      final secondShopPage = Completer<ExpensePage>();
      var callCount = 0;
      when(getExpenses.call).thenAnswer((_) {
        callCount++;
        return callCount == 1
            ? staleFirstShopPage.future
            : secondShopPage.future;
      });
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(
            () => _TestAuthController('shop-1'),
          ),
          getExpensesUseCaseProvider.overrideWithValue(getExpenses),
        ],
      );
      final subscription = container.listen(
        expensesControllerProvider,
        (_, _) {},
      );
      addTearDown(subscription.close);
      addTearDown(container.dispose);
      final controller = container.read(expensesControllerProvider.notifier);
      await _waitForAuthSession(container);
      await Future<void>.delayed(Duration.zero);

      controller.state =
          const ExpensesState(
            pageNumber: 3,
            totalCount: 50,
            searchQuery: 'rent',
            statusFilter: ExpenseStatusFilter.voided,
          ).copyWith(
            page: _makePage(
              items: [_item('shop-1-legacy')],
              totalCount: 50,
              pageNumber: 3,
            ),
            loadMoreFailure: const Failure.network(),
            selectedExpense: _detail('selected-expense'),
            selectedExpenseId: 'selected-expense',
            isDetailLoading: true,
            detailFailure: const Failure.network(),
            categories: const [ExpenseCategory(id: 'old', name: 'Old')],
            isLoadingCategories: true,
            categoryFailure: const Failure.timeout(),
            isSubmitting: true,
            submitFailure: const Failure.unknown(),
          );

      await _setAuthActiveShop(container, 'shop-2');
      await Future<void>.delayed(Duration.zero);

      final resetState = container.read(expensesControllerProvider);
      expect(resetState.isLoading, isTrue);
      expect(resetState.page, isNull);
      expect(resetState.pageNumber, 1);
      expect(resetState.totalCount, 0);
      expect(resetState.searchQuery, isEmpty);
      expect(resetState.statusFilter, ExpenseStatusFilter.all);
      expect(resetState.failure, isNull);
      expect(resetState.loadMoreFailure, isNull);
      expect(resetState.isLoadingMore, isFalse);
      expect(resetState.selectedExpense, isNull);
      expect(resetState.selectedExpenseId, isNull);
      expect(resetState.isDetailLoading, isFalse);
      expect(resetState.detailFailure, isNull);
      expect(resetState.categories, isEmpty);
      expect(resetState.isLoadingCategories, isFalse);
      expect(resetState.categoryFailure, isNull);
      expect(resetState.isSubmitting, isFalse);
      expect(resetState.submitFailure, isNull);

      secondShopPage.complete(
        _makePage(items: [_item('shop-2-expense')], totalCount: 1),
      );
      await Future<void>.delayed(Duration.zero);
      staleFirstShopPage.complete(
        _makePage(items: [_item('late-shop-1-expense')], totalCount: 50),
      );
      await Future<void>.delayed(Duration.zero);

      final finalState = container.read(expensesControllerProvider);
      expect(finalState.page?.items.single.id, 'shop-2-expense');
      expect(finalState.pageNumber, 1);
      expect(finalState.totalCount, 1);
      expect(finalState.isLoading, isFalse);
      expect(callCount, greaterThanOrEqualTo(2));
    },
  );

  test('same-shop auth updates do not reset expense state', () async {
    final getExpenses = MockGetExpenses();
    when(() => getExpenses()).thenAnswer(
      (_) async => _makePage(items: [_item('shop-1')], totalCount: 1),
    );
    final container = ProviderContainer(
      overrides: [
        authControllerProvider.overrideWith(
          () => _TestAuthController('shop-1'),
        ),
        getExpensesUseCaseProvider.overrideWithValue(getExpenses),
      ],
    );
    final subscription = container.listen(
      expensesControllerProvider,
      (_, _) {},
    );
    addTearDown(subscription.close);
    addTearDown(container.dispose);
    await _waitForAuthSession(container);
    await Future<void>.delayed(Duration.zero);
    await _setAuthActiveShop(container, 'shop-1');
    await Future<void>.delayed(Duration.zero);
    final controller = container.read(expensesControllerProvider.notifier);

    controller.state =
        const ExpensesState(
          pageNumber: 1,
          statusFilter: ExpenseStatusFilter.active,
          isLoading: false,
          isLoadingMore: true,
          isDetailLoading: true,
          isLoadingCategories: true,
          isSubmitting: true,
          searchQuery: 'rent',
        ).copyWith(
          page: _makePage(items: [_item('old-shop-expense')], totalCount: 2),
          selectedExpense: _detail('selected-expense'),
          selectedExpenseId: 'selected-expense',
          loadMoreFailure: const Failure.network(),
          detailFailure: const Failure.network(),
          categories: const [ExpenseCategory(id: 'old', name: 'Old')],
          categoryFailure: const Failure.network(),
          submitFailure: const Failure.unknown(),
        );

    await _setAuthActiveShop(container, 'shop-1');
    await Future<void>.delayed(Duration.zero);

    final state = container.read(expensesControllerProvider);
    expect(state.page?.items.single.id, 'old-shop-expense');
    expect(state.statusFilter, ExpenseStatusFilter.active);
    expect(state.isLoadingMore, isTrue);
    expect(state.isDetailLoading, isTrue);
    expect(state.isLoadingCategories, isTrue);
    expect(state.isSubmitting, isTrue);
    expect(state.selectedExpenseId, 'selected-expense');
    expect(state.categories, hasLength(1));
  });

  test('ignores stale pagination response after shop switch', () async {
    final getExpenses = MockGetExpenses();
    final oldAppendPage = Completer<ExpensePage>();
    final switchedPage = Completer<ExpensePage>();
    var callCount = 0;
    when(() => getExpenses(page: 2, pageSize: 20)).thenAnswer((_) {
      callCount++;
      return oldAppendPage.future;
    });
    when(getExpenses.call).thenAnswer((_) {
      callCount++;
      return switchedPage.future;
    });
    final container = ProviderContainer(
      overrides: [
        authControllerProvider.overrideWith(
          () => _TestAuthController('shop-1'),
        ),
        getExpensesUseCaseProvider.overrideWithValue(getExpenses),
      ],
    );
    final subscription = container.listen(
      expensesControllerProvider,
      (_, _) {},
    );
    final authSubscription = container.listen(
      authControllerProvider,
      (_, _) {},
    );
    addTearDown(subscription.close);
    addTearDown(authSubscription.close);
    addTearDown(container.dispose);
    await _waitForAuthSession(container);
    final controller = container.read(expensesControllerProvider.notifier);

    await Future<void>.delayed(Duration.zero);
    controller.state = ExpensesState(
      page: ExpensePage(
        items: [
          ExpenseListItem(
            id: 'old-1',
            amount: 50,
            categoryName: 'Travel',
            paidTo: 'Taxi',
            expenseDate: DateTime(2026, 7),
            isVoided: false,
          ),
        ],
        totalCount: 40,
        pageNumber: 1,
        pageSize: 20,
      ),
      totalCount: 40,
      pageNumber: 1,
      pageSize: 20,
    );
    final pendingAppend = controller.loadMore();
    await Future<void>.delayed(Duration.zero);
    expect(container.read(expensesControllerProvider).isLoadingMore, isTrue);

    await _setAuthActiveShop(container, 'shop-2');
    await Future<void>.delayed(Duration.zero);
    expect(container.read(expensesControllerProvider).page, isNull);
    expect(container.read(expensesControllerProvider).isLoadingMore, isFalse);
    switchedPage.complete(
      _makePage(items: [_item('shop-2-expense')], totalCount: 1),
    );
    await Future<void>.delayed(Duration.zero);
    oldAppendPage.complete(
      _makePage(items: [_item('stale-append')], pageNumber: 2, totalCount: 40),
    );
    await Future<void>.delayed(Duration.zero);
    await pendingAppend;

    final state = container.read(expensesControllerProvider);
    expect(state.page?.items.single.id, 'shop-2-expense');
    expect(state.pageNumber, 1);
    expect(state.isLoadingMore, isFalse);
    expect(callCount, greaterThanOrEqualTo(2));
  });

  test('ignores stale search response after shop switch', () async {
    final getExpenses = MockGetExpenses();
    final staleSearchPage = Completer<ExpensePage>();
    final switchedPage = Completer<ExpensePage>();
    when(() => getExpenses()).thenAnswer((_) => switchedPage.future);
    when(
      () => getExpenses(page: 1, pageSize: 20, search: 'rent'),
    ).thenAnswer((_) => staleSearchPage.future);
    final container = ProviderContainer(
      overrides: [
        authControllerProvider.overrideWith(
          () => _TestAuthController('shop-1'),
        ),
        getExpensesUseCaseProvider.overrideWithValue(getExpenses),
      ],
    );
    final subscription = container.listen(
      expensesControllerProvider,
      (_, _) {},
    );
    final authSubscription = container.listen(
      authControllerProvider,
      (_, _) {},
    );
    addTearDown(subscription.close);
    addTearDown(authSubscription.close);
    addTearDown(container.dispose);
    await _waitForAuthSession(container);
    final controller = container.read(expensesControllerProvider.notifier);

    await Future<void>.delayed(Duration.zero);
    controller.updateSearch('rent');
    await Future<void>.delayed(const Duration(milliseconds: 320));
    await _setAuthActiveShop(container, 'shop-2');
    await Future<void>.delayed(Duration.zero);
    expect(container.read(expensesControllerProvider).searchQuery, isEmpty);

    switchedPage.complete(
      _makePage(items: [_item('shop-2-result')], totalCount: 1),
    );
    await Future<void>.delayed(Duration.zero);
    staleSearchPage.complete(
      _makePage(items: [_item('stale-search')], totalCount: 1),
    );
    await Future<void>.delayed(Duration.zero);

    final state = container.read(expensesControllerProvider);
    expect(state.page?.items.single.id, 'shop-2-result');
    expect(state.pageNumber, 1);
    expect(state.searchQuery, isEmpty);
    verify(() => getExpenses(page: 1, pageSize: 20, search: 'rent')).called(1);
  });

  test('ignores stale detail response after shop switch', () async {
    final getExpenses = MockGetExpenses();
    final getDetail = MockGetExpenseDetail();
    final initialPage = Completer<ExpensePage>();
    final switchedPage = Completer<ExpensePage>();
    final staleDetail = Completer<ExpenseDetail>();
    var pageCalls = 0;
    when(getExpenses.call).thenAnswer((_) {
      pageCalls++;
      return pageCalls == 1 ? initialPage.future : switchedPage.future;
    });
    when(() => getDetail('expense-1')).thenAnswer((_) => staleDetail.future);
    final container = ProviderContainer(
      overrides: [
        authControllerProvider.overrideWith(
          () => _TestAuthController('shop-1'),
        ),
        getExpensesUseCaseProvider.overrideWithValue(getExpenses),
        getExpenseDetailUseCaseProvider.overrideWithValue(getDetail),
      ],
    );
    final controller = container.read(expensesControllerProvider.notifier);
    addTearDown(container.dispose);
    await _waitForAuthSession(container);
    final subscription = container.listen(
      expensesControllerProvider,
      (_, _) {},
    );
    final authSubscription = container.listen(
      authControllerProvider,
      (_, _) {},
    );
    addTearDown(subscription.close);
    addTearDown(authSubscription.close);

    await Future<void>.delayed(Duration.zero);
    initialPage.complete(
      _makePage(items: [_item('old-expense')], totalCount: 1),
    );
    await Future<void>.delayed(Duration.zero);
    unawaited(controller.openExpense('expense-1'));
    await _setAuthActiveShop(container, 'shop-2');
    await Future<void>.delayed(Duration.zero);
    switchedPage.complete(_makePage(items: [_item('shop-2')], totalCount: 1));
    await Future<void>.delayed(Duration.zero);
    staleDetail.complete(_detail('expense-1'));
    await Future<void>.delayed(Duration.zero);

    final state = container.read(expensesControllerProvider);
    expect(state.selectedExpense, isNull);
    expect(state.selectedExpenseId, isNull);
    expect(state.page?.items.single.id, 'shop-2');
  });

  test('ignores stale category response after shop switch', () async {
    final getExpenses = MockGetExpenses();
    final getExpenseCategories = MockGetExpenseCategories();
    final firstPage = Completer<ExpensePage>();
    final staleCategories = Completer<List<ExpenseCategory>>();
    when(getExpenses.call).thenAnswer((_) => firstPage.future);
    when(getExpenseCategories.call).thenAnswer((_) => staleCategories.future);
    final container = ProviderContainer(
      overrides: [
        authControllerProvider.overrideWith(
          () => _TestAuthController('shop-1'),
        ),
        getExpensesUseCaseProvider.overrideWithValue(getExpenses),
        getExpenseCategoriesUseCaseProvider.overrideWithValue(
          getExpenseCategories,
        ),
      ],
    );
    final controller = container.read(expensesControllerProvider.notifier);
    addTearDown(container.dispose);
    await _waitForAuthSession(container);
    final subscription = container.listen(
      expensesControllerProvider,
      (_, _) {},
    );
    final authSubscription = container.listen(
      authControllerProvider,
      (_, _) {},
    );
    addTearDown(subscription.close);
    addTearDown(authSubscription.close);

    await Future<void>.delayed(Duration.zero);
    firstPage.complete(_makePage(items: [_item('expense-1')], totalCount: 1));
    await Future<void>.delayed(Duration.zero);
    unawaited(controller.loadCategories());
    await _setAuthActiveShop(container, 'shop-2');
    await Future<void>.delayed(Duration.zero);
    await Future<void>.delayed(Duration.zero);
    staleCategories.complete([const ExpenseCategory(id: 'old', name: 'Old')]);
    await Future<void>.delayed(Duration.zero);

    expect(container.read(expensesControllerProvider).categories, isEmpty);
  });

  test(
    'preserves mutation success and ignores stale record response',
    () async {
      final getExpenses = MockGetExpenses();
      final recordExpense = MockRecordExpense();
      final initialPage = Completer<ExpensePage>();
      final switchedPage = Completer<ExpensePage>();
      final recordResult = Completer<ExpenseDetail>();
      var callCount = 0;
      when(getExpenses.call).thenAnswer((_) {
        callCount++;
        return callCount == 1 ? initialPage.future : switchedPage.future;
      });
      when(
        () => recordExpense(
          categoryName: 'Rent',
          amount: 25,
          paidTo: 'Vendor',
          description: null,
          expenseDate: DateTime(2026, 7, 2),
        ),
      ).thenAnswer((_) => recordResult.future);

      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(
            () => _TestAuthController('shop-1'),
          ),
          getExpensesUseCaseProvider.overrideWithValue(getExpenses),
          recordExpenseUseCaseProvider.overrideWithValue(recordExpense),
        ],
      );
      addTearDown(container.dispose);
      await _waitForAuthSession(container);
      final subscription = container.listen(
        expensesControllerProvider,
        (_, _) {},
      );
      final authSubscription = container.listen(
        authControllerProvider,
        (_, _) {},
      );
      addTearDown(subscription.close);
      addTearDown(authSubscription.close);

      await Future<void>.delayed(Duration.zero);
      initialPage.complete(_makePage(items: [_item('shop-1')], totalCount: 1));
      await Future<void>.delayed(Duration.zero);
      final controller = container.read(expensesControllerProvider.notifier);
      container.read(expensesControllerProvider);
      expect(container.read(expensesControllerProvider).isSubmitting, isFalse);
      final record = controller.recordExpense(
        categoryName: 'Rent',
        amount: 25,
        paidTo: 'Vendor',
        expenseDate: DateTime(2026, 7, 2),
      );
      await _setAuthActiveShop(container, 'shop-2');
      await Future<void>.delayed(Duration.zero);
      recordResult.complete(_detail('new'));
      switchedPage.complete(_makePage(items: [_item('shop-2')], totalCount: 1));
      final recorded = await record;
      await Future<void>.delayed(Duration.zero);

      final state = container.read(expensesControllerProvider);
      expect(recorded, isTrue);
      expect(state.submitFailure, isNull);
      expect(state.page?.items.single.id, 'shop-2');
      expect(state.isSubmitting, isFalse);
      expect(callCount, greaterThanOrEqualTo(2));
    },
  );

  test(
    'preserves mutation success and ignores stale correction response',
    () async {
      final getExpenses = MockGetExpenses();
      final correctExpense = MockCorrectExpense();
      final initialPage = Completer<ExpensePage>();
      final switchedPage = Completer<ExpensePage>();
      final correctionResult = Completer<ExpenseDetail>();
      var callCount = 0;
      when(getExpenses.call).thenAnswer((_) {
        callCount++;
        return callCount == 1 ? initialPage.future : switchedPage.future;
      });
      when(
        () => correctExpense(
          'expense-1',
          categoryName: 'Rent',
          amount: 25,
          paidTo: 'Vendor',
          description: null,
          expenseDate: DateTime(2026, 7, 2),
        ),
      ).thenAnswer((_) => correctionResult.future);

      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(
            () => _TestAuthController('shop-1'),
          ),
          getExpensesUseCaseProvider.overrideWithValue(getExpenses),
          correctExpenseUseCaseProvider.overrideWithValue(correctExpense),
        ],
      );
      addTearDown(container.dispose);
      final subscription = container.listen(
        expensesControllerProvider,
        (_, _) {},
      );
      final authSubscription = container.listen(
        authControllerProvider,
        (_, _) {},
      );
      addTearDown(subscription.close);
      addTearDown(authSubscription.close);
      await _waitForAuthSession(container);

      await Future<void>.delayed(Duration.zero);
      initialPage.complete(_makePage(items: [_item('shop-1')], totalCount: 1));
      await Future<void>.delayed(Duration.zero);
      final controller = container.read(expensesControllerProvider.notifier);
      container.read(expensesControllerProvider);
      expect(container.read(expensesControllerProvider).isSubmitting, isFalse);
      final correction = controller.correctExpense(
        'expense-1',
        categoryName: 'Rent',
        amount: 25,
        paidTo: 'Vendor',
        description: null,
        expenseDate: DateTime(2026, 7, 2),
      );
      await _setAuthActiveShop(container, 'shop-2');
      await Future<void>.delayed(Duration.zero);
      correctionResult.complete(_detail('corrected'));
      switchedPage.complete(_makePage(items: [_item('shop-2')], totalCount: 1));
      final corrected = await correction;
      await Future<void>.delayed(Duration.zero);

      final state = container.read(expensesControllerProvider);
      verify(
        () => correctExpense(
          'expense-1',
          categoryName: any(named: 'categoryName'),
          amount: any(named: 'amount'),
          paidTo: any(named: 'paidTo'),
          description: any(named: 'description'),
          expenseDate: any(named: 'expenseDate'),
        ),
      ).called(1);
      expect(corrected, isTrue);
      expect(state.submitFailure, isNull);
      expect(state.selectedExpense, isNull);
      expect(state.page?.items.single.id, 'shop-2');
      expect(state.isSubmitting, isFalse);
      expect(callCount, greaterThanOrEqualTo(2));
    },
  );

  test(
    'does not write state or request stale work after disposal',
    () async {
      final getExpenses = MockGetExpenses();
      final refreshCompleter = Completer<ExpensePage>();
      final searchCompleter = Completer<ExpensePage>();
      var callCount = 0;
      when(getExpenses.call).thenAnswer((_) {
        callCount++;
        return refreshCompleter.future;
      });
      when(
        () => getExpenses(page: 1, pageSize: 20, search: 'office rent'),
      ).thenAnswer((_) => searchCompleter.future);
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(
            () => _TestAuthController('shop-1'),
          ),
          getExpensesUseCaseProvider.overrideWithValue(getExpenses),
        ],
      );
      final subscription = container.listen(
        expensesControllerProvider,
        (_, _) {},
      );
      addTearDown(subscription.close);

      addTearDown(container.dispose);
      final controller = container.read(expensesControllerProvider.notifier);
      await _waitForAuthSession(container);
      await Future<void>.delayed(Duration.zero);
      controller.updateSearch('office rent');
      unawaited(controller.refresh());
      await Future<void>.delayed(const Duration(milliseconds: 20));

      container.dispose();
      refreshCompleter.complete(
        _makePage(items: [_item('late-expense')], totalCount: 1),
      );
      searchCompleter.complete(
        _makePage(items: [_item('late-search')], totalCount: 1),
      );

      await Future<void>.delayed(Duration.zero);
      verify(
        () => getExpenses(page: 1, pageSize: 20, search: 'office rent'),
      ).called(1);
      await Future<void>.delayed(const Duration(milliseconds: 360));

      expect(callCount, greaterThanOrEqualTo(2));
    },
  );

  test('loads first expenses page', () async {
    final getExpenses = MockGetExpenses();
    when(getExpenses.call).thenAnswer((_) async => _page);
    final container = ProviderContainer(
      overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
    );
    addTearDown(container.dispose);

    expect(container.read(expensesControllerProvider).isLoading, isTrue);
    await container.read(expensesControllerProvider.notifier).refresh();

    final state = container.read(expensesControllerProvider);
    expect(state.page, _page);
    expect(state.isLoading, isFalse);
    expect(state.failure, isNull);
    verify(getExpenses.call).called(greaterThanOrEqualTo(1));
  });

  test(
    'loads categories and records an expense before refreshing page one',
    () async {
      final getCategories = MockGetExpenseCategories();
      final recordExpense = MockRecordExpense();
      final getExpenses = MockGetExpenses();
      const categories = [ExpenseCategory(id: 'rent', name: 'Rent')];
      when(getCategories.call).thenAnswer((_) async => categories);
      when(
        () => recordExpense(
          categoryName: 'Rent',
          amount: 25,
          paidTo: 'Vendor',
          expenseDate: DateTime(2026, 7, 2),
        ),
      ).thenAnswer((_) async => _detail('new-expense'));
      when(getExpenses.call).thenAnswer((_) async => _page);
      final container = ProviderContainer(
        overrides: [
          getExpenseCategoriesUseCaseProvider.overrideWithValue(getCategories),
          recordExpenseUseCaseProvider.overrideWithValue(recordExpense),
          getExpensesUseCaseProvider.overrideWithValue(getExpenses),
          expensesControllerProvider.overrideWith(
            () => _TestExpensesController(const ExpensesState()),
          ),
        ],
      );
      addTearDown(container.dispose);

      final controller = container.read(expensesControllerProvider.notifier);
      await controller.loadCategories();
      final recorded = await controller.recordExpense(
        categoryName: 'Rent',
        amount: 25,
        paidTo: 'Vendor',
        expenseDate: DateTime(2026, 7, 2),
      );

      expect(recorded, isTrue);
      expect(container.read(expensesControllerProvider).categories, categories);
      expect(container.read(expensesControllerProvider).page, _page);
      verify(getCategories.call).called(1);
      verify(
        () => recordExpense(
          categoryName: 'Rent',
          amount: 25,
          paidTo: 'Vendor',
          expenseDate: DateTime(2026, 7, 2),
        ),
      ).called(1);
    },
  );

  test('reports committed record success when page refresh fails', () async {
    final recordExpense = MockRecordExpense();
    final getExpenses = MockGetExpenses();
    when(
      () => recordExpense(
        categoryName: 'Rent',
        amount: 25,
        paidTo: 'Vendor',
        expenseDate: DateTime(2026, 7, 2),
      ),
    ).thenAnswer((_) async => _detail('new-expense'));
    when(
      getExpenses.call,
    ).thenThrow(AppException(failure: const Failure.network()));
    final container = ProviderContainer(
      overrides: [
        recordExpenseUseCaseProvider.overrideWithValue(recordExpense),
        getExpensesUseCaseProvider.overrideWithValue(getExpenses),
        expensesControllerProvider.overrideWith(
          () => _TestExpensesController(const ExpensesState()),
        ),
      ],
    );
    addTearDown(container.dispose);

    final recorded = await container
        .read(expensesControllerProvider.notifier)
        .recordExpense(
          categoryName: 'Rent',
          amount: 25,
          paidTo: 'Vendor',
          expenseDate: DateTime(2026, 7, 2),
        );

    final state = container.read(expensesControllerProvider);
    expect(recorded, isTrue);
    expect(state.isSubmitting, isFalse);
    expect(state.submitFailure, isNull);
    expect(state.failure, isA<NetworkFailure>());
    verify(
      () => recordExpense(
        categoryName: 'Rent',
        amount: 25,
        paidTo: 'Vendor',
        expenseDate: DateTime(2026, 7, 2),
      ),
    ).called(1);
  });

  test('exposes mapped failure when loading fails', () async {
    final getExpenses = MockGetExpenses();
    when(
      getExpenses.call,
    ).thenThrow(AppException(failure: const Failure.network()));
    final container = ProviderContainer(
      overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
    );
    addTearDown(container.dispose);

    await container.read(expensesControllerProvider.notifier).refresh();

    final state = container.read(expensesControllerProvider);
    expect(state.isLoading, isFalse);
    expect(state.failure, isA<NetworkFailure>());
  });

  test('preserves prior page data when refresh fails', () async {
    final getExpenses = MockGetExpenses();
    var callCount = 0;
    when(getExpenses.call).thenAnswer((_) async {
      callCount++;
      if (callCount == 1) return _page;
      throw AppException(failure: const Failure.network());
    });
    final container = ProviderContainer(
      overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
    );
    addTearDown(container.dispose);

    await container.read(expensesControllerProvider.notifier).refresh();
    var state = container.read(expensesControllerProvider);
    expect(state.page, _page);
    expect(state.failure, isNull);

    await container.read(expensesControllerProvider.notifier).refresh();

    state = container.read(expensesControllerProvider);
    expect(state.page, _page);
    expect(state.failure, isA<NetworkFailure>());
  });

  test('debounces search, trims query, and resets to page one', () async {
    final getExpenses = MockGetExpenses();
    final searchedPage = _makePage(items: [_item('searched')]);
    when(
      () => getExpenses(search: 'rent', page: 1, pageSize: 20),
    ).thenAnswer((_) async => searchedPage);
    final container = ProviderContainer(
      overrides: [
        getExpensesUseCaseProvider.overrideWithValue(getExpenses),
        expensesControllerProvider.overrideWith(
          () => _TestExpensesController(
            ExpensesState(
              page: _makePage(
                items: [_item('old')],
                pageNumber: 3,
                totalCount: 60,
              ),
              pageNumber: 3,
              totalCount: 60,
            ),
          ),
        ),
      ],
    );
    addTearDown(container.dispose);
    final subscription = container.listen(
      expensesControllerProvider,
      (_, _) {},
    );
    final authSubscription = container.listen(
      authControllerProvider,
      (_, _) {},
    );
    addTearDown(subscription.close);
    addTearDown(authSubscription.close);
    final controller = container.read(expensesControllerProvider.notifier);

    controller.updateSearch('  rent  ');

    await Future<void>.delayed(const Duration(milliseconds: 250));
    verifyNever(() => getExpenses(search: 'rent', page: 1, pageSize: 20));
    expect(container.read(expensesControllerProvider).pageNumber, 3);

    await Future<void>.delayed(const Duration(milliseconds: 100));
    await Future<void>.delayed(Duration.zero);
    verify(() => getExpenses(search: 'rent', page: 1, pageSize: 20)).called(1);
    final state = container.read(expensesControllerProvider);
    expect(state.searchQuery, '  rent  ');
    expect(state.pageNumber, 1);
    expect(state.page, searchedPage);
  });

  test('clearing search reloads unfiltered page one', () async {
    final getExpenses = MockGetExpenses();
    final unfilteredPage = _makePage(items: [_item('all-expenses')]);
    when(getExpenses.call).thenAnswer((_) async => unfilteredPage);
    final container = ProviderContainer(
      overrides: [
        getExpensesUseCaseProvider.overrideWithValue(getExpenses),
        expensesControllerProvider.overrideWith(
          () => _TestExpensesController(
            ExpensesState(
              page: _makePage(
                items: [_item('old')],
                pageNumber: 2,
                totalCount: 40,
              ),
              pageNumber: 2,
              totalCount: 40,
              searchQuery: 'rent',
            ),
          ),
        ),
      ],
    );
    addTearDown(container.dispose);
    final subscription = container.listen(
      expensesControllerProvider,
      (_, _) {},
    );
    addTearDown(subscription.close);
    final controller = container.read(expensesControllerProvider.notifier);

    controller.updateSearch('');
    await Future<void>.delayed(const Duration(milliseconds: 300));
    await Future<void>.delayed(Duration.zero);
    verify(getExpenses.call).called(1);

    final state = container.read(expensesControllerProvider);
    expect(state.searchQuery, isEmpty);
    expect(state.pageNumber, 1);
    expect(state.page, unfilteredPage);
  });

  test('stale search response cannot replace newer results', () async {
    final getExpenses = MockGetExpenses();
    final older = Completer<ExpensePage>();
    final newer = Completer<ExpensePage>();
    when(
      () => getExpenses(search: 'old', page: 1, pageSize: 20),
    ).thenAnswer((_) => older.future);
    when(
      () => getExpenses(search: 'new', page: 1, pageSize: 20),
    ).thenAnswer((_) => newer.future);
    final container = ProviderContainer(
      overrides: [
        getExpensesUseCaseProvider.overrideWithValue(getExpenses),
        expensesControllerProvider.overrideWith(
          () => _TestExpensesController(const ExpensesState()),
        ),
      ],
    );
    addTearDown(container.dispose);
    final subscription = container.listen(
      expensesControllerProvider,
      (_, _) {},
    );
    addTearDown(subscription.close);
    final controller = container.read(expensesControllerProvider.notifier);

    controller.updateSearch('old');
    await Future<void>.delayed(const Duration(milliseconds: 300));
    await Future<void>.delayed(Duration.zero);
    verify(() => getExpenses(search: 'old', page: 1, pageSize: 20)).called(1);
    controller.updateSearch('new');
    await Future<void>.delayed(const Duration(milliseconds: 300));
    await Future<void>.delayed(Duration.zero);
    verify(() => getExpenses(search: 'new', page: 1, pageSize: 20)).called(1);

    newer.complete(_makePage(items: [_item('new-result')]));
    await Future<void>.delayed(Duration.zero);
    older.complete(_makePage(items: [_item('old-result')]));
    await Future<void>.delayed(Duration.zero);

    expect(
      container.read(expensesControllerProvider).page?.items.single.id,
      'new-result',
    );
  });

  test(
    'filters loaded expenses by status without another repository call',
    () async {
      final getExpenses = MockGetExpenses();
      when(getExpenses.call).thenAnswer((_) async => _pageWithStatuses);
      final container = ProviderContainer(
        overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
      );
      addTearDown(container.dispose);

      await container.read(expensesControllerProvider.notifier).refresh();
      clearInteractions(getExpenses);
      final controller = container.read(expensesControllerProvider.notifier);

      controller.updateStatusFilter(ExpenseStatusFilter.active);
      expect(
        container.read(expensesControllerProvider).filteredExpenses,
        hasLength(1),
      );
      expect(
        container.read(expensesControllerProvider).filteredExpenses.single.id,
        'active-expense',
      );

      controller.updateStatusFilter(ExpenseStatusFilter.voided);
      expect(
        container.read(expensesControllerProvider).filteredExpenses.single.id,
        'voided-expense',
      );

      controller.updateStatusFilter(ExpenseStatusFilter.all);
      expect(
        container.read(expensesControllerProvider).filteredExpenses,
        hasLength(2),
      );
      verifyNever(getExpenses.call);
    },
  );

  test(
    'returns empty filtered results without another repository call',
    () async {
      final getExpenses = MockGetExpenses();
      when(getExpenses.call).thenAnswer((_) async => _page);
      final container = ProviderContainer(
        overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
      );
      addTearDown(container.dispose);

      await container.read(expensesControllerProvider.notifier).refresh();
      clearInteractions(getExpenses);

      container
          .read(expensesControllerProvider.notifier)
          .updateStatusFilter(ExpenseStatusFilter.voided);

      expect(
        container.read(expensesControllerProvider).filteredExpenses,
        isEmpty,
      );
      verifyNever(getExpenses.call);
    },
  );

  test(
    'summary metrics report server total count and loaded-row breakdown',
    () async {
      final getExpenses = MockGetExpenses();
      when(getExpenses.call).thenAnswer((_) async => _pageWithStatuses);
      final container = ProviderContainer(
        overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
      );
      addTearDown(container.dispose);

      await container.read(expensesControllerProvider.notifier).refresh();

      final state = container.read(expensesControllerProvider);
      expect(state.totalCount, 2);
      expect(state.loadedAmount, 100);
      expect(state.loadedActiveCount, 1);
      expect(state.loadedVoidedCount, 1);
    },
  );

  test(
    'summary metrics update after an appended page loads more rows',
    () async {
      final getExpenses = MockGetExpenses();
      final page1 = _makePage(
        items: [_item('active-1')],
        pageSize: 1,
        totalCount: 3,
      );
      final page2 = _makePage(
        items: [
          ExpenseListItem(
            id: 'active-2',
            amount: 50,
            categoryName: 'Travel',
            paidTo: 'Uber',
            expenseDate: DateTime(2026, 7),
            isVoided: false,
          ),
        ],
        pageNumber: 2,
        pageSize: 1,
        totalCount: 3,
      );
      when(getExpenses.call).thenAnswer((_) async => page1);
      when(
        () => getExpenses(page: 2, pageSize: 1),
      ).thenAnswer((_) async => page2);
      final container = ProviderContainer(
        overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
      );
      addTearDown(container.dispose);

      await container.read(expensesControllerProvider.notifier).refresh();
      var state = container.read(expensesControllerProvider);
      expect(state.totalCount, 3);
      expect(state.loadedActiveCount, 1);
      expect(state.loadedAmount, 100);

      await container.read(expensesControllerProvider.notifier).loadMore();

      state = container.read(expensesControllerProvider);
      expect(state.totalCount, 3);
      expect(state.loadedActiveCount, 2);
      expect(state.loadedAmount, 150);
      expect(state.loadedVoidedCount, 0);
    },
  );

  test('openExpense loads detail without changing ledger state', () async {
    final getDetail = MockGetExpenseDetail();
    final completer = Completer<ExpenseDetail>();
    when(() => getDetail('expense-1')).thenAnswer((_) => completer.future);
    final container = detailContainer(getDetail);
    addTearDown(container.dispose);
    final page = container.read(expensesControllerProvider).page;

    final opening = container
        .read(expensesControllerProvider.notifier)
        .openExpense('expense-1');
    expect(container.read(expensesControllerProvider).isDetailLoading, isTrue);
    expect(container.read(expensesControllerProvider).page, page);

    final detail = _detail('expense-1');
    completer.complete(detail);
    await opening;
    final state = container.read(expensesControllerProvider);
    expect(state.selectedExpense, detail);
    expect(state.isDetailLoading, isFalse);
    expect(state.page, page);
  });

  test('detail failure and retry stay operation-specific', () async {
    final getDetail = MockGetExpenseDetail();
    when(() => getDetail('expense-1')).thenThrow(
      AppException(failure: const Failure.timeout(message: 'detail slow')),
    );
    final container = detailContainer(getDetail);
    addTearDown(container.dispose);

    await container
        .read(expensesControllerProvider.notifier)
        .openExpense('expense-1');
    expect(
      container.read(expensesControllerProvider).detailFailure,
      isA<TimeoutFailure>(),
    );

    final detail = _detail('expense-1');
    when(() => getDetail('expense-1')).thenAnswer((_) async => detail);
    await container
        .read(expensesControllerProvider.notifier)
        .retryExpenseDetail();
    expect(container.read(expensesControllerProvider).selectedExpense, detail);
    expect(container.read(expensesControllerProvider).detailFailure, isNull);
  });

  test('stale same-id failure cannot replace reopened detail state', () async {
    final getDetail = MockGetExpenseDetail();
    final first = Completer<ExpenseDetail>();
    final second = Completer<ExpenseDetail>();
    var calls = 0;
    when(
      () => getDetail('expense-1'),
    ).thenAnswer((_) => ++calls == 1 ? first.future : second.future);
    final container = detailContainer(getDetail);
    addTearDown(container.dispose);
    final controller = container.read(expensesControllerProvider.notifier);

    final firstOpening = controller.openExpense('expense-1');
    controller.clearSelectedExpense();
    final secondOpening = controller.openExpense('expense-1');
    final detail = _detail('expense-1');
    second.complete(detail);
    await secondOpening;
    first.completeError(
      AppException(failure: const Failure.network(message: 'stale')),
    );
    await firstOpening;

    final state = container.read(expensesControllerProvider);
    expect(state.selectedExpense, detail);
    expect(state.detailFailure, isNull);
  });

  test('clearSelectedExpense removes only detail state', () {
    final container = detailContainer(MockGetExpenseDetail());
    addTearDown(container.dispose);
    final controller = container.read(expensesControllerProvider.notifier);
    controller.clearSelectedExpense();
    final state = container.read(expensesControllerProvider);
    expect(state.selectedExpenseId, isNull);
    expect(state.selectedExpense, isNull);
    expect(state.detailFailure, isNull);
  });

  group('loadMore', () {
    test(
      'requests next page and appends unique rows in backend order',
      () async {
        final getExpenses = MockGetExpenses();
        final page1 = _makePage(
          items: [
            ExpenseListItem(
              id: 'expense-1',
              amount: 100,
              categoryName: 'Rent',
              paidTo: 'Landlord',
              expenseDate: DateTime(2026, 7),
              isVoided: false,
            ),
          ],
          totalCount: 21,
        );
        final page2 = _makePage(
          items: [
            ExpenseListItem(
              id: 'expense-1',
              amount: 100,
              categoryName: 'Duplicate',
              paidTo: 'Landlord',
              expenseDate: DateTime(2026, 7),
              isVoided: false,
            ),
            ExpenseListItem(
              id: 'expense-2',
              amount: 50,
              categoryName: 'Travel',
              paidTo: 'Uber',
              expenseDate: DateTime(2026, 7),
              isVoided: false,
            ),
          ],
          pageNumber: 2,
          totalCount: 21,
        );
        when(getExpenses.call).thenAnswer((_) async => page1);
        var callCount = 0;
        when(
          () => getExpenses(
            page: any(named: 'page'),
            pageSize: any(named: 'pageSize'),
          ),
        ).thenAnswer((_) async {
          callCount++;
          return callCount == 1 ? page1 : page2;
        });
        final container = ProviderContainer(
          overrides: [
            getExpensesUseCaseProvider.overrideWithValue(getExpenses),
          ],
        );
        addTearDown(container.dispose);

        await container.read(expensesControllerProvider.notifier).refresh();
        await container.read(expensesControllerProvider.notifier).loadMore();

        final state = container.read(expensesControllerProvider);
        expect(state.page?.items.map((e) => e.id).toList(), [
          'expense-1',
          'expense-2',
        ]);
        expect(state.page?.pageNumber, 2);
        expect(state.page?.pageSize, 20);
        expect(state.page?.totalCount, 21);
        expect(state.isLoadingMore, isFalse);
        expect(state.loadMoreFailure, isNull);
      },
    );

    test('requests next page without refreshing data', () async {
      final getExpenses = MockGetExpenses();
      final page1 = _makePage(
        items: [
          ExpenseListItem(
            id: 'expense-1',
            amount: 100,
            categoryName: 'Rent',
            paidTo: 'Landlord',
            expenseDate: DateTime(2026, 7),
            isVoided: false,
          ),
        ],
        totalCount: 21,
      );
      final page2 = _makePage(
        items: [
          ExpenseListItem(
            id: 'expense-2',
            amount: 50,
            categoryName: 'Travel',
            paidTo: 'Uber',
            expenseDate: DateTime(2026, 7),
            isVoided: false,
          ),
        ],
        pageNumber: 2,
        totalCount: 21,
      );
      when(getExpenses.call).thenAnswer((_) async => page1);
      when(
        () => getExpenses(page: 2, pageSize: 20),
      ).thenAnswer((_) async => page2);
      final container = ProviderContainer(
        overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
      );
      addTearDown(container.dispose);

      await container.read(expensesControllerProvider.notifier).refresh();
      await container.read(expensesControllerProvider.notifier).loadMore();

      final state = container.read(expensesControllerProvider);
      expect(state.page?.items.map((e) => e.id).toList(), [
        'expense-1',
        'expense-2',
      ]);
      expect(state.page?.pageNumber, 2);
      expect(state.isLoadingMore, isFalse);
      verify(() => getExpenses(page: 2, pageSize: 20)).called(1);
    });

    test('prevents loadMore when at end of list', () async {
      final getExpenses = MockGetExpenses();
      final lastPage = _makePage(
        items: [
          ExpenseListItem(
            id: 'expense-1',
            amount: 100,
            categoryName: 'Rent',
            paidTo: 'Landlord',
            expenseDate: DateTime(2026, 7),
            isVoided: false,
          ),
        ],
        totalCount: 1,
      );
      when(getExpenses.call).thenAnswer((_) async => lastPage);
      final container = ProviderContainer(
        overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
      );
      addTearDown(container.dispose);

      await container.read(expensesControllerProvider.notifier).refresh();
      await container.read(expensesControllerProvider.notifier).loadMore();

      final state = container.read(expensesControllerProvider);
      expect(state.page?.items.length, 1);
      expect(state.isLoadingMore, isFalse);
      verifyNever(() => getExpenses(page: 2, pageSize: 20));
    });

    test(
      'loadMore failure preserves loaded rows and exposes failure',
      () async {
        final getExpenses = MockGetExpenses();
        const failure = Failure.network(message: 'offline');
        final page1 = _makePage(
          items: [
            ExpenseListItem(
              id: 'expense-1',
              amount: 100,
              categoryName: 'Rent',
              paidTo: 'Landlord',
              expenseDate: DateTime(2026, 7),
              isVoided: false,
            ),
          ],
          totalCount: 21,
        );
        var callCount = 0;
        when(getExpenses.call).thenAnswer((_) async => page1);
        when(
          () => getExpenses(
            page: any(named: 'page'),
            pageSize: any(named: 'pageSize'),
          ),
        ).thenAnswer((_) async {
          callCount++;
          if (callCount == 1) return page1;
          throw AppException(failure: failure);
        });
        final container = ProviderContainer(
          overrides: [
            getExpensesUseCaseProvider.overrideWithValue(getExpenses),
          ],
        );
        addTearDown(container.dispose);

        await container.read(expensesControllerProvider.notifier).refresh();
        await container.read(expensesControllerProvider.notifier).loadMore();

        final state = container.read(expensesControllerProvider);
        expect(state.page?.items.length, 1);
        expect(state.page?.items.single.id, 'expense-1');
        expect(state.isLoadingMore, isFalse);
        expect(state.loadMoreFailure, isA<NetworkFailure>());
      },
    );

    test('loadMore failure retry recovers to success', () async {
      final getExpenses = MockGetExpenses();
      const failure = Failure.network(message: 'offline');
      final page1 = _makePage(
        items: [
          ExpenseListItem(
            id: 'expense-1',
            amount: 100,
            categoryName: 'Rent',
            paidTo: 'Landlord',
            expenseDate: DateTime(2026, 7),
            isVoided: false,
          ),
        ],
        totalCount: 21,
      );
      final page2 = _makePage(
        items: [
          ExpenseListItem(
            id: 'expense-2',
            amount: 50,
            categoryName: 'Travel',
            paidTo: 'Uber',
            expenseDate: DateTime(2026, 7),
            isVoided: false,
          ),
        ],
        pageNumber: 2,
        totalCount: 21,
      );
      var callCount = 0;
      when(getExpenses.call).thenAnswer((_) async => page1);
      when(() => getExpenses(page: 2, pageSize: 20)).thenAnswer((_) async {
        callCount++;
        if (callCount == 1) throw AppException(failure: failure);
        return page2;
      });
      final container = ProviderContainer(
        overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
      );
      addTearDown(container.dispose);

      await container.read(expensesControllerProvider.notifier).refresh();
      await container.read(expensesControllerProvider.notifier).loadMore();

      var state = container.read(expensesControllerProvider);
      expect(state.isLoadingMore, isFalse);
      expect(state.loadMoreFailure, isA<NetworkFailure>());

      await container.read(expensesControllerProvider.notifier).loadMore();

      state = container.read(expensesControllerProvider);
      expect(state.page?.items.map((e) => e.id).toList(), [
        'expense-1',
        'expense-2',
      ]);
      expect(state.isLoadingMore, isFalse);
      expect(state.loadMoreFailure, isNull);
    });

    test('does not issue concurrent append requests', () async {
      final getExpenses = MockGetExpenses();
      final page1 = _makePage(items: [_item('expense-1')], totalCount: 21);
      final page2 = _makePage(
        items: [_item('expense-2')],
        pageNumber: 2,
        totalCount: 21,
      );
      final nextPage = Completer<ExpensePage>();
      when(getExpenses.call).thenAnswer((_) async => page1);
      when(
        () => getExpenses(page: 2, pageSize: 20),
      ).thenAnswer((_) => nextPage.future);
      final container = ProviderContainer(
        overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
      );
      addTearDown(container.dispose);

      final controller = container.read(expensesControllerProvider.notifier);
      await controller.refresh();
      final firstRequest = controller.loadMore();
      final secondRequest = controller.loadMore();

      expect(container.read(expensesControllerProvider).isLoadingMore, isTrue);
      verify(() => getExpenses(page: 2, pageSize: 20)).called(1);

      nextPage.complete(page2);
      await Future.wait([firstRequest, secondRequest]);

      final state = container.read(expensesControllerProvider);
      expect(state.page?.items.map((e) => e.id).toList(), [
        'expense-1',
        'expense-2',
      ]);
      expect(state.isLoadingMore, isFalse);
    });

    test('refresh invalidates a stale append result', () async {
      final getExpenses = MockGetExpenses();
      final initialPage = _makePage(
        items: [_item('old-expense')],
        totalCount: 21,
      );
      final appendPage = _makePage(
        items: [_item('stale-expense')],
        pageNumber: 2,
        totalCount: 21,
      );
      final refreshedPage = _makePage(
        items: [_item('refreshed-expense')],
        totalCount: 1,
      );
      final appendCompleter = Completer<ExpensePage>();
      final refreshCompleter = Completer<ExpensePage>();
      when(getExpenses.call).thenAnswer((_) => refreshCompleter.future);
      when(
        () => getExpenses(page: 2, pageSize: 20),
      ).thenAnswer((_) => appendCompleter.future);
      final container = ProviderContainer(
        overrides: [getExpensesUseCaseProvider.overrideWithValue(getExpenses)],
      );
      addTearDown(container.dispose);
      container.read(expensesControllerProvider.notifier).state = ExpensesState(
        page: initialPage,
        totalCount: 21,
      );
      final controller = container.read(expensesControllerProvider.notifier);

      final appendRequest = controller.loadMore();
      final refreshRequest = controller.refresh();
      refreshCompleter.complete(refreshedPage);
      await refreshRequest;
      appendCompleter.complete(appendPage);
      await appendRequest;

      final state = container.read(expensesControllerProvider);
      expect(state.page?.items.map((e) => e.id).toList(), [
        'refreshed-expense',
      ]);
      expect(state.pageNumber, 1);
      expect(state.totalCount, 1);
      expect(state.isLoadingMore, isFalse);
      expect(state.hasMore, isFalse);
    });

    test(
      'loadMore issues a fresh request after refresh invalidates a pending append',
      () async {
        final getExpenses = MockGetExpenses();
        final initialPage = _makePage(
          items: [_item('old-expense')],
          totalCount: 21,
        );
        final refreshedPage = _makePage(
          items: [_item('refreshed-expense')],
          totalCount: 21,
        );
        final freshAppendPage = _makePage(
          items: [_item('fresh-page2')],
          pageNumber: 2,
          totalCount: 21,
        );
        final staleAppendPage = _makePage(
          items: [_item('stale-page2')],
          pageNumber: 2,
          totalCount: 21,
        );

        final staleAppendCompleter = Completer<ExpensePage>();
        final refreshCompleter = Completer<ExpensePage>();
        var page2CallCount = 0;
        when(getExpenses.call).thenAnswer((_) => refreshCompleter.future);
        when(() => getExpenses(page: 2, pageSize: 20)).thenAnswer((_) {
          page2CallCount++;
          return page2CallCount == 1
              ? staleAppendCompleter.future
              : Future.value(freshAppendPage);
        });

        final container = ProviderContainer(
          overrides: [
            getExpensesUseCaseProvider.overrideWithValue(getExpenses),
          ],
        );
        addTearDown(container.dispose);
        container.read(expensesControllerProvider.notifier).state =
            ExpensesState(page: initialPage, totalCount: 21);
        final controller = container.read(expensesControllerProvider.notifier);

        final staleAppend = controller.loadMore();
        final refreshRequest = controller.refresh();
        refreshCompleter.complete(refreshedPage);
        await refreshRequest;

        final freshAppend = controller.loadMore();
        await freshAppend;

        expect(page2CallCount, 2);
        var state = container.read(expensesControllerProvider);
        expect(state.page?.items.map((e) => e.id).toList(), [
          'refreshed-expense',
          'fresh-page2',
        ]);
        expect(state.isLoadingMore, isFalse);

        staleAppendCompleter.complete(staleAppendPage);
        await staleAppend;

        state = container.read(expensesControllerProvider);
        expect(
          state.page?.items.map((e) => e.id).contains('stale-page2'),
          isFalse,
        );
      },
    );
  });

  test('corrects an expense and refreshes list', () async {
    final correctExpense = MockCorrectExpense();
    final getExpenses = MockGetExpenses();
    when(
      () => correctExpense(
        'expense-1',
        categoryName: 'Utilities',
        amount: 150.0,
        paidTo: 'Power Co',
        description: null,
        expenseDate: DateTime(2026, 7, 3),
      ),
    ).thenAnswer((_) async => _detail('expense-2'));
    when(getExpenses.call).thenAnswer((_) async => _makePage(items: []));
    final container = ProviderContainer(
      overrides: [
        correctExpenseUseCaseProvider.overrideWithValue(correctExpense),
        getExpensesUseCaseProvider.overrideWithValue(getExpenses),
        expensesControllerProvider.overrideWith(
          () => _TestExpensesController(const ExpensesState()),
        ),
      ],
    );
    addTearDown(container.dispose);

    final corrected = await container
        .read(expensesControllerProvider.notifier)
        .correctExpense(
          'expense-1',
          categoryName: 'Utilities',
          amount: 150.0,
          paidTo: 'Power Co',
          description: null,
          expenseDate: DateTime(2026, 7, 3),
        );

    expect(corrected, isTrue);
    final state = container.read(expensesControllerProvider);
    expect(state.isSubmitting, isFalse);
    expect(state.submitFailure, isNull);
    verify(
      () => correctExpense(
        'expense-1',
        categoryName: 'Utilities',
        amount: 150.0,
        paidTo: 'Power Co',
        description: null,
        expenseDate: DateTime(2026, 7, 3),
      ),
    ).called(1);
    verify(() => getExpenses()).called(1);
  });

  test('retains form on correction failure', () async {
    final correctExpense = MockCorrectExpense();
    when(
      () => correctExpense(
        'expense-1',
        categoryName: 'Utilities',
        amount: 150.0,
        paidTo: 'Power Co',
        description: null,
        expenseDate: DateTime(2026, 7, 3),
      ),
    ).thenThrow(
      AppException(failure: const Failure.server(message: 'already voided')),
    );
    final container = ProviderContainer(
      overrides: [
        correctExpenseUseCaseProvider.overrideWithValue(correctExpense),
        expensesControllerProvider.overrideWith(
          () => _TestExpensesController(const ExpensesState()),
        ),
      ],
    );
    addTearDown(container.dispose);

    final corrected = await container
        .read(expensesControllerProvider.notifier)
        .correctExpense(
          'expense-1',
          categoryName: 'Utilities',
          amount: 150.0,
          paidTo: 'Power Co',
          description: null,
          expenseDate: DateTime(2026, 7, 3),
        );

    expect(corrected, isFalse);
    final state = container.read(expensesControllerProvider);
    expect(state.isSubmitting, isFalse);
    expect(state.submitFailure, isNotNull);
  });
}
