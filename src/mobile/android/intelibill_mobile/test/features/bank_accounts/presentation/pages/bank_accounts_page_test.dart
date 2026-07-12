import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/bank_account.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/save_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/use_cases/add_bank_account.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/use_cases/delete_bank_account.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/use_cases/get_bank_accounts.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/use_cases/update_bank_account.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/controllers/bank_accounts_controller.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/pages/bank_accounts_page.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/widgets/bank_account_card.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/widgets/bank_account_form.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/widgets/create_bank_account_sheet.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/widgets/edit_bank_account_sheet.dart';
import 'package:mocktail/mocktail.dart';

class MockGetBankAccounts extends Mock implements GetBankAccounts {}

class MockAddBankAccount extends Mock implements AddBankAccount {}

class MockDeleteBankAccount extends Mock implements DeleteBankAccount {}

class MockUpdateBankAccount extends Mock implements UpdateBankAccount {}

class _StubAuthController extends AuthController {
  _StubAuthController(this._state);

  final AuthControllerState _state;

  @override
  Future<AuthControllerState> build() async => _state;
}

class _TestApp extends StatelessWidget {
  const _TestApp({
    required this.getBankAccounts,
    required this.addBankAccount,
    required this.updateBankAccount,
    required this.session,
    this.deleteBankAccount,
  });

  final MockGetBankAccounts getBankAccounts;
  final MockAddBankAccount addBankAccount;
  final MockDeleteBankAccount? deleteBankAccount;
  final MockUpdateBankAccount updateBankAccount;
  final AuthSession session;

  @override
  Widget build(BuildContext context) {
    return ProviderScope(
      overrides: [
        getBankAccountsUseCaseProvider.overrideWithValue(getBankAccounts),
        addBankAccountUseCaseProvider.overrideWithValue(addBankAccount),
        if (deleteBankAccount != null)
          deleteBankAccountUseCaseProvider.overrideWithValue(
            deleteBankAccount!,
          ),
        updateBankAccountUseCaseProvider.overrideWithValue(updateBankAccount),
        authControllerProvider.overrideWith(
          () => _StubAuthController(AuthControllerState(session: session)),
        ),
      ],
      child: MaterialApp(
        theme: AppTheme.lightTheme,
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: const BankAccountsPage(),
      ),
    );
  }
}

AuthSession _session(String role) {
  return AuthSession(
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessTokenExpiresAt: DateTime.utc(2026, 6),
    refreshTokenExpiresAt: DateTime.utc(2026, 7),
    user: const AuthUser(
      id: 'user-1',
      email: 'owner@example.com',
      phoneNumber: null,
      firstName: 'Alex',
      lastName: 'Smith',
      language: 'en-IN',
    ),
    activeShopId: 'shop-1',
    shops: [
      UserShop(
        shopId: 'shop-1',
        shopName: 'Primary Shop',
        role: role,
        isDefault: true,
        lastUsedAt: DateTime.utc(2026, 6),
      ),
    ],
    rememberMe: false,
  );
}

const _account = BankAccount(
  id: 'account-1',
  bankName: 'Acme Bank',
  accountNumber: '123456789012',
  accountType: 'Current',
  ifscCode: 'ACME0001234',
  accountHolderName: 'Alex Smith',
);

const _directoryAccounts = [
  BankAccount(
    id: 'account-zeta',
    bankName: 'Zeta Bank',
    accountNumber: '111122223333',
    accountType: 'Savings',
    ifscCode: 'ZETA0001234',
    accountHolderName: 'Zoe Holder',
  ),
  BankAccount(
    id: 'account-alpha',
    bankName: 'Alpha Bank',
    accountNumber: '444455556666',
    accountType: 'Current',
    ifscCode: 'ALPHA0005678',
    accountHolderName: 'Asha Holder',
  ),
];

void main() {
  late MockGetBankAccounts getBankAccounts;
  late MockAddBankAccount addBankAccount;
  late MockDeleteBankAccount deleteBankAccount;
  late MockUpdateBankAccount updateBankAccount;

  setUpAll(() {
    registerFallbackValue('');
    registerFallbackValue(
      const SaveBankAccountRequest(
        bankName: 'Fallback Bank',
        accountNumber: '0',
        accountType: 'Savings',
      ),
    );
  });

  setUp(() {
    getBankAccounts = MockGetBankAccounts();
    addBankAccount = MockAddBankAccount();
    deleteBankAccount = MockDeleteBankAccount();
    updateBankAccount = MockUpdateBankAccount();
  });

  testWidgets('shows loading while accounts are requested', (tester) async {
    final request = Completer<List<BankAccount>>();
    when(() => getBankAccounts()).thenAnswer((_) => request.future);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pump();

    expect(find.byType(CircularProgressIndicator), findsOneWidget);
  });

  testWidgets('shows account cards with masked number and optional details', (
    tester,
  ) async {
    when(() => getBankAccounts()).thenAnswer((_) async => [_account]);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Acme Bank'), findsOneWidget);
    expect(find.text('********9012'), findsOneWidget);
    expect(find.text('123456789012'), findsNothing);
    expect(find.text('IFSC: ACME0001234'), findsOneWidget);
    expect(find.text('Account holder: Alex Smith'), findsOneWidget);
  });

  testWidgets('shows empty state when no accounts exist', (tester) async {
    when(() => getBankAccounts()).thenAnswer((_) async => []);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('No bank accounts found'), findsOneWidget);
  });

  testWidgets('sorts accounts alphabetically by bank name', (tester) async {
    when(() => getBankAccounts()).thenAnswer((_) async => _directoryAccounts);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();

    final cards = tester.widgetList<BankAccountCard>(
      find.byType(BankAccountCard),
    );
    expect(cards.map((card) => card.account.bankName), [
      'Alpha Bank',
      'Zeta Bank',
    ]);
  });

  testWidgets('searches every account field case-insensitively', (
    tester,
  ) async {
    when(() => getBankAccounts()).thenAnswer((_) async => _directoryAccounts);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();
    final searchField = find.byType(TextField);

    for (final match in const [
      ('zEtA', 'Zeta Bank'),
      ('444455556666', 'Alpha Bank'),
      ('sAvInGs', 'Zeta Bank'),
      ('alpha0005678', 'Alpha Bank'),
      ('zoe holder', 'Zeta Bank'),
    ]) {
      await tester.enterText(searchField, match.$1);
      await tester.pump();
      expect(find.text(match.$2), findsOneWidget);
      expect(
        find.byType(BankAccountCard),
        findsOneWidget,
        reason: 'query ${match.$1} should match one account',
      );
    }

    expect(find.text('444455556666'), findsNothing);
  });

  testWidgets('clears search and restores the full directory', (tester) async {
    when(() => getBankAccounts()).thenAnswer((_) async => _directoryAccounts);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();
    final searchField = find.byType(TextField);
    await tester.enterText(searchField, 'alpha');
    await tester.pump();
    await tester.tap(find.byTooltip('Clear bank account search'));
    await tester.pump();

    expect(tester.widget<TextField>(searchField).controller!.text, isEmpty);
    expect(find.byType(BankAccountCard), findsNWidgets(2));
  });

  testWidgets('shows a distinct no-results state for an unmatched search', (
    tester,
  ) async {
    when(() => getBankAccounts()).thenAnswer((_) async => _directoryAccounts);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField), 'does-not-exist');
    await tester.pump();

    expect(find.text('No bank accounts match your search'), findsOneWidget);
    expect(find.text('No bank accounts found'), findsNothing);
    expect(find.byType(BankAccountCard), findsNothing);
  });

  testWidgets('pull-to-refresh keeps the query and shows refreshed matches', (
    tester,
  ) async {
    var requestCount = 0;
    when(() => getBankAccounts()).thenAnswer((_) async {
      requestCount++;
      return requestCount == 1
          ? _directoryAccounts
          : [
              const BankAccount(
                id: 'account-zeta-refreshed',
                bankName: 'Zeta Bank',
                accountNumber: '999988887777',
                accountType: 'Savings',
              ),
              const BankAccount(
                id: 'account-beta-refreshed',
                bankName: 'Beta Bank',
                accountNumber: '222233334444',
                accountType: 'Current',
              ),
            ];
    });

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField), 'zeta');
    await tester.pump();

    await tester.fling(find.byType(ListView), const Offset(0, 400), 1000);
    await tester.pumpAndSettle();

    expect(requestCount, greaterThanOrEqualTo(2));
    expect(find.text('Zeta Bank'), findsOneWidget);
    expect(find.text('Beta Bank'), findsNothing);
    expect(find.text('********7777'), findsOneWidget);
  });

  testWidgets('shows retry and reloads after a failure', (tester) async {
    when(() => getBankAccounts()).thenAnswer(
      (_) async => throw AppException(
        failure: const Failure.unknown(message: 'offline'),
      ),
    );

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Unable to load bank accounts'), findsOneWidget);
    expect(find.text('Retry'), findsOneWidget);

    when(() => getBankAccounts()).thenAnswer((_) async => [_account]);
    await tester.tap(find.text('Retry'));
    await tester.pumpAndSettle();

    expect(find.text('Acme Bank'), findsOneWidget);
    verify(() => getBankAccounts()).called(2);
  });

  testWidgets('shows the add action only to an owner', (tester) async {
    when(() => getBankAccounts()).thenAnswer((_) async => []);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byKey(BankAccountsPage.addBankAccountFabKey), findsOneWidget);

    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pumpAndSettle();
    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Staff'),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byKey(BankAccountsPage.addBankAccountFabKey), findsNothing);
  });

  testWidgets('owner creates an account and sees the refreshed card', (
    tester,
  ) async {
    var requests = 0;
    when(() => getBankAccounts()).thenAnswer((_) async {
      requests += 1;
      return requests == 1
          ? []
          : [
              const BankAccount(
                id: 'account-2',
                bankName: 'New Bank',
                accountNumber: '12345678',
                accountType: 'Savings',
              ),
            ];
    });
    when(() => addBankAccount(any())).thenAnswer((_) async {});

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(BankAccountsPage.addBankAccountFabKey));
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(BankAccountForm.bankNameFieldKey),
      ' New Bank ',
    );
    await tester.enterText(
      find.byKey(BankAccountForm.accountNumberFieldKey),
      '12345678',
    );
    await tester.tap(find.byKey(BankAccountForm.accountTypeFieldKey));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Savings').last);
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.byKey(BankAccountForm.submitButtonKey));
    await tester.tap(find.byKey(BankAccountForm.submitButtonKey));
    await tester.pumpAndSettle();

    expect(find.text('Bank account created successfully.'), findsOneWidget);
    expect(find.text('New Bank'), findsOneWidget);
    expect(find.byType(CreateBankAccountSheet), findsNothing);
    verify(
      () => addBankAccount(
        const SaveBankAccountRequest(
          bankName: 'New Bank',
          accountNumber: '12345678',
          accountType: 'Savings',
        ),
      ),
    ).called(1);
    verify(() => getBankAccounts()).called(greaterThanOrEqualTo(2));
  });

  testWidgets('keeps the sheet and inputs open after a failed submission', (
    tester,
  ) async {
    when(() => getBankAccounts()).thenAnswer((_) async => []);
    when(() => addBankAccount(any())).thenThrow(
      AppException(failure: const Failure.network()),
    );

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(BankAccountsPage.addBankAccountFabKey));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(BankAccountForm.bankNameFieldKey),
      'Failed Bank',
    );
    await tester.enterText(
      find.byKey(BankAccountForm.accountNumberFieldKey),
      '12345678',
    );
    await tester.tap(find.byKey(BankAccountForm.accountTypeFieldKey));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Savings').last);
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.byKey(BankAccountForm.submitButtonKey));
    await tester.tap(find.byKey(BankAccountForm.submitButtonKey));
    await tester.pumpAndSettle();

    expect(find.byType(CreateBankAccountSheet), findsOneWidget);
    expect(
      find.text('Unable to connect. Please check your network.'),
      findsOneWidget,
    );
    expect(find.text('Failed Bank'), findsOneWidget);
    verify(() => addBankAccount(any())).called(1);
  });

  testWidgets('prevents duplicate submissions while an account is saving', (
    tester,
  ) async {
    final pendingCreate = Completer<void>();
    when(() => getBankAccounts()).thenAnswer((_) async => []);
    when(() => addBankAccount(any())).thenAnswer((_) => pendingCreate.future);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(BankAccountsPage.addBankAccountFabKey));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(BankAccountForm.bankNameFieldKey),
      'Guarded Bank',
    );
    await tester.enterText(
      find.byKey(BankAccountForm.accountNumberFieldKey),
      '12345678',
    );
    await tester.tap(find.byKey(BankAccountForm.accountTypeFieldKey));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Savings').last);
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.byKey(BankAccountForm.submitButtonKey));

    await tester.tap(find.byKey(BankAccountForm.submitButtonKey));
    await tester.pump();
    final submitButton = tester.widget<FilledButton>(
      find.byKey(BankAccountForm.submitButtonKey),
    );
    expect(submitButton.onPressed, isNull);
    await tester.tap(
      find.byKey(BankAccountForm.submitButtonKey),
      warnIfMissed: false,
    );
    await tester.pump();

    expect(find.byType(CreateBankAccountSheet), findsOneWidget);
    verify(() => addBankAccount(any())).called(1);

    pendingCreate.complete();
    await tester.pumpAndSettle();
  });

  testWidgets('owner opens edit sheet with prefilled account data', (
    tester,
  ) async {
    when(() => getBankAccounts()).thenAnswer((_) async => [_account]);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(BankAccountCard.editActionKey));
    await tester.pumpAndSettle();

    expect(find.byType(EditBankAccountSheet), findsOneWidget);
    expect(
      find.byKey(BankAccountForm.bankNameFieldKey),
      findsOneWidget,
    );
    final bankNameField = tester.widget<TextFormField>(
      find.byKey(BankAccountForm.bankNameFieldKey),
    );
    expect(
      bankNameField.controller!.text,
      'Acme Bank',
    );
    final accountNumberField = tester.widget<TextFormField>(
      find.byKey(BankAccountForm.accountNumberFieldKey),
    );
    expect(
      accountNumberField.controller!.text,
      '123456789012',
    );
  });

  testWidgets('keeps edit sheet open after failed update', (tester) async {
    when(() => getBankAccounts()).thenAnswer((_) async => [_account]);
    when(() => updateBankAccount(any(), any())).thenThrow(
      AppException(failure: const Failure.network()),
    );

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(BankAccountCard.editActionKey));
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(BankAccountForm.bankNameFieldKey),
      'Changed Bank',
    );
    await tester.ensureVisible(find.byKey(BankAccountForm.submitButtonKey));
    await tester.tap(find.byKey(BankAccountForm.submitButtonKey));
    await tester.pumpAndSettle();

    expect(find.byType(EditBankAccountSheet), findsOneWidget);
    expect(
      find.text('Unable to connect. Please check your network.'),
      findsOneWidget,
    );
    expect(find.text('Changed Bank'), findsOneWidget);
    verify(() => updateBankAccount(any(), any())).called(1);
  });

  testWidgets('prevents duplicate edit submissions', (tester) async {
    final pendingUpdate = Completer<void>();
    when(() => getBankAccounts()).thenAnswer((_) async => [_account]);
    when(
      () => updateBankAccount(any(), any()),
    ).thenAnswer((_) => pendingUpdate.future);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(BankAccountCard.editActionKey));
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(BankAccountForm.bankNameFieldKey),
      'Guarded Bank',
    );
    await tester.ensureVisible(find.byKey(BankAccountForm.submitButtonKey));

    await tester.tap(find.byKey(BankAccountForm.submitButtonKey));
    await tester.pump();
    final submitButton = tester.widget<FilledButton>(
      find.byKey(BankAccountForm.submitButtonKey),
    );
    expect(submitButton.onPressed, isNull);
    await tester.tap(
      find.byKey(BankAccountForm.submitButtonKey),
      warnIfMissed: false,
    );
    await tester.pump();

    expect(find.byType(EditBankAccountSheet), findsOneWidget);
    verify(() => updateBankAccount(any(), any())).called(1);

    pendingUpdate.complete();
    await tester.pumpAndSettle();
  });

  testWidgets('staff cannot see edit action', (tester) async {
    when(() => getBankAccounts()).thenAnswer((_) async => [_account]);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Staff'),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(BankAccountCard.editActionKey), findsNothing);
  });

  testWidgets('shows delete action only to an owner', (tester) async {
    when(() => getBankAccounts()).thenAnswer((_) async => [_account]);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        deleteBankAccount: deleteBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byKey(BankAccountCard.deleteActionKey), findsOneWidget);

    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pumpAndSettle();
    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        deleteBankAccount: deleteBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Staff'),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byKey(BankAccountCard.deleteActionKey), findsNothing);
  });

  testWidgets('cancelled deletion does not send a delete request', (
    tester,
  ) async {
    when(() => getBankAccounts()).thenAnswer((_) async => [_account]);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        deleteBankAccount: deleteBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(BankAccountCard.deleteActionKey));
    await tester.pumpAndSettle();

    expect(find.byType(AlertDialog), findsOneWidget);
    expect(
      find.text('Permanently delete Acme Bank? This cannot be undone.'),
      findsOneWidget,
    );

    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();

    expect(find.byType(AlertDialog), findsNothing);
    verifyNever(() => deleteBankAccount(any()));
  });

  testWidgets('confirmed deletion refreshes the list and shows success', (
    tester,
  ) async {
    var requests = 0;
    when(() => getBankAccounts()).thenAnswer((_) async {
      requests += 1;
      return requests == 1 ? [_account] : [];
    });
    when(() => deleteBankAccount('account-1')).thenAnswer((_) async {});

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        deleteBankAccount: deleteBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(BankAccountCard.deleteActionKey));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(BankAccountsPage.deleteConfirmButtonKey));
    await tester.pumpAndSettle();

    expect(find.text('Bank account deleted successfully.'), findsOneWidget);
    expect(find.text('Acme Bank'), findsNothing);
    expect(find.byType(AlertDialog), findsNothing);
    verify(() => deleteBankAccount('account-1')).called(1);
    verify(() => getBankAccounts()).called(greaterThanOrEqualTo(2));
  });

  testWidgets('failed deletion retains the account and shows feedback', (
    tester,
  ) async {
    when(() => getBankAccounts()).thenAnswer((_) async => [_account]);
    when(() => deleteBankAccount('account-1')).thenThrow(
      AppException(failure: const Failure.network()),
    );

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        deleteBankAccount: deleteBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(BankAccountCard.deleteActionKey));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(BankAccountsPage.deleteConfirmButtonKey));
    await tester.pumpAndSettle();

    expect(find.byType(AlertDialog), findsOneWidget);
    expect(find.text('Acme Bank'), findsOneWidget);
    expect(
      find.text('Unable to delete bank account. Please try again.'),
      findsOneWidget,
    );
    verify(() => deleteBankAccount('account-1')).called(1);
    verify(() => getBankAccounts()).called(1);
  });

  testWidgets('prevents duplicate delete requests while deletion is pending', (
    tester,
  ) async {
    final pendingDelete = Completer<void>();
    when(() => getBankAccounts()).thenAnswer((_) async => [_account]);
    when(
      () => deleteBankAccount('account-1'),
    ).thenAnswer((_) => pendingDelete.future);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        deleteBankAccount: deleteBankAccount,
        updateBankAccount: updateBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(BankAccountCard.deleteActionKey));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(BankAccountsPage.deleteConfirmButtonKey));
    await tester.pump();

    final confirm = tester.widget<FilledButton>(
      find.byKey(BankAccountsPage.deleteConfirmButtonKey),
    );
    expect(confirm.onPressed, isNull);
    await tester.tap(
      find.byKey(BankAccountsPage.deleteConfirmButtonKey),
      warnIfMissed: false,
    );
    await tester.pump();

    verify(() => deleteBankAccount('account-1')).called(1);

    pendingDelete.complete();
    await tester.pumpAndSettle();
  });
}
